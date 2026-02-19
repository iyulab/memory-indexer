using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Caching;

/// <summary>
/// Analyzes recall patterns to detect inefficient usage and provide recommendations.
/// Phase v0.5.0: Recall Pattern Telemetry
/// </summary>
public sealed partial class RecallPatternAnalyzer : IRecallPatternAnalyzer
{
    private readonly ILogger<RecallPatternAnalyzer> _logger;
    private readonly RecallPatternOptions _options;

    // Per-user tracking
    private readonly ConcurrentDictionary<string, UserRecallPattern> _userPatterns = new();

    // Global tracking for cross-user analysis
    private long _totalRecalls;
    private long _duplicateRecalls;
    private long _rapidFireCount;

    public RecallPatternAnalyzer(
        ILogger<RecallPatternAnalyzer> logger,
        RecallPatternOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new RecallPatternOptions();
    }

    /// <inheritdoc/>
    public void RecordRecall(string userId, string query, string tier, int limit)
    {
        Interlocked.Increment(ref _totalRecalls);

        var pattern = _userPatterns.GetOrAdd(userId, _ => new UserRecallPattern(userId, _options));
        var analysis = pattern.RecordRecall(query, tier, limit);

        if (analysis.IsDuplicate)
        {
            Interlocked.Increment(ref _duplicateRecalls);
            var queryPreview = TruncateQuery(query);
            LogDuplicateRecall(_logger, userId, queryPreview, analysis.DuplicateCount);
        }

        if (analysis.IsRapidFire)
        {
            Interlocked.Increment(ref _rapidFireCount);
            LogRapidFireRecall(_logger, userId, analysis.RecallsInWindow, _options.RapidFireWindowMs);
        }
    }

