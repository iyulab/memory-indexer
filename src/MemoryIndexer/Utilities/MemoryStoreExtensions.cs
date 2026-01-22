using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

namespace MemoryIndexer.Utilities;

/// <summary>
/// Extension methods and helper utilities for IMemoryStore implementations.
/// Provides common boilerplate logic that custom storage implementations can reuse.
/// </summary>
/// <remarks>
/// These extensions are designed to reduce implementation effort for custom IMemoryStore
/// providers (e.g., PostgreSQL, Qdrant, Redis) while maintaining full flexibility.
///
/// Usage in custom implementations:
/// <code>
/// public async Task&lt;MemoryUnit&gt; StoreAsync(MemoryUnit memory, CancellationToken ct)
/// {
///     memory.PrepareForStore();           // Sets Id, CreatedAt, UpdatedAt
///     memory.ValidateForStore();          // Throws if invalid
///     // ... your storage logic
/// }
/// </code>
/// </remarks>
public static class MemoryStoreExtensions
{
    /// <summary>
    /// Prepares a MemoryUnit for storage by setting default values.
    /// Call this before persisting to ensure consistent Id and timestamps.
    /// </summary>
    /// <param name="memory">The memory to prepare.</param>
    /// <returns>The same memory instance for chaining.</returns>
    public static MemoryUnit PrepareForStore(this MemoryUnit memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        memory.Id = memory.Id == Guid.Empty ? Guid.NewGuid() : memory.Id;
        memory.CreatedAt = memory.CreatedAt == default ? DateTime.UtcNow : memory.CreatedAt;
        memory.UpdatedAt = DateTime.UtcNow;

        return memory;
    }

    /// <summary>
    /// Validates that a MemoryUnit has required fields before storage.
    /// Throws ArgumentException if validation fails.
    /// </summary>
    /// <param name="memory">The memory to validate.</param>
    /// <exception cref="ArgumentNullException">If memory is null.</exception>
    /// <exception cref="ArgumentException">If UserId or Content is null/empty.</exception>
    public static void ValidateForStore(this MemoryUnit memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (string.IsNullOrWhiteSpace(memory.UserId))
        {
            throw new ArgumentException("UserId is required", nameof(memory));
        }

        if (string.IsNullOrWhiteSpace(memory.Content))
        {
            throw new ArgumentException("Content is required", nameof(memory));
        }
    }

