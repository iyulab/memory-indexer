using System.Text.Json.Serialization;
using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Extracts factual statements from conversational content with context awareness.
/// Distinguishes direct statements from quoted/fictional context.
/// </summary>
/// <remarks>
/// Unlike IKnowledgeExtractor (which handles Q&A exchanges), IFactExtractor
/// analyzes direct statements like "My name is John" and determines:
/// - Whether the statement is a direct fact or quoted/fictional
/// - Confidence level based on linguistic markers
/// - Recommended promotion path (FastTrack, Standard, SessionOnly)
///
/// Example direct fact: "My name is John" → FastTrack to User scope
/// Example quoted text: "In the novel, he says 'My name is Lincoln'" → SessionOnly
/// </remarks>
public interface IFactExtractor
{
    /// <summary>
    /// Extracts facts from conversational content.
    /// </summary>
    /// <param name="context">Context containing content and metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with facts and recommended promotion paths.</returns>
    Task<FactExtractionResult> ExtractAsync(
        FactExtractionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a fact against existing memories for conflict detection.
    /// </summary>
    /// <param name="fact">The fact to validate.</param>
    /// <param name="existingFacts">Existing facts in the same domain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with conflict information.</returns>
    Task<FactValidationResult> ValidateAsync(
        UserFact fact,
        IReadOnlyList<MemoryUnit> existingFacts,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for fact extraction from conversational content.
/// </summary>
public sealed class FactExtractionContext
{
    /// <summary>
    /// The content to analyze for facts.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// User ID for context-specific extraction.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Session ID for session context.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Role of the speaker (user, assistant, system).
    /// Facts from user role have higher confidence.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Previous turns for context (to detect quoted/fictional content).
    /// </summary>
    public IReadOnlyList<string>? PreviousTurns { get; init; }

    /// <summary>
    /// Additional metadata for extraction context.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of fact extraction analysis.
/// </summary>
public sealed class FactExtractionResult
{
    /// <summary>
    /// Extracted user facts with promotion recommendations.
    /// </summary>
    public IReadOnlyList<UserFact> Facts { get; init; } = [];

    /// <summary>
    /// Overall extraction confidence (0-1).
    /// </summary>
    public float OverallConfidence { get; init; }

    /// <summary>
    /// Whether any facts were detected in the content.
    /// </summary>
    public bool HasFacts => Facts.Count > 0;

    /// <summary>
    /// Facts recommended for fast-track promotion.
    /// </summary>
    public IReadOnlyList<UserFact> FastTrackFacts =>
        Facts.Where(f => f.PromotionPath == PromotionPath.FastTrack).ToList();

    /// <summary>
    /// Context type detected (Direct, Quoted, Hypothetical, etc.).
    /// </summary>
    public StatementContextType ContextType { get; init; } = StatementContextType.Direct;

    /// <summary>
    /// Reasoning for the extraction decisions.
    /// </summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// A single user fact extracted from content.
/// </summary>
public sealed class UserFact
{
    /// <summary>
    /// The factual content (e.g., "User's name is John").
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Category of the fact (Identity, Preference, Relationship, etc.).
    /// </summary>
    [JsonPropertyName("category")]
    public FactCategory Category { get; init; } = FactCategory.General;

    /// <summary>
    /// Extraction confidence (0-1). Higher = more certain this is a valid fact.
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; init; } = 0.5f;

    /// <summary>
    /// Importance score (0-1). Higher = more valuable for future context.
    /// </summary>
    [JsonPropertyName("importance")]
    public float Importance { get; init; } = 0.5f;

    /// <summary>
    /// Recommended promotion path based on confidence and category.
    /// </summary>
    [JsonPropertyName("promotionPath")]
    public PromotionPath PromotionPath { get; init; } = PromotionPath.Standard;

    /// <summary>
    /// Subject entity of the fact (e.g., "user", "user's mother").
    /// </summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>
    /// Predicate/attribute of the fact (e.g., "name", "favorite color").
    /// </summary>
    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }

    /// <summary>
    /// Object/value of the fact (e.g., "John", "blue").
    /// </summary>
    [JsonPropertyName("object")]
    public string? Object { get; init; }

    /// <summary>
    /// Related entities mentioned in the fact.
    /// </summary>
    [JsonPropertyName("entities")]
    public List<string>? Entities { get; init; }

    /// <summary>
    /// Temporal validity start (when the fact became true).
    /// </summary>
    [JsonPropertyName("validFrom")]
    public DateTime? ValidFrom { get; init; }

    /// <summary>
    /// Temporal validity end (when the fact stopped being true).
    /// Null means currently valid.
    /// </summary>
    [JsonPropertyName("validUntil")]
    public DateTime? ValidUntil { get; init; }

    /// <summary>
    /// Source of the extraction for debugging.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>
/// Result of fact validation against existing memories.
/// </summary>
public sealed class FactValidationResult
{
    /// <summary>
    /// Whether the fact is valid (no unresolved conflicts).
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Type of conflict detected (if any).
    /// </summary>
    public FactConflictType ConflictType { get; init; } = FactConflictType.None;

    /// <summary>
    /// Conflicting memory if a conflict was detected.
    /// </summary>
    public MemoryUnit? ConflictingMemory { get; init; }

    /// <summary>
    /// Recommended action for the conflict.
    /// </summary>
    public FactConflictAction RecommendedAction { get; init; } = FactConflictAction.Add;

    /// <summary>
    /// Confidence in the validation decision (0-1).
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Reasoning for the validation decision.
    /// </summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// If action is Merge, this contains the merged content.
    /// </summary>
    public string? MergedContent { get; init; }
}

/// <summary>
/// Promotion paths for extracted facts.
/// </summary>
public enum PromotionPath
{
    /// <summary>
    /// Standard promotion: Buffer → Short → Long → Archive
    /// For facts with moderate confidence (0.5-0.8).
    /// </summary>
    Standard,

    /// <summary>
    /// Fast track: Buffer → Archive directly
    /// For high-confidence facts (≥0.9) like direct identity statements.
    /// </summary>
    FastTrack,

    /// <summary>
    /// Session only: Stays in session scope, not promoted to user level.
    /// For quoted/fictional/hypothetical statements.
    /// </summary>
    SessionOnly,

    /// <summary>
    /// Discard: Do not store as fact.
    /// For clearly fictional or irrelevant statements.
    /// </summary>
    Discard
}

/// <summary>
/// Context type of the statement being analyzed.
/// </summary>
public enum StatementContextType
{
    /// <summary>
    /// Direct statement from the user about themselves.
    /// Example: "My name is John"
    /// </summary>
    Direct,

    /// <summary>
    /// Quoted text (from a book, movie, etc.).
    /// Example: "In the novel, he says 'My name is Lincoln'"
    /// </summary>
    Quoted,

    /// <summary>
    /// Hypothetical or conditional statement.
    /// Example: "If my name were John, I would..."
    /// </summary>
    Hypothetical,

    /// <summary>
    /// Narrative or story context.
    /// Example: "In my story, the character's name is..."
    /// </summary>
    Narrative,

    /// <summary>
    /// Role-play or persona context.
    /// Example: "As the wizard, I say..."
    /// </summary>
    RolePlay,

    /// <summary>
    /// Question or inquiry (not a fact).
    /// Example: "Is my name John?"
    /// </summary>
    Question,

    /// <summary>
    /// Past/historical context (may no longer be true).
    /// Example: "My name used to be..."
    /// </summary>
    Historical,

    /// <summary>
    /// Unknown or ambiguous context.
    /// </summary>
    Unknown
}

/// <summary>
/// Categories of user facts.
/// </summary>
public enum FactCategory
{
    /// <summary>
    /// General uncategorized fact.
    /// </summary>
    General,

    /// <summary>
    /// Identity facts (name, age, occupation).
    /// </summary>
    Identity,

    /// <summary>
    /// Preference facts (likes, dislikes, favorites).
    /// </summary>
    Preference,

    /// <summary>
    /// Relationship facts (family, friends, colleagues).
    /// </summary>
    Relationship,

    /// <summary>
    /// Location facts (address, workplace, birthplace).
    /// </summary>
    Location,

    /// <summary>
    /// Temporal facts (birthday, anniversary, schedule).
    /// </summary>
    Temporal,

    /// <summary>
    /// Skill/ability facts (languages, expertise).
    /// </summary>
    Skill,

    /// <summary>
    /// Goal/intention facts (plans, aspirations).
    /// </summary>
    Goal,

    /// <summary>
    /// Health/medical facts (allergies, conditions).
    /// </summary>
    Health,

    /// <summary>
    /// Work/professional facts (job, company, role).
    /// </summary>
    Professional
}

/// <summary>
/// Types of conflicts between facts.
/// </summary>
public enum FactConflictType
{
    /// <summary>
    /// No conflict detected.
    /// </summary>
    None,

    /// <summary>
    /// Exact duplicate (same content).
    /// </summary>
    Duplicate,

    /// <summary>
    /// Semantic duplicate (same meaning, different words).
    /// </summary>
    SemanticDuplicate,

    /// <summary>
    /// Direct contradiction (opposite values).
    /// Example: "likes apples" vs "hates apples"
    /// </summary>
    Contradiction,

    /// <summary>
    /// Value update (same attribute, different value).
    /// Example: "age 25" vs "age 26"
    /// </summary>
    ValueUpdate,

    /// <summary>
    /// Temporal conflict (outdated information).
    /// Example: "lives in Seoul" vs "moved to Busan"
    /// </summary>
    TemporalConflict,

    /// <summary>
    /// Refinement (new fact adds detail to existing).
    /// Example: "likes pizza" → "favorite pizza is margherita"
    /// </summary>
    Refinement
}

/// <summary>
/// Actions for resolving fact conflicts.
/// </summary>
public enum FactConflictAction
{
    /// <summary>
    /// Add as new fact (no conflict or independent fact).
    /// </summary>
    Add,

    /// <summary>
    /// Skip (duplicate exists).
    /// </summary>
    Skip,

    /// <summary>
    /// Replace existing fact with new one.
    /// </summary>
    Replace,

    /// <summary>
    /// Merge new information into existing fact.
    /// </summary>
    Merge,

    /// <summary>
    /// Archive old fact, add new as current.
    /// </summary>
    ArchiveAndAdd,

    /// <summary>
    /// Mark for human review (ambiguous conflict).
    /// </summary>
    RequireReview
}
