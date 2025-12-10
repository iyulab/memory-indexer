using System.ComponentModel;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Intelligence.KnowledgeGraph;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Mcp.Tools;

/// <summary>
/// MCP tools for knowledge graph operations.
/// Enables entity extraction, relationship discovery, and graph queries.
/// </summary>
[McpServerToolType]
public sealed class KnowledgeGraphTools
{
    private readonly IKnowledgeGraphService _knowledgeGraphService;
    private readonly IMemoryStore _memoryStore;

    // In-memory graph cache per user (in production, use distributed cache)
    private static readonly Dictionary<string, KnowledgeGraph> GraphCache = new();
    private static readonly object CacheLock = new();

    private const string DefaultUserId = "default";

    public KnowledgeGraphTools(
        IKnowledgeGraphService knowledgeGraphService,
        IMemoryStore memoryStore)
    {
        _knowledgeGraphService = knowledgeGraphService;
        _memoryStore = memoryStore;
    }

    /// <summary>
    /// Extract entities from text content.
    /// Identifies people, organizations, dates, emails, URLs, and more.
    /// </summary>
    /// <param name="content">The text content to analyze.</param>
    /// <returns>List of extracted entities with types and confidence scores.</returns>
    [McpServerTool, Description("Extract entities (people, organizations, dates, etc.) from text content")]
    public async Task<ExtractEntitiesResult> ExtractEntities(
        [Description("Text content to extract entities from")] string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ExtractEntitiesResult
            {
                Success = false,
                Message = "Content cannot be empty"
            };
        }

        var entities = await _knowledgeGraphService.ExtractEntitiesAsync(content);