    /// <summary>
    /// Validates search options for common issues.
    /// </summary>
    /// <param name="options">The search options to validate.</param>
    /// <exception cref="ArgumentNullException">If options is null.</exception>
    /// <exception cref="ArgumentException">If Limit is less than 1.</exception>
    public static void ValidateSearchOptions(this MemorySearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Limit < 1)
        {
            throw new ArgumentException("Limit must be at least 1", nameof(options));
        }
    }

    /// <summary>
    /// Default implementation of StoreBatchAsync that iterates over StoreAsync.
    /// Override in your implementation if your storage supports batch operations natively.
    /// </summary>
    /// <param name="store">The memory store.</param>
    /// <param name="memories">The memories to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memories.</returns>
    /// <remarks>
    /// This is a convenience method for simple implementations. For production use
    /// with high throughput requirements, implement batch operations natively in your
    /// storage provider (e.g., using transactions or bulk insert).
    /// </remarks>
    public static async Task<IReadOnlyList<MemoryUnit>> StoreBatchDefaultAsync(
        this IMemoryStore store,
        IEnumerable<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MemoryUnit>();
        foreach (var memory in memories)
        {
            results.Add(await store.StoreAsync(memory, cancellationToken));
        }
        return results;
    }

    /// <summary>
    /// Default implementation of GetByIdsAsync that iterates over GetByIdAsync.
    /// Override in your implementation if your storage supports multi-get natively.
    /// </summary>
    /// <param name="store">The memory store.</param>
    /// <param name="ids">The IDs to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The found memories.</returns>
    /// <remarks>
    /// This is a convenience method for simple implementations. For production use,
    /// implement multi-get natively (e.g., SQL IN clause, batch GET).
    /// </remarks>
    public static async Task<IReadOnlyList<MemoryUnit>> GetByIdsDefaultAsync(
        this IMemoryStore store,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MemoryUnit>();
        foreach (var id in ids)
        {
            var memory = await store.GetByIdAsync(id, cancellationToken);
            if (memory != null)
            {
                results.Add(memory);
            }
        }
        return results;
    }

    /// <summary>
    /// Checks if a memory with the same ContentHash already exists.
    /// Useful for deduplication before storage.
    /// </summary>
    /// <param name="memory">The memory to check.</param>
    /// <param name="existingMemories">Existing memories to compare against.</param>
    /// <returns>True if a duplicate exists.</returns>
    public static bool HasDuplicateHash(this MemoryUnit memory, IEnumerable<MemoryUnit> existingMemories)
    {
        if (string.IsNullOrEmpty(memory.ContentHash))
        {
            return false;
        }

        return existingMemories.Any(m =>
            m.ContentHash == memory.ContentHash &&
            m.Id != memory.Id);
    }

    /// <summary>
    /// Applies common filter logic to a collection of memories.
    /// Useful for in-memory implementations or post-query filtering.
    /// </summary>
    /// <param name="memories">The memories to filter.</param>
    /// <param name="options">Filter options.</param>
    /// <returns>Filtered memories.</returns>
    public static IEnumerable<MemoryUnit> ApplyFilter(
        this IEnumerable<MemoryUnit> memories,
        MemoryFilterOptions? options)
    {
        if (options == null)
        {
            return memories.Where(m => !m.IsDeleted);
        }

        var query = memories.AsEnumerable();

        if (!string.IsNullOrEmpty(options.SessionId))
        {
            query = query.Where(m => m.SessionId == options.SessionId);
        }

        if (options.Types is { Length: > 0 })
        {
            query = query.Where(m => options.Types.Contains(m.Type));
        }

        if (options.Roles is { Length: > 0 })
        {
            query = query.Where(m => m.Role != null && options.Roles.Contains(m.Role));
        }

        if (options.Tiers is { Length: > 0 })
        {
            query = query.Where(m => options.Tiers.Contains(m.Tier));
        }

        if (options.Scopes is { Length: > 0 })
        {
            query = query.Where(m => options.Scopes.Contains(m.Scope));
        }

        if (options.CreatedAfter.HasValue)
        {
            query = query.Where(m => m.CreatedAt >= options.CreatedAfter.Value);
        }

        if (options.CreatedBefore.HasValue)
        {
            query = query.Where(m => m.CreatedAt <= options.CreatedBefore.Value);
        }

        if (!options.IncludeDeleted)
        {
            query = query.Where(m => !m.IsDeleted);
        }

        // Metadata filter (Phase 28)
        if (options.MetadataFilter is { Count: > 0 })
        {
            foreach (var kvp in options.MetadataFilter)
            {
                query = query.Where(m =>
                    m.Metadata != null &&
                    m.Metadata.TryGetValue(kvp.Key, out var value) &&
                    value == kvp.Value);
            }
        }

        // Ordering
        query = options.OrderBy switch
        {
            MemoryOrderBy.CreatedAtAsc => query.OrderBy(m => m.CreatedAt),
            MemoryOrderBy.UpdatedAtDesc => query.OrderByDescending(m => m.UpdatedAt),
            MemoryOrderBy.UpdatedAtAsc => query.OrderBy(m => m.UpdatedAt),
            MemoryOrderBy.ImportanceDesc => query.OrderByDescending(m => m.ImportanceScore),
            MemoryOrderBy.AccessCountDesc => query.OrderByDescending(m => m.AccessCount),
            _ => query.OrderByDescending(m => m.CreatedAt)
        };

        // Pagination
        if (options.Skip > 0)
        {
            query = query.Skip(options.Skip);
        }

        if (options.Limit.HasValue)
        {
            query = query.Take(options.Limit.Value);
        }

        return query;
    }

    /// <summary>
    /// Applies search filter logic to a collection of memories.
    /// Useful for pre-filtering before vector similarity calculation.
    /// </summary>
    /// <param name="memories">The memories to filter.</param>
    /// <param name="options">Search options.</param>
    /// <returns>Filtered memories.</returns>
    public static IEnumerable<MemoryUnit> ApplySearchFilter(
        this IEnumerable<MemoryUnit> memories,
        MemorySearchOptions options)
    {
        var query = memories.AsEnumerable();

        if (!string.IsNullOrEmpty(options.UserId))
        {
            query = query.Where(m => m.UserId == options.UserId);
        }

        if (!string.IsNullOrEmpty(options.SessionId))
        {
            query = query.Where(m => m.SessionId == options.SessionId);
        }

        if (options.Types is { Length: > 0 })
        {
            query = query.Where(m => options.Types.Contains(m.Type));
        }

        if (options.Roles is { Length: > 0 })
        {
            query = query.Where(m => m.Role != null && options.Roles.Contains(m.Role));
        }

        if (options.CreatedAfter.HasValue)
        {
            query = query.Where(m => m.CreatedAt >= options.CreatedAfter.Value);
        }

        if (options.CreatedBefore.HasValue)
        {
            query = query.Where(m => m.CreatedAt <= options.CreatedBefore.Value);
        }

        if (!options.IncludeDeleted)
        {
            query = query.Where(m => !m.IsDeleted);
        }

        // Metadata filter (Phase 28)
        if (options.MetadataFilter is { Count: > 0 })
        {
            foreach (var kvp in options.MetadataFilter)
            {
                query = query.Where(m =>
                    m.Metadata != null &&
                    m.Metadata.TryGetValue(kvp.Key, out var value) &&
                    value == kvp.Value);
            }
        }

        return query;
    }

    /// <summary>
    /// Calculates cosine similarity search results from pre-filtered memories.
    /// Useful for implementations that don't have native vector search.
    /// </summary>
    /// <param name="memories">Pre-filtered memories with embeddings.</param>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <returns>Sorted search results.</returns>
    public static IReadOnlyList<MemorySearchResult> CalculateSimilarityResults(
        this IEnumerable<MemoryUnit> memories,
        ReadOnlyMemory<float> queryEmbedding,
        float minScore = 0f,
        int limit = 10)
    {
        return memories
            .Where(m => m.Embedding.HasValue)
            .Select(m => new MemorySearchResult
            {
                Memory = m,
                Score = VectorMath.CosineSimilarity(queryEmbedding, m.Embedding!.Value)
            })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }
}
