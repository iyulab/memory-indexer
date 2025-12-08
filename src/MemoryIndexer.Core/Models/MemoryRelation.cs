namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents a relationship between two memories.
/// Used for knowledge graph construction and traversal.
/// </summary>
public sealed class MemoryRelation
{
    /// <summary>
    /// Unique identifier for this relation.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The source memory ID (subject of the relation).
    /// </summary>
    public Guid SourceMemoryId { get; set; }

    /// <summary>
    /// The target memory ID (object of the relation).
    /// </summary>
    public Guid TargetMemoryId { get; set; }

    /// <summary>
    /// The type of relationship.
    /// </summary>
    public MemoryRelationType RelationType { get; set; }

    /// <summary>
    /// Confidence score for this relationship (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>
    /// When this relation was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional description or context for the relationship.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Additional metadata for the relation.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
