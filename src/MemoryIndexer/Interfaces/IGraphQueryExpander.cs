using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Expands queries using knowledge graph structure for improved retrieval.
/// </summary>
/// <remarks>
/// Research basis: Graph-based query expansion (Mem0g, LightRAG).
/// Uses entity relationships to include related context in retrieval.
/// </remarks>
public interface IGraphQueryExpander
{
    /// <summary>
    /// Expands a query by incorporating related entities from the knowledge graph.
    /// </summary>
    /// <param name="query">Original user query.</param>
    /// <param name="userId">User ID for multi-tenant isolation.</param>
    /// <param name="options">Expansion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expanded query with graph-derived context.</returns>
    Task<ExpandedQuery> ExpandQueryAsync(
        string query,
        string userId,
        QueryExpansionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets entities mentioned or implied by a query.
    /// </summary>
    /// <param name="query">The query to analyze.</param>
    /// <param name="userId">User ID for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entities relevant to the query.</returns>
    Task<IReadOnlyList<QueryEntity>> ExtractQueryEntitiesAsync(
        string query,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates sub-queries for multi-hop graph traversal.
    /// </summary>
    /// <param name="query">Original query.</param>
    /// <param name="entities">Entities identified in the query.</param>
    /// <param name="options">Generation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sub-queries for graph-based retrieval.</returns>
    Task<IReadOnlyList<GraphSubQuery>> GenerateSubQueriesAsync(
        string query,
        IReadOnlyList<QueryEntity> entities,
        SubQueryOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for query expansion.
/// </summary>
public sealed class QueryExpansionOptions
{
    /// <summary>
    /// Maximum graph hops for expansion.
    /// </summary>
    public int MaxHops { get; init; } = 2;

    /// <summary>
    /// Maximum related entities to include.
    /// </summary>
    public int MaxRelatedEntities { get; init; } = 10;

    /// <summary>
    /// Minimum importance score for including an entity.
    /// </summary>
    public float MinImportanceScore { get; init; } = 0.1f;

    /// <summary>
    /// Whether to include community context.
    /// </summary>
    public bool IncludeCommunityContext { get; init; } = true;

    /// <summary>
    /// Whether to boost high-importance entities.
    /// </summary>
    public bool ApplyImportanceBoost { get; init; } = true;

    /// <summary>
    /// Maximum tokens for expanded query.
    /// </summary>
    public int MaxExpansionTokens { get; init; } = 500;
}

/// <summary>
/// Result of query expansion.
/// </summary>
public sealed class ExpandedQuery
{
    /// <summary>
    /// Original query.
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// Expanded query with graph context.
    /// </summary>
    public required string ExpandedText { get; init; }

    /// <summary>
    /// Entities mentioned in the query.
    /// </summary>
    public IReadOnlyList<QueryEntity> MentionedEntities { get; init; } = [];

    /// <summary>
    /// Related entities added through graph expansion.
    /// </summary>
    public IReadOnlyList<QueryEntity> RelatedEntities { get; init; } = [];

    /// <summary>
    /// Relevant facts from the graph.
    /// </summary>
    public IReadOnlyList<EntityTriple> RelevantFacts { get; init; } = [];

    /// <summary>
    /// Community context if available.
    /// </summary>
    public string? CommunityContext { get; init; }

    /// <summary>
    /// Sub-queries for structured retrieval.
    /// </summary>
    public IReadOnlyList<GraphSubQuery> SubQueries { get; init; } = [];

    /// <summary>
    /// Expansion statistics.
    /// </summary>
    public ExpansionStatistics Statistics { get; init; } = new();
}

/// <summary>
/// An entity identified in or related to a query.
/// </summary>
public sealed class QueryEntity
{
    /// <summary>
    /// Entity name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Importance score from PageRank.
    /// </summary>
    public float ImportanceScore { get; init; }

    /// <summary>
    /// How the entity relates to the query.
    /// </summary>
    public EntityRelation Relation { get; init; }

    /// <summary>
    /// Graph distance from query entities (0 = directly mentioned).
    /// </summary>
    public int GraphDistance { get; init; }

    /// <summary>
    /// Entity type if known.
    /// </summary>
    public EntityType? Type { get; init; }

    /// <summary>
    /// Boost factor for retrieval ranking.
    /// </summary>
    public float RetrievalBoost { get; init; } = 1.0f;
}

/// <summary>
/// How an entity relates to the query.
/// </summary>
public enum EntityRelation
{
    /// <summary>
    /// Directly mentioned in the query.
    /// </summary>
    Mentioned,

    /// <summary>
    /// Implied by query context.
    /// </summary>
    Implied,

    /// <summary>
    /// Related through graph connection.
    /// </summary>
    GraphRelated,

    /// <summary>
    /// In the same community/topic cluster.
    /// </summary>
    CommunityRelated
}

/// <summary>
/// A sub-query for structured graph retrieval.
/// </summary>
public sealed class GraphSubQuery
{
    /// <summary>
    /// The sub-query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Type of sub-query.
    /// </summary>
    public SubQueryType Type { get; init; }

    /// <summary>
    /// Target entities for this sub-query.
    /// </summary>
    public IReadOnlyList<string> TargetEntities { get; init; } = [];

    /// <summary>
    /// Relationship type to explore.
    /// </summary>
    public string? RelationType { get; init; }

    /// <summary>
    /// Priority for this sub-query (higher = more important).
    /// </summary>
    public float Priority { get; init; } = 1.0f;
}

/// <summary>
/// Type of sub-query for graph retrieval.
/// </summary>
public enum SubQueryType
{
    /// <summary>
    /// Find facts about an entity.
    /// </summary>
    EntityFacts,

    /// <summary>
    /// Find relationships between entities.
    /// </summary>
    EntityRelationship,

    /// <summary>
    /// Find entities matching a pattern.
    /// </summary>
    PatternMatch,

    /// <summary>
    /// Find memories in a community.
    /// </summary>
    CommunitySearch
}

/// <summary>
/// Options for sub-query generation.
/// </summary>
public sealed class SubQueryOptions
{
    /// <summary>
    /// Maximum sub-queries to generate.
    /// </summary>
    public int MaxSubQueries { get; init; } = 5;

    /// <summary>
    /// Include relationship exploration queries.
    /// </summary>
    public bool IncludeRelationshipQueries { get; init; } = true;

    /// <summary>
    /// Include community-based queries.
    /// </summary>
    public bool IncludeCommunityQueries { get; init; } = true;
}

/// <summary>
/// Statistics from query expansion.
/// </summary>
public sealed class ExpansionStatistics
{
    /// <summary>
    /// Number of entities found in query.
    /// </summary>
    public int MentionedEntityCount { get; init; }

    /// <summary>
    /// Number of related entities added.
    /// </summary>
    public int RelatedEntityCount { get; init; }

    /// <summary>
    /// Number of relevant facts found.
    /// </summary>
    public int FactCount { get; init; }

    /// <summary>
    /// Number of sub-queries generated.
    /// </summary>
    public int SubQueryCount { get; init; }

    /// <summary>
    /// Graph hops explored.
    /// </summary>
    public int MaxHopsExplored { get; init; }

    /// <summary>
    /// Expansion duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
