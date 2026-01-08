using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Orchestrates Working memory operations with session archival triggers.
/// Acts as a higher-level coordinator for Working→Session transitions.
/// </summary>
/// <remarks>
/// 4-Tier Architecture:
/// - Recently (Buffer): Raw conversation staging
/// - Short (L1): Topic-grouped active context - THIS TIER
/// - Session (L2): Archived session summaries - PROMOTION TARGET
/// - User (L3): Profile dictionary
///
/// Multi-signal promotion triggers (OR logic):
/// - IdleTimeout: 10 minutes of inactivity
/// - TokenThreshold: 2K tokens accumulated
/// - TurnThreshold: 10 conversation turns
/// - TopicChange: Significant topic shift detected
/// </remarks>
public interface IShortTermMemoryOrchestrator
{
    /// <summary>
    /// Records activity in working memory for a user.
    /// Updates turn counts and token tracking for trigger evaluation.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="memory">The memory being added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordActivityAsync(
        string userId,
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any session archival trigger is satisfied for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trigger type if archival should occur, null otherwise.</returns>
    Task<WorkingPromotionTrigger?> CheckArchivalTriggerAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives working memory contents to Session tier.
    /// Optionally summarizes before archiving.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="trigger">The trigger that caused the archival.</param>
    /// <param name="summarize">Whether to summarize before archiving.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the archival operation.</returns>
    Task<WorkingArchivalResult> ArchiveToSessionAsync(
        string userId,
        WorkingPromotionTrigger trigger,
        bool summarize = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of working memory for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Working memory state.</returns>
    WorkingMemoryState GetState(string userId);

    /// <summary>
    /// Gets all active user IDs with working memory state.
    /// </summary>
    /// <returns>List of active user IDs.</returns>
    IReadOnlyList<string> GetActiveUserIds();

    /// <summary>
    /// Clears state for a user (e.g., on session end).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    void ClearState(string userId);
}

/// <summary>
/// Types of triggers for Working→Session promotion.
/// </summary>
public enum WorkingPromotionTrigger
{
    /// <summary>
    /// No trigger (shouldn't be used in results).
    /// </summary>
    None = 0,

    /// <summary>
    /// Idle timeout exceeded (10 minutes default).
    /// </summary>
    IdleTimeout = 1,

    /// <summary>
    /// Token threshold exceeded (2000 tokens default).
    /// </summary>
    TokenThreshold = 2,

    /// <summary>
    /// Turn threshold exceeded (10 turns default).
    /// </summary>
    TurnThreshold = 3,

    /// <summary>
    /// Significant topic change detected.
    /// </summary>
    TopicChange = 4,

    /// <summary>
    /// Manual archival request.
    /// </summary>
    Manual = 5,

    /// <summary>
    /// Session end triggered archival.
    /// </summary>
    SessionEnd = 6,

    /// <summary>
    /// Capacity exceeded (Baddeley's 7±2 limit).
    /// Phase 51: Triggers when Short tier exceeds working memory capacity.
    /// </summary>
    CapacityExceeded = 7
}

/// <summary>
/// State of working memory for a user.
/// </summary>
public sealed class WorkingMemoryState
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Number of memories in working memory.
    /// </summary>
    public int MemoryCount { get; init; }

    /// <summary>
    /// Total estimated tokens in working memory.
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Number of conversation turns.
    /// </summary>
    public int TurnCount { get; init; }

    /// <summary>
    /// Time since last activity.
    /// </summary>
    public TimeSpan? IdleDuration { get; init; }

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    public DateTime? LastActivityTime { get; init; }

    /// <summary>
    /// Whether any trigger is satisfied.
    /// </summary>
    public bool TriggerSatisfied { get; init; }

    /// <summary>
    /// The satisfied trigger type, if any.
    /// </summary>
    public WorkingPromotionTrigger? SatisfiedTrigger { get; init; }

    /// <summary>
    /// Current topic signature (for topic change detection).
    /// </summary>
    public string? CurrentTopic { get; init; }

    /// <summary>
    /// Empty state.
    /// </summary>
    public static WorkingMemoryState Empty(string userId) => new()
    {
        UserId = userId,
        MemoryCount = 0,
        TotalTokens = 0,
        TurnCount = 0
    };
}

/// <summary>
/// Result of a working memory archival operation.
/// </summary>
public sealed record WorkingArchivalResult
{
    /// <summary>
    /// Whether the archival succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The trigger that caused the archival.
    /// </summary>
    public WorkingPromotionTrigger Trigger { get; init; }

    /// <summary>
    /// Number of memories archived.
    /// </summary>
    public int MemoriesArchived { get; init; }

    /// <summary>
    /// ID of the created session summary (if summarized).
    /// </summary>
    public Guid? SummaryId { get; init; }

    /// <summary>
    /// Error message if archival failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Empty result.
    /// </summary>
    public static WorkingArchivalResult Empty => new()
    {
        Success = true,
        Trigger = WorkingPromotionTrigger.None,
        MemoriesArchived = 0
    };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static WorkingArchivalResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Configuration for working memory orchestration.
/// </summary>
public sealed class WorkingMemoryOrchestratorOptions
{
    /// <summary>
    /// Idle timeout before triggering archival.
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Token threshold for triggering archival.
    /// Default: 2000 tokens.
    /// </summary>
    public int TokenThreshold { get; set; } = 2000;

    /// <summary>
    /// Turn threshold for triggering archival.
    /// Default: 10 turns.
    /// </summary>
    public int TurnThreshold { get; set; } = 10;

    /// <summary>
    /// Whether to enable topic change detection.
    /// </summary>
    public bool EnableTopicChangeDetection { get; set; } = true;

    /// <summary>
    /// Similarity threshold for topic change detection.
    /// Below this threshold, a topic change is detected.
    /// </summary>
    public float TopicChangeSimilarityThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Whether to summarize before archiving.
    /// </summary>
    public bool SummarizeBeforeArchival { get; set; } = true;

    /// <summary>
    /// Maximum items in Short tier before triggering capacity-based promotion.
    /// Based on Baddeley's Working Memory Model (7±2 items).
    /// Default: 9 (upper bound of 7±2).
    /// Phase 51: Added for cognitive compliance.
    /// </summary>
    public int Capacity { get; set; } = 9;

    /// <summary>
    /// Whether to enable capacity-based promotion.
    /// When enabled, oldest items are promoted to Long tier when capacity is exceeded.
    /// Default: true.
    /// </summary>
    public bool EnableCapacityEnforcement { get; set; } = true;
}
