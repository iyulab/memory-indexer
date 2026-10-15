using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MemoryIndexer.Utilities;
using MemoryIndexer.VectorData;
using Microsoft.Extensions.VectorData;

namespace MemoryIndexer.Sdk.Tests.Storage.VectorData;

/// <summary>
/// An in-memory <see cref="VectorStore"/> for <see cref="MemoryRecord"/> collections that behaves like a database
/// connector where it matters to <see cref="VectorStoreMemoryStore"/>: records are copied in and out (no shared references),
/// vectors come back only when asked for, the vector dimension is enforced, and — the point of it — a filter or ordering
/// is refused unless a real connector could translate it: only <c>==</c>, <c>&lt;</c>/<c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c>,
/// <c>&amp;&amp;</c>, <c>||</c> and string-list <c>Contains</c> over constants, and only on properties the definition marks
/// indexed. A store that compiled any lambda here would pass filters Qdrant or pgvector reject.
/// </summary>
internal sealed class TranslatingInMemoryVectorStore : VectorStore
{
    private readonly ConcurrentDictionary<string, Collection> _collections = new(StringComparer.Ordinal);

    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
    {
        if (typeof(TKey) != typeof(Guid) || typeof(TRecord) != typeof(MemoryRecord))
            throw new NotSupportedException("This test store serves Guid-keyed MemoryRecord collections only.");
        ArgumentNullException.ThrowIfNull(definition);
        var collection = _collections.GetOrAdd(name, n => new Collection(n, definition));
        return (VectorStoreCollection<TKey, TRecord>)(object)collection;
    }

    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
        => throw new NotSupportedException();

    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var (name, collection) in _collections)
        {
            if (collection.Exists)
                yield return name;
        }
    }

    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(_collections.TryGetValue(name, out var c) && c.Exists);

    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_collections.TryGetValue(name, out var c))
            c.Drop();
        return Task.CompletedTask;
    }

    public override object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <summary>Number of filters and orderings checked — a guard that ran on nothing proves nothing.</summary>
    public int ExpressionsChecked => _collections.Values.Sum(c => c.ExpressionsChecked);

    private sealed class Collection : VectorStoreCollection<Guid, MemoryRecord>
    {
        private readonly ConcurrentDictionary<Guid, MemoryRecord> _rows = new();
        private readonly HashSet<string> _indexed;
        private readonly int _dimensions;
        private int _checked;

        public Collection(string name, VectorStoreCollectionDefinition definition)
        {
            Name = name;
            _indexed = [.. definition.Properties.OfType<VectorStoreDataProperty>().Where(p => p.IsIndexed).Select(p => p.Name)];
            _dimensions = definition.Properties.OfType<VectorStoreVectorProperty>().Single().Dimensions;
        }

        public override string Name { get; }

        public bool Exists { get; private set; }

        public int ExpressionsChecked => _checked;

        public void Drop()
        {
            _rows.Clear();
            Exists = false;
        }

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Exists);

        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        {
            Exists = true;
            return Task.CompletedTask;
        }

        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        {
            Drop();
            return Task.CompletedTask;
        }

        public override Task<MemoryRecord?> GetAsync(Guid key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_rows.TryGetValue(key, out var row) ? Copy(row, options?.IncludeVectors ?? false) : null);

        public override async IAsyncEnumerable<MemoryRecord> GetAsync(
            IEnumerable<Guid> keys, RecordRetrievalOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (var key in keys)
            {
                if (_rows.TryGetValue(key, out var row))
                    yield return Copy(row, options?.IncludeVectors ?? false);
            }
        }

        public override async IAsyncEnumerable<MemoryRecord> GetAsync(
            Expression<Func<MemoryRecord, bool>> filter, int top, FilteredRecordRetrievalOptions<MemoryRecord>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
            var predicate = Translate(filter);
            IEnumerable<MemoryRecord> rows = _rows.Values.Where(predicate);

            if (options?.OrderBy is { } orderBy)
            {
                IOrderedEnumerable<MemoryRecord>? ordered = null;
                foreach (var sort in orderBy(new FilteredRecordRetrievalOptions<MemoryRecord>.OrderByDefinition()).Values)
                {
                    var key = TranslateSort(sort.PropertySelector);
                    ordered = ordered is null
                        ? sort.Ascending ? rows.OrderBy(key) : rows.OrderByDescending(key)
                        : sort.Ascending ? ordered.ThenBy(key) : ordered.ThenByDescending(key);
                }

                rows = ordered ?? rows;
            }

            await Task.Yield();
            foreach (var row in rows.Skip(options?.Skip ?? 0).Take(top).ToList())
                yield return Copy(row, options?.IncludeVectors ?? false);
        }

        public override Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
        {
            _rows.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public override Task DeleteAsync(IEnumerable<Guid> keys, CancellationToken cancellationToken = default)
        {
            foreach (var key in keys)
                _rows.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public override Task UpsertAsync(MemoryRecord record, CancellationToken cancellationToken = default)
        {
            if (record.Embedding is { } embedding && embedding.Length != _dimensions)
                throw new VectorStoreException($"Vector has {embedding.Length} dimensions, the collection {_dimensions}.");
            Exists = true;
            _rows[record.Id] = Copy(record, includeVectors: true);
            return Task.CompletedTask;
        }

        public override async Task UpsertAsync(IEnumerable<MemoryRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var record in records)
                await UpsertAsync(record, cancellationToken);
        }

        public override async IAsyncEnumerable<VectorSearchResult<MemoryRecord>> SearchAsync<TInput>(
            TInput searchValue, int top, VectorSearchOptions<MemoryRecord>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (searchValue is not ReadOnlyMemory<float> query)
                throw new NotSupportedException($"Search input {typeof(TInput).Name} is not a vector.");
            if (query.Length != _dimensions)
                throw new VectorStoreException($"Query has {query.Length} dimensions, the collection {_dimensions}.");

            var predicate = options?.Filter is { } filter ? Translate(filter) : _ => true;
            var hits = _rows.Values
                .Where(r => r.Embedding.HasValue && predicate(r))
                .Select(r => (Row: r, Score: (double)VectorMath.CosineSimilarity(query, r.Embedding!.Value)))
                .OrderByDescending(h => h.Score)
                .Skip(options?.Skip ?? 0)
                .Take(top)
                .ToList();

            await Task.Yield();
            foreach (var hit in hits)
                yield return new VectorSearchResult<MemoryRecord>(Copy(hit.Row, options?.IncludeVectors ?? false), hit.Score);
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) => null;

        private Func<MemoryRecord, bool> Translate(Expression<Func<MemoryRecord, bool>> filter)
        {
            Interlocked.Increment(ref _checked);
            new Guard(_indexed).Visit(filter.Body);
            var compiled = filter.Compile();
            return compiled;
        }

        private Func<MemoryRecord, object?> TranslateSort(Expression<Func<MemoryRecord, object?>> selector)
        {
            Interlocked.Increment(ref _checked);
            var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : selector.Body;
            if (body is not MemberExpression { Expression: ParameterExpression } member || !_indexed.Contains(member.Member.Name))
                throw new NotSupportedException($"A connector orders only by an indexed property; got {selector}.");
            return selector.Compile();
        }

        private static MemoryRecord Copy(MemoryRecord row, bool includeVectors)
        {
            var copy = MemoryRecord.From(row.ToMemory());
            copy.UpdatedAtTicks = row.UpdatedAtTicks;
            copy.CreatedAtTicks = row.CreatedAtTicks;
            if (!includeVectors)
                copy.Embedding = null;
            return copy;
        }
    }

    /// <summary>Throws on any filter node outside what the connectors translate.</summary>
    private sealed class Guard(HashSet<string> indexed) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            switch (node)
            {
                case null:
                    return null;
                case BinaryExpression b when b.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse:
                    Visit(b.Left);
                    Visit(b.Right);
                    return node;
                case BinaryExpression b when b.NodeType is ExpressionType.Equal or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual:
                    RequireIndexedMember(b.Left, node);
                    RequireConstant(b.Right, node);
                    return node;
                case MethodCallExpression { Method.Name: "Contains", Object: { } list, Arguments: [var value] } call
                    when list.Type == typeof(List<string>):
                    RequireIndexedMember(list, call);
                    RequireConstant(value, call);
                    return node;
                case ConstantExpression { Value: false }:
                    return node;
                default:
                    throw new NotSupportedException($"A connector cannot translate '{node}' ({node.NodeType}).");
            }
        }

        private void RequireIndexedMember(Expression expression, Expression whole)
        {
            if (expression is not MemberExpression { Expression: ParameterExpression } member)
                throw new NotSupportedException($"The left side of '{whole}' is not a record property.");
            if (!indexed.Contains(member.Member.Name))
                throw new NotSupportedException($"'{member.Member.Name}' is filtered on but not indexed.");
        }

        private static void RequireConstant(Expression expression, Expression whole)
        {
            if (expression is not ConstantExpression)
                throw new NotSupportedException($"The right side of '{whole}' is not a constant.");
        }
    }
}
