using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Summarization;

/// <summary>
/// Threshold-based summarization trigger that monitors various context conditions.
/// </summary>
public sealed class ThresholdBasedTrigger : ISummarizationTrigger
{
    private readonly TriggerOptions _options;
    private readonly ILogger<ThresholdBasedTrigger> _logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessionStates = new();

    public ThresholdBasedTrigger(
        IOptions<TriggerOptions> options,
        ILogger<ThresholdBasedTrigger> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TriggerEvaluation> EvaluateAsync(
        SummarizationContext context,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<(TriggerCondition Condition, float Urgency, string Reason)>();

        // Evaluate token budget
        var tokenUsage = (float)context.CurrentTokenCount / context.MaxTokenBudget;
        if (tokenUsage >= _options.CriticalTokenThreshold)
        {
            conditions.Add((TriggerCondition.TokenBudget, 1.0f,
                $"Token usage critical: {tokenUsage:P0} of budget"));
        }
        else if (tokenUsage >= _options.HighTokenThreshold)
        {
            conditions.Add((TriggerCondition.TokenBudget, 0.8f,
                $"Token usage high: {tokenUsage:P0} of budget"));
        }
        else if (tokenUsage >= _options.MediumTokenThreshold)
        {
            conditions.Add((TriggerCondition.TokenBudget, 0.5f,
                $"Token usage medium: {tokenUsage:P0} of budget"));
        }

        // Evaluate session end
        if (context.IsSessionEnding)
        {
            conditions.Add((TriggerCondition.SessionEnd, 0.9f,
                "Session is ending; consolidation recommended"));
        }

        // Evaluate message count
        if (context.MessageCount >= _options.MessageCountThreshold)
        {
            var urgency = Math.Min(1.0f, (float)context.MessageCount / (_options.MessageCountThreshold * 2));
            conditions.Add((TriggerCondition.MessageCount, urgency,
                $"Message count ({context.MessageCount}) exceeds threshold ({_options.MessageCountThreshold})"));
        }

        // Evaluate time since last summarization
        if (context.TimeSinceLastSummarization.HasValue &&
            context.TimeSinceLastSummarization.Value >= _options.MaxTimeBetweenSummarizations)
        {
            var overtime = context.TimeSinceLastSummarization.Value - _options.MaxTimeBetweenSummarizations;
            var urgency = Math.Min(1.0f, (float)overtime.TotalMinutes / 30);
            conditions.Add((TriggerCondition.TimeBased, urgency,
                $"Time since last summarization: {context.TimeSinceLastSummarization.Value.TotalMinutes:F0} minutes"));
        }

        // Evaluate accumulated importance
        if (context.AccumulatedImportance >= _options.ImportanceThreshold)
        {
            var urgency = Math.Min(1.0f, context.AccumulatedImportance / (_options.ImportanceThreshold * 2));
            conditions.Add((TriggerCondition.ImportanceThreshold, urgency,
                $"Accumulated importance ({context.AccumulatedImportance:F2}) exceeds threshold"));
        }

        // Evaluate memory count
        if (context.MemoriesCreated >= _options.MemoryCountThreshold)
        {
            var urgency = Math.Min(1.0f, (float)context.MemoriesCreated / (_options.MemoryCountThreshold * 2));
            conditions.Add((TriggerCondition.MemoryCount, urgency,
                $"Memory count ({context.MemoriesCreated}) exceeds threshold"));
        }

        // Determine result
        if (conditions.Count == 0)
        {
            _logger.LogDebug("No summarization trigger conditions met for session {SessionId}",
                context.SessionId);

            return Task.FromResult(new TriggerEvaluation
            {
                ShouldSummarize = false,
                Priority = SummarizationPriority.None,
                Condition = TriggerCondition.None,
                RecommendedStrategy = SummarizationStrategy.Extractive,
                Explanation = "No trigger conditions met",
                SummarizationRatio = 0
            });
        }

        // Calculate overall priority based on highest urgency
        var maxUrgency = conditions.Max(c => c.Urgency);
        var priority = maxUrgency switch
        {
            >= 0.9f => SummarizationPriority.Critical,
            >= 0.7f => SummarizationPriority.High,
            >= 0.4f => SummarizationPriority.Medium,
            _ => SummarizationPriority.Low
        };

        // Determine primary condition
        var primaryCondition = conditions.Count > 1
            ? TriggerCondition.Combined
            : conditions[0].Condition;

        // Choose strategy based on conditions
        var strategy = DetermineStrategy(conditions, context);

        // Calculate target token count
        var targetTokens = CalculateTargetTokenCount(context, priority);
        var summarizationRatio = 1.0f - ((float)targetTokens / context.CurrentTokenCount);

        var explanation = string.Join("; ", conditions.Select(c => c.Reason));

        _logger.LogInformation(
            "Summarization triggered for session {SessionId}: {Priority} priority, {Condition} condition",
            context.SessionId, priority, primaryCondition);

        return Task.FromResult(new TriggerEvaluation
        {
            ShouldSummarize = true,
            Priority = priority,
            Condition = primaryCondition,
            RecommendedStrategy = strategy,
            Explanation = explanation,
            TargetTokenCount = targetTokens,
            SummarizationRatio = summarizationRatio
        });
    }

    /// <inheritdoc />
    public void RegisterEvent(string sessionId, SessionEventType eventType, Dictionary<string, string>? metadata = null)
    {
        var state = _sessionStates.GetOrAdd(sessionId, _ => new SessionState());

        lock (state)
        {
            state.LastEventTime = DateTime.UtcNow;
            state.EventCount++;

            switch (eventType)
            {
                case SessionEventType.SessionStart:
                    state.SessionStartTime = DateTime.UtcNow;
                    break;
                case SessionEventType.SessionEnd:
                    state.IsEnding = true;
                    break;
                case SessionEventType.UserMessage:
                case SessionEventType.AssistantResponse:
                    state.MessageCount++;
                    break;
                case SessionEventType.MemoryStored:
                    state.MemoryCount++;
                    if (metadata?.TryGetValue("importance", out var importance) == true &&
                        float.TryParse(importance, out var score))
                    {
                        state.AccumulatedImportance += score;
                    }
                    break;
                case SessionEventType.ManualRequest:
                    state.ManualTriggerRequested = true;
                    break;
            }
        }

        _logger.LogDebug(
            "Session {SessionId} event: {EventType}, messages: {Messages}, memories: {Memories}",
            sessionId, eventType, state.MessageCount, state.MemoryCount);
    }

    private SummarizationStrategy DetermineStrategy(
        List<(TriggerCondition Condition, float Urgency, string Reason)> conditions,
        SummarizationContext context)
    {
        // Session end: prefer reflection to consolidate learnings
        if (conditions.Any(c => c.Condition == TriggerCondition.SessionEnd))
        {
            return SummarizationStrategy.Reflection;
        }

        // Critical token pressure: use compression for immediate relief
        if (conditions.Any(c => c.Condition == TriggerCondition.TokenBudget && c.Urgency >= 0.9f))
        {
            return SummarizationStrategy.Compression;
        }

        // High token pressure: hybrid approach
        if (conditions.Any(c => c.Condition == TriggerCondition.TokenBudget && c.Urgency >= 0.7f))
        {
            return SummarizationStrategy.Hybrid;
        }

        // Many memories: archive older ones
        if (conditions.Any(c => c.Condition == TriggerCondition.MemoryCount))
        {
            return SummarizationStrategy.Archive;
        }

        // Default: extractive summarization
        return SummarizationStrategy.Extractive;
    }

    private int CalculateTargetTokenCount(SummarizationContext context, SummarizationPriority priority)
    {
        var targetRatio = priority switch
        {
            SummarizationPriority.Critical => _options.CriticalTargetRatio,
            SummarizationPriority.High => _options.HighTargetRatio,
            SummarizationPriority.Medium => _options.MediumTargetRatio,
            _ => _options.LowTargetRatio
        };

        return (int)(context.MaxTokenBudget * targetRatio);
    }

    private sealed class SessionState
    {
        public DateTime SessionStartTime { get; set; } = DateTime.UtcNow;
        public DateTime LastEventTime { get; set; } = DateTime.UtcNow;
        public DateTime? LastSummarizationTime { get; set; }
        public int EventCount { get; set; }
        public int MessageCount { get; set; }
        public int MemoryCount { get; set; }
        public float AccumulatedImportance { get; set; }
        public bool IsEnding { get; set; }
        public bool ManualTriggerRequested { get; set; }
    }
}

/// <summary>
/// Configuration options for the threshold-based trigger.
/// </summary>
public sealed class TriggerOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "MemoryIndexer:Summarization:Trigger";