        return new ExtractEntitiesResult
        {
            Success = true,
            Entities = entities.Select(e => new EntityInfo
            {
                Id = e.Id,
                Name = e.Name,
                Type = e.Type.ToString(),
                Confidence = e.Confidence,
                OccurrenceCount = e.OccurrenceCount
            }).ToList(),
            Message = $"Extracted {entities.Count} entities"
        };
    }

    /// <summary>
    /// Extract relationships between entities in text.
    /// Identifies connections like "works at", "located in", "created by", etc.
    /// </summary>
    /// <param name="content">The text content to analyze.</param>
    /// <returns>List of relationships between entities.</returns>
    [McpServerTool, Description("Extract relationships between entities in text content")]
    public async Task<ExtractRelationsResult> ExtractRelations(
        [Description("Text content to extract relations from")] string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ExtractRelationsResult
            {
                Success = false,
                Message = "Content cannot be empty"
            };
        }

        var entities = await _knowledgeGraphService.ExtractEntitiesAsync(content);
        var relations = await _knowledgeGraphService.ExtractRelationsAsync(content, entities);

        return new ExtractRelationsResult
        {
            Success = true,
            Relations = relations.Select(r => new RelationInfo
            {
                SourceName = r.Source.Name,
                SourceType = r.Source.Type.ToString(),
                TargetName = r.Target.Name,
                TargetType = r.Target.Type.ToString(),
                RelationType = r.RelationType,
                Confidence = r.Confidence,
                Evidence = r.Evidence
            }).ToList(),
            Message = $"Found {relations.Count} relationships between {entities.Count} entities"
        };
    }

    /// <summary>
    /// Build a knowledge graph from user's memories.
    /// Creates an interconnected graph of entities and their relationships.
    /// </summary>
    /// <param name="userId">User ID to build graph for.</param>
    /// <param name="rebuildIfExists">Whether to rebuild if graph already exists.</param>
    /// <returns>Summary of the built knowledge graph.</returns>
    [McpServerTool, Description("Build a knowledge graph from stored memories")]
    public async Task<BuildGraphResult> BuildKnowledgeGraph(
        [Description("User ID (optional, defaults to 'default')")] string? userId = null,
        [Description("Rebuild graph even if it exists")] bool rebuildIfExists = false)
    {
        var uid = userId ?? DefaultUserId;

        lock (CacheLock)
        {
            if (!rebuildIfExists && GraphCache.TryGetValue(uid, out var existingGraph))
            {
                return new BuildGraphResult
                {
                    Success = true,
                    EntityCount = existingGraph.EntityCount,
                    RelationCount = existingGraph.RelationCount,
                    Message = "Using cached knowledge graph",
                    WasRebuilt = false
                };
            }
        }

        var memories = await _memoryStore.GetAllAsync(uid);
        var contents = memories.Select(m => m.Content).ToList();

        if (contents.Count == 0)
        {
            return new BuildGraphResult
            {
                Success = false,
                Message = "No memories found for user"
            };
        }

        var graph = await _knowledgeGraphService.BuildGraphAsync(contents);

        lock (CacheLock)
        {
            GraphCache[uid] = graph;
        }

        return new BuildGraphResult
        {
            Success = true,
            EntityCount = graph.EntityCount,
            RelationCount = graph.RelationCount,
            TopEntities = graph.Entities.Values
                .OrderByDescending(e => e.OccurrenceCount)
                .Take(10)
                .Select(e => new EntityInfo
                {
                    Id = e.Id,
                    Name = e.Name,
                    Type = e.Type.ToString(),
                    Confidence = e.Confidence,
                    OccurrenceCount = e.OccurrenceCount
                }).ToList(),
            Message = $"Built knowledge graph from {contents.Count} memories",
            WasRebuilt = true
        };
    }

    /// <summary>
    /// Query the knowledge graph for entities matching a search query.
    /// Returns matching entities, their relationships, and connected entities.
    /// </summary>
    /// <param name="query">Search query for entities.</param>
    /// <param name="userId">User ID whose graph to query.</param>
    /// <param name="maxResults">Maximum entities to return.</param>
    /// <returns>Matching entities and their relationships.</returns>
    [McpServerTool, Description("Query the knowledge graph for entities and relationships")]
    public async Task<QueryGraphResult> QueryKnowledgeGraph(
        [Description("Search query for entities")] string query,
        [Description("User ID (optional)")] string? userId = null,
        [Description("Maximum results to return")] int maxResults = 10)
    {
        var uid = userId ?? DefaultUserId;

        KnowledgeGraph? graph;
        lock (CacheLock)
        {
            GraphCache.TryGetValue(uid, out graph);
        }

        if (graph == null)
        {
            // Try to build graph first
            var buildResult = await BuildKnowledgeGraph(uid);
            if (!buildResult.Success)
            {
                return new QueryGraphResult
                {
                    Success = false,
                    Message = "No knowledge graph available. Store some memories first."
                };
            }

            lock (CacheLock)
            {
                GraphCache.TryGetValue(uid, out graph);
            }
        }

        var result = _knowledgeGraphService.QueryGraph(query, graph!, maxResults);

        return new QueryGraphResult
        {
            Success = true,
            MatchedEntities = result.MatchedEntities.Select(e => new EntityInfo
            {
                Id = e.Id,
                Name = e.Name,
                Type = e.Type.ToString(),
                Confidence = e.Confidence,
                OccurrenceCount = e.OccurrenceCount,
                RelevanceScore = result.RelevanceScores.GetValueOrDefault(e.Id)
            }).ToList(),
            RelatedRelations = result.RelatedRelations.Select(r => new RelationInfo
            {
                SourceName = r.Source.Name,
                SourceType = r.Source.Type.ToString(),
                TargetName = r.Target.Name,
                TargetType = r.Target.Type.ToString(),
                RelationType = r.RelationType,
                Confidence = r.Confidence
            }).ToList(),
            RelatedEntities = result.RelatedEntities.Select(e => new EntityInfo
            {
                Id = e.Id,
                Name = e.Name,
                Type = e.Type.ToString(),
                OccurrenceCount = e.OccurrenceCount
            }).ToList(),
            Message = $"Found {result.MatchedEntities.Count} matching entities"
        };
    }

    /// <summary>
    /// Get statistics about the knowledge graph.
    /// </summary>
    /// <param name="userId">User ID whose graph to analyze.</param>
    /// <returns>Graph statistics including entity and relation counts by type.</returns>
    [McpServerTool, Description("Get statistics about the knowledge graph")]
    public Task<GraphStatsResult> GetGraphStats(
        [Description("User ID (optional)")] string? userId = null)
    {
        var uid = userId ?? DefaultUserId;

        KnowledgeGraph? graph;
        lock (CacheLock)
        {
            GraphCache.TryGetValue(uid, out graph);
        }

        if (graph == null)
        {
            return Task.FromResult(new GraphStatsResult
            {
                Success = false,
                Message = "No knowledge graph exists. Call BuildKnowledgeGraph first."
            });
        }

        var entityCountsByType = graph.TypeIndex
            .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.Count);

        var relationCountsByType = graph.Relations
            .GroupBy(r => r.RelationType)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult(new GraphStatsResult
        {
            Success = true,
            TotalEntities = graph.EntityCount,
            TotalRelations = graph.RelationCount,
            EntitiesByType = entityCountsByType,
            RelationsByType = relationCountsByType,
            CreatedAt = graph.CreatedAt,
            UpdatedAt = graph.UpdatedAt,
            Message = "Graph statistics retrieved"
        });
    }

    /// <summary>
    /// Clear the cached knowledge graph for a user.
    /// </summary>
    /// <param name="userId">User ID whose graph to clear.</param>
    /// <returns>Result of the clear operation.</returns>
    [McpServerTool, Description("Clear the cached knowledge graph")]
    public Task<ClearGraphResult> ClearKnowledgeGraph(
        [Description("User ID (optional)")] string? userId = null)
    {
        var uid = userId ?? DefaultUserId;

        bool removed;
        lock (CacheLock)
        {
            removed = GraphCache.Remove(uid);
        }

        return Task.FromResult(new ClearGraphResult
        {
            Success = true,
            WasCleared = removed,
            Message = removed ? "Knowledge graph cleared" : "No knowledge graph existed"
        });
    }
}

#region Result Types

public sealed class ExtractEntitiesResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<EntityInfo> Entities { get; set; } = [];
}

public sealed class ExtractRelationsResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<RelationInfo> Relations { get; set; } = [];
}

public sealed class BuildGraphResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int EntityCount { get; set; }
    public int RelationCount { get; set; }
    public List<EntityInfo> TopEntities { get; set; } = [];
    public bool WasRebuilt { get; set; }
}

public sealed class QueryGraphResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<EntityInfo> MatchedEntities { get; set; } = [];
    public List<RelationInfo> RelatedRelations { get; set; } = [];
    public List<EntityInfo> RelatedEntities { get; set; } = [];
}

public sealed class GraphStatsResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TotalEntities { get; set; }
    public int TotalRelations { get; set; }
    public Dictionary<string, int> EntitiesByType { get; set; } = [];
    public Dictionary<string, int> RelationsByType { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ClearGraphResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool WasCleared { get; set; }
}

public sealed class EntityInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public float Confidence { get; set; }
    public int OccurrenceCount { get; set; }
    public float RelevanceScore { get; set; }
}

public sealed class RelationInfo
{
    public string SourceName { get; set; } = default!;
    public string SourceType { get; set; } = default!;
    public string TargetName { get; set; } = default!;
    public string TargetType { get; set; } = default!;
    public string RelationType { get; set; } = default!;
    public float Confidence { get; set; }
    public string? Evidence { get; set; }
}

#endregion