    /// <inheritdoc/>
    public RecallPatternStatistics GetStatistics(string? userId = null)
    {
        if (userId != null)
        {
            return _userPatterns.TryGetValue(userId, out var pattern)
                ? pattern.GetStatistics()
                : new RecallPatternStatistics { UserId = userId };
        }

        // Global statistics
        return new RecallPatternStatistics
        {
            UserId = null,
            TotalRecalls = Interlocked.Read(ref _totalRecalls),
            DuplicateRecalls = Interlocked.Read(ref _duplicateRecalls),
            RapidFireCount = Interlocked.Read(ref _rapidFireCount),
            DuplicateRatio = _totalRecalls > 0
                ? (float)_duplicateRecalls / _totalRecalls
                : 0f,
            UniqueUsers = _userPatterns.Count,
            AverageRecallsPerUser = !_userPatterns.IsEmpty
                ? (float)_totalRecalls / _userPatterns.Count
                : 0f
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<RecallPatternAlert> GetAlerts(string? userId = null)
    {
        var alerts = new List<RecallPatternAlert>();

        if (userId != null)
        {
            if (_userPatterns.TryGetValue(userId, out var pattern))
            {
                alerts.AddRange(pattern.GetAlerts());
            }
        }
        else
        {
            foreach (var kvp in _userPatterns)
            {
                alerts.AddRange(kvp.Value.GetAlerts());
            }
        }

        return alerts.OrderByDescending(a => a.Severity).ThenByDescending(a => a.Timestamp).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<RecallOptimizationRecommendation> GetRecommendations(string userId)
    {
        if (!_userPatterns.TryGetValue(userId, out var pattern))
        {
            return [];
        }

        return pattern.GetRecommendations();
    }

    /// <inheritdoc/>
    public void Reset(string? userId = null)
    {
        if (userId != null)
        {
            _userPatterns.TryRemove(userId, out _);
        }
        else
        {
            _userPatterns.Clear();
            Interlocked.Exchange(ref _totalRecalls, 0);
            Interlocked.Exchange(ref _duplicateRecalls, 0);
            Interlocked.Exchange(ref _rapidFireCount, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Duplicate recall detected for user {UserId}: {Query} (count: {Count})")]
    private static partial void LogDuplicateRecall(ILogger logger, string userId, string query, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rapid-fire recall pattern detected for user {UserId}: {Count} recalls in {WindowMs}ms")]
    private static partial void LogRapidFireRecall(ILogger logger, string userId, int count, int windowMs);

    private static string TruncateQuery(string query)
        => query.Length > 50 ? query[..50] + "..." : query;
}

/// <summary>
/// Per-user recall pattern tracking.
/// </summary>
internal sealed class UserRecallPattern
{
    private readonly string _userId;
    private readonly RecallPatternOptions _options;
    private readonly ConcurrentDictionary<string, QueryPattern> _queryPatterns = new();
    private readonly ConcurrentQueue<DateTimeOffset> _recentRecalls = new();
    private readonly object _lock = new();

    private long _totalRecalls;
    private long _duplicateRecalls;
    private long _rapidFireCount;

    public UserRecallPattern(string userId, RecallPatternOptions options)
    {
        _userId = userId;
        _options = options;
    }

    public RecallAnalysis RecordRecall(string query, string tier, int limit)
    {
        var now = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref _totalRecalls);

        // Track query pattern
        var queryKey = GetQueryKey(query, tier, limit);
        var queryPattern = _queryPatterns.GetOrAdd(queryKey, _ => new QueryPattern(query, tier, limit));
        var duplicateCount = queryPattern.RecordAccess(now);

        var isDuplicate = duplicateCount > 1;
        if (isDuplicate)
        {
            Interlocked.Increment(ref _duplicateRecalls);
        }

        // Track rapid-fire pattern
        var isRapidFire = CheckRapidFire(now);

        return new RecallAnalysis
        {
            IsDuplicate = isDuplicate,
            DuplicateCount = duplicateCount,
            IsRapidFire = isRapidFire,
            RecallsInWindow = GetRecallsInWindow(now)
        };
    }

    public RecallPatternStatistics GetStatistics()
    {
        var uniqueQueries = _queryPatterns.Count;
        var totalRecalls = Interlocked.Read(ref _totalRecalls);
        var duplicateRecalls = Interlocked.Read(ref _duplicateRecalls);

        var topDuplicates = _queryPatterns.Values
            .Where(p => p.AccessCount > 1)
            .OrderByDescending(p => p.AccessCount)
            .Take(10)
            .Select(p => new DuplicateQueryInfo
            {
                Query = p.Query,
                Tier = p.Tier,
                Count = p.AccessCount,
                FirstSeen = p.FirstAccess,
                LastSeen = p.LastAccess
            })
            .ToList();

        return new RecallPatternStatistics
        {
            UserId = _userId,
            TotalRecalls = totalRecalls,
            DuplicateRecalls = duplicateRecalls,
            RapidFireCount = Interlocked.Read(ref _rapidFireCount),
            UniqueQueries = uniqueQueries,
            DuplicateRatio = totalRecalls > 0 ? (float)duplicateRecalls / totalRecalls : 0f,
            TopDuplicateQueries = topDuplicates
        };
    }

    public IReadOnlyList<RecallPatternAlert> GetAlerts()
    {
        var alerts = new List<RecallPatternAlert>();
        var stats = GetStatistics();

        // High duplicate ratio alert
        if (stats.DuplicateRatio > _options.HighDuplicateThreshold)
        {
            alerts.Add(new RecallPatternAlert
            {
                UserId = _userId,
                AlertType = RecallPatternAlertType.HighDuplicateRatio,
                Severity = AlertSeverity.Warning,
                Message = $"High duplicate recall ratio: {stats.DuplicateRatio:P1}",
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["duplicate_ratio"] = stats.DuplicateRatio,
                    ["threshold"] = _options.HighDuplicateThreshold
                }
            });
        }

        // Excessive queries alert
        var excessiveQueries = _queryPatterns.Values
            .Where(p => p.AccessCount > _options.ExcessiveDuplicateThreshold)
            .ToList();

        foreach (var query in excessiveQueries)
        {
            alerts.Add(new RecallPatternAlert
            {
                UserId = _userId,
                AlertType = RecallPatternAlertType.ExcessiveDuplicates,
                Severity = AlertSeverity.Warning,
                Message = $"Query repeated {query.AccessCount} times: {TruncateQuery(query.Query)}",
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["query"] = query.Query,
                    ["count"] = query.AccessCount
                }
            });
        }

        return alerts;
    }

    public IReadOnlyList<RecallOptimizationRecommendation> GetRecommendations()
    {
        var recommendations = new List<RecallOptimizationRecommendation>();
        var stats = GetStatistics();

        // Recommend caching if high duplicates
        if (stats.DuplicateRatio > 0.3f)
        {
            recommendations.Add(new RecallOptimizationRecommendation
            {
                RecommendationType = RecommendationType.EnableCaching,
                Priority = RecommendationPriority.High,
                Description = "Enable session-level recall caching to eliminate duplicate queries",
                ExpectedImpact = $"Could reduce recall operations by {stats.DuplicateRatio:P0}",
                ActionItems =
                [
                    "Set LatencyOptions.QueryCacheEnabled = true",
                    "Configure appropriate TTL via LatencyOptions.QueryCacheTtlMinutes"
                ]
            });
        }

        // Recommend query consolidation if many similar queries
        if (stats.UniqueQueries > 50 && stats.TopDuplicateQueries?.Count > 5)
        {
            recommendations.Add(new RecallOptimizationRecommendation
            {
                RecommendationType = RecommendationType.ConsolidateQueries,
                Priority = RecommendationPriority.Medium,
                Description = "Consider consolidating similar queries to reduce total operations",
                ExpectedImpact = "Reduce unique query count and improve cache effectiveness",
                ActionItems =
                [
                    "Review top duplicate queries for consolidation opportunities",
                    "Use broader queries with client-side filtering if appropriate"
                ]
            });
        }

        // Recommend batching if rapid-fire detected
        if (Interlocked.Read(ref _rapidFireCount) > 5)
        {
            recommendations.Add(new RecallOptimizationRecommendation
            {
                RecommendationType = RecommendationType.UseBatchRecall,
                Priority = RecommendationPriority.Medium,
                Description = "Use batch recall instead of multiple sequential recalls",
                ExpectedImpact = "Reduce latency through parallel processing",
                ActionItems =
                [
                    "Use OptimizedRecallService.BatchRecallAsync for multiple queries",
                    "Configure LatencyOptions.BatchProcessingEnabled = true"
                ]
            });
        }

        return recommendations;
    }

    private bool CheckRapidFire(DateTimeOffset now)
    {
        lock (_lock)
        {
            _recentRecalls.Enqueue(now);

            // Remove old entries
            var cutoff = now.AddMilliseconds(-_options.RapidFireWindowMs);
            while (_recentRecalls.TryPeek(out var oldest) && oldest < cutoff)
            {
                _recentRecalls.TryDequeue(out _);
            }

            if (_recentRecalls.Count >= _options.RapidFireThreshold)
            {
                Interlocked.Increment(ref _rapidFireCount);
                return true;
            }
        }

        return false;
    }

    private int GetRecallsInWindow(DateTimeOffset now)
    {
        lock (_lock)
        {
            var cutoff = now.AddMilliseconds(-_options.RapidFireWindowMs);
            return _recentRecalls.Count(r => r >= cutoff);
        }
    }

    private static string GetQueryKey(string query, string tier, int limit)
        => $"{tier}:{limit}:{query}";

    private static string TruncateQuery(string query)
        => query.Length > 50 ? query[..50] + "..." : query;
}

/// <summary>
/// Tracks individual query pattern.
/// </summary>
internal sealed class QueryPattern
{
    public string Query { get; }
    public string Tier { get; }
    public int Limit { get; }
    public DateTimeOffset FirstAccess { get; }
    public DateTimeOffset LastAccess { get; private set; }
    public int AccessCount { get; private set; }

    private readonly object _lock = new();

    public QueryPattern(string query, string tier, int limit)
    {
        Query = query;
        Tier = tier;
        Limit = limit;
        FirstAccess = DateTimeOffset.UtcNow;
        LastAccess = FirstAccess;
        AccessCount = 0;
    }

    public int RecordAccess(DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            AccessCount++;
            LastAccess = timestamp;
            return AccessCount;
        }
    }
}

/// <summary>
/// Result of analyzing a single recall operation.
/// </summary>
internal readonly struct RecallAnalysis
{
    public bool IsDuplicate { get; init; }
    public int DuplicateCount { get; init; }
    public bool IsRapidFire { get; init; }
    public int RecallsInWindow { get; init; }
}

/// <summary>
/// Configuration options for recall pattern analysis.
/// </summary>
public sealed class RecallPatternOptions
{
    /// <summary>
    /// Threshold ratio for high duplicate alert (0.0 to 1.0). Default: 0.3 (30%)
    /// </summary>
    public float HighDuplicateThreshold { get; set; } = 0.3f;

    /// <summary>
    /// Count threshold for excessive duplicate alert. Default: 5
    /// </summary>
    public int ExcessiveDuplicateThreshold { get; set; } = 5;

    /// <summary>
    /// Time window in milliseconds for rapid-fire detection. Default: 1000 (1 second)
    /// </summary>
    public int RapidFireWindowMs { get; set; } = 1000;

    /// <summary>
    /// Number of recalls in window to trigger rapid-fire alert. Default: 5
    /// </summary>
    public int RapidFireThreshold { get; set; } = 5;
}

/// <summary>
/// Statistics about recall patterns.
/// </summary>
public sealed class RecallPatternStatistics
{
    /// <summary>User ID (null for global stats).</summary>
    public string? UserId { get; init; }

    /// <summary>Total recall operations.</summary>
    public long TotalRecalls { get; init; }

    /// <summary>Duplicate recall operations.</summary>
    public long DuplicateRecalls { get; init; }

    /// <summary>Rapid-fire pattern occurrences.</summary>
    public long RapidFireCount { get; init; }

    /// <summary>Number of unique queries.</summary>
    public int UniqueQueries { get; init; }

    /// <summary>Ratio of duplicates to total (0.0 to 1.0).</summary>
    public float DuplicateRatio { get; init; }

    /// <summary>Number of unique users (global only).</summary>
    public int UniqueUsers { get; init; }

    /// <summary>Average recalls per user (global only).</summary>
    public float AverageRecallsPerUser { get; init; }

    /// <summary>Top duplicate queries.</summary>
    public IReadOnlyList<DuplicateQueryInfo>? TopDuplicateQueries { get; init; }
}

/// <summary>
/// Information about a duplicate query.
/// </summary>
public sealed class DuplicateQueryInfo
{
    /// <summary>The query text.</summary>
    public required string Query { get; init; }

    /// <summary>Memory tier.</summary>
    public required string Tier { get; init; }

    /// <summary>Number of times repeated.</summary>
    public int Count { get; init; }

    /// <summary>First occurrence.</summary>
    public DateTimeOffset FirstSeen { get; init; }

    /// <summary>Last occurrence.</summary>
    public DateTimeOffset LastSeen { get; init; }
}

/// <summary>
/// Alert about problematic recall patterns.
/// </summary>
public sealed class RecallPatternAlert
{
    /// <summary>User ID.</summary>
    public required string UserId { get; init; }

    /// <summary>Type of alert.</summary>
    public RecallPatternAlertType AlertType { get; init; }

    /// <summary>Alert severity.</summary>
    public AlertSeverity Severity { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>When the alert was generated.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Additional metadata.</summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Types of recall pattern alerts.
/// </summary>
public enum RecallPatternAlertType
{
    /// <summary>High ratio of duplicate queries.</summary>
    HighDuplicateRatio,

    /// <summary>Individual query repeated excessively.</summary>
    ExcessiveDuplicates,

    /// <summary>Too many recalls in short time window.</summary>
    RapidFireRecalls
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Warning - should investigate.</summary>
    Warning,

    /// <summary>Critical - immediate action needed.</summary>
    Critical
}

/// <summary>
/// Recommendation for optimizing recall patterns.
/// </summary>
public sealed class RecallOptimizationRecommendation
{
    /// <summary>Type of recommendation.</summary>
    public RecommendationType RecommendationType { get; init; }

    /// <summary>Priority level.</summary>
    public RecommendationPriority Priority { get; init; }

    /// <summary>Description of the recommendation.</summary>
    public required string Description { get; init; }

    /// <summary>Expected impact if implemented.</summary>
    public required string ExpectedImpact { get; init; }

    /// <summary>Specific action items.</summary>
    public required IReadOnlyList<string> ActionItems { get; init; }
}

/// <summary>
/// Types of optimization recommendations.
/// </summary>
public enum RecommendationType
{
    /// <summary>Enable or configure caching.</summary>
    EnableCaching,

    /// <summary>Consolidate similar queries.</summary>
    ConsolidateQueries,

    /// <summary>Use batch recall operations.</summary>
    UseBatchRecall,

    /// <summary>Reduce recall frequency.</summary>
    ReduceFrequency
}

/// <summary>
/// Priority levels for recommendations.
/// </summary>
public enum RecommendationPriority
{
    /// <summary>Low priority - nice to have.</summary>
    Low,

    /// <summary>Medium priority - should consider.</summary>
    Medium,

    /// <summary>High priority - should implement soon.</summary>
    High
}
