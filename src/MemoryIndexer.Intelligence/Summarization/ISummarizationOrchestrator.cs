using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.Summarization;

/// <summary>
/// Orchestrates the summarization process by integrating triggers, services, and storage.
/// Manages session-aware summarization workflows with automatic trigger evaluation.
/// </summary>
public interface ISummarizationOrchestrator
{
    /// <summary>
    /// Starts tracking a session for summarization.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="userId">User identifier.</param>
    /// <param name="maxTokenBudget">Maximum token budget for the session.</param>
    void StartSession(string sessionId, string userId, int maxTokenBudget = 100000);

    /// <summary>
    /// Ends a session, triggering final summarization if needed.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final session summary if generated.</returns>
    Task<MemorySummary?> EndSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory operation and evaluates if summarization is needed.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="memory">The memory that was stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summarization result if triggered, null otherwise.</returns>
    Task<SummarizationResult?> RecordMemoryAsync(
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a message event in the session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="tokenCount">Token count of the message.</param>
    /// <param name="isUserMessage">Whether this is a user message (vs assistant).</param>
    void RecordMessage(string sessionId, int tokenCount, bool isUserMessage);

    /// <summary>
    /// Manually triggers summarization for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="strategy">Summarization strategy to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summarization result.</returns>
    Task<SummarizationResult> TriggerSummarizationAsync(
        string sessionId,
        SummarizationStrategy? strategy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates if summarization should be triggered without executing it.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trigger evaluation result.</returns>
    Task<TriggerEvaluation> EvaluateTriggerAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current session state for monitoring.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Session state if exists, null otherwise.</returns>
    SessionState? GetSessionState(string sessionId);

    /// <summary>
    /// Gets all active session IDs.
    /// </summary>
    /// <returns>Collection of active session IDs.</returns>
    IReadOnlyCollection<string> GetActiveSessionIds();
}

/// <summary>
/// Result of a summarization operation.
/// </summary>
public sealed class SummarizationResult
{
    /// <summary>
    /// Whether summarization was performed.
    /// </summary>
    public bool Summarized { get; init; }

    /// <summary>
    /// The generated summary.
    /// </summary>
    public MemorySummary? Summary { get; init; }

    /// <summary>
    /// The trigger evaluation that caused summarization.
    /// </summary>
    public TriggerEvaluation? Trigger { get; init; }

    /// <summary>
    /// Strategy used for summarization.
    /// </summary>
    public SummarizationStrategy Strategy { get; init; }

    /// <summary>
    /// Token count before summarization.
    /// </summary>
    public int TokensBefore { get; init; }

    /// <summary>
    /// Token count after summarization.
    /// </summary>
    public int TokensAfter { get; init; }

    /// <summary>
    /// Number of memories that were summarized.
    /// </summary>
    public int MemoriesProcessed { get; init; }

    /// <summary>
    /// Duration of the summarization operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Error message if summarization failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a skipped result (no summarization needed).
    /// </summary>
    public static SummarizationResult Skipped(TriggerEvaluation trigger) => new()
    {
        Summarized = false,
        Trigger = trigger
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static SummarizationResult Failed(string error) => new()
    {
        Summarized = false,
        Error = error
    };
}

/// <summary>
/// Tracks the state of an active session for summarization purposes.
/// </summary>
public sealed class SessionState
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
    /// When the session started.
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Maximum token budget for the session.
    /// </summary>
    public int MaxTokenBudget { get; init; }

    /// <summary>
    /// Current token count in the session.
    /// </summary>
    public int CurrentTokenCount { get; set; }

    /// <summary>
    /// Number of messages in the session.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Number of memories created in the session.
    /// </summary>
    public int MemoriesCreated { get; set; }

    /// <summary>
    /// Accumulated importance score since last summarization.
    /// </summary>
    public float AccumulatedImportance { get; set; }

    /// <summary>
    /// IDs of memories created in this session.
    /// </summary>
    public List<Guid> MemoryIds { get; } = [];

    /// <summary>
    /// When the last summarization occurred.
    /// </summary>
    public DateTime? LastSummarizedAt { get; set; }

    /// <summary>
    /// Summaries generated for this session.
    /// </summary>
    public List<MemorySummary> Summaries { get; } = [];

    /// <summary>
    /// Whether the session is ending.
    /// </summary>
    public bool IsEnding { get; set; }

    /// <summary>
    /// Gets the session duration.
    /// </summary>
    public TimeSpan Duration => DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Gets time since last summarization.
    /// </summary>
    public TimeSpan? TimeSinceLastSummarization =>
        LastSummarizedAt.HasValue ? DateTime.UtcNow - LastSummarizedAt.Value : null;

    /// <summary>
    /// Converts to SummarizationContext for trigger evaluation.
    /// </summary>
    public SummarizationContext ToContext() => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        CurrentTokenCount = CurrentTokenCount,
        MaxTokenBudget = MaxTokenBudget,
        MessageCount = MessageCount,
        SessionDuration = Duration,
        TimeSinceLastSummarization = TimeSinceLastSummarization,
        MemoriesCreated = MemoriesCreated,
        AccumulatedImportance = AccumulatedImportance,
        IsSessionEnding = IsEnding
    };
}
