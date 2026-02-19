using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Graph;

/// <summary>
/// Community detection using Label Propagation algorithm.
/// Efficient O(m) algorithm for finding communities in sparse graphs.
/// </summary>
/// <remarks>
/// Research basis: Label Propagation for community detection (Raghavan et al., 2007).
/// Adapted for memory graphs with weighted edges based on entity co-occurrence.
/// </remarks>
public sealed partial class LabelPropagationCommunityDetector : ICommunityDetector
{
    private readonly IMemoryGraphService _graphService;
    private readonly ITemporalEntityStore _entityStore;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<LabelPropagationCommunityDetector> _logger;

    // Cache for community assignments
    private readonly Dictionary<Guid, int> _memoryToCommunity = new();
    private readonly Dictionary<string, int> _entityToCommunity = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HashSet<Guid>> _communityToMemories = new();
    private readonly object _cacheLock = new();

    public LabelPropagationCommunityDetector(
        IMemoryGraphService graphService,
        ITemporalEntityStore entityStore,
        IMemoryStore memoryStore,
        ILogger<LabelPropagationCommunityDetector> logger)
    {
        _graphService = graphService;
        _entityStore = entityStore;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommunityDetectionResult> DetectCommunitiesAsync(
        string userId,
        CommunityDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CommunityDetectionOptions();
        var stopwatch = Stopwatch.StartNew();
        var random = options.RandomSeed.HasValue
            ? new Random(options.RandomSeed.Value)
            : new Random();

        LogStartingCommunityDetectionUserUserId(_logger, userId);

        // Build adjacency list from entity store
        var triples = await _entityStore.GetAllActiveAsync(userId, cancellationToken);
        var tripleList = triples.ToList();

        // Build entity graph
        var entityNeighbors = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

        foreach (var triple in tripleList)
        {
            AddEdge(entityNeighbors, triple.Subject, triple.ObjectValue, triple.Confidence, options.UseWeightedEdges);
            AddEdge(entityNeighbors, triple.ObjectValue, triple.Subject, triple.Confidence, options.UseWeightedEdges);
        }

        var entities = entityNeighbors.Keys.ToList();
        if (entities.Count == 0)
        {
            LogEntitiesFoundUserUserId(_logger, userId);
            return new CommunityDetectionResult
            {
                CommunityCount = 0,
                Duration = stopwatch.Elapsed
            };
        }

        // Initialize: each entity gets its own unique label
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entities.Count; i++)
        {
            labels[entities[i]] = i;
        }

        // Label propagation iterations
        var iterations = 0;
        var converged = false;

        while (iterations < options.MaxIterations && !converged)
        {
            iterations++;
            var changedCount = 0;

            // Shuffle entities for async update order
            var shuffled = entities.OrderBy(_ => random.Next()).ToList();

            foreach (var entity in shuffled)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entityNeighbors.TryGetValue(entity, out var neighbors) || neighbors.Count == 0)
                    continue;

                // Count neighbor labels (weighted)
                var labelScores = new Dictionary<int, float>();
                foreach (var (neighbor, weight) in neighbors)
                {
                    if (labels.TryGetValue(neighbor, out var neighborLabel))
                    {
                        labelScores.TryGetValue(neighborLabel, out var score);
                        labelScores[neighborLabel] = score + weight;
                    }
                }

                if (labelScores.Count == 0)
                    continue;

                // Find max label (random tie-breaking)
                var maxScore = labelScores.Values.Max();
                var maxLabels = labelScores.Where(kv => kv.Value == maxScore).Select(kv => kv.Key).ToList();
                var newLabel = maxLabels[random.Next(maxLabels.Count)];

                if (labels[entity] != newLabel)
                {
                    labels[entity] = newLabel;
                    changedCount++;
                }
            }

            var changeRate = (float)changedCount / entities.Count;
            converged = changeRate < options.ConvergenceThreshold;

