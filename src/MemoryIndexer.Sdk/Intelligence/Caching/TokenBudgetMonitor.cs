using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Monitors token usage across sessions and provides budget awareness hooks.
/// Phase v0.5.0: Token Budget Awareness Hooks
/// </summary>
public sealed class TokenBudgetMonitor : ITokenBudgetMonitor
{
    private readonly ILogger<TokenBudgetMonitor> _logger;
    private readonly ConcurrentDictionary<string, SessionTokenTracker> _sessions = new();

    // Global statistics
    private long _totalTokensConsumed;
    private int _totalSessionsStarted;
    private int _exceededSessionCount;

    /// <inheritdoc/>
    public event EventHandler<TokenBudgetEventArgs>? OnBudgetWarning;

    /// <inheritdoc/>
    public event EventHandler<TokenBudgetEventArgs>? OnBudgetExceeded;

    /// <inheritdoc/>
    public event EventHandler<SessionTokenSummaryEventArgs>? OnSessionEnded;

    public TokenBudgetMonitor(ILogger<TokenBudgetMonitor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void StartSession(string sessionId, string userId, int maxTokenBudget, float warningThreshold = 0.8f)
    {
        var tracker = new SessionTokenTracker(sessionId, userId, maxTokenBudget, warningThreshold);

        if (_sessions.TryAdd(sessionId, tracker))
        {
            Interlocked.Increment(ref _totalSessionsStarted);
            _logger.LogDebug(
                "Token budget monitoring started for session {SessionId}, user {UserId}, budget {Budget}",
                sessionId, userId, maxTokenBudget);
        }
        else
        {
            _logger.LogWarning("Session {SessionId} already exists, resetting", sessionId);
            _sessions[sessionId] = tracker;
        }
    }

    /// <inheritdoc/>
    public void RecordTokenUsage(string sessionId, int tokens, string operation)
    {
        if (!_sessions.TryGetValue(sessionId, out var tracker))
        {
            _logger.LogWarning("Recording tokens for unknown session {SessionId}", sessionId);
            return;
        }

        var previousRatio = tracker.UsageRatio;
        tracker.RecordUsage(tokens, operation);
        Interlocked.Add(ref _totalTokensConsumed, tokens);

        var currentRatio = tracker.UsageRatio;

        // Check for warning threshold crossing
        if (!tracker.WarningFired && currentRatio >= tracker.WarningThreshold)
        {
            tracker.WarningFired = true;
            tracker.WarningCount++;

            var args = CreateEventArgs(tracker, TokenBudgetEventType.Warning);
            _logger.LogWarning(
                "Token budget warning for session {SessionId}: {Usage}/{Budget} ({Ratio:P0})",
                sessionId, tracker.TotalTokens, tracker.MaxBudget, currentRatio);

            OnBudgetWarning?.Invoke(this, args);
        }

        // Check for budget exceeded
        if (!tracker.ExceededFired && tracker.TotalTokens > tracker.MaxBudget)
        {
            tracker.ExceededFired = true;
            Interlocked.Increment(ref _exceededSessionCount);

            var args = CreateEventArgs(tracker, TokenBudgetEventType.Exceeded);
            _logger.LogError(
                "Token budget EXCEEDED for session {SessionId}: {Usage}/{Budget}",
                sessionId, tracker.TotalTokens, tracker.MaxBudget);

            OnBudgetExceeded?.Invoke(this, args);
        }
    }

    /// <inheritdoc/>
    public int EstimateTokens(string content)
    {
        // Simple estimation: ~4 characters per token for English
        // This is a rough approximation - production should use tiktoken or similar
        if (string.IsNullOrEmpty(content)) return 0;
        return (int)Math.Ceiling(content.Length / 4.0);
    }

    /// <inheritdoc/>
    public TokenBudgetStatus? GetSessionStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var tracker))
            return null;

        return new TokenBudgetStatus
        {
            SessionId = sessionId,
            UserId = tracker.UserId,
            TotalTokens = tracker.TotalTokens,
            MaxBudget = tracker.MaxBudget,
            IsWarning = tracker.WarningFired,
            IsExceeded = tracker.ExceededFired,
            StartedAt = tracker.StartedAt,
            OperationBreakdown = tracker.GetOperationBreakdown()
        };
    }

    /// <inheritdoc/>
    public bool CanAfford(string sessionId, int estimatedTokens)
    {
        if (!_sessions.TryGetValue(sessionId, out var tracker))
            return true; // Unknown session, allow operation

        return tracker.TotalTokens + estimatedTokens <= tracker.MaxBudget;
    }

    /// <inheritdoc/>
    public TokenBudgetRecommendation GetRecommendation(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var tracker))
        {
            return new TokenBudgetRecommendation
            {
                Type = TokenRecommendationType.Continue,
                Message = "No active budget tracking for this session.",
                SuggestedAction = "Continue normal operation.",
                Urgency = 0f
            };
        }

        var ratio = tracker.UsageRatio;

        if (ratio >= 1.0f)
        {
            return new TokenBudgetRecommendation
            {
                Type = TokenRecommendationType.Stop,
                Message = $"Token budget exceeded ({ratio:P0}). Minimize further memory operations.",
                SuggestedAction = "Stop recall operations. Use only cached/already-retrieved information.",
                Urgency = 1.0f
            };
        }

        if (ratio >= 0.9f)
        {
            return new TokenBudgetRecommendation
            {
                Type = TokenRecommendationType.Conserve,
                Message = $"Token budget nearly exhausted ({ratio:P0}). Critical conservation needed.",
                SuggestedAction = "Reduce recall limits, use compressed context, avoid new store operations.",
                Urgency = 0.9f
            };
        }

        if (ratio >= 0.8f)
        {
            return new TokenBudgetRecommendation
            {
                Type = TokenRecommendationType.Compress,
                Message = $"Token budget at warning level ({ratio:P0}). Consider compression.",
                SuggestedAction = "Enable context compression, reduce recall scope, summarize if needed.",
                Urgency = 0.7f
            };
        }

        if (ratio >= 0.6f)
        {
            return new TokenBudgetRecommendation
            {
                Type = TokenRecommendationType.ReduceScope,
                Message = $"Token budget at moderate level ({ratio:P0}). Monitor usage.",
                SuggestedAction = "Consider reducing recall limits or using more specific queries.",
                Urgency = 0.4f
            };
        }

        return new TokenBudgetRecommendation
        {
            Type = TokenRecommendationType.Continue,
            Message = $"Token budget healthy ({ratio:P0}).",
            SuggestedAction = "Continue normal operation.",
            Urgency = 0f
        };
    }

    /// <inheritdoc/>
    public SessionTokenSummary? EndSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var tracker))
            return null;

        var summary = new SessionTokenSummary
        {
            SessionId = sessionId,
            UserId = tracker.UserId,
            TotalTokens = tracker.TotalTokens,
            MaxBudget = tracker.MaxBudget,
            Duration = DateTimeOffset.UtcNow - tracker.StartedAt,
            OperationCount = tracker.OperationCount,
            OperationBreakdown = tracker.GetOperationBreakdown(),
            PeakUsageRatio = tracker.PeakUsageRatio,
            WasExceeded = tracker.ExceededFired,
            WarningCount = tracker.WarningCount
        };

        _logger.LogInformation(
            "Session {SessionId} ended: {Tokens}/{Budget} tokens ({Ratio:P0}), {Duration}",
            sessionId, summary.TotalTokens, summary.MaxBudget, summary.FinalUsageRatio, summary.Duration);

        OnSessionEnded?.Invoke(this, new SessionTokenSummaryEventArgs { Summary = summary });

        return summary;
    }

    /// <inheritdoc/>
    public TokenBudgetGlobalStats GetGlobalStats()
    {
        var activeSessions = _sessions.Values.ToList();
        var avgRatio = activeSessions.Count > 0
            ? activeSessions.Average(s => s.UsageRatio)
            : 0f;

        var operationCounts = activeSessions
            .SelectMany(s => s.GetOperationBreakdown())
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var topOperation = operationCounts.Count > 0
            ? operationCounts.OrderByDescending(kvp => kvp.Value).First().Key
            : null;

        return new TokenBudgetGlobalStats
        {
            ActiveSessions = activeSessions.Count,
            TotalSessions = _totalSessionsStarted,
            TotalTokens = Interlocked.Read(ref _totalTokensConsumed),
            AverageUsageRatio = avgRatio,
            ExceededCount = _exceededSessionCount,
            TopOperation = topOperation
        };
    }

    private TokenBudgetEventArgs CreateEventArgs(SessionTokenTracker tracker, TokenBudgetEventType eventType)
    {
        var recommendation = GetRecommendation(tracker.SessionId);
        return new TokenBudgetEventArgs
        {
            SessionId = tracker.SessionId,
            UserId = tracker.UserId,
            CurrentUsage = tracker.TotalTokens,
            MaxBudget = tracker.MaxBudget,
            UsageRatio = tracker.UsageRatio,
            EventType = eventType,
            Recommendation = recommendation.SuggestedAction
        };
    }
}

