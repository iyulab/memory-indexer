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

    #region Temporal Properties (Bitemporal Modeling)

    /// <summary>
    /// When this fact became true in the real world (event time).
    /// Null means the fact is considered valid from the beginning of time.
    /// Part of bitemporal modeling for temporal knowledge graphs.
    /// </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// When this fact ceased to be true in the real world (event time).
    /// Null means the fact is still valid (no known end date).
    /// Part of bitemporal modeling for temporal knowledge graphs.
    /// </summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// When this fact was recorded in the system (transaction time).
    /// This is automatically set and should not be modified.
    /// Part of bitemporal modeling for temporal knowledge graphs.
    /// </summary>
    public DateTime TransactionTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The ID of the previous version of this fact that this triple supersedes.
    /// Forms a version chain for tracking fact evolution.
    /// </summary>
    public Guid? SupersedesId { get; set; }

    /// <summary>
    /// Version number in the fact evolution chain.
    /// Starts at 1 for the first version.
    /// </summary>
    public int Version { get; set; } = 1;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if this fact was valid at a specific point in time.
    /// </summary>
    /// <param name="asOfDate">The date to check validity for.</param>
    /// <returns>True if the fact was valid at the specified date.</returns>
    public bool WasValidAt(DateTime asOfDate)
    {
        var validFrom = ValidFrom ?? DateTime.MinValue;
        var validTo = ValidTo ?? DateTime.MaxValue;
        return asOfDate >= validFrom && asOfDate <= validTo;
    }

    /// <summary>
    /// Checks if this fact is currently valid (not superseded and within valid time range).
    /// </summary>
    public bool IsCurrentlyValid => IsActive && WasValidAt(DateTime.UtcNow);

    /// <summary>
    /// Creates a new version of this triple that supersedes the current one.
    /// </summary>
    /// <param name="newObjectValue">The new object value for the superseding triple.</param>
    /// <param name="validFrom">When the new fact became true (defaults to now).</param>
    /// <returns>A new EntityTriple that supersedes this one.</returns>
    public EntityTriple CreateSupersedingVersion(string newObjectValue, DateTime? validFrom = null)
    {
        return new EntityTriple
        {
            Subject = Subject,
            Predicate = Predicate,
            ObjectValue = newObjectValue,
            UserId = UserId,
            Context = Context,
            Confidence = Confidence,
            SourceMemoryId = SourceMemoryId,
            ValidFrom = validFrom ?? DateTime.UtcNow,
            SupersedesId = Id,
            Version = Version + 1
        };
    }

    #endregion
}