    /// <summary>
    /// Token usage threshold for medium priority (default: 0.6 = 60%).
    /// </summary>
    public float MediumTokenThreshold { get; set; } = 0.6f;

    /// <summary>
    /// Token usage threshold for high priority (default: 0.8 = 80%).
    /// </summary>
    public float HighTokenThreshold { get; set; } = 0.8f;

    /// <summary>
    /// Token usage threshold for critical priority (default: 0.95 = 95%).
    /// </summary>
    public float CriticalTokenThreshold { get; set; } = 0.95f;

    /// <summary>
    /// Message count threshold to trigger summarization (default: 50).
    /// </summary>
    public int MessageCountThreshold { get; set; } = 50;

    /// <summary>
    /// Memory count threshold to trigger summarization (default: 20).
    /// </summary>
    public int MemoryCountThreshold { get; set; } = 20;

    /// <summary>
    /// Accumulated importance threshold (default: 5.0).
    /// </summary>
    public float ImportanceThreshold { get; set; } = 5.0f;

    /// <summary>
    /// Maximum time between summarizations (default: 30 minutes).
    /// </summary>
    public TimeSpan MaxTimeBetweenSummarizations { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Target token ratio for low priority (default: 0.7 = 70%).
    /// </summary>
    public float LowTargetRatio { get; set; } = 0.7f;

    /// <summary>
    /// Target token ratio for medium priority (default: 0.6 = 60%).
    /// </summary>
    public float MediumTargetRatio { get; set; } = 0.6f;

    /// <summary>
    /// Target token ratio for high priority (default: 0.5 = 50%).
    /// </summary>
    public float HighTargetRatio { get; set; } = 0.5f;

    /// <summary>
    /// Target token ratio for critical priority (default: 0.4 = 40%).
    /// </summary>
    public float CriticalTargetRatio { get; set; } = 0.4f;
}
