using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for graph-based memory operations including community detection,
/// importance propagation, and relationship traversal.
/// </summary>
[McpServerToolType]
public sealed class GraphTraversalTools
{
    private readonly IMemoryGraphService _graphService;
    private readonly IImportancePropagator _importancePropagator;
    private readonly ICommunityDetector _communityDetector;

    private const string DefaultUserId = "default";

    public GraphTraversalTools(
        IMemoryGraphService graphService,
        IImportancePropagator importancePropagator,
        ICommunityDetector communityDetector)
    {
        _graphService = graphService;
        _importancePropagator = importancePropagator;
        _communityDetector = communityDetector;
    }

    #region Community Detection

    /// <summary>
    /// Detect communities (clusters) in the memory graph using Label Propagation algorithm.
    /// Useful for organizing memories into thematic groups.
    /// </summary>
    /// <param name="maxIterations">Maximum iterations for label propagation (1-100).</param>
    /// <param name="minCommunitySize">Minimum members for a valid community.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Community detection result with cluster assignments.</returns>
    [McpServerTool]
    [Description("Detect communities in the memory graph. Groups related memories into thematic clusters.")]
    public async Task<DetectCommunitiesToolResult> DetectCommunities(
        [Description("Max iterations (1-100)")] int maxIterations = 20,
        [Description("Min community size")] int minCommunitySize = 2,
        CancellationToken cancellationToken = default)
    {
        var options = new CommunityDetectionOptions
        {
            MaxIterations = Math.Clamp(maxIterations, 1, 100),
            MinCommunitySize = Math.Max(1, minCommunitySize)
        };

        var result = await _communityDetector.DetectCommunitiesAsync(
            DefaultUserId, options, cancellationToken);

        // Build community info from CommunitySizes dictionary
        var communities = result.CommunitySizes.Select(kv => new CommunityInfo
        {
            CommunityId = kv.Key,
            MemberCount = kv.Value
        }).ToList();

        return new DetectCommunitiesToolResult
        {
            Success = true,
            CommunityCount = result.CommunityCount,
            TotalMemories = result.MemoryAssignments.Count,
            ConvergedIterations = result.IterationsToConverge,
            Modularity = result.Modularity,
            Communities = communities,
            Message = $"Detected {result.CommunityCount} communities across {result.MemoryAssignments.Count} memories."
        };
    }

    /// <summary>
    /// Get all memories belonging to a specific community.
    /// </summary>
    /// <param name="communityId">The community ID to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of memories in the community.</returns>
    [McpServerTool]
    [Description("Get all memories in a specific community cluster.")]
    public async Task<GetCommunityMemoriesToolResult> GetCommunityMemories(
        [Description("Community ID")] int communityId,
        CancellationToken cancellationToken = default)
    {
        var memories = await _communityDetector.GetCommunityMemoriesAsync(
            communityId, DefaultUserId, cancellationToken);

        return new GetCommunityMemoriesToolResult
        {
            Success = true,
            CommunityId = communityId,
            Count = memories.Count,
            Memories = memories.Select(m => new MemoryInfo
            {
                Id = m.Id.ToString(),
                Content = m.Content,
                Type = m.Type.ToString().ToLowerInvariant(),
                Tier = m.Tier.ToString().ToLowerInvariant(),
                Importance = m.ImportanceScore,
                CreatedAt = m.CreatedAt
            }).ToList(),
            Message = $"Found {memories.Count} memories in community {communityId}."
        };
    }

    /// <summary>
    /// Get a summary description of a community based on its members.
    /// </summary>
    /// <param name="communityId">The community ID to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Community summary with topic label and key entities.</returns>
    [McpServerTool]
    [Description("Get a summary description of a community cluster.")]
    public async Task<CommunitySummaryToolResult> GetCommunitySummary(
        [Description("Community ID")] int communityId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _communityDetector.GetCommunitySummaryAsync(
            communityId, DefaultUserId, cancellationToken);

        return new CommunitySummaryToolResult
        {
            Success = true,
            CommunityId = summary.CommunityId,
            TopicLabel = summary.TopicLabel,
            KeyEntities = summary.KeyEntities.ToList(),
            MemoryCount = summary.MemoryCount,
            EntityCount = summary.EntityCount,
            CommonPredicates = summary.CommonPredicates.ToList(),
            Message = $"Community {communityId}: {summary.TopicLabel}"
        };
    }

