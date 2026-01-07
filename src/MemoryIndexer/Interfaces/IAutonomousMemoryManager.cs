using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Autonomous Memory Manager - LLM-directed memory management with self-optimization.
/// Inspired by MemGPT's self-directed memory management approach.
/// </summary>
/// <remarks>
/// Key concepts:
/// - LLM-triggered paging (agent decides when to page in/out)
/// - Heartbeat mechanism for function chaining
/// - Proactive memory optimization
/// - Access pattern learning
/// </remarks>
public interface IAutonomousMemoryManager
{
    /// <summary>
    /// Gets current memory state summary.
    /// </summary>
    MemoryState CurrentState { get; }

    /// <summary>
    /// Requests a memory operation based on current context.
    /// LLM calls this to trigger autonomous memory management.
    /// </summary>
    /// <param name="request">The memory operation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with recommended follow-up actions.</returns>
    Task<MemoryOperationResponse> RequestOperationAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Heartbeat function for autonomous memory checks.
    /// Called periodically or when context changes significantly.
    /// </summary>
    /// <param name="currentContext">Current conversation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommendations for memory operations.</returns>
    Task<HeartbeatResponse> HeartbeatAsync(
        string currentContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages in relevant memories based on query intent.
    /// Autonomous decision on what to page in.
    /// </summary>
    /// <param name="query">The query to find relevant memories for.</param>
    /// <param name="intent">Optional query intent classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged-in memories with relevance scores.</returns>
    Task<PageInResponse> AutonomousPageInAsync(
        string query,
        QueryIntent? intent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Autonomously pages out memories to make room.
    /// Uses importance scores and access patterns.
    /// </summary>
    /// <param name="tokensNeeded">Tokens needed to free up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of page-out operation.</returns>
    Task<PageOutResponse> AutonomousPageOutAsync(
        int tokensNeeded,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs proactive memory optimization.
    /// May reorganize, compress, or consolidate memories.
    /// </summary>
    /// <param name="options">Optimization options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Optimization result with changes made.</returns>
    Task<OptimizationResult> OptimizeMemoryAsync(
        OptimizationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a memory access for pattern learning.
    /// </summary>
    /// <param name="memoryId">Accessed memory ID.</param>
    /// <param name="accessType">Type of access.</param>
    /// <param name="context">Context of access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAccessAsync(
        Guid memoryId,
        MemoryAccessType accessType,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memory access statistics for optimization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Access statistics.</returns>
    Task<AccessStatistics> GetAccessStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests memory operations based on current state and patterns.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Suggested operations.</returns>
    Task<IReadOnlyList<SuggestedOperation>> GetSuggestedOperationsAsync(
        CancellationToken cancellationToken = default);
}

#region Request/Response Types

/// <summary>
/// Current memory state summary.
/// </summary>
public sealed class MemoryState
{
    /// <summary>
    /// User ID for this memory state.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session ID for this memory state.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Tokens currently in main context.
    /// </summary>
    public int MainContextTokens { get; set; }

    /// <summary>
    /// Maximum token capacity.
    /// </summary>
    public int MaxContextTokens { get; set; }

    /// <summary>
    /// Context utilization percentage.
    /// </summary>
    public float UtilizationPercent => MaxContextTokens > 0
        ? (float)MainContextTokens / MaxContextTokens * 100
        : 0;

    /// <summary>
    /// Number of memories in working tier.
    /// </summary>
    public int WorkingMemoryCount { get; set; }

    /// <summary>
    /// Number of memories in session tier.
    /// </summary>
    public int SessionMemoryCount { get; set; }

    /// <summary>
    /// Number of memories in archival storage.
    /// </summary>
    public int ArchivalMemoryCount { get; set; }

    /// <summary>
    /// Last heartbeat timestamp.
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Whether optimization is recommended.
    /// </summary>
    public bool OptimizationRecommended { get; set; }

    /// <summary>
    /// Current operation mode.
    /// </summary>
    public MemoryOperationMode Mode { get; set; }
}

/// <summary>
/// Memory operation request from LLM.
/// </summary>
public sealed class MemoryOperationRequest
{
    /// <summary>
    /// Type of operation requested.
    /// </summary>
    public required MemoryOperationType OperationType { get; init; }

    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Target memory IDs (for specific operations).
    /// </summary>
    public IReadOnlyList<Guid>? TargetMemoryIds { get; init; }

    /// <summary>
    /// Query for search-based operations.
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// Context for the operation.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Maximum items to process.
    /// </summary>
    public int MaxItems { get; init; } = 10;

    /// <summary>
    /// Request heartbeat after this operation.
    /// </summary>
    public bool RequestHeartbeat { get; init; }
}

/// <summary>
/// Response to a memory operation request.
/// </summary>
public sealed class MemoryOperationResponse
{
    /// <summary>
    /// Whether operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Operation that was performed.
    /// </summary>
    public MemoryOperationType OperationType { get; set; }

    /// <summary>
    /// Result message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Memories affected by operation.
    /// </summary>
    public IReadOnlyList<MemoryReference> AffectedMemories { get; set; } = [];

    /// <summary>
    /// Tokens changed (positive = added, negative = removed).
    /// </summary>
    public int TokenDelta { get; set; }

    /// <summary>
    /// Whether a follow-up operation is recommended.
    /// </summary>
    public bool FollowUpRecommended { get; set; }

    /// <summary>
    /// Recommended follow-up operations.
    /// </summary>
    public IReadOnlyList<SuggestedOperation> RecommendedFollowUps { get; set; } = [];

    /// <summary>
    /// Whether heartbeat should be scheduled.
    /// </summary>
    public bool ScheduleHeartbeat { get; set; }

    /// <summary>
    /// Timestamp of operation.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response from heartbeat check.
/// </summary>
public sealed class HeartbeatResponse
{
    /// <summary>
    /// Current memory state.
    /// </summary>
    public required MemoryState State { get; init; }

    /// <summary>
    /// Health status.
    /// </summary>
    public MemoryHealthStatus HealthStatus { get; set; }

    /// <summary>
    /// Recommended actions.
    /// </summary>
    public IReadOnlyList<SuggestedOperation> RecommendedActions { get; set; } = [];

    /// <summary>
    /// Alerts requiring attention.
    /// </summary>
    public IReadOnlyList<MemoryAlert> Alerts { get; set; } = [];

    /// <summary>
    /// Whether immediate action is needed.
    /// </summary>
    public bool ImmediateActionRequired { get; set; }

    /// <summary>
    /// Time until next recommended heartbeat.
    /// </summary>
    public TimeSpan NextHeartbeatIn { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Response from autonomous page-in.
/// </summary>
public sealed class PageInResponse
{
    /// <summary>
    /// Whether page-in succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Memories paged into main context.
    /// </summary>
    public IReadOnlyList<MemoryWithScore> PagedInMemories { get; set; } = [];

    /// <summary>
    /// Total tokens added to context.
    /// </summary>
    public int TokensAdded { get; set; }

    /// <summary>
    /// Whether memories were evicted to make room.
    /// </summary>
    public bool EvictionRequired { get; set; }

    /// <summary>
    /// Memories that were evicted.
    /// </summary>
    public IReadOnlyList<MemoryReference> EvictedMemories { get; set; } = [];
}

/// <summary>
/// Response from autonomous page-out.
/// </summary>
public sealed class PageOutResponse
{
    /// <summary>
    /// Whether page-out succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Memories paged out.
    /// </summary>
    public IReadOnlyList<MemoryReference> PagedOutMemories { get; set; } = [];

    /// <summary>
    /// Tokens freed.
    /// </summary>
    public int TokensFreed { get; set; }

    /// <summary>
    /// Memories that were archived (not just paged out).
    /// </summary>
    public IReadOnlyList<MemoryReference> ArchivedMemories { get; set; } = [];
}

/// <summary>
/// Memory optimization options.
/// </summary>
public sealed class OptimizationOptions
{
    /// <summary>
    /// Target utilization percentage.
    /// </summary>
    public float TargetUtilization { get; init; } = 70f;

    /// <summary>
    /// Whether to compress memories.
    /// </summary>
    public bool EnableCompression { get; init; } = true;

    /// <summary>
    /// Whether to consolidate related memories.
    /// </summary>
    public bool EnableConsolidation { get; init; } = true;

    /// <summary>
    /// Whether to archive old memories.
    /// </summary>
    public bool EnableArchival { get; init; } = true;

    /// <summary>
    /// Minimum importance score to retain.
    /// </summary>
    public float MinImportanceToRetain { get; init; } = 0.3f;

    /// <summary>
    /// Maximum age for working memory (hours).
    /// </summary>
    public int MaxWorkingMemoryAgeHours { get; init; } = 24;
}

/// <summary>
/// Result of memory optimization.
/// </summary>
public sealed class OptimizationResult
{
    /// <summary>
    /// Whether optimization succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Actions taken during optimization.
    /// </summary>
    public IReadOnlyList<OptimizationAction> ActionsTaken { get; set; } = [];

    /// <summary>
    /// Tokens before optimization.
    /// </summary>
    public int TokensBefore { get; set; }

    /// <summary>
    /// Tokens after optimization.
    /// </summary>
    public int TokensAfter { get; set; }

    /// <summary>
    /// Memories compressed.
    /// </summary>
    public int MemoriesCompressed { get; set; }

    /// <summary>
    /// Memories consolidated.
    /// </summary>
    public int MemoriesConsolidated { get; set; }

    /// <summary>
    /// Memories archived.
    /// </summary>
    public int MemoriesArchived { get; set; }

    /// <summary>
    /// Duration of optimization.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Memory access statistics.
/// </summary>
public sealed class AccessStatistics
{
    /// <summary>
    /// Total memory accesses.
    /// </summary>
    public int TotalAccesses { get; set; }

    /// <summary>
    /// Cache hit rate (found in main context).
    /// </summary>
    public float HitRate { get; set; }

    /// <summary>
    /// Average access latency.
    /// </summary>
    public TimeSpan AverageLatency { get; set; }

    /// <summary>
    /// Most accessed memories.
    /// </summary>
    public IReadOnlyList<AccessPattern> TopAccessedMemories { get; set; } = [];

    /// <summary>
    /// Access patterns by time of day.
    /// </summary>
    public IReadOnlyDictionary<int, int> AccessesByHour { get; set; } = new Dictionary<int, int>();

    /// <summary>
    /// Common access sequences.
    /// </summary>
    public IReadOnlyList<AccessSequence> CommonSequences { get; set; } = [];
}

/// <summary>
/// Suggested memory operation.
/// </summary>
public sealed class SuggestedOperation
{
    /// <summary>
    /// Suggested operation type.
    /// </summary>
    public MemoryOperationType OperationType { get; set; }

    /// <summary>
    /// Priority of the suggestion.
    /// </summary>
    public OperationPriority Priority { get; set; }

    /// <summary>
    /// Reason for the suggestion.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Target memory IDs if applicable.
    /// </summary>
    public IReadOnlyList<Guid>? TargetMemoryIds { get; set; }

    /// <summary>
    /// Estimated benefit (tokens saved, relevance improved, etc.).
    /// </summary>
    public float EstimatedBenefit { get; set; }
}

/// <summary>
/// Memory reference with basic info.
/// </summary>
public sealed class MemoryReference
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Content preview.
    /// </summary>
    public string Preview { get; set; } = string.Empty;

    /// <summary>
    /// Token count.
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Memory tier.
    /// </summary>
    public Tier Tier { get; set; }
}

/// <summary>
/// Memory with relevance score.
/// </summary>
public sealed class MemoryWithScore
{
    /// <summary>
    /// The memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Relevance score (0-1).
    /// </summary>
    public float RelevanceScore { get; set; }

    /// <summary>
    /// Source tier before page-in.
    /// </summary>
    public Tier SourceTier { get; set; }
}

/// <summary>
/// Memory access pattern.
/// </summary>
public sealed class AccessPattern
{
    /// <summary>
    /// Memory ID.
    /// </summary>
    public Guid MemoryId { get; set; }

    /// <summary>
    /// Access count.
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// Last access time.
    /// </summary>
    public DateTime LastAccessed { get; set; }

    /// <summary>
    /// Average interval between accesses.
    /// </summary>
    public TimeSpan AverageInterval { get; set; }
}

/// <summary>
/// Common access sequence.
/// </summary>
public sealed class AccessSequence
{
    /// <summary>
    /// Memory IDs in sequence order.
    /// </summary>
    public IReadOnlyList<Guid> MemoryIds { get; set; } = [];

    /// <summary>
    /// How often this sequence occurs.
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// Confidence that this is a meaningful pattern.
    /// </summary>
    public float Confidence { get; set; }
}

/// <summary>
/// Memory alert.
/// </summary>
public sealed class MemoryAlert
{
    /// <summary>
    /// Alert type.
    /// </summary>
    public AlertType Type { get; set; }

    /// <summary>
    /// Alert severity.
    /// </summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>
    /// Alert message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Related memory IDs.
    /// </summary>
    public IReadOnlyList<Guid>? RelatedMemoryIds { get; set; }
}

/// <summary>
/// Optimization action taken.
/// </summary>
public sealed class OptimizationAction
{
    /// <summary>
    /// Action type.
    /// </summary>
    public OptimizationActionType Type { get; set; }

    /// <summary>
    /// Affected memory IDs.
    /// </summary>
    public IReadOnlyList<Guid> AffectedMemoryIds { get; set; } = [];

    /// <summary>
    /// Description of action.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Tokens saved by this action.
    /// </summary>
    public int TokensSaved { get; set; }
}

#endregion

#region Enums

/// <summary>
/// Types of memory operations.
/// </summary>
public enum MemoryOperationType
{
    /// <summary>Page memories into main context.</summary>
    PageIn,
    /// <summary>Page memories out of main context.</summary>
    PageOut,
    /// <summary>Archive memories to long-term storage.</summary>
    Archive,
    /// <summary>Retrieve memories from archive.</summary>
    Retrieve,
    /// <summary>Consolidate related memories.</summary>
    Consolidate,
    /// <summary>Compress memory content.</summary>
    Compress,
    /// <summary>Delete memory.</summary>
    Delete,
    /// <summary>Update memory content.</summary>
    Update,
    /// <summary>Optimize memory organization.</summary>
    Optimize,
    /// <summary>Trigger reflection.</summary>
    Reflect
}

/// <summary>
/// Memory access types.
/// </summary>
public enum MemoryAccessType
{
    /// <summary>Read access.</summary>
    Read,
    /// <summary>Write/update access.</summary>
    Write,
    /// <summary>Search/query access.</summary>
    Search,
    /// <summary>Paged into context.</summary>
    PageIn,
    /// <summary>Paged out of context.</summary>
    PageOut
}

/// <summary>
/// Memory operation mode.
/// </summary>
public enum MemoryOperationMode
{
    /// <summary>Normal operation.</summary>
    Normal,
    /// <summary>Low memory mode (aggressive eviction).</summary>
    LowMemory,
    /// <summary>High priority mode (retain more).</summary>
    HighPriority,
    /// <summary>Optimization in progress.</summary>
    Optimizing
}

/// <summary>
/// Memory health status.
/// </summary>
public enum MemoryHealthStatus
{
    /// <summary>Healthy, no action needed.</summary>
    Healthy,
    /// <summary>Some optimization recommended.</summary>
    NeedsOptimization,
    /// <summary>High utilization, consider eviction.</summary>
    HighUtilization,
    /// <summary>Critical, immediate action needed.</summary>
    Critical
}

/// <summary>
/// Operation priority.
/// </summary>
public enum OperationPriority
{
    /// <summary>Low priority, can be deferred.</summary>
    Low,
    /// <summary>Normal priority.</summary>
    Normal,
    /// <summary>High priority, should be done soon.</summary>
    High,
    /// <summary>Critical, do immediately.</summary>
    Critical
}

/// <summary>
/// Alert types.
/// </summary>
public enum AlertType
{
    /// <summary>Context approaching capacity.</summary>
    HighUtilization,
    /// <summary>Stale memories detected.</summary>
    StaleMemories,
    /// <summary>Contradictions detected.</summary>
    Contradictions,
    /// <summary>Redundant memories detected.</summary>
    Redundancy,
    /// <summary>Important memory at risk of eviction.</summary>
    ImportantMemoryAtRisk,
    /// <summary>Optimization opportunity.</summary>
    OptimizationOpportunity
}

/// <summary>
/// Alert severity.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Informational.</summary>
    Info,
    /// <summary>Warning, consider action.</summary>
    Warning,
    /// <summary>Error, action recommended.</summary>
    Error,
    /// <summary>Critical, immediate action needed.</summary>
    Critical
}

/// <summary>
/// Optimization action types.
/// </summary>
public enum OptimizationActionType
{
    /// <summary>Compressed memory content.</summary>
    Compressed,
    /// <summary>Consolidated multiple memories.</summary>
    Consolidated,
    /// <summary>Archived to long-term storage.</summary>
    Archived,
    /// <summary>Evicted from working memory.</summary>
    Evicted,
    /// <summary>Reorganized memory structure.</summary>
    Reorganized,
    /// <summary>Updated importance scores.</summary>
    RecomputedImportance
}

#endregion
