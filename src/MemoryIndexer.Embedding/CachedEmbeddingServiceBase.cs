using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Embedding;

/// <summary>
/// Base class for embedding services with caching and batch processing support.
/// </summary>
public abstract class CachedEmbeddingServiceBase : IEmbeddingService
{
    protected readonly IMemoryCache Cache;
    protected readonly ILogger Logger;
    protected readonly TimeSpan CacheTtl;
    protected readonly int BatchSize;

    /// <summary>
    /// Unique prefix for cache keys to avoid collisions between providers.
    /// </summary>
    protected abstract string CacheKeyPrefix { get; }

    /// <inheritdoc />
    public abstract int Dimensions { get; }

    protected CachedEmbeddingServiceBase(
        IMemoryCache cache,
        ILogger logger,
        EmbeddingOptions options)
    {
        Cache = cache;
        Logger = logger;
        CacheTtl = TimeSpan.FromMinutes(options.CacheTtlMinutes);
        BatchSize = options.BatchSize;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        var cacheKey = GetCacheKey(text);

        if (CacheTtl > TimeSpan.Zero && Cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
        {
            Logger.LogDebug("Cache hit for embedding");
            return cached;
        }

        var embedding = await GenerateSingleEmbeddingAsync(text, cancellationToken);

        if (CacheTtl > TimeSpan.Zero)
        {
            Cache.Set(cacheKey, embedding, CacheTtl);
        }

        return embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return [];
        }

        Logger.LogDebug("Generating batch embeddings for {Count} texts", textList.Count);

        var results = new ReadOnlyMemory<float>[textList.Count];
        var uncached = new List<(int Index, string Text)>();

        // Check cache first
        for (var i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results[i] = new float[Dimensions];
                continue;
            }

            var cacheKey = GetCacheKey(text);
            if (CacheTtl > TimeSpan.Zero && Cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
            {
                results[i] = cached;
            }
            else
            {
                uncached.Add((i, text));
            }
        }

        if (uncached.Count == 0)
        {
            Logger.LogDebug("All {Count} embeddings found in cache", textList.Count);
            return results;
        }

        // Process uncached texts
        await ProcessUncachedBatchAsync(textList, results, uncached, cancellationToken);

        return results;
    }

    /// <summary>
    /// Generates a single embedding. Implement provider-specific logic here.
    /// </summary>
    protected abstract Task<ReadOnlyMemory<float>> GenerateSingleEmbeddingAsync(
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Processes uncached texts in batches. Override for providers with native batch support.
    /// Default implementation processes items sequentially.
    /// </summary>
    protected virtual async Task ProcessUncachedBatchAsync(
        List<string> allTexts,
        ReadOnlyMemory<float>[] results,
        List<(int Index, string Text)> uncached,
        CancellationToken cancellationToken)
    {
        // Default: process sequentially with batch grouping
        var batches = uncached
            .Select((item, i) => (item, BatchIndex: i / BatchSize))
            .GroupBy(x => x.BatchIndex)
            .Select(g => g.Select(x => x.item).ToList());

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (index, text) in batch)
            {
                var embedding = await GenerateSingleEmbeddingAsync(text, cancellationToken);
                results[index] = embedding;
                CacheEmbedding(text, embedding);
            }
        }
    }

    /// <summary>
    /// Caches an embedding if caching is enabled.
    /// </summary>
    protected void CacheEmbedding(string text, ReadOnlyMemory<float> embedding)
    {
        if (CacheTtl > TimeSpan.Zero)
        {
            Cache.Set(GetCacheKey(text), embedding, CacheTtl);
        }
    }

    /// <summary>
    /// Gets the cache key for a text. Uses SHA256 hash for consistent, short keys.
    /// </summary>
    protected string GetCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"emb:{CacheKeyPrefix}:{Convert.ToHexString(hash)}";
    }
}
