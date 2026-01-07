using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Manages tier promotion logic in the 4-tier cognitive architecture.
/// Coordinates promotion between: Buffer (T0) → Short (T1) → Long (T2) → Archive (T3).
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - Buffer (T0): Sensory register, async processing
/// - Short (T1): Working memory, active context
/// - Long (T2): Episodic store, session experiences
/// - Archive (T3): Semantic store, long-term knowledge
///
/// Promotion Logic:
/// - Buffer → Short: OR logic (time OR tokens OR turns)
/// - Short → Long: OR logic (time OR tokens OR turns OR topic change)
/// - Long → Archive: AND logic (confidence AND confirmations)
/// </remarks>
public interface ITierManager
{
    /// <summary>
    /// Evaluates whether a memory should be promoted from current tier to next tier.
    /// </summary>
    /// <param name="memory">The memory to evaluate.</param>
    /// <param name="currentContext">Current context for evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Promotion recommendation with target tier and reasoning.</returns>
    Task<TierPromotionRecommendation> EvaluatePromotionAsync(
        MemoryUnit memory,
        TierEvaluationContext currentContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if promotion trigger conditions are satisfied for a tier.
    /// </summary>
    /// <param name="sourceTier">Source tier to check.</param>
    /// <param name="context">Current context for trigger evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trigger status with satisfied conditions.</returns>
    Task<TierTriggerStatus> CheckPromotionTriggersAsync(
        Tier sourceTier,
        TierEvaluationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes a memory to the specified target tier.
    /// Handles tier-specific promotion logic and metadata updates.
    /// </summary>
    /// <param name="memory">The memory to promote.</param>
    /// <param name="targetTier">Target tier for promotion.</param>
    /// <param name="reason">Reason for promotion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Promotion result with updated memory.</returns>
    Task<TierPromotionResult> PromoteAsync(
        MemoryUnit memory,
        Tier targetTier,
        PromotionReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Demotes a memory to a lower tier (eviction/page-out).
    /// </summary>
    /// <param name="memory">The memory to demote.</param>
    /// <param name="targetTier">Target tier for demotion.</param>
    /// <param name="reason">Reason for demotion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Demotion result with updated memory.</returns>
    Task<TierPromotionResult> DemoteAsync(
        MemoryUnit memory,
        Tier targetTier,
        PromotionReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets promotion statistics for a tier.
    /// </summary>
    /// <param name="tier">Tier to get statistics for.</param>
    /// <returns>Tier promotion statistics.</returns>
    TierPromotionStatistics GetTierStatistics(Tier tier);
}

/// <summary>
/// Context for tier evaluation and promotion decisions.
/// </summary>
public sealed class TierEvaluationContext
{
    /// <summary>
    /// User ID for context.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Session ID (optional).
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Current turn count.
    /// </summary>
    public int TurnCount { get; init; }

    /// <summary>
    /// Current token count in tier.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Time elapsed since tier initialization.
    /// </summary>
    public TimeSpan TimeElapsed { get; init; }

    /// <summary>
    /// Topic change detected (for Short → Long promotion).
    /// </summary>
    public bool TopicChangeDetected { get; init; }

    /// <summary>
    /// Current topic ID.
    /// </summary>
    public string? TopicId { get; init; }

    /// <summary>
    /// Session ending (for Long → Archive promotion).
    /// </summary>
    public bool SessionEnding { get; init; }

    /// <summary>
    /// Additional metadata for evaluation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Recommendation for tier promotion.
/// </summary>
public sealed class TierPromotionRecommendation
{
    /// <summary>
    /// Whether promotion is recommended.
    /// </summary>
    public bool ShouldPromote { get; init; }

    /// <summary>
    /// Target tier for promotion (if recommended).
    /// </summary>
    public Tier? TargetTier { get; init; }

    /// <summary>
    /// Reason for promotion recommendation.
    /// </summary>
    public PromotionReason Reason { get; init; }

    /// <summary>
    /// Confidence in recommendation (0.0-1.0).
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Explanation for recommendation.
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// Satisfied trigger conditions.
    /// </summary>
    public IReadOnlyList<string> SatisfiedTriggers { get; init; } = [];
}

/// <summary>
/// Status of promotion triggers for a tier.
/// </summary>
public sealed class TierTriggerStatus
{
    /// <summary>
    /// Whether any trigger is satisfied.
    /// </summary>
    public bool IsTriggered { get; init; }

    /// <summary>
    /// Logic type for tier triggers (OR or AND).
    /// </summary>
    public TriggerLogicType LogicType { get; init; }

    /// <summary>
    /// Satisfied trigger conditions.
    /// </summary>
    public IReadOnlyList<PromotionTrigger> SatisfiedTriggers { get; init; } = [];

    /// <summary>
    /// All trigger conditions for tier.
    /// </summary>
    public IReadOnlyList<PromotionTrigger> AllTriggers { get; init; } = [];

    /// <summary>
    /// Primary trigger that caused satisfaction.
    /// </summary>
    public PromotionTrigger? PrimaryTrigger { get; init; }
}

/// <summary>
/// Individual promotion trigger.
/// </summary>
public sealed class PromotionTrigger
{
    /// <summary>
    /// Trigger type.
    /// </summary>
    public required PromotionTriggerType Type { get; init; }

    /// <summary>
    /// Whether this trigger is satisfied.
    /// </summary>
    public bool IsSatisfied { get; init; }

    /// <summary>
    /// Current value for trigger metric.
    /// </summary>
    public object? CurrentValue { get; init; }

    /// <summary>
    /// Threshold value for trigger.
    /// </summary>
    public object? ThresholdValue { get; init; }

    /// <summary>
    /// Description of trigger condition.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Logic type for trigger evaluation.
/// </summary>
public enum TriggerLogicType
{
    /// <summary>
    /// OR logic: Any trigger satisfied → promote.
    /// </summary>
    Or,

    /// <summary>
    /// AND logic: All triggers must be satisfied → promote.
    /// </summary>
    And
}

/// <summary>
/// Reason for tier promotion/demotion.
/// </summary>
public enum PromotionReason
{
    /// <summary>
    /// Unknown or not specified.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Automatic promotion by trigger.
    /// </summary>
    AutomaticTrigger = 1,

    /// <summary>
    /// Manual promotion requested.
    /// </summary>
    Manual = 2,

    /// <summary>
    /// Eviction due to capacity.
    /// </summary>
    CapacityEviction = 3,

    /// <summary>
    /// Topic change boundary.
    /// </summary>
    TopicBoundary = 4,

    /// <summary>
    /// Session end boundary.
    /// </summary>
    SessionBoundary = 5,

    /// <summary>
    /// Confidence/confirmation threshold met.
    /// </summary>
    ThresholdMet = 6,

    /// <summary>
    /// Low retention score.
    /// </summary>
    LowRetention = 7
}

/// <summary>
/// Result of tier promotion/demotion operation.
/// </summary>
public sealed class TierPromotionResult
{
    /// <summary>
    /// Whether operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Updated memory after promotion/demotion.
    /// </summary>
    public MemoryUnit? UpdatedMemory { get; init; }

    /// <summary>
    /// Original tier before operation.
    /// </summary>
    public Tier OriginalTier { get; init; }

    /// <summary>
    /// New tier after operation.
    /// </summary>
    public Tier NewTier { get; init; }

    /// <summary>
    /// Reason for operation.
    /// </summary>
    public PromotionReason Reason { get; init; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Additional metadata about operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Statistics for a tier.
/// </summary>
public sealed class TierPromotionStatistics
{
    /// <summary>
    /// Tier being tracked.
    /// </summary>
    public Tier Tier { get; init; }

    /// <summary>
    /// Total promotions from this tier.
    /// </summary>
    public int TotalPromotions { get; init; }

    /// <summary>
    /// Total demotions to this tier.
    /// </summary>
    public int TotalDemotions { get; init; }

    /// <summary>
    /// Current memory count in tier.
    /// </summary>
    public int CurrentCount { get; init; }

    /// <summary>
    /// Average retention time in tier.
    /// </summary>
    public TimeSpan AverageRetentionTime { get; init; }

    /// <summary>
    /// Most common promotion trigger.
    /// </summary>
    public PromotionTriggerType? MostCommonTrigger { get; init; }

    /// <summary>
    /// Promotion rate (promotions per hour).
    /// </summary>
    public float PromotionRate { get; init; }
}
