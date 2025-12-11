using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Security.MultiTenant;

/// <summary>
/// In-memory implementation of audit logging.
/// Suitable for development and testing. For production, use a persistent store.
/// </summary>
public sealed class InMemoryAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<InMemoryAuditLogger> _logger;
    private readonly int _maxEvents;

    public InMemoryAuditLogger(
        ITenantContext tenantContext,
        ILogger<InMemoryAuditLogger> logger,
        int maxEvents = 10_000)
    {
        _tenantContext = tenantContext;
        _logger = logger;
        _maxEvents = maxEvents;
    }

    public Task LogAsync(AuditEvent @event, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(@event);

        // Trim if exceeding max
        while (_events.Count > _maxEvents && _events.TryDequeue(out _))
        {
        }

        // Log based on severity
        var logMessage = $"[AUDIT] {{{@event.Action}}} {{{@event.ResourceType}}}/{{{@event.ResourceId}}} - {{{@event.Outcome}}}";

        switch (@event.Severity)
        {
            case AuditSeverity.Critical:
                _logger.LogCritical(logMessage);
                break;
            case AuditSeverity.Error:
                _logger.LogError(logMessage);
                break;
            case AuditSeverity.Warning:
                _logger.LogWarning(logMessage);
                break;
            default:
                _logger.LogInformation(logMessage);
                break;
        }

        return Task.CompletedTask;
    }

    public Task LogAsync(
        AuditAction action,
        string resourceType,
        string resourceId,
        AuditOutcome outcome,
        Dictionary<string, string>? details = null,
        CancellationToken cancellationToken = default)
    {
        var severity = DetermineSeverity(action, outcome);

        var @event = new AuditEvent
        {
            TenantId = _tenantContext.TenantId,
            UserId = _tenantContext.UserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = outcome,
            Details = details ?? [],
            CorrelationId = (_tenantContext as AsyncLocalTenantContextAccessor)?.Context?.CorrelationId,
            Severity = severity
        };

        return LogAsync(@event, cancellationToken);
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var events = _events.AsEnumerable();

        // Apply filters
        if (query.TenantId != null)
        {
            events = events.Where(e => e.TenantId == query.TenantId);
        }

        if (query.UserId != null)
        {
            events = events.Where(e => e.UserId == query.UserId);
        }

        if (query.Actions != null && query.Actions.Length > 0)
        {
            var actions = query.Actions.ToHashSet();
            events = events.Where(e => actions.Contains(e.Action));
        }

        if (query.ResourceType != null)
        {
            events = events.Where(e => e.ResourceType == query.ResourceType);
        }

        if (query.ResourceId != null)
        {
            events = events.Where(e => e.ResourceId == query.ResourceId);
        }

        if (query.Outcomes != null && query.Outcomes.Length > 0)
        {
            var outcomes = query.Outcomes.ToHashSet();
            events = events.Where(e => outcomes.Contains(e.Outcome));
        }

        if (query.MinSeverity.HasValue)
        {
            events = events.Where(e => e.Severity >= query.MinSeverity.Value);
        }

        if (query.StartTime.HasValue)
        {
            events = events.Where(e => e.Timestamp >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            events = events.Where(e => e.Timestamp <= query.EndTime.Value);
        }

        if (query.CorrelationId != null)
        {
            events = events.Where(e => e.CorrelationId == query.CorrelationId);
        }

        var result = events
            .OrderByDescending(e => e.Timestamp)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEvent>>(result);
    }

    public Task<AuditStatistics> GetStatisticsAsync(
        string? tenantId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var events = _events.AsEnumerable()
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime);

        if (tenantId != null)
        {
            events = events.Where(e => e.TenantId == tenantId);
        }

        var eventList = events.ToList();

        var stats = new AuditStatistics
        {
            TotalEvents = eventList.Count,
            EventsByAction = eventList
                .GroupBy(e => e.Action)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            EventsByOutcome = eventList
                .GroupBy(e => e.Outcome)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            EventsBySeverity = eventList
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            UniqueUsers = eventList
                .Where(e => e.UserId != null)
                .Select(e => e.UserId)
                .Distinct()
                .Count(),
            FailedAuthAttempts = eventList
                .Count(e => e.Action == AuditAction.LoginFailed),
            AuthorizationDenials = eventList
                .Count(e => e.Outcome == AuditOutcome.Denied),
            SecurityEvents = eventList
                .Count(e => e.Action is AuditAction.PiiDetected
                    or AuditAction.InjectionAttempt
                    or AuditAction.SuspiciousActivity),
            RateLimitViolations = eventList
                .Count(e => e.Action == AuditAction.RateLimitExceeded)
        };

        return Task.FromResult(stats);
    }

    private static AuditSeverity DetermineSeverity(AuditAction action, AuditOutcome outcome)
    {
        // Security events are always high severity
        if (action is AuditAction.InjectionAttempt or AuditAction.SuspiciousActivity)
        {
            return AuditSeverity.Critical;
        }

        if (action is AuditAction.PiiDetected or AuditAction.RateLimitExceeded)
        {
            return AuditSeverity.Warning;
        }

        // Failed auth attempts are warnings
        if (action == AuditAction.LoginFailed)
        {
            return AuditSeverity.Warning;
        }

        // Authorization denials
        if (outcome == AuditOutcome.Denied)
        {
            return AuditSeverity.Warning;
        }

        // Failures
        if (outcome == AuditOutcome.Failure)
        {
            return AuditSeverity.Error;
        }

        // Blocked actions
        if (outcome == AuditOutcome.Blocked)
        {
            return AuditSeverity.Warning;
        }

        return AuditSeverity.Info;
    }
}
