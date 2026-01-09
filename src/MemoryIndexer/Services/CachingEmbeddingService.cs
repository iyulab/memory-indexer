using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryIndexer.Services;

/// <summary>
/// Decorator that adds caching to any <see cref="IEmbeddingService"/> implementation.
/// Reduces API calls by caching embeddings with SHA256-based keys.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var openAi = new OpenAIEmbeddingService(apiKey, model);
/// var cached = new CachingEmbeddingService(openAi);
/// services.AddSingleton&lt;IEmbeddingService&gt;(cached);
/// </code>
/// </remarks>
public sealed class CachingEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly IEmbeddingService _inner;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly EmbeddingCacheOptions _options;
    private readonly ILogger<CachingEmbeddingService> _logger;
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Creates a caching decorator around an embedding service.
    /// </summary>
    /// <param name="inner">The underlying embedding service to cache.</param>
    /// <param name="options">Cache configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public CachingEmbeddingService(
        IEmbeddingService inner,
        EmbeddingCacheOptions? options = null,
        ILogger<CachingEmbeddingService>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new EmbeddingCacheOptions();
        _logger = logger ?? NullLogger<CachingEmbeddingService>.Instance;

        // Periodic cleanup of expired entries (every 5 minutes)
        if (_options.Enabled)
        {
            _cleanupTimer = new Timer(
                _ => CleanupExpiredEntries(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        _logger.LogInformation(
            "Embedding cache initialized: enabled={Enabled}, ttl={Ttl}, maxSize={MaxSize}",
            _options.Enabled, _options.Ttl, _options.MaxSize);
    }

    /// <inheritdoc />
    public int Dimensions => _inner.Dimensions;

    /// <summary>
    /// Gets the current number of cached entries.
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await _inner.GenerateEmbeddingAsync(text, cancellationToken);
        }

        var key = ComputeCacheKey(text);

        // Check cache
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_options.Ttl))
        {
            _logger.LogDebug("Cache hit for embedding (key={Key})", key[..8]);
            return entry.Embedding;
        }

        // Generate embedding
        var embedding = await _inner.GenerateEmbeddingAsync(text, cancellationToken);

        // Cache result
        CacheEmbedding(key, embedding);

        return embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        if (!_options.Enabled)
        {
            return await _inner.GenerateBatchEmbeddingsAsync(textList, cancellationToken);
        }

        // Check cache for each text
        var results = new ReadOnlyMemory<float>[textList.Count];
        var uncachedIndices = new List<int>();
        var uncachedTexts = new List<string>();

        for (int i = 0; i < textList.Count; i++)
        {
            var key = ComputeCacheKey(textList[i]);
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_options.Ttl))
            {
                results[i] = entry.Embedding;
            }
            else
            {
                uncachedIndices.Add(i);
                uncachedTexts.Add(textList[i]);
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            var newEmbeddings = await _inner.GenerateBatchEmbeddingsAsync(uncachedTexts, cancellationToken);

            for (int i = 0; i < uncachedIndices.Count; i++)
            {
                var originalIndex = uncachedIndices[i];
                var embedding = newEmbeddings[i];
                results[originalIndex] = embedding;

                // Cache result
                var key = ComputeCacheKey(textList[originalIndex]);
                CacheEmbedding(key, embedding);
            }
        }

        var cacheHits = textList.Count - uncachedTexts.Count;
        _logger.LogDebug(
            "Batch embedding: {Total} total, {Cached} cached, {Generated} generated",
            textList.Count, cacheHits, uncachedTexts.Count);

        return results;
    }

    /// <summary>
    /// Clears all cached embeddings.
    /// </summary>
    public void ClearCache()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("Cache cleared: {Count} entries removed", count);
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        var now = DateTime.UtcNow;
        var entries = _cache.Values.ToList();
        var expired = entries.Count(e => e.IsExpired(_options.Ttl));
        var active = entries.Count - expired;

        return new CacheStatistics
        {
            TotalEntries = entries.Count,
            ActiveEntries = active,
            ExpiredEntries = expired,
            MaxSize = _options.MaxSize,
            Ttl = _options.Ttl
        };
    }

    private static string ComputeCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }

    private void CacheEmbedding(string key, ReadOnlyMemory<float> embedding)
    {
        // Check if eviction is needed
        if (_cache.Count >= _options.MaxSize)
        {
            EvictOldEntries();
        }

        _cache[key] = new CacheEntry(embedding);
    }

    private void EvictOldEntries()
    {
        var toEvict = (int)(_cache.Count * _options.EvictionRatio);
        if (toEvict < 1) toEvict = 1;

        var oldestKeys = _cache
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(toEvict)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var oldKey in oldestKeys)
        {
            _cache.TryRemove(oldKey, out _);
        }

        _logger.LogDebug("Cache eviction: removed {Count} entries", oldestKeys.Count);
    }

    private void CleanupExpiredEntries()
    {
        if (_disposed) return;

        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired(_options.Ttl))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleanup: removed {Count} expired entries", expiredKeys.Count);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer?.Dispose();
        _cache.Clear();
    }

    private sealed class CacheEntry
    {
        public ReadOnlyMemory<float> Embedding { get; }
        public DateTime CreatedAt { get; }

        public CacheEntry(ReadOnlyMemory<float> embedding)
        {
            Embedding = embedding;
            CreatedAt = DateTime.UtcNow;
        }

        public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CreatedAt > ttl;
    }
}

/// <summary>
/// Statistics about the embedding cache.
/// </summary>
public sealed class CacheStatistics
{
    /// <summary>Total entries in cache.</summary>
    public int TotalEntries { get; init; }

    /// <summary>Active (non-expired) entries.</summary>
    public int ActiveEntries { get; init; }

    /// <summary>Expired entries awaiting cleanup.</summary>
    public int ExpiredEntries { get; init; }

    /// <summary>Maximum cache size.</summary>
    public int MaxSize { get; init; }

    /// <summary>Time-to-live for entries.</summary>
    public TimeSpan Ttl { get; init; }

    /// <summary>Cache utilization percentage.</summary>
    public double UtilizationPercent => MaxSize > 0 ? (double)TotalEntries / MaxSize * 100 : 0;
}
