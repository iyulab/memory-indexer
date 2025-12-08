namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents a Subject-Predicate-Object triple for knowledge graph entities.
/// Based on RDF-style triples for structured knowledge representation.
/// </summary>
public sealed class EntityTriple
{
    /// <summary>
    /// Unique identifier for this triple.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The subject entity (e.g., "Alice", "User", "ProjectX").
    /// </summary>
    public string Subject { get; set; } = default!;

    /// <summary>
    /// The predicate/relationship (e.g., "prefers", "works_on", "is_located_in").
    /// </summary>
    public string Predicate { get; set; } = default!;

    /// <summary>
    /// The object entity or value (e.g., "dark_mode", "NLP_project", "Seoul").
    /// </summary>
    public string ObjectValue { get; set; } = default!;

    /// <summary>
    /// The user or tenant this triple belongs to.
    /// </summary>
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Optional context or source for this triple.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Confidence score for this triple (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>
    /// The memory unit ID this triple was extracted from.
    /// </summary>
    public Guid? SourceMemoryId { get; set; }

    /// <summary>
    /// When this triple was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this triple was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this triple is still valid/active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
