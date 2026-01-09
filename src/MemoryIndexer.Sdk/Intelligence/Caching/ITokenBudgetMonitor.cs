namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Interface for monitoring token usage and providing budget awareness hooks.
/// Phase v0.5.0: Token Budget Awareness Hooks
/// </summary>
public interface ITokenBudgetMonitor
{
    /// <summary>
    /// Event fired when token usage exceeds a warning threshold.
    /// </summary>
    event EventHandler<TokenBudgetEventArgs>? OnBudgetWarning;

    /// <summary>
    /// Event fired when token budget is exceeded.
    /// </summary>
    event EventHandler<TokenBudgetEventArgs>? OnBudgetExceeded;

    /// <summary>
    /// Event fired when a session ends, providing final usage statistics.
    /// </summary>
    event EventHandler<SessionTokenSummaryEventArgs>? OnSessionEnded;

    /// <summary>
    /// Starts monitoring a session with given budget.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="userId">User identifier.</param>
    /// <param name="maxTokenBudget">Maximum token budget for the session.</param>
    /// <param name="warningThreshold">Threshold ratio (0-1) to trigger warning. Default: 0.8</param>
    void StartSession(string sessionId, string userId, int maxTokenBudget, float warningThreshold = 0.8f);

    /// <summary>
    /// Records token consumption for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="tokens">Number of tokens consumed.</param>
    /// <param name="operation">Type of operation (e.g., "recall", "store", "embedding").</param>
    void RecordTokenUsage(string sessionId, int tokens, string operation);

    /// <summary>
    /// Estimates tokens for given content.
    /// </summary>
    /// <param name="content">Content to estimate.</param>
    /// <returns>Estimated token count.</returns>
    int EstimateTokens(string content);

    /// <summary>
    /// Gets current usage for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Current session usage or null if session not found.</returns>
    TokenBudgetStatus? GetSessionStatus(string sessionId);

    /// <summary>
    /// Checks if a session can afford additional tokens.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="estimatedTokens">Estimated tokens for the operation.</param>
    /// <returns>True if operation can proceed within budget.</returns>
    bool CanAfford(string sessionId, int estimatedTokens);

    /// <summary>
    /// Gets a recommendation for the session based on current usage.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Recommendation for the session.</returns>
    TokenBudgetRecommendation GetRecommendation(string sessionId);

    /// <summary>
    /// Ends a session and returns final statistics.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Final session summary.</returns>
    SessionTokenSummary? EndSession(string sessionId);

    /// <summary>
    /// Gets global statistics across all sessions.
    /// </summary>
    TokenBudgetGlobalStats GetGlobalStats();
}

/// <summary>
/// Event arguments for token budget events.
/// </summary>
public sealed class TokenBudgetEventArgs : EventArgs
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>User identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>Current token usage.</summary>
    public int CurrentUsage { get; init; }

    /// <summary>Maximum budget.</summary>
    public int MaxBudget { get; init; }

    /// <summary>Usage ratio (0-1).</summary>
    public float UsageRatio { get; init; }

    /// <summary>Type of event.</summary>
    public TokenBudgetEventType EventType { get; init; }

    /// <summary>Recommendation for the LLM.</summary>
    public required string Recommendation { get; init; }
}

/// <summary>
/// Event arguments for session end summary.
/// </summary>
public sealed class SessionTokenSummaryEventArgs : EventArgs
{
    /// <summary>Session summary.</summary>
    public required SessionTokenSummary Summary { get; init; }
}

/// <summary>
/// Type of token budget event.
/// </summary>
public enum TokenBudgetEventType
{
    /// <summary>Usage approaching budget (warning threshold).</summary>
    Warning,

    /// <summary>Usage exceeded budget.</summary>
    Exceeded,

    /// <summary>Session ended normally.</summary>
    SessionEnded
}

/// <summary>
/// Current status of a session's token budget.
/// </summary>
public sealed class TokenBudgetStatus
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>User identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>Total tokens consumed.</summary>
    public int TotalTokens { get; init; }

    /// <summary>Maximum budget.</summary>
    public int MaxBudget { get; init; }

    /// <summary>Remaining tokens.</summary>
    public int RemainingTokens => MaxBudget - TotalTokens;

    /// <summary>Usage ratio (0-1).</summary>
    public float UsageRatio => MaxBudget > 0 ? (float)TotalTokens / MaxBudget : 0f;

    /// <summary>Whether warning threshold was reached.</summary>
    public bool IsWarning { get; init; }

    /// <summary>Whether budget was exceeded.</summary>
    public bool IsExceeded { get; init; }

    /// <summary>Session start time.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Session duration.</summary>
    public TimeSpan Duration => DateTimeOffset.UtcNow - StartedAt;

    /// <summary>Breakdown by operation type.</summary>
    public IReadOnlyDictionary<string, int> OperationBreakdown { get; init; }
        = new Dictionary<string, int>();
}

/// <summary>
/// Recommendation for handling token budget.
/// </summary>
public sealed class TokenBudgetRecommendation
{
    /// <summary>Recommendation type.</summary>
    public TokenRecommendationType Type { get; init; }

    /// <summary>Human-readable message for the LLM.</summary>
    public required string Message { get; init; }

    /// <summary>Suggested action.</summary>
    public required string SuggestedAction { get; init; }

    /// <summary>Urgency level (0-1).</summary>
    public float Urgency { get; init; }
}

/// <summary>
/// Types of token budget recommendations.
/// </summary>
public enum TokenRecommendationType
{
    /// <summary>Continue normally.</summary>
    Continue,

    /// <summary>Consider reducing recall scope.</summary>
    ReduceScope,

    /// <summary>Use compression or summarization.</summary>
    Compress,

    /// <summary>Stop non-essential operations.</summary>
    Conserve,

    /// <summary>Budget exceeded, minimize further usage.</summary>
    Stop
}

/// <summary>
/// Summary of a session's token usage.
/// </summary>
public sealed class SessionTokenSummary
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>User identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>Total tokens consumed.</summary>
    public int TotalTokens { get; init; }

    /// <summary>Maximum budget.</summary>
    public int MaxBudget { get; init; }

    /// <summary>Final usage ratio.</summary>
    public float FinalUsageRatio => MaxBudget > 0 ? (float)TotalTokens / MaxBudget : 0f;

    /// <summary>Session duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Number of operations performed.</summary>
    public int OperationCount { get; init; }

    /// <summary>Breakdown by operation type.</summary>
    public IReadOnlyDictionary<string, int> OperationBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Peak usage ratio reached.</summary>
    public float PeakUsageRatio { get; init; }

    /// <summary>Whether budget was ever exceeded.</summary>
    public bool WasExceeded { get; init; }

    /// <summary>Number of warnings issued.</summary>
    public int WarningCount { get; init; }
}

/// <summary>
/// Global statistics across all sessions.
/// </summary>
public sealed class TokenBudgetGlobalStats
{
    /// <summary>Number of active sessions.</summary>
    public int ActiveSessions { get; init; }

    /// <summary>Total sessions monitored (including ended).</summary>
    public int TotalSessions { get; init; }

    /// <summary>Total tokens consumed across all sessions.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Average usage ratio across active sessions.</summary>
    public float AverageUsageRatio { get; init; }

    /// <summary>Number of sessions that exceeded budget.</summary>
    public int ExceededCount { get; init; }

    /// <summary>Most common operation type.</summary>
    public string? TopOperation { get; init; }
}