    #endregion

    #region Importance Propagation

    /// <summary>
    /// Compute importance scores for all entities using PageRank algorithm.
    /// Identifies the most significant entities in the knowledge graph.
    /// </summary>
    /// <param name="dampingFactor">PageRank damping factor (0.1-0.99).</param>
    /// <param name="maxIterations">Maximum PageRank iterations (1-100).</param>
    /// <param name="tolerance">Convergence tolerance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Importance computation result.</returns>
    [McpServerTool]
    [Description("Compute entity importance using PageRank. Identifies key entities in the knowledge graph.")]
    public async Task<ComputeImportanceToolResult> ComputeImportance(
        [Description("Damping factor (0.1-0.99)")] float dampingFactor = 0.85f,
        [Description("Max iterations (1-100)")] int maxIterations = 50,
        [Description("Convergence tolerance")] float tolerance = 0.0001f,
        CancellationToken cancellationToken = default)
    {
        var options = new ImportanceOptions
        {
            DampingFactor = Math.Clamp(dampingFactor, 0.1f, 0.99f),
            MaxIterations = Math.Clamp(maxIterations, 1, 100),
            ConvergenceThreshold = Math.Max(0.000001f, tolerance)
        };

        var result = await _importancePropagator.ComputeImportanceAsync(
            DefaultUserId, options, cancellationToken);

        // Get top 10 entities from the score dictionary
        var topEntities = result.EntityScores
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select((kv, index) => new EntityImportanceInfo
            {
                EntityName = kv.Key,
                Score = kv.Value,
                Rank = index + 1
            })
            .ToList();

        return new ComputeImportanceToolResult
        {
            Success = true,
            EntityCount = result.EntityCount,
            IterationsUsed = result.Iterations,
            Converged = result.Converged,
            FinalResidual = result.FinalDifference,
            TopEntities = topEntities,
            Message = $"Computed importance for {result.EntityCount} entities in {result.Iterations} iterations."
        };
    }

    /// <summary>
    /// Get the importance score for a specific entity.
    /// </summary>
    /// <param name="entityName">The entity name to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entity importance score.</returns>
    [McpServerTool]
    [Description("Get the importance score for a specific entity.")]
    public async Task<GetEntityImportanceToolResult> GetEntityImportance(
        [Description("Entity name")] string entityName,
        CancellationToken cancellationToken = default)
    {
        var score = await _importancePropagator.GetEntityImportanceAsync(
            entityName, DefaultUserId, cancellationToken);

        return new GetEntityImportanceToolResult
        {
            Success = true,
            EntityName = entityName,
            Score = score,
            Found = score.HasValue,
            Message = score.HasValue
                ? $"Entity '{entityName}' has importance score: {score.Value:F4}"
                : $"Entity '{entityName}' not found in the graph."
        };
    }

    /// <summary>
    /// Get the top-K most important entities.
    /// </summary>
    /// <param name="topK">Number of entities to return (1-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of important entities.</returns>
    [McpServerTool]
    [Description("Get the most important entities ranked by PageRank score.")]
    public async Task<GetTopEntitiesToolResult> GetTopEntities(
        [Description("Number to return (1-100)")] int topK = 20,
        CancellationToken cancellationToken = default)
    {
        var entities = await _importancePropagator.GetTopEntitiesAsync(
            DefaultUserId, Math.Clamp(topK, 1, 100), cancellationToken);

        return new GetTopEntitiesToolResult
        {
            Success = true,
            Count = entities.Count,
            Entities = entities.Select(e => new EntityImportanceInfo
            {
                EntityName = e.EntityName,
                Score = e.Score,
                Rank = e.Rank,
                MemoryConnectionCount = e.MemoryConnectionCount
            }).ToList(),
            Message = $"Top {entities.Count} entities by importance."
        };
    }

