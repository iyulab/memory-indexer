using System.Diagnostics;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Optimized recall service with batch processing, early termination, and latency tracking.
/// Phase 22.2: Recall Latency Optimization
/// </summary>
public sealed class OptimizedRecallService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly ILatencyProfiler? _profiler;
    private readonly ILogger<OptimizedRecallService> _logger;
    private readonly LatencyOptions _options;

    public OptimizedRecallService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        ILatencyProfiler? profiler,
        ILogger<OptimizedRecallService> logger,
        IOptions<MemoryIndexerOptions> options)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _profiler = profiler;
        _logger = logger;
        _options = options.Value.Latency;
    }

    /// <summary>
    /// Recall memories with latency optimization (embedding cache, early termination, profiling)
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

                    return searchResults.Take(limit).Select(r => r.Memory).ToList();
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

            return searchResults.OrderByDescending(r => r.Score).Take(limit).Select(r => r.Memory).ToList();
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
}
