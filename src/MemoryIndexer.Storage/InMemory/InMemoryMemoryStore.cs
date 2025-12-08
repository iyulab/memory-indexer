using System.Collections.Concurrent;
using System.Numerics.Tensors;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Storage.InMemory;

/// <summary>
/// In-memory implementation of IMemoryStore.
/// Useful for development and testing.
/// </summary>
public sealed class InMemoryMemoryStore(ILogger<InMemoryMemoryStore> logger) : IMemoryStore
{
    private readonly ConcurrentDictionary<Guid, MemoryUnit> _memories = new();

    /// <inheritdoc />
    public Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        memory.Id = memory.Id == Guid.Empty ? Guid.NewGuid() : memory.Id;
        memory.CreatedAt = DateTime.UtcNow;
        memory.UpdatedAt = DateTime.UtcNow;

        _memories[memory.Id] = memory;
        logger.LogDebug("Stored memory {MemoryId} for user {UserId}", memory.Id, memory.UserId);

        return Task.FromResult(memory);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(
        IEnumerable<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MemoryUnit>();
        foreach (var memory in memories)
        {
            results.Add(await StoreAsync(memory, cancellationToken));
        }
        return results;
    }

    /// <inheritdoc />
    public Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _memories.TryGetValue(id, out var memory);
        return Task.FromResult(memory);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var results = ids
            .Select(id => _memories.TryGetValue(id, out var m) ? m : null)
            .Where(m => m is not null)
            .Cast<MemoryUnit>()
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryUnit>>(results);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        if (!_memories.ContainsKey(memory.Id))
            return Task.FromResult(false);

        memory.UpdatedAt = DateTime.UtcNow;
        _memories[memory.Id] = memory;
        logger.LogDebug("Updated memory {MemoryId}", memory.Id);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        if (hardDelete)
        {
            var removed = _memories.TryRemove(id, out _);
            if (removed)
                logger.LogDebug("Hard deleted memory {MemoryId}", id);
            return Task.FromResult(removed);
        }

        if (_memories.TryGetValue(id, out var memory))
        {
            memory.IsDeleted = true;
            memory.UpdatedAt = DateTime.UtcNow;
            logger.LogDebug("Soft deleted memory {MemoryId}", id);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var query = _memories.Values.AsEnumerable();

        // Apply filters
        if (!string.IsNullOrEmpty(options.UserId))
            query = query.Where(m => m.UserId == options.UserId);

        if (!string.IsNullOrEmpty(options.SessionId))
            query = query.Where(m => m.SessionId == options.SessionId);

        if (options.Types is { Length: > 0 })
            query = query.Where(m => options.Types.Contains(m.Type));

        if (options.CreatedAfter.HasValue)
            query = query.Where(m => m.CreatedAt >= options.CreatedAfter.Value);

        if (options.CreatedBefore.HasValue)
            query = query.Where(m => m.CreatedAt <= options.CreatedBefore.Value);

        if (!options.IncludeDeleted)
            query = query.Where(m => !m.IsDeleted);

        // Calculate similarity scores
        var results = query
            .Where(m => m.Embedding.HasValue)
            .Select(m => new
            {
                Memory = m,
                Score = CalculateCosineSimilarity(queryEmbedding, m.Embedding!.Value)
            })
            .Where(r => r.Score >= options.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(options.Limit)
            .Select(r => new MemorySearchResult
            {
                Memory = r.Memory,
                Score = r.Score
            })
            .ToList();

        logger.LogDebug("Search returned {Count} results", results.Count);
        return Task.FromResult<IReadOnlyList<MemorySearchResult>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var query = _memories.Values
            .Where(m => m.UserId == userId);

        if (options is not null)
        {
            if (!string.IsNullOrEmpty(options.SessionId))
                query = query.Where(m => m.SessionId == options.SessionId);

            if (options.Types is { Length: > 0 })
                query = query.Where(m => options.Types.Contains(m.Type));

            if (options.CreatedAfter.HasValue)
                query = query.Where(m => m.CreatedAt >= options.CreatedAfter.Value);

            if (options.CreatedBefore.HasValue)
                query = query.Where(m => m.CreatedAt <= options.CreatedBefore.Value);

            if (!options.IncludeDeleted)
                query = query.Where(m => !m.IsDeleted);

            query = options.OrderBy switch
            {
                MemoryOrderBy.CreatedAtAsc => query.OrderBy(m => m.CreatedAt),
                MemoryOrderBy.UpdatedAtDesc => query.OrderByDescending(m => m.UpdatedAt),
                MemoryOrderBy.UpdatedAtAsc => query.OrderBy(m => m.UpdatedAt),
                MemoryOrderBy.ImportanceDesc => query.OrderByDescending(m => m.ImportanceScore),
                MemoryOrderBy.AccessCountDesc => query.OrderByDescending(m => m.AccessCount),
                _ => query.OrderByDescending(m => m.CreatedAt)
            };

            if (options.Skip > 0)
                query = query.Skip(options.Skip);

            if (options.Limit.HasValue)
                query = query.Take(options.Limit.Value);
        }
        else
        {
            query = query.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt);
        }

        return Task.FromResult<IReadOnlyList<MemoryUnit>>(query.ToList());
    }

    /// <inheritdoc />
    public Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var count = _memories.Values.Count(m => m.UserId == userId && !m.IsDeleted);
        return Task.FromResult((long)count);
    }

    /// <inheritdoc />
    public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        // No-op for in-memory store
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
    {
        _memories.Clear();
        logger.LogInformation("Cleared all memories from in-memory store");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
    private static float CalculateCosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        if (spanA.Length != spanB.Length)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(spanA, spanB);
        var normA = TensorPrimitives.Norm(spanA);
        var normB = TensorPrimitives.Norm(spanB);

        if (normA == 0 || normB == 0)
            return 0f;

        return dotProduct / (normA * normB);
    }
}
