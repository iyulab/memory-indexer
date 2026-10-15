using System.Linq.Expressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace MemoryIndexer.VectorData;

/// <summary>
/// Options for <see cref="VectorStoreMemoryStore"/>.
/// </summary>
public sealed class VectorStoreMemoryStoreOptions
{
    /// <summary>Collection (table, index) the memories live in. Default <c>memories</c>.</summary>
    public string CollectionName { get; set; } = "memories";

    /// <summary>Embedding dimension — the one your embedding model produces. Required.</summary>
    public int Dimensions { get; set; }

    /// <summary>
    /// Distance function of the vector index (a <see cref="DistanceFunction"/> name). Default
    /// <see cref="DistanceFunction.CosineSimilarity"/>. It decides the database's ranking; the score a search returns is
    /// the cosine similarity computed from the stored vectors, the same as the other stores'.
    /// </summary>
    public string DistanceFunction { get; set; } = Microsoft.Extensions.VectorData.DistanceFunction.CosineSimilarity;

    /// <summary>Records read per request when a call has to walk a user's memories (counts, user list, deletes). Default 500.</summary>
    public int PageSize { get; set; } = 500;
}

/// <summary>
/// An <see cref="IMemoryStore"/> over any Microsoft.Extensions.VectorData <see cref="VectorStore"/> — Qdrant, PostgreSQL
/// (pgvector), Azure AI Search and the other connectors — so a server deployment keeps memories in the database it already
/// runs. Rows are <see cref="MemoryRecord"/>s; the schema is built from <see cref="VectorStoreMemoryStoreOptions.Dimensions"/>.
/// </summary>
/// <remarks>
/// <para>Every filter goes to the database as equality, range, <c>&amp;&amp;</c>/<c>||</c> and string-list <c>Contains</c>
/// on indexed properties — the subset the connectors translate.</para>
/// <para>Counts, the user list and bulk deletes walk the matching records page by page (vector databases have no
/// aggregates); they cost one request per <see cref="VectorStoreMemoryStoreOptions.PageSize"/> records.</para>
/// <para>A memory needs an embedding to be found by <see cref="SearchAsync"/>; whether one without an embedding can be
/// stored at all depends on the connector.</para>
/// </remarks>
public sealed partial class VectorStoreMemoryStore : IMemoryStore
{
    private readonly VectorStoreCollection<Guid, MemoryRecord> _collection;
    private readonly VectorStoreMemoryStoreOptions _options;
    private readonly ILogger<VectorStoreMemoryStore> _logger;

    /// <summary>Creates the store over <paramref name="vectorStore"/>.</summary>
    public VectorStoreMemoryStore(VectorStore vectorStore, VectorStoreMemoryStoreOptions options, ILogger<VectorStoreMemoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(vectorStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectionName);
        if (options.Dimensions < 1)
            throw new ArgumentException("VectorStoreMemoryStoreOptions.Dimensions must be the embedding model's dimension (at least 1).", nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageSize, 1);

        _options = options;
        _logger = logger;
        _collection = vectorStore.GetCollection<Guid, MemoryRecord>(
            options.CollectionName, MemoryRecord.CreateDefinition(options.Dimensions, options.DistanceFunction));
    }