            LogIterationIterationChangedNodesChanged(_logger, iterations, changedCount, changeRate);
        }

        // Renumber labels to be contiguous
        var uniqueLabels = labels.Values.Distinct().OrderBy(x => x).ToList();
        var labelMap = uniqueLabels.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i);

        foreach (var entity in entities)
        {
            labels[entity] = labelMap[labels[entity]];
        }

        // Filter small communities
        var communitySizes = labels.Values
            .GroupBy(l => l)
            .ToDictionary(g => g.Key, g => g.Count());

        var validCommunities = communitySizes
            .Where(kv => kv.Value >= options.MinCommunitySize)
            .Select(kv => kv.Key)
            .ToHashSet();

        // Assign orphaned entities to nearest valid community
        foreach (var entity in entities)
        {
            if (!validCommunities.Contains(labels[entity]))
            {
                // Find most connected valid community
                if (entityNeighbors.TryGetValue(entity, out var neighbors))
                {
                    var neighborLabels = neighbors.Keys
                        .Where(n => labels.TryGetValue(n, out var l) && validCommunities.Contains(l))
                        .GroupBy(n => labels[n])
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();

                    if (neighborLabels != null)
                    {
                        labels[entity] = neighborLabels.Key;
                    }
                }
            }
        }

        // Build final community sizes
        var finalSizes = labels.Values
            .GroupBy(l => l)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate modularity (simplified)
        var modularity = CalculateModularity(entityNeighbors, labels, tripleList.Count);

        // Update cache
        lock (_cacheLock)
        {
            _entityToCommunity.Clear();
            foreach (var (entity, community) in labels)
            {
                _entityToCommunity[entity] = community;
            }
        }

        stopwatch.Stop();

        LogCommunityDetectionCompletedCommunitiesCommunities(_logger, finalSizes.Count, entities.Count, modularity, stopwatch.ElapsedMilliseconds);

        return new CommunityDetectionResult
        {
            CommunityCount = finalSizes.Count,
            EntityAssignments = new Dictionary<string, int>(labels, StringComparer.OrdinalIgnoreCase),
            CommunitySizes = finalSizes,
            Modularity = modularity,
            IterationsToConverge = iterations,
            Duration = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GetCommunityMemoriesAsync(
        int communityId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var memories = new List<MemoryUnit>();

        // Get entities in this community
        List<string> communityEntities;
        lock (_cacheLock)
        {
            communityEntities = _entityToCommunity
                .Where(kv => kv.Value == communityId)
                .Select(kv => kv.Key)
                .ToList();
        }

        if (communityEntities.Count == 0)
            return memories;

        // Get memories connected to these entities
        var seenMemories = new HashSet<Guid>();

        foreach (var entity in communityEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var triples = await _entityStore.GetBySubjectAsync(entity, userId, cancellationToken);
            var objTriples = await _entityStore.GetByObjectAsync(entity, userId, cancellationToken);

            foreach (var triple in triples.Concat(objTriples))
            {
                if (triple.SourceMemoryId.HasValue && !seenMemories.Contains(triple.SourceMemoryId.Value))
                {
                    seenMemories.Add(triple.SourceMemoryId.Value);

                    var memory = await _memoryStore.GetByIdAsync(triple.SourceMemoryId.Value, cancellationToken);
                    if (memory != null && memory.UserId == userId)
                    {
                        memories.Add(memory);
                    }
                }
            }
        }

        return memories.OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <inheritdoc />
    public Task<int> AssignToCommunityAsync(
        Guid memoryId,
        IReadOnlyList<string> connectedEntities,
        CancellationToken cancellationToken = default)
    {
        if (connectedEntities.Count == 0)
            return Task.FromResult(-1);

        // Find most common community among connected entities
        var communityVotes = new Dictionary<int, int>();

        lock (_cacheLock)
        {
            foreach (var entity in connectedEntities)
            {
                if (_entityToCommunity.TryGetValue(entity, out var community))
                {
                    communityVotes.TryGetValue(community, out var votes);
                    communityVotes[community] = votes + 1;
                }
            }
        }

        if (communityVotes.Count == 0)
        {
            // No known communities, create new one
            var newCommunity = _entityToCommunity.Count > 0
                ? _entityToCommunity.Values.Max() + 1
                : 0;

            lock (_cacheLock)
            {
                foreach (var entity in connectedEntities)
                {
                    _entityToCommunity[entity] = newCommunity;
                }
                _memoryToCommunity[memoryId] = newCommunity;
            }

            return Task.FromResult(newCommunity);
        }

        var assignedCommunity = communityVotes.OrderByDescending(kv => kv.Value).First().Key;

        lock (_cacheLock)
        {
            _memoryToCommunity[memoryId] = assignedCommunity;
        }

        return Task.FromResult(assignedCommunity);
    }

    /// <inheritdoc />
    public async Task<CommunitySummary> GetCommunitySummaryAsync(
        int communityId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Get entities in this community
        List<string> communityEntities;
        lock (_cacheLock)
        {
            communityEntities = _entityToCommunity
                .Where(kv => kv.Value == communityId)
                .Select(kv => kv.Key)
                .ToList();
        }

        if (communityEntities.Count == 0)
        {
            return new CommunitySummary
            {
                CommunityId = communityId,
                TopicLabel = "Empty Community"
            };
        }

        // Get all triples for these entities
        var allTriples = new List<EntityTriple>();
        DateTime? earliest = null;
        DateTime? latest = null;

        foreach (var entity in communityEntities.Take(20)) // Limit for performance
        {
            cancellationToken.ThrowIfCancellationRequested();

            var triples = await _entityStore.GetBySubjectAsync(entity, userId, cancellationToken);
            allTriples.AddRange(triples);

            foreach (var t in triples)
            {
                if (!earliest.HasValue || t.CreatedAt < earliest)
                    earliest = t.CreatedAt;
                if (!latest.HasValue || t.CreatedAt > latest)
                    latest = t.CreatedAt;
            }
        }

        // Find key entities (most connected)
        var entityCounts = allTriples
            .SelectMany(t => new[] { t.Subject, t.ObjectValue })
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        // Find common predicates
        var commonPredicates = allTriples
            .GroupBy(t => t.Predicate, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        // Generate topic label from key entities
        var topicLabel = entityCounts.Count > 0
            ? string.Join(", ", entityCounts.Take(3))
            : "Unnamed Topic";

        var memories = await GetCommunityMemoriesAsync(communityId, userId, cancellationToken);

        return new CommunitySummary
        {
            CommunityId = communityId,
            TopicLabel = topicLabel,
            KeyEntities = entityCounts,
            MemoryCount = memories.Count,
            EntityCount = communityEntities.Count,
            CommonPredicates = commonPredicates,
            TimeRange = (earliest, latest)
        };
    }

    #region Helper Methods

    private static void AddEdge(
        Dictionary<string, Dictionary<string, float>> graph,
        string from,
        string to,
        float weight,
        bool useWeights)
    {
        if (!graph.TryGetValue(from, out var neighbors))
        {
            neighbors = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            graph[from] = neighbors;
        }

        var edgeWeight = useWeights ? weight : 1.0f;
        neighbors.TryGetValue(to, out var existing);
        neighbors[to] = Math.Max(existing, edgeWeight);
    }

    private static float CalculateModularity(
        Dictionary<string, Dictionary<string, float>> graph,
        Dictionary<string, int> labels,
        int totalEdges)
    {
        if (totalEdges == 0)
            return 0f;

        var m = totalEdges * 2.0f; // Total edge weight
        var q = 0f;

        // Calculate degree for each node
        var degrees = graph.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Values.Sum(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (node1, neighbors) in graph)
        {
            if (!labels.TryGetValue(node1, out var label1))
                continue;

            var k1 = degrees.GetValueOrDefault(node1, 0f);

            foreach (var (node2, weight) in neighbors)
            {
                if (!labels.TryGetValue(node2, out var label2) || label1 != label2)
                    continue;

                var k2 = degrees.GetValueOrDefault(node2, 0f);
                q += weight - (k1 * k2 / m);
            }
        }

        return q / m;
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting community detection for user {UserId}")]
    private static partial void LogStartingCommunityDetectionUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No entities found for user {UserId}")]
    private static partial void LogEntitiesFoundUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Iteration {Iteration}: {Changed} nodes changed ({Rate:P1})")]
    private static partial void LogIterationIterationChangedNodesChanged(ILogger logger, int iteration, int changed, float rate);

    [LoggerMessage(Level = LogLevel.Information, Message = "Community detection completed: {Communities} communities, {Entities} entities, modularity={Modularity:F3} in {Duration}ms")]
    private static partial void LogCommunityDetectionCompletedCommunitiesCommunities(ILogger logger, int communities, int entities, float modularity, long duration);
}
