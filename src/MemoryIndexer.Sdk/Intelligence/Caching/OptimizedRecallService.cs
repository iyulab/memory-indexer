using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Optimized recall service with query result caching, batch processing, early termination, and latency tracking.
/// Phase 22.2: Recall Latency Optimization
/// Phase v0.5.0: Session-level Recall Caching
/// </summary>
public sealed class OptimizedRecallService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly IMemoryCache _queryCache;
    private readonly ILatencyProfiler? _profiler;
    private readonly ILogger<OptimizedRecallService> _logger;
    private readonly LatencyOptions _options;
    private readonly TimeSpan _queryCacheTtl;

    // Telemetry counters (Phase v0.5.0)
    private long _cacheHits;
    private long _cacheMisses;
    private long _duplicateQueryCount;

    public OptimizedRecallService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        IMemoryCache queryCache,
        ILatencyProfiler? profiler,
        ILogger<OptimizedRecallService> logger,
        IOptions<MemoryIndexerOptions> options)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _queryCache = queryCache;
        _profiler = profiler;
        _logger = logger;
        _options = options.Value.Latency;
        _queryCacheTtl = TimeSpan.FromMinutes(_options.QueryCacheTtlMinutes);
    }

    /// <summary>
    /// Gets recall cache statistics for telemetry.
    /// </summary>
    public RecallCacheStatistics GetCacheStatistics() => new()
    {
        CacheHits = Interlocked.Read(ref _cacheHits),
        CacheMisses = Interlocked.Read(ref _cacheMisses),
        DuplicateQueryCount = Interlocked.Read(ref _duplicateQueryCount),
        HitRatio = _cacheHits + _cacheMisses > 0
            ? (float)_cacheHits / (_cacheHits + _cacheMisses)
            : 0f
    };

    /// <summary>
    /// Recall memories with latency optimization (query result caching, embedding cache, early termination, profiling)
    /// </summary>
    public async Task<IReadOnlyList<MemoryUnit>> RecallAsync(
        string userId,
        string query,
        string tier,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var componentLatencies = new Dictionary<string, double>();

        // Phase v0.5.0: Check query result cache first
        if (_options.QueryCacheEnabled)
        {
            var cacheKey = GetQueryCacheKey(userId, query, tier, limit);
            if (_queryCache.TryGetValue(cacheKey, out IReadOnlyList<MemoryUnit>? cachedResults) && cachedResults != null)
            {
                Interlocked.Increment(ref _cacheHits);
                Interlocked.Increment(ref _duplicateQueryCount);

                stopwatch.Stop();
                componentLatencies["CacheHit"] = stopwatch.Elapsed.TotalMilliseconds;

                _logger.LogDebug(
                    "Query cache hit for user {UserId}, tier {Tier}: {Query} (duplicates: {Count})",
                    userId, tier, query.Length > 50 ? query[..50] + "..." : query, _duplicateQueryCount);

                return cachedResults;
            }
            Interlocked.Increment(ref _cacheMisses);
        }

        try
        {
            // Generate embedding (cached via CachedEmbeddingService)
            var embeddingStart = Stopwatch.GetTimestamp();
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            var embeddingMs = Stopwatch.GetElapsedTime(embeddingStart).TotalMilliseconds;
            componentLatencies["Embedding"] = embeddingMs;

            // Search
            var searchStart = Stopwatch.GetTimestamp();
            var searchOptions = new MemorySearchOptions
            {
                UserId = userId,
                Limit = limit * 2 // Retrieve more for early termination check
            };

            var searchResults = await _memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);
            var searchMs = Stopwatch.GetElapsedTime(searchStart).TotalMilliseconds;
            componentLatencies["Search"] = searchMs;

            // Early termination check
            if (_options.EarlyTerminationEnabled && searchResults.Count >= _options.EarlyTerminationMinResults)
            {
                var averageScore = searchResults.Take(_options.EarlyTerminationMinResults)
                    .Average(r => r.Score);

                if (averageScore >= _options.EarlyTerminationConfidence)
                {
                    _logger.LogDebug(
                        "Early termination triggered: {Count} results with avg score {Score:F3}",
                        searchResults.Count, averageScore);

                    stopwatch.Stop();
                    componentLatencies["EarlyTermination"] = stopwatch.Elapsed.TotalMilliseconds - embeddingMs - searchMs;

                    if (_profiler != null)
                    {
                        await _profiler.RecordLatencyAsync(
                            userId,
                            tier,
                            stopwatch.Elapsed.TotalMilliseconds,
                            componentLatencies,
                            cancellationToken);
                    }

                    var earlyResults = searchResults.Take(limit).Select(r => r.Memory).ToList();
                    CacheQueryResult(userId, query, tier, limit, earlyResults);
                    return earlyResults;
                }
            }

            stopwatch.Stop();

            // Record latency metrics
            if (_profiler != null)
            {
                await _profiler.RecordLatencyAsync(
                    userId,
                    tier,
                    stopwatch.Elapsed.TotalMilliseconds,
                    componentLatencies,
                    cancellationToken);
            }

            _logger.LogDebug(
                "Recall completed for {Tier} tier: {TotalMs}ms (Embedding: {EmbeddingMs}ms, Search: {SearchMs}ms)",
                tier, stopwatch.Elapsed.TotalMilliseconds, embeddingMs, searchMs);

            var results = searchResults.OrderByDescending(r => r.Score).Take(limit).Select(r => r.Memory).ToList();
            CacheQueryResult(userId, query, tier, limit, results);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during recall for user {UserId}, tier {Tier}", userId, tier);

            // Still record latency even on error
            if (_profiler != null)
            {
                await _profiler.RecordLatencyAsync(
                    userId,
                    tier,
                    stopwatch.Elapsed.TotalMilliseconds,
                    componentLatencies.Count > 0 ? componentLatencies : null,
                    cancellationToken);
            }

            throw;
        }
    }

    /// <summary>
    /// Batch recall for parallel processing of multiple queries
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<MemoryUnit>>> BatchRecallAsync(
        string userId,
        IReadOnlyList<string> queries,
        string tier,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_options.BatchProcessingEnabled || queries.Count == 1)
        {
            // Fall back to sequential processing
            var sequentialResults = new Dictionary<string, IReadOnlyList<MemoryUnit>>();
            foreach (var query in queries)
            {
                sequentialResults[query] = await RecallAsync(userId, query, tier, limit, cancellationToken);
            }
            return sequentialResults;
        }

        _logger.LogDebug("Batch recall for {Count} queries, tier {Tier}", queries.Count, tier);

        // Process queries in parallel batches
        var batches = queries
            .Select((query, index) => new { query, index })
            .GroupBy(x => x.index / _options.MaxBatchSize)
            .Select(g => g.Select(x => x.query).ToList())
            .ToList();

        var batchRecallResults = new Dictionary<string, IReadOnlyList<MemoryUnit>>();

        foreach (var batch in batches)
        {
            var tasks = batch.Select(query => Task.Run(async () =>
            {
                var memories = await RecallAsync(userId, query, tier, limit, cancellationToken);
                return (query, memories);
            }, cancellationToken));

            var batchTaskResults = await Task.WhenAll(tasks);

            foreach (var (query, memories) in batchTaskResults)
            {
                batchRecallResults[query] = memories;
            }
        }

        return batchRecallResults;
    }

    /// <summary>
    /// Generates a cache key for query results.
    /// Uses SHA256 hash for consistent, collision-resistant keys.
    /// </summary>
    private static string GetQueryCacheKey(string userId, string query, string tier, int limit)
    {
        var keySource = $"{userId}:{tier}:{limit}:{query}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(keySource));
        return $"recall:{Convert.ToHexString(hash)}";
    }

    /// <summary>
    /// Caches query results if caching is enabled.
    /// </summary>
    private void CacheQueryResult(string userId, string query, string tier, int limit, IReadOnlyList<MemoryUnit> results)
    {
        if (!_options.QueryCacheEnabled || _queryCacheTtl <= TimeSpan.Zero)
            return;

        var cacheKey = GetQueryCacheKey(userId, query, tier, limit);
        _queryCache.Set(cacheKey, results, _queryCacheTtl);
    }
}

/// <summary>
/// Statistics about recall cache performance.
/// </summary>
public sealed class RecallCacheStatistics
{
    /// <summary>Number of cache hits.</summary>
    public long CacheHits { get; init; }

    /// <summary>Number of cache misses.</summary>
    public long CacheMisses { get; init; }

    /// <summary>Number of duplicate queries detected (same query called multiple times).</summary>
    public long DuplicateQueryCount { get; init; }

    /// <summary>Cache hit ratio (0.0 to 1.0).</summary>
    public float HitRatio { get; init; }
}
