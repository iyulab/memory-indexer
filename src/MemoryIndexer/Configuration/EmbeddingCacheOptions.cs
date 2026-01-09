namespace MemoryIndexer.Configuration;

/// <summary>
/// Configuration options for embedding caching.
/// Used with <see cref="Services.CachingEmbeddingService"/> decorator.
/// </summary>
public sealed class EmbeddingCacheOptions
{
    /// <summary>
    /// Whether caching is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time-to-live for cached embeddings. Default: 30 minutes.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum number of cached embeddings. Default: 10000.
    /// When exceeded, oldest 25% entries are evicted.
    /// </summary>
    public int MaxSize { get; set; } = 10000;

    /// <summary>
    /// Percentage of cache to evict when max size is reached. Default: 0.25 (25%).
    /// </summary>
    public double EvictionRatio { get; set; } = 0.25;
}
