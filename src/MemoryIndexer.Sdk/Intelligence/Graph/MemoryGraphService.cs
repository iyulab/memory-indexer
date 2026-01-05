using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Graph;

/// <summary>
/// In-memory implementation of memory graph service.
/// Links memories to the knowledge graph for relationship-aware retrieval.
/// </summary>
/// <remarks>
/// Research basis: Mem0g graph memory architecture - memories as nodes in a directed labeled graph.
/// </remarks>
public sealed class MemoryGraphService : IMemoryGraphService
{
    private readonly ITemporalEntityStore _entityStore;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<MemoryGraphService> _logger;

    // In-memory graph storage
    private readonly ConcurrentDictionary<Guid, MemoryGraphNode> _memoryNodes = new();
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _entityToMemories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();

    public MemoryGraphService(
        ITemporalEntityStore entityStore,
        IMemoryStore memoryStore,
        ILogger<MemoryGraphService> logger)
    {
        _entityStore = entityStore;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MemoryGraphNode> LinkMemoryToGraphAsync(
        MemoryUnit memory,
        IReadOnlyList<EntityTriple> entities,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Linking memory {MemoryId} to graph with {EntityCount} entities",
            memory.Id, entities.Count);

        // Extract unique entity names from triples
        var connectedEntities = entities
            .SelectMany(e => new[] { e.Subject, e.ObjectValue })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var node = new MemoryGraphNode
        {
            MemoryId = memory.Id,
            UserId = memory.UserId,
            ConnectedEntities = connectedEntities,
            EntityTripleIds = entities.Select(e => e.Id).ToList(),
            ImportanceScore = CalculateInitialImportance(memory, entities)
        };

        // Store the node
        _memoryNodes[memory.Id] = node;

        // Update entity-to-memory index
        lock (_indexLock)
        {
            foreach (var entity in connectedEntities)
            {
                if (!_entityToMemories.TryGetValue(entity, out var memorySet))
                {
                    memorySet = new HashSet<Guid>();
                    _entityToMemories[entity] = memorySet;
                }
                memorySet.Add(memory.Id);
            }
        }

        _logger.LogInformation("Memory {MemoryId} linked to graph: {EntityCount} entities, importance={Importance:F2}",
            memory.Id, connectedEntities.Count, node.ImportanceScore);

        return node;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RelatedMemory>> FindRelatedMemoriesAsync(
        Guid memoryId,
        int maxHops = 2,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_memoryNodes.TryGetValue(memoryId, out var sourceNode))
        {
            _logger.LogDebug("Memory {MemoryId} not found in graph", memoryId);
            return [];
        }

        var related = new Dictionary<Guid, (int Distance, HashSet<string> SharedEntities, List<string> Path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<(string Entity, int Distance, List<string> Path)>();

        // Initialize with source memory's entities
        foreach (var entity in sourceNode.ConnectedEntities)
        {
            frontier.Enqueue((entity, 0, new List<string> { entity }));
        }

        while (frontier.Count > 0)
        {
            var (currentEntity, distance, path) = frontier.Dequeue();

            if (visited.Contains(currentEntity) || distance > maxHops)
                continue;

            visited.Add(currentEntity);

            // Find memories connected to this entity
            if (_entityToMemories.TryGetValue(currentEntity, out var connectedMemories))
            {
                foreach (var connectedMemoryId in connectedMemories)
                {
                    if (connectedMemoryId == memoryId)
                        continue;

                    if (!related.TryGetValue(connectedMemoryId, out var info))
                    {
                        info = (distance, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>(path));
                        related[connectedMemoryId] = info;
                    }
                    info.SharedEntities.Add(currentEntity);
                }
            }

            // Expand to related entities (1-hop in entity graph)
            if (distance < maxHops)
            {
                var entityTriples = await _entityStore.GetBySubjectAsync(currentEntity, sourceNode.UserId, cancellationToken) ?? [];
                var objectTriples = await _entityStore.GetByObjectAsync(currentEntity, sourceNode.UserId, cancellationToken) ?? [];

                foreach (var triple in entityTriples.Concat(objectTriples))
                {
                    var neighbor = triple.Subject.Equals(currentEntity, StringComparison.OrdinalIgnoreCase)
                        ? triple.ObjectValue
                        : triple.Subject;

                    if (!visited.Contains(neighbor))
                    {
                        var newPath = new List<string>(path) { neighbor };
                        frontier.Enqueue((neighbor, distance + 1, newPath));
                    }
                }
            }
        }

        // Fetch memories and build results
        var results = new List<RelatedMemory>();
        foreach (var (relatedId, (distance, sharedEntities, connectionPath)) in related
            .OrderBy(x => x.Value.Distance)
            .ThenByDescending(x => x.Value.SharedEntities.Count)
            .Take(topK))
        {
            var memory = await _memoryStore.GetByIdAsync(relatedId, cancellationToken);
            if (memory == null)
                continue;

            var strength = CalculateRelationshipStrength(distance, sharedEntities.Count, maxHops);
            results.Add(new RelatedMemory
            {
                Memory = memory,
                GraphDistance = distance,
                SharedEntities = sharedEntities.ToList(),
                RelationshipStrength = strength,
                ConnectionPath = connectionPath
            });
        }

        _logger.LogDebug("Found {Count} related memories for {MemoryId}", results.Count, memoryId);
        return results;
    }

    /// <inheritdoc />
    public async Task<MemorySubgraph> ExtractSubgraphAsync(
        IReadOnlyList<Guid> memoryIds,
        SubgraphOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SubgraphOptions();
        var stopwatch = Stopwatch.StartNew();

        var subgraphNodes = new List<MemoryGraphNode>();
        var subgraphEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subgraphTriples = new List<EntityTriple>();
        var memoryEntityEdges = new List<MemoryEntityEdge>();
        var visitedMemories = new HashSet<Guid>();
        var visitedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BFS from center memories
        var memoryFrontier = new Queue<(Guid MemoryId, int Depth)>();
        foreach (var memoryId in memoryIds)
        {
            memoryFrontier.Enqueue((memoryId, 0));
        }

        while (memoryFrontier.Count > 0 && subgraphNodes.Count < options.MaxMemories)
        {
            var (currentMemoryId, depth) = memoryFrontier.Dequeue();

            if (visitedMemories.Contains(currentMemoryId) || depth > options.MaxHops)
                continue;

            visitedMemories.Add(currentMemoryId);

            if (!_memoryNodes.TryGetValue(currentMemoryId, out var node))
                continue;

            subgraphNodes.Add(node);

            // Process connected entities
            foreach (var entity in node.ConnectedEntities)
            {
                if (subgraphEntities.Count >= options.MaxEntities)
                    break;

                subgraphEntities.Add(entity);

                // Determine entity role and create edge
                var role = await DetermineEntityRoleAsync(currentMemoryId, entity, cancellationToken);
                memoryEntityEdges.Add(new MemoryEntityEdge
                {
                    MemoryId = currentMemoryId,
                    EntityName = entity,
                    Role = role.Role,
                    Confidence = role.Confidence
                });

                // Get triples for this entity
                if (!visitedEntities.Contains(entity))
                {
                    visitedEntities.Add(entity);
                    var triples = await _entityStore.GetBySubjectAsync(entity, node.UserId, cancellationToken) ?? [];
                    var objTriples = await _entityStore.GetByObjectAsync(entity, node.UserId, cancellationToken) ?? [];

                    foreach (var triple in triples.Concat(objTriples))
                    {
                        if (triple.Confidence >= options.MinConfidence)
                        {
                            subgraphTriples.Add(triple);
                        }
                    }
                }

                // Expand to connected memories
                if (depth < options.MaxHops && _entityToMemories.TryGetValue(entity, out var connectedMemories))
                {
                    foreach (var connectedMemoryId in connectedMemories)
                    {
                        if (!visitedMemories.Contains(connectedMemoryId))
                        {
                            memoryFrontier.Enqueue((connectedMemoryId, depth + 1));
                        }
                    }
                }
            }
        }

        // Deduplicate triples
        subgraphTriples = subgraphTriples
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        stopwatch.Stop();

        var maxDepth = subgraphNodes.Count > 0
            ? visitedMemories.Count - memoryIds.Count
            : 0;

        var formattedContext = FormatSubgraphContext(subgraphNodes, subgraphTriples);

        _logger.LogInformation(
            "Subgraph extracted: {Memories} memories, {Entities} entities, {Triples} triples in {Duration}ms",
            subgraphNodes.Count, subgraphEntities.Count, subgraphTriples.Count, stopwatch.ElapsedMilliseconds);

        return new MemorySubgraph
        {
            CenterMemoryIds = memoryIds.ToList(),
            MemoryNodes = subgraphNodes,
            Entities = subgraphEntities.ToList(),
            Triples = subgraphTriples,
            MemoryEntityEdges = memoryEntityEdges,
            FormattedContext = formattedContext,
            Statistics = new SubgraphStatistics
            {
                MemoryCount = subgraphNodes.Count,
                EntityCount = subgraphEntities.Count,
                TripleCount = subgraphTriples.Count,
                EdgeCount = memoryEntityEdges.Count,
                MaxDepthReached = maxDepth,
                Duration = stopwatch.Elapsed
            }
        };
    }

    /// <inheritdoc />
    public Task<MemoryGraphNode?> GetMemoryNodeAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        _memoryNodes.TryGetValue(memoryId, out var node);
        return Task.FromResult(node);
    }

