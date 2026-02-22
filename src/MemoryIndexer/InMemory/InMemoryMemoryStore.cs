using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Utilities;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.InMemory;

/// <summary>
/// In-memory implementation of IMemoryStore.
/// Useful for development and testing.
/// </summary>
public sealed partial class InMemoryMemoryStore(ILogger<InMemoryMemoryStore> logger) : IMemoryStore
{
    private readonly ConcurrentDictionary<Guid, MemoryUnit> _memories = new();

    /// <inheritdoc />
    public Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        memory.Id = memory.Id == Guid.Empty ? Guid.NewGuid() : memory.Id;
        memory.CreatedAt = memory.CreatedAt == default ? DateTime.UtcNow : memory.CreatedAt;
        memory.UpdatedAt = memory.UpdatedAt == default ? DateTime.UtcNow : memory.UpdatedAt;

        _memories[memory.Id] = memory;
        LogStoredMemory(logger, memory.Id, memory.UserId);

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
        LogUpdatedMemory(logger, memory.Id);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        if (hardDelete)
        {
            var removed = _memories.TryRemove(id, out _);
            if (removed)
                LogHardDeletedMemory(logger, id);
            return Task.FromResult(removed);
        }

        if (_memories.TryGetValue(id, out var memory))
        {
            memory.IsDeleted = true;
            memory.UpdatedAt = DateTime.UtcNow;
            LogSoftDeletedMemory(logger, id);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<int> DeleteByUserAsync(string userId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        var toDelete = _memories.Values.Where(m => m.UserId == userId).ToList();
        var count = 0;

        foreach (var memory in toDelete)
        {
            if (hardDelete)
            {
                if (_memories.TryRemove(memory.Id, out _))
                    count++;
            }
            else
            {
                memory.IsDeleted = true;
                memory.UpdatedAt = DateTime.UtcNow;
                count++;
            }
        }

        LogDeletedMemoriesForUser(logger, hardDelete ? "Hard" : "Soft", count, userId);
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> DeleteBySessionAsync(string userId, string sessionId, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        var toDelete = _memories.Values
            .Where(m => m.UserId == userId && m.SessionId == sessionId)
            .ToList();
        var count = 0;

        foreach (var memory in toDelete)
        {
            if (hardDelete)
            {
                if (_memories.TryRemove(memory.Id, out _))
                    count++;
            }
            else
            {
                memory.IsDeleted = true;
                memory.UpdatedAt = DateTime.UtcNow;
                count++;
            }
        }

        LogDeletedMemoriesForSession(logger, hardDelete ? "Hard" : "Soft", count, userId, sessionId);
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> DeleteByNamespaceAsync(string userId, string namespaceName, bool hardDelete = false, CancellationToken cancellationToken = default)
    {
        var matches = _memories.Values
            .Where(m => m.UserId == userId && m.Namespace == namespaceName && !m.IsDeleted)
            .ToList();

        var count = 0;
        foreach (var memory in matches)
        {
            if (hardDelete)
            {
                if (_memories.TryRemove(memory.Id, out _))
                    count++;
            }
            else
            {
                memory.IsDeleted = true;
                memory.UpdatedAt = DateTime.UtcNow;
                count++;
            }
        }

        LogDeletedMemoriesForNamespace(logger, hardDelete ? "Hard" : "Soft", count, userId, namespaceName);
        return Task.FromResult(count);
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

        if (!string.IsNullOrEmpty(options.Namespace))
            query = query.Where(m => m.Namespace == options.Namespace);

        if (options.Types is { Length: > 0 })
            query = query.Where(m => options.Types.Contains(m.Type));

        if (options.Roles is { Length: > 0 })
            query = query.Where(m => m.Role != null && options.Roles.Contains(m.Role));

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
                Score = VectorMath.CosineSimilarity(queryEmbedding, m.Embedding!.Value)
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

        LogSearchResults(logger, results.Count);
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

            if (!string.IsNullOrEmpty(options.Namespace))
                query = query.Where(m => m.Namespace == options.Namespace);

            if (options.Types is { Length: > 0 })
                query = query.Where(m => options.Types.Contains(m.Type));

            if (options.Roles is { Length: > 0 })
                query = query.Where(m => m.Role != null && options.Roles.Contains(m.Role));

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
    public Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var counts = _memories.Values
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .GroupBy(m => m.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<MemoryType, int>>(counts);
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
        LogClearedAllMemories(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored memory {MemoryId} for user {UserId}")]
    private static partial void LogStoredMemory(ILogger logger, Guid memoryId, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated memory {MemoryId}")]
    private static partial void LogUpdatedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hard deleted memory {MemoryId}")]
    private static partial void LogHardDeletedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Soft deleted memory {MemoryId}")]
    private static partial void LogSoftDeletedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{DeleteType} deleted {Count} memories for user {UserId}")]
    private static partial void LogDeletedMemoriesForUser(ILogger logger, string deleteType, int count, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{DeleteType} deleted {Count} memories for user {UserId} session {SessionId}")]
    private static partial void LogDeletedMemoriesForSession(ILogger logger, string deleteType, int count, string userId, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{DeleteType} deleted {Count} memories for user {UserId} namespace {Namespace}")]
    private static partial void LogDeletedMemoriesForNamespace(ILogger logger, string deleteType, int count, string userId, string @namespace);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search returned {Count} results")]
    private static partial void LogSearchResults(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared all memories from in-memory store")]
    private static partial void LogClearedAllMemories(ILogger logger);
}