    #endregion

    #region Memory Graph Traversal

    /// <summary>
    /// Find memories related to a given memory through shared entities.
    /// </summary>
    /// <param name="memoryId">The memory ID to find relations for.</param>
    /// <param name="maxHops">Maximum graph hops (1-5).</param>
    /// <param name="topK">Maximum related memories to return (1-50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Related memories ranked by graph proximity.</returns>
    [McpServerTool]
    [Description("Find memories related to a given memory through shared entities.")]
    public async Task<FindRelatedMemoriesToolResult> FindRelatedMemories(
        [Description("Source memory ID")] string memoryId,
        [Description("Max graph hops (1-5)")] int maxHops = 2,
        [Description("Max results (1-50)")] int topK = 10,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(memoryId, out var id))
        {
            return new FindRelatedMemoriesToolResult
            {
                Success = false,
                Message = "Invalid memory ID format."
            };
        }

        var related = await _graphService.FindRelatedMemoriesAsync(
            id, Math.Clamp(maxHops, 1, 5), Math.Clamp(topK, 1, 50), cancellationToken);

        return new FindRelatedMemoriesToolResult
        {
            Success = true,
            SourceMemoryId = memoryId,
            Count = related.Count,
            RelatedMemories = related.Select(r => new RelatedMemoryInfo
            {
                MemoryId = r.Memory.Id.ToString(),
                Content = r.Memory.Content,
                RelationshipStrength = r.RelationshipStrength,
                GraphDistance = r.GraphDistance,
                SharedEntities = r.SharedEntities.ToList(),
                ConnectionPath = r.ConnectionPath.ToList()
            }).ToList(),
            Message = $"Found {related.Count} memories related to {memoryId}."
        };
    }

    /// <summary>
    /// Extract a subgraph centered on specific memories.
    /// Returns memories, entities, and their relationships.
    /// </summary>
    /// <param name="memoryIds">Comma-separated memory IDs to center on.</param>
    /// <param name="maxHops">Maximum hops from center memories (1-3).</param>
    /// <param name="maxMemories">Maximum memories in subgraph (1-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted subgraph with memories, entities, and relations.</returns>
    [McpServerTool]
    [Description("Extract a subgraph centered on specific memories. Returns nodes and relationships.")]
    public async Task<ExtractSubgraphToolResult> ExtractSubgraph(
        [Description("Comma-separated memory IDs")] string memoryIds,
        [Description("Max hops from center (1-3)")] int maxHops = 2,
        [Description("Max memories (1-100)")] int maxMemories = 20,
        CancellationToken cancellationToken = default)
    {
        var ids = memoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (ids.Count == 0)
        {
            return new ExtractSubgraphToolResult
            {
                Success = false,
                Message = "No valid memory IDs provided."
            };
        }

        var options = new SubgraphOptions
        {
            MaxHops = Math.Clamp(maxHops, 1, 3),
            MaxMemories = Math.Clamp(maxMemories, 1, 100)
        };

        var subgraph = await _graphService.ExtractSubgraphAsync(ids, options, cancellationToken);

        return new ExtractSubgraphToolResult
        {
            Success = true,
            CenterMemoryIds = ids.Select(id => id.ToString()).ToList(),
            MemoryNodeCount = subgraph.MemoryNodes.Count,
            EntityCount = subgraph.Entities.Count,
            TripleCount = subgraph.Triples.Count,
            EdgeCount = subgraph.MemoryEntityEdges.Count,
            FormattedContext = subgraph.FormattedContext,
            Statistics = new SubgraphStats
            {
                MemoryCount = subgraph.Statistics.MemoryCount,
                EntityCount = subgraph.Statistics.EntityCount,
                TripleCount = subgraph.Statistics.TripleCount,
                EdgeCount = subgraph.Statistics.EdgeCount,
                MaxDepthReached = subgraph.Statistics.MaxDepthReached
            },
            Message = $"Extracted subgraph with {subgraph.MemoryNodes.Count} memories and {subgraph.Entities.Count} entities."
        };
    }

    #endregion
}

