using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// Service for managing alerts and notifications.
/// </summary>
public interface IAlertingService
{
    /// <summary>
    /// Triggers an alert.
    /// </summary>
    /// <param name="alert">The alert to trigger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TriggerAlertAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an alert.
    /// </summary>
    /// <param name="alertId">The alert ID.</param>
    /// <param name="resolution">Resolution message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResolveAlertAsync(Guid alertId, string? resolution = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active alerts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active alerts.</returns>
    Task<IReadOnlyList<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets alert history.
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Alert history.</returns>
    Task<IReadOnlyList<AlertHistoryEntry>> GetAlertHistoryAsync(
        AlertHistoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an alert rule.
    /// </summary>
    /// <param name="rule">The alert rule.</param>
    /// <returns>Rule ID.</returns>
    Guid RegisterRule(AlertRule rule);

    /// <summary>
    /// Removes an alert rule.
    /// </summary>
    /// <param name="ruleId">Rule ID.</param>
    bool RemoveRule(Guid ruleId);

    /// <summary>
    /// Gets all registered rules.
    /// </summary>
    /// <returns>List of alert rules.</returns>
    IReadOnlyList<AlertRule> GetRules();

    /// <summary>
    /// Evaluates all rules against current state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EvaluateRulesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Alert rule definition.
/// </summary>
public sealed class AlertRule
{
    /// <summary>
    /// Rule ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Rule name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Rule description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the rule is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Metric to evaluate.
    /// </summary>
    public string MetricName { get; init; } = string.Empty;

    /// <summary>
    /// Comparison operator.
    /// </summary>
    public ComparisonOperator Operator { get; init; }

    /// <summary>
    /// Threshold value.
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Duration the condition must be true before alerting.
    /// </summary>
    public TimeSpan? ForDuration { get; init; }

    /// <summary>
    /// Alert severity when triggered.
    /// </summary>
    public AlertSeverity Severity { get; init; } = AlertSeverity.Warning;

    /// <summary>
    /// Labels to add to the alert.
    /// </summary>
    public Dictionary<string, string> Labels { get; init; } = [];

    /// <summary>
    /// Notification channels to use.
    /// </summary>
    public List<string> NotificationChannels { get; init; } = [];

    /// <summary>
    /// Cooldown period between alerts.
    /// </summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Comparison operators for alert rules.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>
    /// Greater than.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less than.
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Equal.
    /// </summary>
    Equal,

    /// <summary>
    /// Not equal.
    /// </summary>
    NotEqual
}

/// <summary>
/// Alert history entry.
/// </summary>
public sealed class AlertHistoryEntry
{
    /// <summary>
    /// Entry ID.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Original alert.
    /// </summary>
    public Alert Alert { get; init; } = null!;

    /// <summary>
    /// When the alert was triggered.
    /// </summary>
    public DateTimeOffset TriggeredAt { get; init; }

    /// <summary>
    /// When the alert was resolved (if resolved).
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; init; }

    /// <summary>
    /// Resolution message.
    /// </summary>
    public string? Resolution { get; init; }

    /// <summary>
    /// Duration of the alert.
    /// </summary>
    public TimeSpan? Duration => ResolvedAt.HasValue ? ResolvedAt.Value - TriggeredAt : null;

    /// <summary>
    /// Notifications sent.
    /// </summary>
    public List<NotificationRecord> Notifications { get; init; } = [];
}

/// <summary>
/// Record of a notification sent.
/// </summary>
public sealed class NotificationRecord
{
    /// <summary>
    /// Notification channel.
    /// </summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// When the notification was sent.
    /// </summary>
    public DateTimeOffset SentAt { get; init; }

    /// <summary>
    /// Whether the notification was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Query parameters for alert history.
/// </summary>
public sealed class AlertHistoryQuery
{
    /// <summary>
    /// Start time filter.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// End time filter.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Severity filter.
    /// </summary>
    public AlertSeverity[]? Severities { get; set; }

    /// <summary>
    /// Source filter.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Include only resolved alerts.
    /// </summary>
    public bool? Resolved { get; set; }

