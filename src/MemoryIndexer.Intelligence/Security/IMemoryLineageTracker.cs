namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Service for tracking memory lineage and provenance.
/// Records the history of memory operations for audit and debugging.
/// </summary>
public interface IMemoryLineageTracker
{
    /// <summary>
    /// Records a memory creation event.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="userId">The user who created the memory.</param>
    /// <param name="source">Source of the memory content.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordCreationAsync(
        Guid memoryId,
        string userId,
        MemorySource source,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory update event.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="userId">The user who updated the memory.</param>
    /// <param name="changeType">Type of change made.</param>
    /// <param name="previousContentHash">Hash of previous content.</param>
    /// <param name="newContentHash">Hash of new content.</param>
    /// <param name="reason">Reason for the update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordUpdateAsync(
        Guid memoryId,
        string userId,
        MemoryChangeType changeType,
        string? previousContentHash,
        string? newContentHash,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory access event.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="userId">The user who accessed the memory.</param>
    /// <param name="accessType">Type of access.</param>
    /// <param name="context">Access context (e.g., query that retrieved it).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAccessAsync(
        Guid memoryId,
        string userId,
        MemoryAccessType accessType,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory deletion event.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="userId">The user who deleted the memory.</param>
    /// <param name="isHardDelete">Whether this is a permanent deletion.</param>
    /// <param name="reason">Reason for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordDeletionAsync(
        Guid memoryId,
        string userId,
        bool isHardDelete,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory merge event.
    /// </summary>
    /// <param name="resultMemoryId">The resulting merged memory ID.</param>
    /// <param name="sourceMemoryIds">IDs of memories that were merged.</param>
    /// <param name="userId">The user who performed the merge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordMergeAsync(
        Guid resultMemoryId,
        IEnumerable<Guid> sourceMemoryIds,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the lineage history for a memory.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="options">Query options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of lineage events.</returns>
    Task<IReadOnlyList<MemoryLineageEvent>> GetLineageAsync(
        Guid memoryId,
        LineageQueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets related memories through lineage (derived from, merged with, etc.).
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="relationTypes">Types of relations to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of related memory IDs with their relation types.</returns>
    Task<IReadOnlyList<MemoryRelation>> GetRelatedMemoriesAsync(
        Guid memoryId,
        MemoryLineageRelation[]? relationTypes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Source of memory content.
/// </summary>
public enum MemorySource
{
    /// <summary>
    /// Direct user input.
    /// </summary>
    UserInput,

    /// <summary>
    /// Extracted from conversation.
    /// </summary>
    ConversationExtraction,

    /// <summary>
    /// Generated summary.
    /// </summary>
    Summarization,

    /// <summary>
    /// External document import.
    /// </summary>
    DocumentImport,

    /// <summary>
    /// System-generated (e.g., reflection).
    /// </summary>
    SystemGenerated,

    /// <summary>
    /// Merged from multiple sources.
    /// </summary>
    Merged,

    /// <summary>
    /// API import.
    /// </summary>
    ApiImport,

    /// <summary>
    /// Unknown source.
    /// </summary>
    Unknown
}

/// <summary>
/// Type of change made to a memory.
/// </summary>
public enum MemoryChangeType
{
    /// <summary>
    /// Content was modified.
    /// </summary>
    ContentUpdate,

    /// <summary>
    /// Metadata was modified.
    /// </summary>
    MetadataUpdate,

    /// <summary>
    /// Importance score was updated.
    /// </summary>
    ImportanceUpdate,

    /// <summary>
    /// Memory type was changed.
    /// </summary>
    TypeChange,

    /// <summary>
    /// Tags/topics were updated.
    /// </summary>
    TagsUpdate,

    /// <summary>
    /// Embedding was regenerated.
    /// </summary>
    EmbeddingRegeneration,

    /// <summary>
    /// Memory was archived.
    /// </summary>
    Archive,

    /// <summary>
    /// Memory was restored.
    /// </summary>
    Restore,

    /// <summary>
    /// Memory was soft-deleted.
    /// </summary>
    SoftDelete
}

/// <summary>
/// Type of memory access.
/// </summary>
public enum MemoryAccessType
{
    /// <summary>
    /// Direct retrieval by ID.
    /// </summary>
    DirectRetrieval,

    /// <summary>
    /// Retrieved through search.
    /// </summary>
    SearchResult,

    /// <summary>
    /// Retrieved through related memories.
    /// </summary>
    RelatedRetrieval,

    /// <summary>
    /// Retrieved for summary generation.
    /// </summary>
    SummarizationInput,

    /// <summary>
    /// Retrieved for export.
    /// </summary>
    Export,

    /// <summary>
    /// Admin access.
    /// </summary>
    AdminAccess
}

/// <summary>
/// Types of lineage relations between memories.
/// </summary>
public enum MemoryLineageRelation
{
    /// <summary>
    /// Memory was derived from another.
    /// </summary>
    DerivedFrom,

    /// <summary>
    /// Memory supersedes another.
    /// </summary>
    Supersedes,

    /// <summary>
    /// Memory was merged from others.
    /// </summary>
    MergedFrom,

    /// <summary>
    /// Memory is a summary of others.
    /// </summary>
    SummarizesFrom,

    /// <summary>
    /// Memory was split from another.
    /// </summary>
    SplitFrom,

    /// <summary>
    /// Memory was duplicated.
    /// </summary>
    DuplicatedFrom
}

/// <summary>
/// A memory lineage event.
/// </summary>
public sealed class MemoryLineageEvent
{
    /// <summary>
    /// Unique event ID.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Memory ID this event relates to.
    /// </summary>
    public Guid MemoryId { get; init; }

    /// <summary>
    /// Type of event.
    /// </summary>
    public LineageEventType EventType { get; init; }

    /// <summary>
    /// User who triggered the event.
    /// </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Additional event details.
    /// </summary>
    public Dictionary<string, string> Details { get; init; } = [];

    /// <summary>
    /// Related memory IDs (for merge, derive operations).
    /// </summary>
    public List<Guid> RelatedMemoryIds { get; init; } = [];

    /// <summary>
    /// Content hash before change (for updates).
    /// </summary>
    public string? PreviousContentHash { get; init; }

    /// <summary>
    /// Content hash after change (for updates).
    /// </summary>
    public string? NewContentHash { get; init; }
}

/// <summary>
/// Type of lineage event.
/// </summary>
public enum LineageEventType
{
    /// <summary>
    /// Memory was created.
    /// </summary>
    Created,

    /// <summary>
    /// Memory was updated.
    /// </summary>
    Updated,

    /// <summary>
    /// Memory was accessed.
    /// </summary>
    Accessed,

    /// <summary>
    /// Memory was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// Memories were merged.
    /// </summary>
    Merged,

    /// <summary>
    /// Memory was derived from another.
    /// </summary>
    Derived,

    /// <summary>
    /// Memory was restored.
    /// </summary>
    Restored
}

/// <summary>
/// Options for querying lineage.
/// </summary>
public sealed class LineageQueryOptions
{
    /// <summary>
    /// Event types to include.
    /// </summary>
    public LineageEventType[]? EventTypes { get; set; }

    /// <summary>
    /// Start time filter.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// End time filter.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Maximum number of events to return.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Whether to include related memory events.
    /// </summary>
    public bool IncludeRelated { get; set; }
}

/// <summary>
/// Represents a relation between memories.
/// </summary>
public sealed class MemoryRelation
{
    /// <summary>
    /// Related memory ID.
    /// </summary>
    public Guid RelatedMemoryId { get; init; }

    /// <summary>
    /// Type of relation.
    /// </summary>
    public MemoryLineageRelation RelationType { get; init; }

    /// <summary>
    /// When the relation was established.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