    /// <inheritdoc />
    public async Task UpdateMemoryGraphAsync(
        Guid memoryId,
        IReadOnlyList<EntityTriple> newEntities,
        CancellationToken cancellationToken = default)
    {
        // Remove old edges
        if (_memoryNodes.TryGetValue(memoryId, out var existingNode))
        {
            lock (_indexLock)
            {
                foreach (var entity in existingNode.ConnectedEntities)
                {
                    if (_entityToMemories.TryGetValue(entity, out var memorySet))
                    {
                        memorySet.Remove(memoryId);
                    }
                }
            }
        }

        // Get the memory to re-link
        var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
        if (memory != null)
        {
            await LinkMemoryToGraphAsync(memory, newEntities, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task UnlinkMemoryFromGraphAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        if (_memoryNodes.TryRemove(memoryId, out var node))
        {
            lock (_indexLock)
            {
                foreach (var entity in node.ConnectedEntities)
                {
                    if (_entityToMemories.TryGetValue(entity, out var memorySet))
                    {
                        memorySet.Remove(memoryId);
                    }
                }
            }

            _logger.LogDebug("Memory {MemoryId} unlinked from graph", memoryId);
        }

        return Task.CompletedTask;
    }

    #region Helper Methods

    private static float CalculateInitialImportance(MemoryUnit memory, IReadOnlyList<EntityTriple> entities)
    {
        // Base importance from entity count (more entities = more connected = more important)
        var entityScore = Math.Min(entities.Count / 10f, 0.5f);

        // Boost for high-confidence entities
        var avgConfidence = entities.Count > 0
            ? entities.Average(e => e.Confidence)
            : 0.5f;

        // Combine with memory's own importance indicators
        var typeBoost = memory.Type switch
        {
            MemoryType.Fact => 0.2f,
            MemoryType.Semantic => 0.15f,
            MemoryType.Episodic => 0.1f,
            _ => 0f
        };

        return Math.Min(entityScore + (avgConfidence * 0.3f) + typeBoost, 1.0f);
    }

    private static float CalculateRelationshipStrength(int distance, int sharedEntityCount, int maxHops)
    {
        // Distance decay
        var distanceFactor = 1.0f - (distance / (float)(maxHops + 1));

        // Shared entity boost
        var sharedBoost = Math.Min(sharedEntityCount / 5f, 0.5f);

        return distanceFactor * 0.7f + sharedBoost * 0.3f;
    }

    private async Task<(EntityRole Role, float Confidence)> DetermineEntityRoleAsync(
        Guid memoryId,
        string entity,
        CancellationToken cancellationToken)
    {
        if (!_memoryNodes.TryGetValue(memoryId, out var node))
            return (EntityRole.Mentioned, 0.5f);

        // Check if entity appears as subject or object in the memory's triples
        foreach (var tripleId in node.EntityTripleIds)
        {
            // For simplicity, check subject/object in the stored connection
            // In a full implementation, we'd query the triple store
        }

        // Default to mentioned with medium confidence
        return (EntityRole.Mentioned, 0.7f);
    }

    private static string FormatSubgraphContext(
        IReadOnlyList<MemoryGraphNode> nodes,
        IReadOnlyList<EntityTriple> triples)
    {
        var sb = new StringBuilder();

        if (nodes.Count > 0)
        {
            sb.AppendLine("## Memory Graph Context");
            sb.AppendLine();
            sb.AppendLine($"Connected memories: {nodes.Count}");

            // Group by community if available
            var communities = nodes
                .Where(n => n.CommunityId.HasValue)
                .GroupBy(n => n.CommunityId!.Value);

            foreach (var community in communities)
            {
                sb.AppendLine($"- Topic cluster {community.Key}: {community.Count()} memories");
            }
            sb.AppendLine();
        }

        if (triples.Count > 0)
        {
            sb.AppendLine("## Knowledge Facts");
            foreach (var triple in triples.Take(15))
            {
                sb.AppendLine($"- {triple.Subject} → {triple.Predicate} → {triple.ObjectValue}");
            }

            if (triples.Count > 15)
            {
                sb.AppendLine($"... and {triples.Count - 15} more facts");
            }
        }

        return sb.ToString();
    }

    #endregion
}
