using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Embedding service decorator with LRU cache.
/// Phase 22.2: Recall Latency Optimization
/// </summary>
public sealed class CachedEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly ILatencyProfiler? _profiler;
    private readonly ILogger<CachedEmbeddingService> _logger;
    private readonly LatencyOptions _options;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lruLock = new();

    public CachedEmbeddingService(
        IEmbeddingService inner,
        ILatencyProfiler? profiler,
        ILogger<CachedEmbeddingService> logger,
        IOptions<MemoryIndexerOptions> options)
    {
        _inner = inner;
        _profiler = profiler;
        _logger = logger;
        _options = options.Value.Latency;
    }

    public int Dimensions => _inner.Dimensions;

    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EmbeddingCacheEnabled)
        {
            return await _inner.GenerateEmbeddingAsync(text, cancellationToken);
        }

        var cacheKey = ComputeHash(text);

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            // Check TTL
            if (DateTime.UtcNow - entry.Timestamp < TimeSpan.FromMinutes(_options.EmbeddingCacheTtlMinutes))
            {
                // Cache hit - update LRU position
                UpdateLru(cacheKey);

                if (_profiler != null)
                {
                    await _profiler.RecordCacheAccessAsync("system", "Embedding", hit: true, cancellationToken);
                }

                _logger.LogDebug("Embedding cache hit for key {CacheKey}", cacheKey);
                return entry.Embedding;
            }
            else
            {
                // Expired - remove from cache
                _cache.TryRemove(cacheKey, out _);
                RemoveFromLru(cacheKey);
            }
        }

        // Cache miss - generate embedding
        if (_profiler != null)
        {
            await _profiler.RecordCacheAccessAsync("system", "Embedding", hit: false, cancellationToken);
        }

        _logger.LogDebug("Embedding cache miss for key {CacheKey}", cacheKey);
        var embedding = await _inner.GenerateEmbeddingAsync(text, cancellationToken);

        // Add to cache with LRU eviction
        AddToCache(cacheKey, embedding);

        return embedding;
    }

    private void AddToCache(string key, ReadOnlyMemory<float> embedding)
    {
        lock (_lruLock)
        {
            // Evict if cache is full
            if (_cache.Count >= _options.EmbeddingCacheSize)
            {
                // Remove least recently used (tail of LRU list)
                if (_lruList.Last != null)
                {
                    var evictKey = _lruList.Last.Value;
                    _lruList.RemoveLast();
                    _cache.TryRemove(evictKey, out _);
                    _logger.LogDebug("Evicted embedding cache entry {EvictKey}", evictKey);
                }
            }

            // Add to cache and LRU head
            _cache[key] = new CacheEntry
            {
                Embedding = embedding,
                Timestamp = DateTime.UtcNow
            };
            _lruList.AddFirst(key);
        }
    }

    private void UpdateLru(string key)
    {
        lock (_lruLock)
        {
            // Find and remove node
            var node = _lruList.Find(key);
            if (node != null)
            {
                _lruList.Remove(node);
                _lruList.AddFirst(key); // Move to head
            }
        }
    }

    private void RemoveFromLru(string key)
    {
        lock (_lruLock)
        {
            var node = _lruList.Find(key);
            if (node != null)
            {
                _lruList.Remove(node);
            }
        }
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EmbeddingCacheEnabled)
        {
            return await _inner.GenerateBatchEmbeddingsAsync(texts, cancellationToken);
        }

        var textsList = texts.ToList();
        var results = new List<ReadOnlyMemory<float>>(textsList.Count);
        var uncachedTexts = new List<(int index, string text)>();

        // Check cache for each text
        for (var i = 0; i < textsList.Count; i++)
        {
            var text = textsList[i];
            var cacheKey = ComputeHash(text);

            if (_cache.TryGetValue(cacheKey, out var entry) &&
                DateTime.UtcNow - entry.Timestamp < TimeSpan.FromMinutes(_options.EmbeddingCacheTtlMinutes))
            {
                UpdateLru(cacheKey);
                results.Add(entry.Embedding);

                if (_profiler != null)
                {
                    await _profiler.RecordCacheAccessAsync("system", "Embedding", hit: true, cancellationToken);
                }
            }
            else
            {
                uncachedTexts.Add((i, text));
                results.Add(default); // Placeholder

                if (_profiler != null)
                {
                    await _profiler.RecordCacheAccessAsync("system", "Embedding", hit: false, cancellationToken);
                }
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            var uncachedResults = await _inner.GenerateBatchEmbeddingsAsync(
                uncachedTexts.Select(t => t.text),
                cancellationToken);

            for (var i = 0; i < uncachedTexts.Count; i++)
            {
                var (index, text) = uncachedTexts[i];
                var embedding = uncachedResults[i];
                var cacheKey = ComputeHash(text);

                AddToCache(cacheKey, embedding);
                results[index] = embedding;
            }
        }

        return results;
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private sealed class CacheEntry
    {
        public required ReadOnlyMemory<float> Embedding { get; init; }
        public required DateTime Timestamp { get; init; }
    }
}
