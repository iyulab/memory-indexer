using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of tier promotion management for the 4-tier cognitive architecture.
/// </summary>
public sealed class TierManager : ITierManager
{
    private readonly ILogger<TierManager> _logger;
    private readonly MemoryIndexerOptions _options;
    private readonly Dictionary<Tier, TierPromotionMetrics> _metrics = new();

    public TierManager(
        IOptions<MemoryIndexerOptions> options,
        ILogger<TierManager> logger)
    {
        _logger = logger;
        _options = options.Value;

        // Initialize metrics for each tier
        foreach (Tier tier in Enum.GetValues<Tier>())
        {
            _metrics[tier] = new TierPromotionMetrics();
        }
    }

    /// <inheritdoc />
    public Task<TierPromotionRecommendation> EvaluatePromotionAsync(
        MemoryUnit memory,
        TierEvaluationContext currentContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(currentContext);

        var currentTier = memory.Tier;
        var targetTier = GetNextTier(currentTier);

        if (targetTier == null)
        {
            // Already at highest tier
            return Task.FromResult(new TierPromotionRecommendation
            {
                ShouldPromote = false,
                Confidence = 1.0f,
                Explanation = "Memory is already at highest tier (Archive)"
            });
        }

        // Evaluate promotion based on tier-specific logic
        var recommendation = currentTier switch
        {
            Tier.Buffer => EvaluateBufferPromotion(memory, currentContext),
            Tier.Short => EvaluateShortPromotion(memory, currentContext),
            Tier.Long => EvaluateLongPromotion(memory, currentContext),
            Tier.Archive => new TierPromotionRecommendation { ShouldPromote = false, Confidence = 1.0f },
            _ => new TierPromotionRecommendation { ShouldPromote = false, Confidence = 0.5f }
        };

        return Task.FromResult(recommendation);
    }

