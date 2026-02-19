using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of L1 Short-Term Memory using IMemoryCache.
/// Manages fast, limited-capacity in-context memory with memory-pressure aware eviction.
/// </summary>
/// <remarks>
/// Research reference: research-03.md, research-04.md
/// - Capacity: 4-7 chunks (Baddeley's Working Memory Model)
/// - Eviction: Least relevant first, memory-pressure aware
/// - Access tracking for relevance updates
/// - Adaptive eviction based on system memory pressure
/// </remarks>
public sealed class ShortTermMemoryService : IShortTermMemory
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<Guid, WorkingMemoryEntry> _entries;
    private readonly WorkingMemoryOptions _options;
    private readonly IMemoryPressureMonitor? _pressureMonitor;
    private readonly object _lock = new();

    public ShortTermMemoryService(
        IMemoryCache cache,
        IOptions<WorkingMemoryOptions> options,
        IMemoryPressureMonitor? pressureMonitor = null)
    {
        _cache = cache;
        _options = options.Value;
        _pressureMonitor = pressureMonitor;
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
            if (_entries.TryGetValue(memory.Id, out var entry))
            {
                entry.Memory = memory;
                entry.LastAccessed = DateTime.UtcNow;
                entry.RelevanceScore = Math.Min(1.0f, entry.RelevanceScore + 0.1f);
                UpdateCache(memory.Id, entry);
                return Task.FromResult<MemoryUnit?>(null);
            }

            // Memory-pressure aware capacity management
            var effectiveCapacity = GetEffectiveCapacity();

            // If at capacity, evict lowest relevance
            if (Count >= effectiveCapacity)
            {
                evicted = EvictLowestRelevance();

                // Under high pressure, proactively evict additional items
                if (_pressureMonitor?.IsUnderPressure(MemoryPressureLevel.High) == true)
                {
                    // Evict one more item to free up space
                    while (Count >= effectiveCapacity - 1)
                    {
                        var additional = EvictLowestRelevance();
                        if (additional == null) break;
                    }
                }
            }

            // Add to working memory
            var newEntry = new WorkingMemoryEntry
            {
                Memory = memory,
                PromotedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow,
                RelevanceScore = memory.ImportanceScore,
                OriginalEmbedding = memory.Embedding // Store original for restoration
            };

            // Lazy embedding loading: Clear embedding from Short-Term Memory to save space
            // Embeddings are only needed for search, not for in-context usage
            if (_options.LazyEmbeddingLoading && memory.Embedding.HasValue)
            {
                memory.Embedding = null;
            }

            _entries[memory.Id] = newEntry;
            UpdateCache(memory.Id, newEntry);

            // Update memory tier
            memory.Tier = Tier.Short;
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

                // Restore original embedding if it was cleared for lazy loading
                if (_options.LazyEmbeddingLoading && entry.OriginalEmbedding.HasValue)
                {
                    entry.Memory.Embedding = entry.OriginalEmbedding;
                }

                entry.Memory.Tier = Tier.Long;
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
            memory.Tier = Tier.Long;
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
            candidate.Memory.Tier = Tier.Long;
            return candidate.Memory;
        }

        return null;
    }

    /// <summary>
    /// Gets effective capacity based on memory pressure.
    /// Under memory pressure, reduces capacity to free up resources.
    /// </summary>
    private int GetEffectiveCapacity()
    {
        if (_pressureMonitor == null) return Capacity;

        var pressure = _pressureMonitor.CurrentPressure;

        return pressure switch
        {
            MemoryPressureLevel.Critical => Math.Max(1, Capacity / 2), // 50% reduction
            MemoryPressureLevel.High => Math.Max(2, Capacity * 2 / 3), // 33% reduction
            MemoryPressureLevel.Medium => Math.Max(3, Capacity * 4 / 5), // 20% reduction
            _ => Capacity // Normal capacity
        };
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
        public ReadOnlyMemory<float>? OriginalEmbedding { get; set; }
    }
}

/// <summary>
/// Configuration options for Short-Term Memory.
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

    /// <summary>
    /// Enable lazy embedding loading to reduce memory footprint.
    /// When true, embeddings are cleared from Short-Term Memory and restored only when needed.
    /// Memory savings: ~3KB per memory unit (768 floats × 4 bytes).
    /// </summary>
    public bool LazyEmbeddingLoading { get; set; }
}