    /// <inheritdoc />
    public async Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        Prepare(memory);
        await _collection.UpsertAsync(MemoryRecord.From(memory), cancellationToken).ConfigureAwait(false);
        LogStored(_logger, memory.Id, memory.UserId);
        return memory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(IEnumerable<MemoryUnit> memories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memories);
        var list = memories.ToList();
        foreach (var memory in list)
            Prepare(memory);
        if (list.Count > 0)
            await _collection.UpsertAsync(list.Select(MemoryRecord.From), cancellationToken).ConfigureAwait(false);
        return list;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> GetByIdAsync(string userId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var record = await _collection.GetAsync(id, new RecordRetrievalOptions { IncludeVectors = true }, cancellationToken).ConfigureAwait(false);
        return record is not null && record.UserId == userId ? record.ToMemory() : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(string userId, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
            return [];

        var found = new List<MemoryUnit>();
        await foreach (var record in _collection.GetAsync(wanted, new RecordRetrievalOptions { IncludeVectors = true }, cancellationToken).ConfigureAwait(false))
        {
            if (record.UserId == userId)
                found.Add(record.ToMemory());
        }

        // In the order asked for, as the other stores return them.
        var order = wanted.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return [.. found.OrderBy(m => order[m.Id])];
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // The stored owner is part of the key: an update never moves a memory to another user.
        var existing = await _collection.GetAsync(memory.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != memory.UserId)
            return false;

        memory.UpdatedAt = DateTime.UtcNow;
        await _collection.UpsertAsync(MemoryRecord.From(memory), cancellationToken).ConfigureAwait(false);
        LogUpdated(_logger, memory.Id);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string userId, Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var record = await _collection.GetAsync(id, new RecordRetrievalOptions { IncludeVectors = true }, cancellationToken).ConfigureAwait(false);
        if (record is null || record.UserId != userId)
            return false;

        await DeleteRecordsAsync([record], hardDelete, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<int> DeleteByUserAsync(string userId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return DeleteMatchingAsync(MemoryRecordFilter.ForUser(userId, includeDeleted: true), hardDelete, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteBySessionAsync(string userId, string sessionId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        return DeleteMatchingAsync(MemoryRecordFilter.ForUser(userId, includeDeleted: true, sessionId: sessionId), hardDelete, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteByNamespaceAsync(string userId, string namespaceName, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(namespaceName);
        return DeleteMatchingAsync(
            MemoryRecordFilter.ForUser(userId, includeDeleted: false, @namespace: namespaceName), hardDelete, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.UserId);
        if (options.Limit <= 0)
            return [];

        var filter = MemoryRecordFilter.For(
            options.UserId, options.SessionId, options.Namespace, options.Types, options.Roles, tiers: null, scopes: null,
            options.CreatedAfter, options.CreatedBefore, options.IncludeDeleted, options.MetadataFilter);

        var results = new List<MemorySearchResult>();
        var search = new VectorSearchOptions<MemoryRecord> { Filter = filter, IncludeVectors = true };
        await foreach (var hit in _collection.SearchAsync(queryEmbedding, options.Limit, search, cancellationToken).ConfigureAwait(false))
        {
            if (hit.Record.Embedding is not { } embedding)
                continue;

            // The cosine similarity from the vectors, so the score means the same on every connector and every store.
            var score = VectorMath.CosineSimilarity(queryEmbedding, embedding);
            if (score >= options.MinScore)
                results.Add(new MemorySearchResult { Memory = hit.Record.ToMemory(), Score = score });
        }

        LogSearched(_logger, results.Count);
        return [.. results.OrderByDescending(r => r.Score)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetAllAsync(string userId, MemoryFilterOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        options ??= new MemoryFilterOptions();

        var filter = MemoryRecordFilter.For(
            userId, options.SessionId, options.Namespace, options.Types, options.Roles, options.Tiers, options.Scopes,
            options.CreatedAfter, options.CreatedBefore, options.IncludeDeleted, options.MetadataFilter);

        var retrieval = new FilteredRecordRetrievalOptions<MemoryRecord> { IncludeVectors = true, Skip = options.Skip };
        retrieval.OrderBy = options.OrderBy switch
        {
            MemoryOrderBy.CreatedAtAsc => o => o.Ascending(r => r.CreatedAtTicks),
            MemoryOrderBy.UpdatedAtDesc => o => o.Descending(r => r.UpdatedAtTicks),
            MemoryOrderBy.UpdatedAtAsc => o => o.Ascending(r => r.UpdatedAtTicks),
            MemoryOrderBy.ImportanceDesc => o => o.Descending(r => r.ImportanceScore),
            MemoryOrderBy.AccessCountDesc => o => o.Descending(r => r.AccessCount),
            _ => o => o.Descending(r => r.CreatedAtTicks)
        };

        var memories = new List<MemoryUnit>();
        if (options.Limit is { } limit)
        {
            if (limit <= 0)
                return [];
            await foreach (var record in _collection.GetAsync(filter, limit, retrieval, cancellationToken).ConfigureAwait(false))
                memories.Add(record.ToMemory());
            return memories;
        }

        await foreach (var record in PageAsync(filter, retrieval, cancellationToken).ConfigureAwait(false))
            memories.Add(record.ToMemory());
        return memories;
    }

    /// <inheritdoc />
    public async Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        long count = 0;
        await foreach (var _ in PageAsync(MemoryRecordFilter.ForUser(userId, includeDeleted: false), null, cancellationToken).ConfigureAwait(false))
            count++;
        return count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserIdsAsync(CancellationToken cancellationToken = default)
    {
        var users = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var record in PageAsync(MemoryRecordFilter.NotDeleted(), null, cancellationToken).ConfigureAwait(false))
            users.Add(record.UserId);
        return [.. users.Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var counts = new Dictionary<MemoryType, int>();
        await foreach (var record in PageAsync(MemoryRecordFilter.ForUser(userId, includeDeleted: false), null, cancellationToken).ConfigureAwait(false))
        {
            var type = Enum.Parse<MemoryType>(record.Type);
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        return counts;
    }

    /// <inheritdoc />
    public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        => _collection.EnsureCollectionExistsAsync(cancellationToken);

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        await _collection.EnsureCollectionDeletedAsync(cancellationToken).ConfigureAwait(false);
        LogCollectionDeleted(_logger, _options.CollectionName);
    }

    private static void Prepare(MemoryUnit memory)
    {
        ArgumentException.ThrowIfNullOrEmpty(memory.UserId);
        memory.Id = memory.Id == Guid.Empty ? Guid.NewGuid() : memory.Id;
        memory.CreatedAt = memory.CreatedAt == default ? DateTime.UtcNow : memory.CreatedAt;
        memory.UpdatedAt = memory.UpdatedAt == default ? DateTime.UtcNow : memory.UpdatedAt;
    }

    // Every record matching the filter, a page at a time. Ordered by key so a page boundary is stable while reading.
    private async IAsyncEnumerable<MemoryRecord> PageAsync(
        Expression<Func<MemoryRecord, bool>> filter,
        FilteredRecordRetrievalOptions<MemoryRecord>? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var skip = options?.Skip ?? 0;
        while (true)
        {
            var page = new FilteredRecordRetrievalOptions<MemoryRecord>
            {
                IncludeVectors = options?.IncludeVectors ?? false,
                Skip = skip,
                OrderBy = options?.OrderBy ?? (o => o.Ascending(r => r.CreatedAtTicks))
            };

            var read = 0;
            await foreach (var record in _collection.GetAsync(filter, _options.PageSize, page, cancellationToken).ConfigureAwait(false))
            {
                read++;
                yield return record;
            }

            if (read < _options.PageSize)
                yield break;
            skip += read;
        }
    }

    private async Task<int> DeleteMatchingAsync(Expression<Func<MemoryRecord, bool>> filter, bool hardDelete, CancellationToken cancellationToken)
    {
        // Read everything first, then write: deleting while paging would shift the pages under the reader.
        var records = new List<MemoryRecord>();
        await foreach (var record in PageAsync(filter, new FilteredRecordRetrievalOptions<MemoryRecord> { IncludeVectors = !hardDelete }, cancellationToken).ConfigureAwait(false))
            records.Add(record);

        await DeleteRecordsAsync(records, hardDelete, cancellationToken).ConfigureAwait(false);
        LogDeletedMany(_logger, hardDelete ? "Hard" : "Soft", records.Count);
        return records.Count;
    }

    private async Task DeleteRecordsAsync(List<MemoryRecord> records, bool hardDelete, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return;

        if (hardDelete)
        {
            await _collection.DeleteAsync(records.Select(r => r.Id), cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = MemoryRecord.Ticks(DateTime.UtcNow);
        foreach (var record in records)
        {
            record.IsDeleted = true;
            record.UpdatedAtTicks = now;
        }

        await _collection.UpsertAsync(records, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored memory {MemoryId} for user {UserId}")]
    private static partial void LogStored(ILogger logger, Guid memoryId, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated memory {MemoryId}")]
    private static partial void LogUpdated(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{DeleteType} deleted {Count} memories")]
    private static partial void LogDeletedMany(ILogger logger, string deleteType, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search returned {Count} results")]
    private static partial void LogSearched(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted memory collection {Collection}")]
    private static partial void LogCollectionDeleted(ILogger logger, string collection);
}