    /// <inheritdoc />
    public Task<TierTriggerStatus> CheckPromotionTriggersAsync(
        Tier sourceTier,
        TierEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = sourceTier switch
        {
            Tier.Buffer => CheckBufferTriggers(context),
            Tier.Short => CheckShortTriggers(context),
            Tier.Long => CheckLongTriggers(context),
            _ => new TierTriggerStatus { IsTriggered = false, LogicType = TriggerLogicType.Or }
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<TierPromotionResult> PromoteAsync(
        MemoryUnit memory,
        Tier targetTier,
        PromotionReason reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var originalTier = memory.Tier;

        if (targetTier <= originalTier)
        {
            return Task.FromResult(new TierPromotionResult
            {
                Success = false,
                OriginalTier = originalTier,
                NewTier = originalTier,
                Reason = reason,
                Error = $"Target tier {targetTier} must be higher than current tier {originalTier}"
            });
        }

        // Update memory tier
        memory.Tier = targetTier;
        memory.MarkUpdated();

        // Update tier-specific metadata
        if (targetTier == Tier.Archive)
        {
            // Archive promotion: mark as semantic
            if (memory.Type == MemoryType.Episodic)
            {
                memory.Type = MemoryType.Semantic;
            }
        }

        // Update metrics
        _metrics[originalTier].RecordPromotion();

        _logger.LogDebug("Promoted memory {MemoryId} from {OriginalTier} to {NewTier} (reason: {Reason})",
            memory.Id, originalTier, targetTier, reason);

        return Task.FromResult(new TierPromotionResult
        {
            Success = true,
            UpdatedMemory = memory,
            OriginalTier = originalTier,
            NewTier = targetTier,
            Reason = reason
        });
    }

    /// <inheritdoc />
    public Task<TierPromotionResult> DemoteAsync(
        MemoryUnit memory,
        Tier targetTier,
        PromotionReason reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var originalTier = memory.Tier;

        if (targetTier >= originalTier)
        {
            return Task.FromResult(new TierPromotionResult
            {
                Success = false,
                OriginalTier = originalTier,
                NewTier = originalTier,
                Reason = reason,
                Error = $"Target tier {targetTier} must be lower than current tier {originalTier}"
            });
        }

        // Update memory tier
        memory.Tier = targetTier;
        memory.MarkUpdated();

        // Update metrics
        _metrics[targetTier].RecordDemotion();

        _logger.LogDebug("Demoted memory {MemoryId} from {OriginalTier} to {NewTier} (reason: {Reason})",
            memory.Id, originalTier, targetTier, reason);

        return Task.FromResult(new TierPromotionResult
        {
            Success = true,
            UpdatedMemory = memory,
            OriginalTier = originalTier,
            NewTier = targetTier,
            Reason = reason
        });
    }

    /// <inheritdoc />
    public TierPromotionStatistics GetTierStatistics(Tier tier)
    {
        var metrics = _metrics[tier];

        return new TierPromotionStatistics
        {
            Tier = tier,
            TotalPromotions = metrics.PromotionCount,
            TotalDemotions = metrics.DemotionCount,
            CurrentCount = 0, // TODO: Track current count
            AverageRetentionTime = TimeSpan.Zero, // TODO: Calculate from metrics
            MostCommonTrigger = metrics.MostCommonTrigger,
            PromotionRate = 0 // TODO: Calculate rate
        };
    }

    #region Private Evaluation Methods

    private TierPromotionRecommendation EvaluateBufferPromotion(
        MemoryUnit memory,
        TierEvaluationContext context)
    {
        var triggers = CheckBufferTriggers(context);

        if (!triggers.IsTriggered)
        {
            return new TierPromotionRecommendation
            {
                ShouldPromote = false,
                Confidence = 0.8f,
                Explanation = "No Buffer → Short triggers satisfied"
            };
        }

        return new TierPromotionRecommendation
        {
            ShouldPromote = true,
            TargetTier = Tier.Short,
            Reason = PromotionReason.AutomaticTrigger,
            Confidence = 0.9f,
            Explanation = $"Buffer trigger satisfied: {triggers.PrimaryTrigger?.Type}",
            SatisfiedTriggers = triggers.SatisfiedTriggers.Select(t => t.Type.ToString()).ToList()
        };
    }

    private TierPromotionRecommendation EvaluateShortPromotion(
        MemoryUnit memory,
        TierEvaluationContext context)
    {
        var triggers = CheckShortTriggers(context);

        if (!triggers.IsTriggered)
        {
            return new TierPromotionRecommendation
            {
                ShouldPromote = false,
                Confidence = 0.8f,
                Explanation = "No Short → Long triggers satisfied"
            };
        }

        return new TierPromotionRecommendation
        {
            ShouldPromote = true,
            TargetTier = Tier.Long,
            Reason = triggers.PrimaryTrigger?.Type == PromotionTriggerType.TopicChange
                ? PromotionReason.TopicBoundary
                : PromotionReason.AutomaticTrigger,
            Confidence = 0.85f,
            Explanation = $"Short trigger satisfied: {triggers.PrimaryTrigger?.Type}",
            SatisfiedTriggers = triggers.SatisfiedTriggers.Select(t => t.Type.ToString()).ToList()
        };
    }

    private TierPromotionRecommendation EvaluateLongPromotion(
        MemoryUnit memory,
        TierEvaluationContext context)
    {
        var triggers = CheckLongTriggers(context);

        // Long → Archive uses AND logic (confidence AND confirmations)
        // TODO: Replace with proper ArchiveStoreOptions when configuration is added
        const float minConfidenceThreshold = 0.8f;
        const int minConfirmationCount = 3;

        var hasConfidence = memory.Confidence >= minConfidenceThreshold;
        var hasConfirmations = memory.ConfirmCount >= minConfirmationCount;

        if (!hasConfidence || !hasConfirmations)
        {
            return new TierPromotionRecommendation
            {
                ShouldPromote = false,
                Confidence = 0.9f,
                Explanation = $"Archive promotion requires confidence ≥ {minConfidenceThreshold} AND confirmations ≥ {minConfirmationCount}. Current: {memory.Confidence:F2} confidence, {memory.ConfirmCount} confirmations"
            };
        }

        return new TierPromotionRecommendation
        {
            ShouldPromote = true,
            TargetTier = Tier.Archive,
            Reason = PromotionReason.ThresholdMet,
            Confidence = 0.95f,
            Explanation = $"Archive thresholds met: {memory.Confidence:F2} confidence, {memory.ConfirmCount} confirmations",
            SatisfiedTriggers = triggers.SatisfiedTriggers.Select(t => t.Type.ToString()).ToList()
        };
    }

    private TierTriggerStatus CheckBufferTriggers(TierEvaluationContext context)
    {
        var bufferOptions = _options.SensoryBuffer;

        var triggers = new List<PromotionTrigger>
        {
            new()
            {
                Type = PromotionTriggerType.IdleTimeout,
                IsSatisfied = context.TimeElapsed >= bufferOptions.IdleTimeout,
                CurrentValue = context.TimeElapsed,
                ThresholdValue = bufferOptions.IdleTimeout,
                Description = $"Idle for {context.TimeElapsed.TotalSeconds:F1}s (threshold: {bufferOptions.IdleTimeout.TotalSeconds}s)"
            },
            new()
            {
                Type = PromotionTriggerType.TokenThreshold,
                IsSatisfied = context.TokenCount >= bufferOptions.TokenThreshold,
                CurrentValue = context.TokenCount,
                ThresholdValue = bufferOptions.TokenThreshold,
                Description = $"{context.TokenCount} tokens (threshold: {bufferOptions.TokenThreshold})"
            },
            new()
            {
                Type = PromotionTriggerType.TurnThreshold,
                IsSatisfied = context.TurnCount >= bufferOptions.TurnThreshold,
                CurrentValue = context.TurnCount,
                ThresholdValue = bufferOptions.TurnThreshold,
                Description = $"{context.TurnCount} turns (threshold: {bufferOptions.TurnThreshold})"
            }
        };

        var satisfiedTriggers = triggers.Where(t => t.IsSatisfied).ToList();
        var primaryTrigger = satisfiedTriggers.FirstOrDefault();

        return new TierTriggerStatus
        {
            IsTriggered = satisfiedTriggers.Count > 0, // OR logic
            LogicType = TriggerLogicType.Or,
            SatisfiedTriggers = satisfiedTriggers,
            AllTriggers = triggers,
            PrimaryTrigger = primaryTrigger
        };
    }

    private TierTriggerStatus CheckShortTriggers(TierEvaluationContext context)
    {
        // TODO: Replace with proper ShortTermMemoryOrchestratorOptions when configuration is added
        var idleTimeout = TimeSpan.FromMinutes(10);
        var tokenThreshold = 2000;
        var turnThreshold = 10;

        var triggers = new List<PromotionTrigger>
        {
            new()
            {
                Type = PromotionTriggerType.IdleTimeout,
                IsSatisfied = context.TimeElapsed >= idleTimeout,
                CurrentValue = context.TimeElapsed,
                ThresholdValue = idleTimeout,
                Description = $"Idle for {context.TimeElapsed.TotalMinutes:F1}min (threshold: {idleTimeout.TotalMinutes}min)"
            },
            new()
            {
                Type = PromotionTriggerType.TokenThreshold,
                IsSatisfied = context.TokenCount >= tokenThreshold,
                CurrentValue = context.TokenCount,
                ThresholdValue = tokenThreshold,
                Description = $"{context.TokenCount} tokens (threshold: {tokenThreshold})"
            },
            new()
            {
                Type = PromotionTriggerType.TurnThreshold,
                IsSatisfied = context.TurnCount >= turnThreshold,
                CurrentValue = context.TurnCount,
                ThresholdValue = turnThreshold,
                Description = $"{context.TurnCount} turns (threshold: {turnThreshold})"
            },
            new()
            {
                Type = PromotionTriggerType.TopicChange,
                IsSatisfied = context.TopicChangeDetected,
                CurrentValue = context.TopicChangeDetected,
                ThresholdValue = true,
                Description = context.TopicChangeDetected ? "Topic change detected" : "No topic change"
            },
            new()
            {
                Type = PromotionTriggerType.SessionEnd,
                IsSatisfied = context.SessionEnding,
                CurrentValue = context.SessionEnding,
                ThresholdValue = true,
                Description = context.SessionEnding ? "Session ending" : "Session active"
            }
        };

        var satisfiedTriggers = triggers.Where(t => t.IsSatisfied).ToList();
        var primaryTrigger = satisfiedTriggers.FirstOrDefault();

        return new TierTriggerStatus
        {
            IsTriggered = satisfiedTriggers.Count > 0, // OR logic
            LogicType = TriggerLogicType.Or,
            SatisfiedTriggers = satisfiedTriggers,
            AllTriggers = triggers,
            PrimaryTrigger = primaryTrigger
        };
    }

    private TierTriggerStatus CheckLongTriggers(TierEvaluationContext context)
    {
        // TODO: Replace with proper ArchiveStoreOptions when configuration is added

        // For Long → Archive, we need actual memory to check confidence/confirmations
        // This method checks context-level triggers only
        var triggers = new List<PromotionTrigger>
        {
            new()
            {
                Type = PromotionTriggerType.SessionEnd,
                IsSatisfied = context.SessionEnding,
                CurrentValue = context.SessionEnding,
                ThresholdValue = true,
                Description = context.SessionEnding ? "Session ending" : "Session active"
            }
        };

        var satisfiedTriggers = triggers.Where(t => t.IsSatisfied).ToList();

        return new TierTriggerStatus
        {
            IsTriggered = satisfiedTriggers.Count > 0,
            LogicType = TriggerLogicType.And, // Archive uses AND logic
            SatisfiedTriggers = satisfiedTriggers,
            AllTriggers = triggers,
            PrimaryTrigger = satisfiedTriggers.FirstOrDefault()
        };
    }

    private static Tier? GetNextTier(Tier currentTier) => currentTier switch
    {
        Tier.Buffer => Tier.Short,
        Tier.Short => Tier.Long,
        Tier.Long => Tier.Archive,
        Tier.Archive => null, // Already at highest tier
        _ => null
    };

    #endregion

    #region Helper Classes

    private sealed class TierPromotionMetrics
    {
        public int PromotionCount { get; private set; }
        public int DemotionCount { get; private set; }
        public PromotionTriggerType? MostCommonTrigger { get; private set; }

        public void RecordPromotion()
        {
            PromotionCount++;
        }

        public void RecordDemotion()
        {
            DemotionCount++;
        }
    }

    #endregion
}