#region Result Types

/// <summary>
/// Result of community detection.
/// </summary>
public sealed class DetectCommunitiesToolResult
{
    public bool Success { get; init; }
    public int CommunityCount { get; init; }
    public int TotalMemories { get; init; }
    public int ConvergedIterations { get; init; }
    public float Modularity { get; init; }
    public List<CommunityInfo>? Communities { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Community information.
/// </summary>
public sealed class CommunityInfo
{
    public int CommunityId { get; init; }
    public int MemberCount { get; init; }
    public List<string>? CentralEntities { get; init; }
    public string? TopicLabel { get; init; }
}

/// <summary>
/// Result of get community memories.
/// </summary>
public sealed class GetCommunityMemoriesToolResult
{
    public bool Success { get; init; }
    public int CommunityId { get; init; }
    public int Count { get; init; }
    public List<MemoryInfo>? Memories { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Memory information.
/// </summary>
public sealed class MemoryInfo
{
    public string? Id { get; init; }
    public string? Content { get; init; }
    public string? Type { get; init; }
    public string? Tier { get; init; }
    public float Importance { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Result of community summary.
/// </summary>
public sealed class CommunitySummaryToolResult
{
    public bool Success { get; init; }
    public int CommunityId { get; init; }
    public string? TopicLabel { get; init; }
    public List<string>? KeyEntities { get; init; }
    public int MemoryCount { get; init; }
    public int EntityCount { get; init; }
    public List<string>? CommonPredicates { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of importance computation.
/// </summary>
public sealed class ComputeImportanceToolResult
{
    public bool Success { get; init; }
    public int EntityCount { get; init; }
    public int IterationsUsed { get; init; }
    public bool Converged { get; init; }
    public float FinalResidual { get; init; }
    public List<EntityImportanceInfo>? TopEntities { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Entity importance information.
/// </summary>
public sealed class EntityImportanceInfo
{
    public string? EntityName { get; init; }
    public float Score { get; init; }
    public int Rank { get; init; }
    public int MemoryConnectionCount { get; init; }
}

/// <summary>
/// Result of get entity importance.
/// </summary>
public sealed class GetEntityImportanceToolResult
{
    public bool Success { get; init; }
    public string? EntityName { get; init; }
    public float? Score { get; init; }
    public bool Found { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of get top entities.
/// </summary>
public sealed class GetTopEntitiesToolResult
{
    public bool Success { get; init; }
    public int Count { get; init; }
    public List<EntityImportanceInfo>? Entities { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of find related memories.
/// </summary>
public sealed class FindRelatedMemoriesToolResult
{
    public bool Success { get; init; }
    public string? SourceMemoryId { get; init; }
    public int Count { get; init; }
    public List<RelatedMemoryInfo>? RelatedMemories { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Related memory information.
/// </summary>
public sealed class RelatedMemoryInfo
{
    public string? MemoryId { get; init; }
    public string? Content { get; init; }
    public float RelationshipStrength { get; init; }
    public int GraphDistance { get; init; }
    public List<string>? SharedEntities { get; init; }
    public List<string>? ConnectionPath { get; init; }
}

/// <summary>
/// Result of extract subgraph.
/// </summary>
public sealed class ExtractSubgraphToolResult
{
    public bool Success { get; init; }
    public List<string>? CenterMemoryIds { get; init; }
    public int MemoryNodeCount { get; init; }
    public int EntityCount { get; init; }
    public int TripleCount { get; init; }
    public int EdgeCount { get; init; }
    public string? FormattedContext { get; init; }
    public SubgraphStats? Statistics { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Subgraph statistics.
/// </summary>
public sealed class SubgraphStats
{
    public int MemoryCount { get; init; }
    public int EntityCount { get; init; }
    public int TripleCount { get; init; }
    public int EdgeCount { get; init; }
    public int MaxDepthReached { get; init; }
}

#endregion
