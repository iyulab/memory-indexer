using MemoryIndexer.Core.Models;
using MemoryIndexer.Intelligence.KnowledgeGraph;

namespace MemoryIndexer.Intelligence.Graph;

/// <summary>
/// Interface for graph-based memory retrieval with multi-hop traversal support.
/// Combines semantic search with knowledge graph navigation for enhanced context.
/// </summary>
/// <remarks>
/// Research basis: Graphiti's temporal-aware graph traversal, LightRAG's entity-relation retrieval.
/// </remarks>
public interface IGraphRetriever
{
    /// <summary>
    /// Retrieves related entities within N hops from a starting entity.
    /// </summary>
    /// <param name="startEntity">The entity to start traversal from.</param>
    /// <param name="options">Traversal options including MaxHops (default: 2).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph traversal result with entities and paths.</returns>
    Task<GraphTraversalResult> TraverseAsync(
        string startEntity,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the shortest path between two entities.
    /// </summary>
    /// <param name="fromEntity">Source entity name.</param>
    /// <param name="toEntity">Target entity name.</param>
    /// <param name="options">Path finding options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path result with entities and relations.</returns>
    Task<GraphPathResult?> FindPathAsync(
        string fromEntity,
        string toEntity,
        GraphTraversalOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all facts (EntityTriples) related to an entity.
    /// </summary>
    /// <param name="entityName">The entity to query.</param>
    /// <param name="options">Query options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Related triples and context.</returns>
    Task<EntityFactsResult> GetEntityFactsAsync(
        string entityName,
        EntityQueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs hybrid retrieval combining semantic search with graph expansion.
    /// </summary>
    /// <param name="query">Natural language query.</param>
    /// <param name="userId">User ID for multi-tenant filtering.</param>
    /// <param name="options">Hybrid retrieval options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hybrid result with memories and graph context.</returns>
    Task<HybridGraphResult> HybridRetrieveAsync(
        string query,
        string userId,
        HybridGraphOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets entities most relevant to a query using semantic similarity.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="topK">Number of entities to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of relevant entities.</returns>
    Task<IReadOnlyList<ScoredEntity>> GetRelevantEntitiesAsync(
        string query,
        int topK = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for graph traversal operations.
/// </summary>
public sealed class GraphTraversalOptions
{
    /// <summary>
    /// Maximum number of hops (default: 2).
    /// </summary>
    public int MaxHops { get; init; } = 2;

    /// <summary>
    /// Maximum entities to visit (default: 100).
    /// </summary>
    public int MaxEntities { get; init; } = 100;

    /// <summary>
    /// Minimum confidence threshold for relations (default: 0.5).
    /// </summary>
    public float MinConfidence { get; init; } = 0.5f;

    /// <summary>
    /// Relation types to include (null = all).
    /// </summary>
    public HashSet<string>? IncludeRelationTypes { get; init; }

    /// <summary>
    /// Relation types to exclude.
    /// </summary>
    public HashSet<string>? ExcludeRelationTypes { get; init; }

    /// <summary>
    /// Point-in-time for temporal queries (default: now).
    /// Temporal validity filtering is always enabled - only facts valid at this time are included.
    /// </summary>
    public DateTime? AsOfDate { get; init; }

    /// <summary>
    /// User ID for multi-tenant filtering.
    /// </summary>
    public string? UserId { get; init; }
}

/// <summary>
/// Result of a graph traversal operation.
/// </summary>
public sealed class GraphTraversalResult
{
    /// <summary>
    /// Starting entity.
    /// </summary>
    public required string StartEntity { get; init; }

    /// <summary>
    /// All entities discovered during traversal.
    /// </summary>
    public IReadOnlyList<DiscoveredEntity> DiscoveredEntities { get; init; } = [];

    /// <summary>
    /// All relations traversed.
    /// </summary>
    public IReadOnlyList<TraversedRelation> TraversedRelations { get; init; } = [];

    /// <summary>
    /// Statistics about the traversal.
    /// </summary>
    public TraversalStatistics Statistics { get; init; } = new();
}

/// <summary>
/// An entity discovered during graph traversal.
/// </summary>
public sealed class DiscoveredEntity
{
    /// <summary>
    /// Entity name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Hop distance from starting entity.
    /// </summary>
    public int HopDistance { get; init; }

    /// <summary>
    /// Aggregated relevance score.
    /// </summary>
    public float RelevanceScore { get; init; }

    /// <summary>
    /// Associated EntityTriples.
    /// </summary>
    public IReadOnlyList<EntityTriple> Facts { get; init; } = [];

    /// <summary>
    /// Entity type if known.
    /// </summary>
    public EntityType? Type { get; init; }
}

/// <summary>
/// A relation traversed during graph navigation.
/// </summary>
public sealed class TraversedRelation
{
    /// <summary>
    /// Source entity.
    /// </summary>
    public required string FromEntity { get; init; }

    /// <summary>
    /// Target entity.
    /// </summary>
    public required string ToEntity { get; init; }

    /// <summary>
    /// Relation type/predicate.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Confidence score.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Hop level at which this relation was discovered.
    /// </summary>
    public int HopLevel { get; init; }
}

/// <summary>
/// Statistics from a graph traversal.
/// </summary>
public sealed class TraversalStatistics
{
    /// <summary>
    /// Number of entities visited.
    /// </summary>
    public int EntitiesVisited { get; init; }

    /// <summary>
    /// Number of relations traversed.
    /// </summary>
    public int RelationsTraversed { get; init; }

    /// <summary>
    /// Maximum hop depth reached.
    /// </summary>
    public int MaxDepthReached { get; init; }

    /// <summary>
    /// Time taken for traversal.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of path finding between two entities.
/// </summary>
public sealed class GraphPathResult
{
    /// <summary>
    /// Source entity.
    /// </summary>
    public required string FromEntity { get; init; }

    /// <summary>
    /// Target entity.
    /// </summary>
    public required string ToEntity { get; init; }

    /// <summary>
    /// Ordered sequence of entities in the path.
    /// </summary>
    public IReadOnlyList<string> PathEntities { get; init; } = [];

    /// <summary>
    /// Relations connecting the entities.
    /// </summary>
    public IReadOnlyList<TraversedRelation> PathRelations { get; init; } = [];

    /// <summary>
    /// Number of hops in the path.
    /// </summary>
    public int PathLength => PathRelations.Count;

    /// <summary>
    /// Whether a path was found.
    /// </summary>
    public bool PathFound => PathEntities.Count > 0;
}

/// <summary>
/// Options for entity fact queries.
/// </summary>
public sealed class EntityQueryOptions
{
    /// <summary>
    /// Include facts where entity is the subject.
    /// </summary>
    public bool IncludeAsSubject { get; init; } = true;

    /// <summary>
    /// Include facts where entity is the object.
    /// </summary>
    public bool IncludeAsObject { get; init; } = true;

    /// <summary>
    /// Filter by predicate types.
    /// </summary>
    public HashSet<string>? PredicateFilter { get; init; }

    /// <summary>
    /// Include only currently valid facts.
    /// </summary>
    public bool CurrentOnly { get; init; } = true;

    /// <summary>
    /// Point-in-time for historical queries.
    /// </summary>
    public DateTime? AsOfDate { get; init; }

    /// <summary>
    /// Maximum facts to return.
    /// </summary>
    public int MaxFacts { get; init; } = 50;

    /// <summary>
    /// User ID for multi-tenant filtering.
    /// </summary>
    public string? UserId { get; init; }
}

/// <summary>
/// Result of entity facts query.
/// </summary>
public sealed class EntityFactsResult
{
    /// <summary>
    /// The queried entity.
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Facts where entity is the subject.
    /// </summary>
    public IReadOnlyList<EntityTriple> SubjectFacts { get; init; } = [];

    /// <summary>
    /// Facts where entity is the object.
    /// </summary>
    public IReadOnlyList<EntityTriple> ObjectFacts { get; init; } = [];

    /// <summary>
    /// All unique predicates found.
    /// </summary>
    public IReadOnlyList<string> UniquePredicates { get; init; } = [];

    /// <summary>
    /// Related entities discovered.
    /// </summary>
    public IReadOnlyList<string> RelatedEntities { get; init; } = [];

    /// <summary>
    /// Total fact count.
    /// </summary>
    public int TotalFacts => SubjectFacts.Count + ObjectFacts.Count;
}

/// <summary>
/// Options for hybrid graph retrieval.
/// </summary>
public sealed class HybridGraphOptions
{
    /// <summary>
    /// Number of semantic search results.
    /// </summary>
    public int SemanticTopK { get; init; } = 10;

    /// <summary>
    /// Number of hops for graph expansion.
    /// </summary>
    public int GraphExpansionHops { get; init; } = 1;

    /// <summary>
    /// Weight for semantic vs graph scores (0-1).
    /// </summary>
    public float SemanticWeight { get; init; } = 0.6f;

    /// <summary>
    /// Minimum similarity for semantic matches.
    /// </summary>
    public float MinSemanticScore { get; init; } = 0.5f;
}

// Note: Graph context is always included in HybridGraphResult.
// FormattedContext provides a ready-to-use string for LLM consumption.

/// <summary>
/// Result of hybrid graph retrieval.
/// </summary>
public sealed class HybridGraphResult
{
    /// <summary>
    /// Original query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Memories found via semantic search.
    /// </summary>
    public IReadOnlyList<MemoryUnit> SemanticResults { get; init; } = [];

    /// <summary>
    /// Additional context from graph expansion.
    /// </summary>
    public IReadOnlyList<EntityTriple> GraphContext { get; init; } = [];

    /// <summary>
    /// Entities relevant to the query.
    /// </summary>
    public IReadOnlyList<ScoredEntity> RelevantEntities { get; init; } = [];

    /// <summary>
    /// Formatted context string for LLM consumption.
    /// </summary>
    public string FormattedContext { get; init; } = string.Empty;
}

/// <summary>
/// An entity with a relevance score.
/// </summary>
public sealed class ScoredEntity
{
    /// <summary>
    /// Entity name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Relevance score (0-1).
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Entity type if known.
    /// </summary>
    public EntityType? Type { get; init; }

    /// <summary>
    /// Number of associated facts.
    /// </summary>
    public int FactCount { get; init; }
}
