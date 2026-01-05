using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of L1 Working Memory using IMemoryCache.
/// Manages fast, limited-capacity in-context memory.
/// </summary>
/// <remarks>
/// Research reference: research-03.md, research-04.md
/// - Capacity: 4-7 chunks (Baddeley's Working Memory Model)
/// - Eviction: Least relevant first
/// - Access tracking for relevance updates
/// </remarks>
public sealed class WorkingMemoryService : IWorkingMemory
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<Guid, WorkingMemoryEntry> _entries;
    private readonly WorkingMemoryOptions _options;
    private readonly object _lock = new();

    public WorkingMemoryService(
        IMemoryCache cache,
        IOptions<WorkingMemoryOptions> options)
    {
        _cache = cache;
        _options = options.Value;
        _entries = new ConcurrentDictionary<Guid, WorkingMemoryEntry>();
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public int Capacity => _options.Capacity;

    /// <inheritdoc />
    public Task<MemoryUnit?> PromoteAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        cancellationToken.ThrowIfCancellationRequested();

        MemoryUnit? evicted = null;

        lock (_lock)
        {
            // If already in working memory, just update
            if (_entries.ContainsKey(memory.Id))
            {
                var entry = _entries[memory.Id];
                entry.Memory = memory;
                entry.LastAccessed = DateTime.UtcNow;
                entry.RelevanceScore = Math.Min(1.0f, entry.RelevanceScore + 0.1f);
                UpdateCache(memory.Id, entry);
                return Task.FromResult<MemoryUnit?>(null);
            }

            // If at capacity, evict lowest relevance
            if (Count >= Capacity)
            {
                evicted = EvictLowestRelevance();
            }

            // Add to working memory
            var newEntry = new WorkingMemoryEntry
            {
                Memory = memory,
                PromotedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                RelevanceScore = memory.ImportanceScore
            };

            _entries[memory.Id] = newEntry;
            UpdateCache(memory.Id, newEntry);

            // Update memory tier
            memory.Tier = MemoryTier.Working;
        }

        return Task.FromResult(evicted);
    }

    /// <inheritdoc />
    public Task<MemoryUnit?> DemoteAsync(Guid memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_entries.TryRemove(memoryId, out var entry))
            {
                _cache.Remove(GetCacheKey(memoryId));
                entry.Memory.Tier = MemoryTier.Session;
                return Task.FromResult<MemoryUnit?>(entry.Memory);
            }
        }

        return Task.FromResult<MemoryUnit?>(null);
    }

    /// <inheritdoc />
    public Task<MemoryUnit?> GetAsync(Guid memoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_entries.TryGetValue(memoryId, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            return Task.FromResult<MemoryUnit?>(entry.Memory);
        }

        return Task.FromResult<MemoryUnit?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryUnit>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memories = _entries.Values
            .OrderByDescending(e => e.RelevanceScore)
            .ThenByDescending(e => e.LastAccessed)
            .Select(e => e.Memory)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryUnit>>(memories);
    }

    /// <inheritdoc />
    public bool Contains(Guid memoryId) => _entries.ContainsKey(memoryId);

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryUnit>> ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<MemoryUnit> cleared;

        lock (_lock)
        {
            cleared = _entries.Values.Select(e => e.Memory).ToList();

            foreach (var id in _entries.Keys)
            {
                _cache.Remove(GetCacheKey(id));
            }

            _entries.Clear();
        }

        // Set tier back to Session for cleared memories
        foreach (var memory in cleared)
        {
            memory.Tier = MemoryTier.Session;
        }

        return Task.FromResult<IReadOnlyList<MemoryUnit>>(cleared);
    }

    /// <inheritdoc />
    public Task TouchAsync(Guid memoryId, float relevanceBoost = 0.1f, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_entries.TryGetValue(memoryId, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            entry.RelevanceScore = Math.Clamp(entry.RelevanceScore + relevanceBoost, 0f, 1f);
            entry.Memory.RecordAccess();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MemoryUnit?> GetEvictionCandidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidate = _entries.Values
            .Where(e => !e.Memory.IsLocked)
            .OrderBy(e => e.RelevanceScore)
            .ThenBy(e => e.LastAccessed)
            .FirstOrDefault();

        return Task.FromResult(candidate?.Memory);
    }

    private MemoryUnit? EvictLowestRelevance()
    {
        var candidate = _entries.Values
            .Where(e => !e.Memory.IsLocked)
            .OrderBy(e => e.RelevanceScore)
            .ThenBy(e => e.LastAccessed)
            .FirstOrDefault();

        if (candidate != null && _entries.TryRemove(candidate.Memory.Id, out _))
        {
            _cache.Remove(GetCacheKey(candidate.Memory.Id));
            candidate.Memory.Tier = MemoryTier.Session;
            return candidate.Memory;
        }

        return null;
    }

    private void UpdateCache(Guid id, WorkingMemoryEntry entry)
    {
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(_options.SlidingExpiration)
            .SetAbsoluteExpiration(_options.AbsoluteExpiration)
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                if (reason != EvictionReason.Replaced)
                {
                    _entries.TryRemove(id, out _);
                }
            });

        _cache.Set(GetCacheKey(id), entry, cacheOptions);
    }

    private static string GetCacheKey(Guid id) => $"wm:{id}";

    private sealed class WorkingMemoryEntry
    {
        public required MemoryUnit Memory { get; set; }
        public DateTime PromotedAt { get; init; }
        public DateTime LastAccessed { get; set; }
        public float RelevanceScore { get; set; }
    }
}

/// <summary>
/// Configuration options for Working Memory.
/// </summary>
public sealed class WorkingMemoryOptions
{
    /// <summary>
    /// Maximum number of items in working memory.
    /// Default: 7 (Baddeley's Working Memory Model).
    /// </summary>
    public int Capacity { get; set; } = 7;

    /// <summary>
    /// Sliding expiration for cache entries.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Absolute expiration for cache entries.
    /// </summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromHours(2);
}
