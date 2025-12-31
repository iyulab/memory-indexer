using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.Summarization;

/// <summary>
/// Manages rolling summaries for active sessions with periodic consolidation.
/// Implements a sliding window approach to maintain fresh, up-to-date summaries.
/// </summary>
public interface IRollingSummaryManager
{
    /// <summary>
    /// Initializes rolling summary tracking for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="userId">User identifier.</param>
    /// <param name="config">Rolling summary configuration.</param>
    void Initialize(string sessionId, string userId, RollingSummaryConfig? config = null);

    /// <summary>
    /// Records a new memory and updates the rolling summary if needed.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="memory">The memory to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated summary if triggered, null otherwise.</returns>
    Task<MemorySummary?> RecordAsync(
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a turn (user message + assistant response) and checks if summary update is needed.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="turnTokens">Token count for this turn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated summary if triggered, null otherwise.</returns>
    Task<MemorySummary?> RecordTurnAsync(
        string sessionId,
        int turnTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces an immediate summary update for the session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated summary.</returns>
    Task<MemorySummary> ForceUpdateAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current rolling summary for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Current summary or null if not initialized.</returns>
    MemorySummary? GetCurrentSummary(string sessionId);

    /// <summary>
    /// Gets the rolling summary state for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Summary state or null if not initialized.</returns>
    RollingSummaryState? GetState(string sessionId);

    /// <summary>
    /// Finalizes and returns the complete session summary.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Final comprehensive summary.</returns>
    Task<MemorySummary> FinalizeAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes session tracking without finalizing.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    void Remove(string sessionId);
}

/// <summary>
/// Configuration for rolling summary behavior.
/// </summary>
public sealed class RollingSummaryConfig
{
    /// <summary>
    /// Number of turns between automatic summary updates.
    /// Default: 5 turns.
    /// </summary>
    public int TurnInterval { get; init; } = 5;

    /// <summary>
    /// Time interval between automatic summary updates.
    /// Default: 10 minutes.
    /// </summary>
    public TimeSpan TimeInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum memories to include in rolling window before forcing summarization.
    /// Default: 20 memories.
    /// </summary>
    public int MaxWindowSize { get; init; } = 20;

    /// <summary>
    /// Token threshold that triggers immediate summarization.
    /// Default: 4000 tokens.
    /// </summary>
    public int TokenThreshold { get; init; } = 4000;

    /// <summary>
    /// Whether to use incremental updates (merge with existing) or full regeneration.
    /// Default: true (incremental).
    /// </summary>
    public bool UseIncrementalUpdates { get; init; } = true;

    /// <summary>
    /// Target compression ratio for summaries.
    /// Default: 0.3 (30% of original).
    /// </summary>
    public float TargetCompressionRatio { get; init; } = 0.3f;

    /// <summary>
    /// Default configuration.
    /// </summary>
    public static RollingSummaryConfig Default => new();
}

/// <summary>
/// Tracks the state of a rolling summary for a session.
/// </summary>
public sealed class RollingSummaryState
{
    /// <summary>
    /// Session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// User identifier.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Active configuration.
    /// </summary>
    public required RollingSummaryConfig Config { get; init; }

    /// <summary>
    /// Current rolling summary.
    /// </summary>
    public MemorySummary? CurrentSummary { get; set; }

    /// <summary>
    /// Memories in the current rolling window (not yet summarized).
    /// </summary>
    public List<MemoryUnit> WindowMemories { get; } = [];

    /// <summary>
    /// Current turn count since last summary.
    /// </summary>
    public int TurnsSinceLastSummary { get; set; }

    /// <summary>
    /// Current token count in window.
    /// </summary>
    public int WindowTokenCount { get; set; }

    /// <summary>
    /// When the session started.
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the last summary was generated.
    /// </summary>
    public DateTime? LastSummaryAt { get; set; }

    /// <summary>
    /// Total summaries generated for this session.
    /// </summary>
    public int TotalSummariesGenerated { get; set; }

    /// <summary>
    /// Total memories processed in this session.
    /// </summary>
    public int TotalMemoriesProcessed { get; set; }

    /// <summary>
    /// Time since last summary.
    /// </summary>
    public TimeSpan? TimeSinceLastSummary =>
        LastSummaryAt.HasValue ? DateTime.UtcNow - LastSummaryAt.Value : null;

    /// <summary>
    /// Checks if summarization should be triggered based on current state.
    /// </summary>
    public bool ShouldTriggerSummary()
    {
        // Turn-based trigger
        if (TurnsSinceLastSummary >= Config.TurnInterval)
            return true;

        // Time-based trigger
        if (TimeSinceLastSummary.HasValue && TimeSinceLastSummary.Value >= Config.TimeInterval)
            return true;

        // Window size trigger
        if (WindowMemories.Count >= Config.MaxWindowSize)
            return true;

        // Token threshold trigger
        if (WindowTokenCount >= Config.TokenThreshold)
            return true;

        return false;
    }
}
