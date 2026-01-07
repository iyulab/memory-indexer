using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Manages scope resolution and boundaries in the 3-axis memory model.
/// Scope represents temporal containment: Turn → Topic → Session → User.
/// </summary>
/// <remarks>
/// 3-Axis Memory Model (DESIGN_V0.4.md):
/// - Type: What (Episodic, Semantic, Procedural, Fact)
/// - Scope: When (Turn, Topic, Session, User)
/// - Tier: Where (Buffer, Short, Long, Archive)
///
/// Scope Resolution:
/// - Turn (S3): Single conversation turn
/// - Topic (S2): Conversation topic cluster (detected by topic change)
/// - Session (S1): Single conversation session
/// - User (S0): Cross-session, permanent user data
/// </remarks>
public interface IScopeManager
{
    /// <summary>
    /// Gets the current scope state.
    /// </summary>
    ScopeState CurrentState { get; }

    /// <summary>
    /// Initializes scope tracking for a new session.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a conversation turn and updates scope state.
    /// Detects topic changes and scope transitions.
    /// </summary>
    /// <param name="content">The turn content.</param>
    /// <param name="role">The role (user/assistant/system).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved scope and whether a scope boundary was crossed.</returns>
    Task<ScopeResolution> RecordTurnAsync(
        string content,
        string? role = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the appropriate scope for a memory based on content and context.
    /// </summary>
    /// <param name="content">Memory content.</param>
    /// <param name="type">Memory type.</param>
    /// <param name="importance">Importance score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommended scope for the memory.</returns>
    Task<Scope> ResolveScopeAsync(
        string content,
        MemoryType type,
        float importance = 0.5f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects whether a topic change occurred based on content similarity.
    /// </summary>
    /// <param name="currentContent">Current turn content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if topic change detected, false otherwise.</returns>
    Task<bool> DetectTopicChangeAsync(
        string currentContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current topic ID.
    /// Topic ID changes when topic transitions are detected.
    /// </summary>
    /// <returns>Current topic ID.</returns>
    string GetCurrentTopicId();

    /// <summary>
    /// Filters memories by scope criteria.
    /// </summary>
    /// <param name="memories">Memories to filter.</param>
    /// <param name="targetScope">Target scope to filter by.</param>
    /// <param name="includeNarrower">Include memories with narrower scopes (e.g., Turn when filtering for Topic).</param>
    /// <returns>Filtered memories matching scope criteria.</returns>
    IReadOnlyList<MemoryUnit> FilterByScope(
        IEnumerable<MemoryUnit> memories,
        Scope targetScope,
        bool includeNarrower = true);

    /// <summary>
    /// Handles session end: finalizes scope tracking and prepares for cleanup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EndSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets scope transition statistics for the current session.
    /// </summary>
    /// <returns>Scope transition statistics.</returns>
    ScopeStatistics GetStatistics();
}

/// <summary>
/// Current state of scope tracking.
/// </summary>
public sealed class ScopeState
{
    /// <summary>
    /// Whether scope manager is initialized.
    /// </summary>
    public bool IsInitialized { get; set; }

    /// <summary>
    /// Current user ID.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Current topic ID.
    /// </summary>
    public string? TopicId { get; set; }

    /// <summary>
    /// Current turn count in session.
    /// </summary>
    public int TurnCount { get; set; }

    /// <summary>
    /// Current turn count in topic.
    /// </summary>
    public int TopicTurnCount { get; set; }

    /// <summary>
    /// Number of topic transitions in session.
    /// </summary>
    public int TopicTransitionCount { get; set; }

    /// <summary>
    /// Session start time.
    /// </summary>
    public DateTime? SessionStartTime { get; set; }

    /// <summary>
    /// Current topic start time.
    /// </summary>
    public DateTime? TopicStartTime { get; set; }

    /// <summary>
    /// Last turn timestamp.
    /// </summary>
    public DateTime? LastTurnTimestamp { get; set; }
}

/// <summary>
/// Result of scope resolution for a conversation turn.
/// </summary>
public sealed class ScopeResolution
{
    /// <summary>
    /// Resolved scope for the turn.
    /// </summary>
    public required Scope ResolvedScope { get; init; }

    /// <summary>
    /// Topic ID associated with this turn.
    /// </summary>
    public required string TopicId { get; init; }

    /// <summary>
    /// Whether a scope boundary was crossed.
    /// </summary>
    public bool BoundaryCrossed { get; init; }

    /// <summary>
    /// Type of boundary crossed (if any).
    /// </summary>
    public ScopeBoundaryType? BoundaryType { get; init; }

    /// <summary>
    /// Turn index within session.
    /// </summary>
    public int TurnIndex { get; init; }

    /// <summary>
    /// Turn index within current topic.
    /// </summary>
    public int TopicTurnIndex { get; init; }

    /// <summary>
    /// Confidence in scope resolution (0.0-1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;
}

/// <summary>
/// Type of scope boundary crossed.
/// </summary>
public enum ScopeBoundaryType
{
    /// <summary>
    /// No boundary crossed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Turn boundary (every turn).
    /// </summary>
    Turn = 1,

    /// <summary>
    /// Topic boundary (topic change detected).
    /// </summary>
    Topic = 2,

    /// <summary>
    /// Session boundary (session end).
    /// </summary>
    Session = 3
}

/// <summary>
/// Scope transition statistics.
/// </summary>
public sealed class ScopeStatistics
{
    /// <summary>
    /// Total turns in session.
    /// </summary>
    public int TotalTurns { get; init; }

    /// <summary>
    /// Total topic transitions.
    /// </summary>
    public int TopicTransitions { get; init; }

    /// <summary>
    /// Average turns per topic.
    /// </summary>
    public float AverageTurnsPerTopic { get; init; }

    /// <summary>
    /// Current topic duration.
    /// </summary>
    public TimeSpan CurrentTopicDuration { get; init; }

    /// <summary>
    /// Session duration.
    /// </summary>
    public TimeSpan SessionDuration { get; init; }

    /// <summary>
    /// Topic IDs encountered in session.
    /// </summary>
    public IReadOnlyList<string> TopicIds { get; init; } = [];
}
