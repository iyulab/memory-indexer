using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for integrating memories into the knowledge graph.
/// Enables relationship-aware retrieval and community-based clustering.
/// </summary>
/// <remarks>
/// Research basis: Mem0g graph memory architecture, Graphiti temporal knowledge graphs.
/// Core value: Memories become nodes in the graph, enabling multi-hop traversal
/// from memory → entity → related memories.
/// </remarks>
public interface IMemoryGraphService
{
    /// <summary>
    /// Links a memory to the knowledge graph by creating edges to its extracted entities.
    /// </summary>
    /// <param name="memory">The memory to link.</param>
    /// <param name="entities">Entities extracted from the memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created memory graph node.</returns>
    Task<MemoryGraphNode> LinkMemoryToGraphAsync(
        MemoryUnit memory,
        IReadOnlyList<EntityTriple> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds memories related to a given memory through shared entities.
    /// </summary>
    /// <param name="memoryId">The memory to find relations for.</param>
    /// <param name="maxHops">Maximum graph hops (default: 2).</param>
    /// <param name="topK">Maximum related memories to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Related memories ranked by graph proximity.</returns>
    Task<IReadOnlyList<RelatedMemory>> FindRelatedMemoriesAsync(
        Guid memoryId,
        int maxHops = 2,
        int topK = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a subgraph centered on specific memories.
    /// </summary>
    /// <param name="memoryIds">Memory IDs to center the subgraph on.</param>
    /// <param name="options">Subgraph extraction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted subgraph with memories, entities, and relations.</returns>
    Task<MemorySubgraph> ExtractSubgraphAsync(
        IReadOnlyList<Guid> memoryIds,
        SubgraphOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the graph node for a memory if it exists.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory graph node or null if not linked.</returns>
    Task<MemoryGraphNode?> GetMemoryNodeAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the graph when a memory's entities change.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="newEntities">New extracted entities.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateMemoryGraphAsync(
        Guid memoryId,
        IReadOnlyList<EntityTriple> newEntities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a memory from the graph.
    /// </summary>
    /// <param name="memoryId">The memory ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnlinkMemoryFromGraphAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a memory as a node in the knowledge graph.
/// </summary>
public sealed class MemoryGraphNode
{
    /// <summary>
    /// The memory ID this node represents.
    /// </summary>
    public Guid MemoryId { get; init; }

    /// <summary>
    /// User ID for multi-tenant isolation.
    /// </summary>
    public string UserId { get; init; } = default!;

    /// <summary>
    /// Entities this memory is connected to.
    /// </summary>
    public IReadOnlyList<string> ConnectedEntities { get; init; } = [];

    /// <summary>
    /// Entity triples extracted from this memory.
    /// </summary>
    public IReadOnlyList<Guid> EntityTripleIds { get; init; } = [];

    /// <summary>
    /// Computed importance score based on graph centrality.
    /// </summary>
    public float ImportanceScore { get; set; }

    /// <summary>
    /// Community/cluster ID this memory belongs to (if computed).
    /// </summary>
    public int? CommunityId { get; set; }

    /// <summary>
    /// When this node was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this node was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A memory with its graph-based relationship information.
/// </summary>
public sealed class RelatedMemory
{
    /// <summary>
    /// The related memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Graph distance from the source memory (hops).
    /// </summary>
    public int GraphDistance { get; init; }

    /// <summary>
    /// Shared entities between source and this memory.
    /// </summary>
    public IReadOnlyList<string> SharedEntities { get; init; } = [];

    /// <summary>
    /// Relationship strength based on shared entities and graph proximity.
    /// </summary>
    public float RelationshipStrength { get; init; }

    /// <summary>
    /// Path of entities connecting the memories.
    /// </summary>
    public IReadOnlyList<string> ConnectionPath { get; init; } = [];
}

/// <summary>
/// Options for subgraph extraction.
/// </summary>
public sealed class SubgraphOptions
{
    /// <summary>
    /// Maximum hops from center memories.
    /// </summary>
    public int MaxHops { get; init; } = 2;

    /// <summary>
    /// Maximum entities to include.
    /// </summary>
    public int MaxEntities { get; init; } = 50;

    /// <summary>
    /// Maximum memories to include.
    /// </summary>
    public int MaxMemories { get; init; } = 20;

    /// <summary>
    /// Minimum relationship confidence.
    /// </summary>
    public float MinConfidence { get; init; } = 0.5f;

    /// <summary>
    /// Include temporal information in subgraph.
    /// </summary>
    public bool IncludeTemporalInfo { get; init; } = true;
}

/// <summary>
/// A subgraph centered on specific memories.
/// </summary>
public sealed class MemorySubgraph
{
    /// <summary>
    /// Center memory IDs.
    /// </summary>
    public IReadOnlyList<Guid> CenterMemoryIds { get; init; } = [];

    /// <summary>
    /// All memory nodes in the subgraph.
    /// </summary>
    public IReadOnlyList<MemoryGraphNode> MemoryNodes { get; init; } = [];

    /// <summary>
    /// All entities in the subgraph.
    /// </summary>
    public IReadOnlyList<string> Entities { get; init; } = [];

    /// <summary>
    /// Entity triples forming the subgraph edges.
    /// </summary>
    public IReadOnlyList<EntityTriple> Triples { get; init; } = [];

    /// <summary>
    /// Memory-to-entity edges.
    /// </summary>
    public IReadOnlyList<MemoryEntityEdge> MemoryEntityEdges { get; init; } = [];

    /// <summary>
    /// Formatted context for LLM consumption.
    /// </summary>
    public string FormattedContext { get; init; } = string.Empty;

    /// <summary>
    /// Statistics about the subgraph.
    /// </summary>
    public SubgraphStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Edge connecting a memory to an entity.
/// </summary>
public sealed class MemoryEntityEdge
{
    /// <summary>
    /// Source memory ID.
    /// </summary>
    public Guid MemoryId { get; init; }

    /// <summary>
    /// Target entity name.
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Role of the entity in the memory (subject, object, mentioned).
    /// </summary>
    public EntityRole Role { get; init; }

    /// <summary>
    /// Confidence of the connection.
    /// </summary>
    public float Confidence { get; init; }
}

/// <summary>
/// Role of an entity in a memory.
/// </summary>
public enum EntityRole
{
    /// <summary>
    /// Entity is the subject of a fact.
    /// </summary>
    Subject,

    /// <summary>
    /// Entity is the object of a fact.
    /// </summary>
    Object,

    /// <summary>
    /// Entity is mentioned but not in a specific triple.
    /// </summary>
    Mentioned
}

/// <summary>
/// Statistics about an extracted subgraph.
/// </summary>
public sealed class SubgraphStatistics
{
    /// <summary>
    /// Number of memory nodes.
    /// </summary>
    public int MemoryCount { get; init; }

    /// <summary>
    /// Number of entities.
    /// </summary>
    public int EntityCount { get; init; }

    /// <summary>
    /// Number of entity triples.
    /// </summary>
    public int TripleCount { get; init; }

    /// <summary>
    /// Number of memory-entity edges.
    /// </summary>
    public int EdgeCount { get; init; }

    /// <summary>
    /// Maximum depth reached.
    /// </summary>
    public int MaxDepthReached { get; init; }

    /// <summary>
    /// Extraction duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
