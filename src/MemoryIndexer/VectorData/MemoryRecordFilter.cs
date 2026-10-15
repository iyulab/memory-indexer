using System.Linq.Expressions;
using System.Reflection;
using MemoryIndexer.Models;

namespace MemoryIndexer.VectorData;

/// <summary>
/// Builds the filter <see cref="VectorStoreMemoryStore"/> sends to the database, from constants only and from the operators
/// every Microsoft.Extensions.VectorData connector translates: <c>==</c>, <c>&gt;=</c>, <c>&lt;=</c>, <c>&amp;&amp;</c>,
/// <c>||</c> and <c>Contains</c> on a string list.
/// </summary>
internal static class MemoryRecordFilter
{
    private static readonly ParameterExpression s_record = Expression.Parameter(typeof(MemoryRecord), "r");
    private static readonly MethodInfo s_listContains = typeof(List<string>).GetMethod(nameof(List<string>.Contains), [typeof(string)])!;

    public static Expression<Func<MemoryRecord, bool>> For(
        string userId,
        string? sessionId,
        string? @namespace,
        MemoryType[]? types,
        string[]? roles,
        Tier[]? tiers,
        Scope[]? scopes,
        DateTime? createdAfter,
        DateTime? createdBefore,
        bool includeDeleted,
        IReadOnlyDictionary<string, string>? metadata,
        bool embeddedOnly = false)
    {
        var body = Equal(nameof(MemoryRecord.UserId), userId);

        if (embeddedOnly)
            body = Expression.AndAlso(body, Expression.Equal(Property(nameof(MemoryRecord.HasEmbedding)), Expression.Constant(true)));

        if (!string.IsNullOrEmpty(sessionId))
            body = Expression.AndAlso(body, Equal(nameof(MemoryRecord.SessionId), sessionId));
        if (!string.IsNullOrEmpty(@namespace))
            body = Expression.AndAlso(body, Equal(nameof(MemoryRecord.Namespace), @namespace));
        if (types is { Length: > 0 })
            body = Expression.AndAlso(body, AnyOf(nameof(MemoryRecord.Type), types.Select(t => t.ToString())));
        if (roles is { Length: > 0 })
            body = Expression.AndAlso(body, AnyOf(nameof(MemoryRecord.Role), roles));
        if (tiers is { Length: > 0 })
            body = Expression.AndAlso(body, AnyOf(nameof(MemoryRecord.Tier), tiers.Select(t => t.ToString())));
        if (scopes is { Length: > 0 })
            body = Expression.AndAlso(body, AnyOf(nameof(MemoryRecord.Scope), scopes.Select(s => s.ToString())));
        if (createdAfter is { } after)
            body = Expression.AndAlso(body, Expression.GreaterThanOrEqual(Property(nameof(MemoryRecord.CreatedAtTicks)), Expression.Constant(MemoryRecord.Ticks(after))));
        if (createdBefore is { } before)
            body = Expression.AndAlso(body, Expression.LessThanOrEqual(Property(nameof(MemoryRecord.CreatedAtTicks)), Expression.Constant(MemoryRecord.Ticks(before))));
        if (!includeDeleted)
            body = Expression.AndAlso(body, Expression.Equal(Property(nameof(MemoryRecord.IsDeleted)), Expression.Constant(false)));
        if (metadata is { Count: > 0 })
        {
            foreach (var (key, value) in metadata)
            {
                var pairs = Property(nameof(MemoryRecord.MetadataPairs));
                body = Expression.AndAlso(body, Expression.Call(pairs, s_listContains, Expression.Constant(MemoryRecord.MetadataPair(key, value))));
            }
        }

        return Expression.Lambda<Func<MemoryRecord, bool>>(body, s_record);
    }

    /// <summary>A user's records, optionally one session or namespace, with or without the soft-deleted ones.</summary>
    public static Expression<Func<MemoryRecord, bool>> ForUser(
        string userId, bool includeDeleted, string? sessionId = null, string? @namespace = null)
        => For(userId, sessionId, @namespace, types: null, roles: null, tiers: null, scopes: null,
            createdAfter: null, createdBefore: null, includeDeleted, metadata: null);

    /// <summary>Every record not soft-deleted, whoever owns it.</summary>
    public static Expression<Func<MemoryRecord, bool>> NotDeleted()
        => Expression.Lambda<Func<MemoryRecord, bool>>(
            Expression.Equal(Property(nameof(MemoryRecord.IsDeleted)), Expression.Constant(false)), s_record);

    private static MemberExpression Property(string name) => Expression.Property(s_record, name);

    private static BinaryExpression Equal(string property, string value)
        => Expression.Equal(Property(property), Expression.Constant(value, typeof(string)));

    private static Expression AnyOf(string property, IEnumerable<string> values)
    {
        Expression? any = null;
        foreach (var value in values.Distinct(StringComparer.Ordinal))
        {
            var one = Equal(property, value);
            any = any is null ? one : Expression.OrElse(any, one);
        }

        return any ?? Expression.Constant(false);
    }
}
