using System.Collections.Concurrent;
using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Graph;

/// <summary>
/// Computes entity importance using PageRank algorithm.
/// Entities that are heavily connected and referenced by memories score higher.
/// </summary>
/// <remarks>
/// Research basis: PageRank (Brin & Page, 1998) adapted for knowledge graphs.
/// Key insight: Important entities are referenced by many other important entities.
/// </remarks>
public sealed partial class PageRankImportancePropagator : IImportancePropagator
{
    private readonly ITemporalEntityStore _entityStore;
    private readonly IMemoryGraphService _graphService;
    private readonly ILogger<PageRankImportancePropagator> _logger;

    // Cache for importance scores
    private readonly ConcurrentDictionary<string, float> _importanceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EntityStats> _entityStats = new(StringComparer.OrdinalIgnoreCase);

    private sealed class EntityStats
    {
        public int InDegree { get; set; }
        public int OutDegree { get; set; }
        public int MemoryCount { get; set; }
    }

    public PageRankImportancePropagator(
        ITemporalEntityStore entityStore,
        IMemoryGraphService graphService,
        ILogger<PageRankImportancePropagator> logger)
    {
        _entityStore = entityStore;
        _graphService = graphService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImportanceResult> ComputeImportanceAsync(
        string userId,
        ImportanceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ImportanceOptions();
        var stopwatch = Stopwatch.StartNew();

        LogComputingPageRankImportanceUserUserId(_logger, userId);

        // Build graph from entity store
        var triples = await _entityStore.GetAllActiveAsync(userId, cancellationToken);
        var tripleList = triples.ToList();

        // Build adjacency structures
        var outLinks = new Dictionary<string, List<(string Target, float Weight)>>(StringComparer.OrdinalIgnoreCase);
        var inLinks = new Dictionary<string, List<(string Source, float Weight)>>(StringComparer.OrdinalIgnoreCase);
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var triple in tripleList)
        {
            entities.Add(triple.Subject);
            entities.Add(triple.ObjectValue);

            var weight = options.UseWeightedEdges ? triple.Confidence : 1.0f;

            // Subject -> Object edge
            if (!outLinks.TryGetValue(triple.Subject, out var subjOut))
            {
                subjOut = new List<(string, float)>();
                outLinks[triple.Subject] = subjOut;
            }
            subjOut.Add((triple.ObjectValue, weight));

            if (!inLinks.TryGetValue(triple.ObjectValue, out var objIn))
            {
                objIn = new List<(string, float)>();
                inLinks[triple.ObjectValue] = objIn;
            }
            objIn.Add((triple.Subject, weight));
        }

        if (entities.Count == 0)
        {
            LogEntitiesFoundUserUserId(_logger, userId);
            return new ImportanceResult
            {
                EntityCount = 0,
                Duration = stopwatch.Elapsed
            };
        }

        var n = entities.Count;
        var d = options.DampingFactor;
        var entityList = entities.ToList();

        // Initialize scores uniformly
        var scores = entityList.ToDictionary(
            e => e,
            _ => 1.0f / n,
            StringComparer.OrdinalIgnoreCase);

        // Compute out-degree for normalization
        var outDegree = entityList.ToDictionary(
            e => e,
            e => outLinks.TryGetValue(e, out var links)
                ? links.Sum(l => l.Weight)
                : 0f,
            StringComparer.OrdinalIgnoreCase);

        // PageRank iteration
        var iterations = 0;
        var converged = false;
        var finalDiff = 0f;

        while (iterations < options.MaxIterations && !converged)
        {
            iterations++;
            cancellationToken.ThrowIfCancellationRequested();

            var newScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var difference = 0f;

            // Base score (teleport probability)
            var baseScore = (1 - d) / n;

            foreach (var entity in entityList)
            {
                var linkScore = 0f;

                // Sum contributions from incoming links
                if (inLinks.TryGetValue(entity, out var incoming))
                {
                    foreach (var (source, weight) in incoming)
                    {
                        var sourceOutDegree = outDegree.GetValueOrDefault(source, 1f);
                        if (sourceOutDegree > 0)
                        {
                            linkScore += (weight / sourceOutDegree) * scores[source];
                        }
                    }
                }

                var newScore = baseScore + d * linkScore;
                newScores[entity] = newScore;
                difference += Math.Abs(newScore - scores[entity]);
            }

            scores = newScores;
            finalDiff = difference;
            converged = difference < options.ConvergenceThreshold;

            if (iterations % 10 == 0)
            {
                LogPageRankIterationIterationDiffDiff(_logger, iterations, difference);
            }
        }

        // Apply memory boost if enabled
        if (options.ApplyMemoryBoost)
        {
            var memoryConnections = await GetMemoryConnectionCountsAsync(entityList, userId, cancellationToken);

            foreach (var entity in entityList)
            {
                if (memoryConnections.TryGetValue(entity, out var memCount) && memCount > 0)
                {
                    var boost = 1 + (options.MemoryBoostFactor * Math.Log(1 + memCount));
                    scores[entity] *= (float)boost;
                }
            }
        }

        // Normalize scores to [0, 1]
        var maxScore = scores.Values.Max();
        if (maxScore > 0)
        {
            foreach (var entity in entityList)
            {
                scores[entity] /= maxScore;
            }
        }

        // Update cache
        _importanceCache.Clear();
        foreach (var (entity, score) in scores)
        {
            _importanceCache[entity] = score;
        }

        // Update stats
        _entityStats.Clear();
        foreach (var entity in entityList)
        {
            _entityStats[entity] = new EntityStats
            {
                InDegree = inLinks.TryGetValue(entity, out var inL) ? inL.Count : 0,
                OutDegree = outLinks.TryGetValue(entity, out var outL) ? outL.Count : 0
            };
        }

        stopwatch.Stop();

        LogPageRankCompletedEntitiesEntitiesEdges(_logger, n, tripleList.Count, iterations, converged, stopwatch.ElapsedMilliseconds);

        return new ImportanceResult
        {
            EntityScores = new Dictionary<string, float>(scores, StringComparer.OrdinalIgnoreCase),
            EntityCount = n,
            EdgeCount = tripleList.Count,
            Iterations = iterations,
            FinalDifference = finalDiff,
            Converged = converged,
            Duration = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public Task<float?> GetEntityImportanceAsync(
        string entityName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_importanceCache.TryGetValue(entityName, out var score) ? (float?)score : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityImportance>> GetTopEntitiesAsync(
        string userId,
        int topK = 20,
        CancellationToken cancellationToken = default)
    {
        var result = _importanceCache
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select((kv, index) =>
            {
                var stats = _entityStats.GetValueOrDefault(kv.Key, new EntityStats());
                return new EntityImportance
                {
                    EntityName = kv.Key,
                    Score = kv.Value,
                    Rank = index + 1,
                    InDegree = stats.InDegree,
                    OutDegree = stats.OutDegree,
                    MemoryConnectionCount = stats.MemoryCount
                };
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityImportance>>(result);
    }

    /// <inheritdoc />
    public async Task UpdateImportanceAsync(
        IReadOnlyList<string> affectedEntities,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // For incremental updates, we recompute (a more sophisticated approach
        // would use personalized PageRank or incremental updates)
        if (affectedEntities.Count > 0)
        {
            LogRecomputingImportanceDueCountAffected(_logger, affectedEntities.Count);
            await ComputeImportanceAsync(userId, cancellationToken: cancellationToken);
        }
    }

    #region Helper Methods

    private async Task<Dictionary<string, int>> GetMemoryConnectionCountsAsync(
        List<string> entities,
        string userId,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var triples = await _entityStore.GetBySubjectAsync(entity, userId, cancellationToken);
            var objTriples = await _entityStore.GetByObjectAsync(entity, userId, cancellationToken);

            var memoryIds = triples.Concat(objTriples)
                .Where(t => t.SourceMemoryId.HasValue)
                .Select(t => t.SourceMemoryId!.Value)
                .Distinct()
                .Count();

            counts[entity] = memoryIds;
        }

        return counts;
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Computing PageRank importance for user {UserId}")]
    private static partial void LogComputingPageRankImportanceUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No entities found for user {UserId}")]
    private static partial void LogEntitiesFoundUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PageRank iteration {Iteration}: diff={Diff:E3}")]
    private static partial void LogPageRankIterationIterationDiffDiff(ILogger logger, int iteration, double diff);

    [LoggerMessage(Level = LogLevel.Information, Message = "PageRank completed: {Entities} entities, {Edges} edges, {Iterations} iterations, converged={Converged} in {Duration}ms")]
    private static partial void LogPageRankCompletedEntitiesEntitiesEdges(ILogger logger, int entities, int edges, int iterations, bool converged, long duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recomputing importance due to {Count} affected entities")]
    private static partial void LogRecomputingImportanceDueCountAffected(ILogger logger, int count);
}
