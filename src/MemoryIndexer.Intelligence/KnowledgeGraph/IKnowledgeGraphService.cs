namespace MemoryIndexer.Intelligence.KnowledgeGraph;

/// <summary>
/// Service for building and querying knowledge graphs from memories.
/// </summary>
public interface IKnowledgeGraphService
{
    /// <summary>
    /// Extracts entities from text content.
    /// </summary>
    /// <param name="content">The text to extract entities from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted entities.</returns>
    Task<IReadOnlyList<Entity>> ExtractEntitiesAsync(
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts relationships between entities.
    /// </summary>
    /// <param name="content">The text to extract relationships from.</param>
    /// <param name="entities">Known entities in the content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted relationships.</returns>
    Task<IReadOnlyList<EntityRelation>> ExtractRelationsAsync(
        string content,
        IEnumerable<Entity> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a knowledge graph from multiple memories.
    /// </summary>
    /// <param name="memoryContents">Content from memories to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The built knowledge graph.</returns>
    Task<KnowledgeGraph> BuildGraphAsync(
        IEnumerable<string> memoryContents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the knowledge graph for entities related to a query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="graph">The knowledge graph to search.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <returns>Relevant entities and their relationships.</returns>
    KnowledgeGraphQueryResult QueryGraph(string query, KnowledgeGraph graph, int maxResults = 10);

    /// <summary>
    /// Merges two knowledge graphs.
    /// </summary>
    /// <param name="graph1">First graph.</param>
    /// <param name="graph2">Second graph.</param>
    /// <returns>Merged knowledge graph.</returns>
    KnowledgeGraph MergeGraphs(KnowledgeGraph graph1, KnowledgeGraph graph2);
}

/// <summary>
/// Represents an entity extracted from text.
/// </summary>
public sealed class Entity
{
    /// <summary>
    /// Unique identifier for the entity.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The entity name/value.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Normalized/canonical form of the entity.
    /// </summary>
    public string NormalizedName { get; init; } = default!;

    /// <summary>
    /// Type of entity.
    /// </summary>
    public EntityType Type { get; init; }

    /// <summary>
    /// Confidence score for extraction (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;

    /// <summary>
    /// Source memory IDs where this entity was found.
    /// </summary>
    public List<Guid> SourceMemoryIds { get; init; } = [];

    /// <summary>
    /// Number of times this entity appears.
    /// </summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// Additional metadata about the entity.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Types of entities that can be extracted.
/// </summary>
public enum EntityType
{
    /// <summary>
    /// Person name.
    /// </summary>
    Person,

    /// <summary>
    /// Organization or company.
    /// </summary>
    Organization,

    /// <summary>
    /// Location or place.
    /// </summary>
    Location,

    /// <summary>
    /// Date or time expression.
    /// </summary>
    DateTime,

    /// <summary>
    /// Email address.
    /// </summary>
    Email,

    /// <summary>
    /// URL or web address.
    /// </summary>
    Url,

    /// <summary>
    /// Numeric value (currency, quantity, etc.).
    /// </summary>
    Numeric,

    /// <summary>
    /// Technical term or concept.
    /// </summary>
    Technical,

    /// <summary>
    /// Product or service name.
    /// </summary>
    Product,

    /// <summary>
    /// Event or meeting.
    /// </summary>
    Event,

    /// <summary>
    /// General concept or topic.
    /// </summary>
    Concept,

    /// <summary>
    /// Unknown or other type.
    /// </summary>
    Unknown
}

/// <summary>
/// Represents a relationship between two entities.
/// </summary>
public sealed class EntityRelation
{
    /// <summary>
    /// Unique identifier for the relationship.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Source entity.
    /// </summary>
    public required Entity Source { get; init; }

    /// <summary>
    /// Target entity.
    /// </summary>
    public required Entity Target { get; init; }

    /// <summary>
    /// Type of relationship.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Confidence score for the relationship (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;

    /// <summary>
    /// Original text evidence for the relationship.
    /// </summary>
    public string? Evidence { get; init; }

    /// <summary>
    /// Whether the relationship is bidirectional.
    /// </summary>
    public bool IsBidirectional { get; init; }

    /// <summary>
    /// Weight/strength of the relationship.
    /// </summary>
    public float Weight { get; set; } = 1.0f;
}

/// <summary>
/// Represents a knowledge graph structure.
/// </summary>
public sealed class KnowledgeGraph
{
    /// <summary>
    /// Unique identifier for the graph.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// All entities in the graph.
    /// </summary>
    public Dictionary<Guid, Entity> Entities { get; init; } = [];

    /// <summary>
    /// All relationships in the graph.
    /// </summary>
    public List<EntityRelation> Relations { get; init; } = [];

    /// <summary>
    /// Adjacency list for efficient graph traversal.
    /// Maps entity ID to list of (relation, neighbor entity ID) pairs.
    /// </summary>
    public Dictionary<Guid, List<(EntityRelation Relation, Guid NeighborId)>> AdjacencyList { get; init; } = [];

    /// <summary>
    /// Index for quick entity lookup by name.
    /// </summary>
    public Dictionary<string, List<Guid>> NameIndex { get; init; } = [];

    /// <summary>
    /// Index for quick entity lookup by type.
    /// </summary>
    public Dictionary<EntityType, List<Guid>> TypeIndex { get; init; } = [];

    /// <summary>
    /// When this graph was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this graph was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of entities in the graph.
    /// </summary>
    public int EntityCount => Entities.Count;

    /// <summary>
    /// Number of relationships in the graph.
    /// </summary>
    public int RelationCount => Relations.Count;
}

/// <summary>
/// Result of querying a knowledge graph.
/// </summary>
public sealed class KnowledgeGraphQueryResult
{
    /// <summary>
    /// Entities matching the query.
    /// </summary>
    public List<Entity> MatchedEntities { get; set; } = [];

    /// <summary>
    /// Relationships involving matched entities.
    /// </summary>
    public List<EntityRelation> RelatedRelations { get; set; } = [];

    /// <summary>
    /// Related entities (neighbors of matched entities).
    /// </summary>
    public List<Entity> RelatedEntities { get; set; } = [];

    /// <summary>
    /// Search relevance scores for matched entities.
    /// </summary>
    public Dictionary<Guid, float> RelevanceScores { get; set; } = [];
}