    /// <summary>
    /// Maximum results.
    /// </summary>
    public int Limit { get; set; } = 100;
}

/// <summary>
/// Notification channel interface.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Channel name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Sends a notification.
    /// </summary>
    /// <param name="alert">The alert to notify about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the notification was successful.</returns>
    Task<bool> SendAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>
/// Console notification channel (for development).
/// </summary>
public sealed class ConsoleNotificationChannel : INotificationChannel
{
    public string Name => "Console";

    public Task<bool> SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        var color = alert.Severity switch
        {
            AlertSeverity.Critical => ConsoleColor.Red,
            AlertSeverity.Error => ConsoleColor.DarkRed,
            AlertSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[ALERT] [{alert.Severity}] {alert.Title}: {alert.Message}");
        Console.ForegroundColor = originalColor;

        return Task.FromResult(true);
    }
}

/// <summary>
/// Log notification channel.
/// </summary>
public sealed class LogNotificationChannel : INotificationChannel
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    public LogNotificationChannel(Microsoft.Extensions.Logging.ILogger logger)
    {
        _logger = logger;
    }

    public string Name => "Log";

    public Task<bool> SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        switch (alert.Severity)
        {
            case AlertSeverity.Critical:
                _logger.LogCritical("[{Source}] {Title}: {Message}", alert.Source, alert.Title, alert.Message);
                break;
            case AlertSeverity.Error:
                _logger.LogError("[{Source}] {Title}: {Message}", alert.Source, alert.Title, alert.Message);
                break;
            case AlertSeverity.Warning:
                _logger.LogWarning("[{Source}] {Title}: {Message}", alert.Source, alert.Title, alert.Message);
                break;
            default:
                _logger.LogInformation("[{Source}] {Title}: {Message}", alert.Source, alert.Title, alert.Message);
                break;
        }

        return Task.FromResult(true);
    }
}

/// <summary>
/// Default alert rules for Memory Indexer.
/// </summary>
public static class DefaultAlertRules
{
    /// <summary>
    /// High latency alert rule.
    /// </summary>
    public static AlertRule HighLatency => new()
    {
        Name = "High Recall Latency",
        Description = "Recall operations are taking longer than expected",
        MetricName = "memory_indexer.recall_latency",
        Operator = ComparisonOperator.GreaterThan,
        Threshold = 200, // 200ms
        ForDuration = TimeSpan.FromMinutes(2),
        Severity = AlertSeverity.Warning
    };

    /// <summary>
    /// High error rate alert rule.
    /// </summary>
    public static AlertRule HighErrorRate => new()
    {
        Name = "High Error Rate",
        Description = "Error rate has exceeded threshold",
        MetricName = "memory_indexer.error_rate",
        Operator = ComparisonOperator.GreaterThan,
        Threshold = 0.05, // 5%
        ForDuration = TimeSpan.FromMinutes(5),
        Severity = AlertSeverity.Error
    };

    /// <summary>
    /// Storage capacity alert rule.
    /// </summary>
    public static AlertRule StorageCapacity => new()
    {
        Name = "Storage Capacity Warning",
        Description = "Storage is approaching capacity limit",
        MetricName = "memory_indexer.storage_utilization",
        Operator = ComparisonOperator.GreaterThan,
        Threshold = 0.85, // 85%
        Severity = AlertSeverity.Warning
    };

    /// <summary>
    /// Security threat alert rule.
    /// </summary>
    public static AlertRule SecurityThreat => new()
    {
        Name = "Security Threat Detected",
        Description = "Potential security threat detected",
        MetricName = "memory_indexer.injection_detections",
        Operator = ComparisonOperator.GreaterThan,
        Threshold = 5,
        ForDuration = TimeSpan.FromMinutes(1),
        Severity = AlertSeverity.Critical
    };

    /// <summary>
    /// Rate limit violations alert.
    /// </summary>
    public static AlertRule RateLimitViolations => new()
    {
        Name = "Rate Limit Violations",
        Description = "Multiple rate limit violations detected",
        MetricName = "memory_indexer.rate_limit_violations",
        Operator = ComparisonOperator.GreaterThan,
        Threshold = 10,
        ForDuration = TimeSpan.FromMinutes(1),
        Severity = AlertSeverity.Warning
    };

    /// <summary>
    /// Gets all default rules.
    /// </summary>
    public static IEnumerable<AlertRule> All => new[]
    {
        HighLatency,
        HighErrorRate,
        StorageCapacity,
        SecurityThreat,
        RateLimitViolations
    };
}
