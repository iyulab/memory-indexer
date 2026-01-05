namespace MemoryIndexer.Sdk.Intelligence.Summarization;

/// <summary>
/// Service that determines when automatic summarization should be triggered.
/// Monitors context conditions and recommends summarization actions.
/// </summary>
public interface ISummarizationTrigger
{
    /// <summary>
    /// Evaluates whether summarization should be triggered for a session.
    /// </summary>
    /// <param name="context">The current context state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trigger evaluation result with recommendations.</returns>
    Task<TriggerEvaluation> EvaluateAsync(
        SummarizationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a session event that may trigger summarization.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="eventType">Type of event that occurred.</param>
    /// <param name="metadata">Optional event metadata.</param>
    void RegisterEvent(string sessionId, SessionEventType eventType, Dictionary<string, string>? metadata = null);
}

/// <summary>
/// Context information for summarization trigger evaluation.
/// </summary>
public sealed class SummarizationContext
{
    /// <summary>
    /// Session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// User/tenant identifier.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Current token count in working memory.
    /// </summary>
    public int CurrentTokenCount { get; init; }

    /// <summary>
    /// Maximum token budget for the context.
    /// </summary>
    public int MaxTokenBudget { get; init; }

    /// <summary>
    /// Number of messages/turns in the current session.
    /// </summary>
    public int MessageCount { get; init; }

    /// <summary>
    /// Time since session started.
    /// </summary>
    public TimeSpan SessionDuration { get; init; }

    /// <summary>
    /// Time since last summarization.
    /// </summary>
    public TimeSpan? TimeSinceLastSummarization { get; init; }

    /// <summary>
    /// Number of memories created in this session.
    /// </summary>
    public int MemoriesCreated { get; init; }

    /// <summary>
    /// Accumulated importance score since last summarization.
    /// </summary>
    public float AccumulatedImportance { get; init; }

    /// <summary>
    /// Whether this is an end-of-session evaluation.
    /// </summary>
    public bool IsSessionEnding { get; init; }
}

/// <summary>
/// Result of trigger evaluation.
/// </summary>
public sealed class TriggerEvaluation
{
    /// <summary>
    /// Whether summarization should be triggered.
    /// </summary>
    public bool ShouldSummarize { get; init; }

    /// <summary>
    /// Priority level of the summarization (higher = more urgent).
    /// </summary>
    public SummarizationPriority Priority { get; init; }

    /// <summary>
    /// The trigger condition that fired.
    /// </summary>
    public TriggerCondition Condition { get; init; }

    /// <summary>
    /// Recommended summarization strategy.
    /// </summary>
    public SummarizationStrategy RecommendedStrategy { get; init; }

    /// <summary>
    /// Explanation of why summarization was triggered or not.
    /// </summary>
    public required string Explanation { get; init; }

    /// <summary>
    /// Target token count after summarization.
    /// </summary>
    public int? TargetTokenCount { get; init; }

    /// <summary>
    /// Percentage of context that should be summarized.
    /// </summary>
    public float SummarizationRatio { get; init; }
}

/// <summary>
/// Priority levels for summarization.
/// </summary>
public enum SummarizationPriority
{
    /// <summary>
    /// No summarization needed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low priority - summarize when convenient.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium priority - summarize soon.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High priority - summarize immediately.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical - context overflow imminent.
    /// </summary>
    Critical = 4
}

/// <summary>
/// Conditions that can trigger summarization.
/// </summary>
public enum TriggerCondition
{
    /// <summary>
    /// No trigger condition met.
    /// </summary>
    None = 0,

    /// <summary>
    /// Token budget threshold exceeded.
    /// </summary>
    TokenBudget = 1,

    /// <summary>
    /// Session is ending.
    /// </summary>
    SessionEnd = 2,

    /// <summary>
    /// Message count threshold exceeded.
    /// </summary>
    MessageCount = 3,

    /// <summary>
    /// Time-based trigger (periodic summarization).
    /// </summary>
    TimeBased = 4,

    /// <summary>
    /// Accumulated importance threshold exceeded.
    /// </summary>
    ImportanceThreshold = 5,

    /// <summary>
    /// Memory count threshold exceeded.
    /// </summary>
    MemoryCount = 6,

    /// <summary>
    /// Manual trigger request.
    /// </summary>
    Manual = 7,

    /// <summary>
    /// Multiple conditions met.
    /// </summary>
    Combined = 8
}

/// <summary>
/// Strategies for performing summarization.
/// </summary>
public enum SummarizationStrategy
{
    /// <summary>
    /// Extract key sentences (fast, preserves original text).
    /// </summary>
    Extractive = 0,

    /// <summary>
    /// Use compression/pruning (removes low-value tokens).
    /// </summary>
    Compression = 1,

    /// <summary>
    /// Archive old content to long-term storage.
    /// </summary>
    Archive = 2,

    /// <summary>
    /// Generate higher-level reflection memories.
    /// </summary>
    Reflection = 3,

    /// <summary>
    /// Combination of strategies.
    /// </summary>
    Hybrid = 4
}

/// <summary>
/// Types of session events that may trigger summarization.
/// </summary>
public enum SessionEventType
{
    /// <summary>
    /// Session started.
    /// </summary>
    SessionStart = 0,

    /// <summary>
    /// Session is ending.
    /// </summary>
    SessionEnd = 1,

    /// <summary>
    /// User message received.
    /// </summary>
    UserMessage = 2,

    /// <summary>
    /// Assistant response generated.
    /// </summary>
    AssistantResponse = 3,

    /// <summary>
    /// Memory was stored.
    /// </summary>
    MemoryStored = 4,

    /// <summary>
    /// Context window pressure detected.
    /// </summary>
    ContextPressure = 5,

    /// <summary>
    /// User requested summarization.
    /// </summary>
    ManualRequest = 6,

    /// <summary>
    /// Idle timeout reached.
    /// </summary>
    IdleTimeout = 7
}