/// <summary>
/// Internal tracker for a single session's token usage.
/// </summary>
internal sealed class SessionTokenTracker
{
    public string SessionId { get; }
    public string UserId { get; }
    public int MaxBudget { get; }
    public float WarningThreshold { get; }
    public DateTimeOffset StartedAt { get; }

    public int TotalTokens { get; private set; }
    public int OperationCount { get; private set; }
    public float PeakUsageRatio { get; private set; }
    public bool WarningFired { get; set; }
    public bool ExceededFired { get; set; }
    public int WarningCount { get; set; }

    private readonly Dictionary<string, int> _operationBreakdown = new();
    private readonly object _lock = new();

    public float UsageRatio => MaxBudget > 0 ? (float)TotalTokens / MaxBudget : 0f;

    public SessionTokenTracker(string sessionId, string userId, int maxBudget, float warningThreshold)
    {
        SessionId = sessionId;
        UserId = userId;
        MaxBudget = maxBudget;
        WarningThreshold = warningThreshold;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void RecordUsage(int tokens, string operation)
    {
        lock (_lock)
        {
            TotalTokens += tokens;
            OperationCount++;

            if (!_operationBreakdown.TryGetValue(operation, out var count))
                count = 0;
            _operationBreakdown[operation] = count + tokens;

            var currentRatio = UsageRatio;
            if (currentRatio > PeakUsageRatio)
                PeakUsageRatio = currentRatio;
        }
    }

    public IReadOnlyDictionary<string, int> GetOperationBreakdown()
    {
        lock (_lock)
        {
            return new Dictionary<string, int>(_operationBreakdown);
        }
    }
}
