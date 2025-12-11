namespace MemoryIndexer.Intelligence.Security.MultiTenant;

/// <summary>
/// Service for logging audit events for compliance and security monitoring.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs an audit event.
    /// </summary>
    /// <param name="event">The audit event to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(AuditEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an audit event with automatic context population.
    /// </summary>
    /// <param name="action">The action performed.</param>
    /// <param name="resourceType">Type of resource affected.</param>
    /// <param name="resourceId">ID of the resource.</param>
    /// <param name="outcome">Outcome of the action.</param>
    /// <param name="details">Additional details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(
        AuditAction action,
        string resourceType,
        string resourceId,
        AuditOutcome outcome,
        Dictionary<string, string>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries audit logs.
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching audit events.</returns>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit statistics for a time period.
    /// </summary>
    /// <param name="tenantId">Tenant ID (null for all tenants).</param>
    /// <param name="startTime">Start of the period.</param>
    /// <param name="endTime">End of the period.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audit statistics.</returns>
    Task<AuditStatistics> GetStatisticsAsync(
        string? tenantId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an audit event.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// Unique event ID.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Tenant ID.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// User ID who performed the action.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Action that was performed.
    /// </summary>
    public AuditAction Action { get; init; }

    /// <summary>
    /// Type of resource affected (e.g., "Memory", "User", "Tenant").
    /// </summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>
    /// ID of the affected resource.
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>
    /// Outcome of the action.
    /// </summary>
    public AuditOutcome Outcome { get; init; }

    /// <summary>
    /// Additional details about the event.
    /// </summary>
    public Dictionary<string, string> Details { get; init; } = [];

    /// <summary>
    /// IP address of the client (if available).
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User agent of the client (if available).
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Duration of the operation in milliseconds.
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Error message if the action failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Severity level of the event.
    /// </summary>
    public AuditSeverity Severity { get; init; } = AuditSeverity.Info;
}

/// <summary>
/// Audit actions.
/// </summary>
public enum AuditAction
{
    // Memory operations
    MemoryCreate,
    MemoryRead,
    MemoryUpdate,
    MemoryDelete,
    MemorySearch,
    MemoryExport,
    MemoryImport,
    MemoryMerge,

    // Authentication
    Login,
    Logout,
    LoginFailed,
    TokenRefresh,
    ApiKeyCreate,
    ApiKeyRevoke,

    // Authorization
    PermissionGranted,
    PermissionDenied,
    RoleAssigned,
    RoleRevoked,

    // Tenant management
    TenantCreate,
    TenantUpdate,
    TenantDisable,
    TenantDelete,

    // User management
    UserCreate,
    UserUpdate,
    UserDisable,
    UserDelete,

    // Security events
    PiiDetected,
    InjectionAttempt,
    RateLimitExceeded,
    SuspiciousActivity,

    // System events
    ConfigurationChange,
    ServiceStart,
    ServiceStop,
    MaintenanceStart,
    MaintenanceEnd,

    // Data events
    DataRetentionPurge,
    BackupCreate,
    BackupRestore
}

/// <summary>
/// Outcome of an audited action.
/// </summary>
public enum AuditOutcome
{
    /// <summary>
    /// Action completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Action failed.
    /// </summary>
    Failure,

    /// <summary>
    /// Action was denied (authorization failure).
    /// </summary>
    Denied,

    /// <summary>
    /// Action was blocked (security policy).
    /// </summary>
    Blocked,

    /// <summary>
    /// Action is pending (async operation).
    /// </summary>
    Pending,

    /// <summary>
    /// Action was partially successful.
    /// </summary>
    PartialSuccess
}

/// <summary>
/// Severity level of audit events.
/// </summary>
public enum AuditSeverity
{
    /// <summary>
    /// Informational event.
    /// </summary>
    Info,

    /// <summary>
    /// Warning event.
    /// </summary>
    Warning,

    /// <summary>
    /// Error event.
    /// </summary>
    Error,

    /// <summary>
    /// Critical security event.
    /// </summary>
    Critical
}

/// <summary>
/// Query parameters for audit logs.
/// </summary>
public sealed class AuditLogQuery
{
    /// <summary>
    /// Tenant ID filter (null for all tenants).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// User ID filter.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Action types to include.
    /// </summary>
    public AuditAction[]? Actions { get; set; }

    /// <summary>
    /// Resource type filter.
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource ID filter.
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Outcomes to include.
    /// </summary>
    public AuditOutcome[]? Outcomes { get; set; }

    /// <summary>
    /// Minimum severity level.
    /// </summary>
    public AuditSeverity? MinSeverity { get; set; }

    /// <summary>
    /// Start time filter.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// End time filter.
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Offset for pagination.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Correlation ID filter.
    /// </summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Audit statistics.
/// </summary>
public sealed class AuditStatistics
{
    /// <summary>
    /// Total number of events.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Events by action type.
    /// </summary>
    public Dictionary<AuditAction, long> EventsByAction { get; init; } = [];

    /// <summary>
    /// Events by outcome.
    /// </summary>
    public Dictionary<AuditOutcome, long> EventsByOutcome { get; init; } = [];

    /// <summary>
    /// Events by severity.
    /// </summary>
    public Dictionary<AuditSeverity, long> EventsBySeverity { get; init; } = [];

    /// <summary>
    /// Unique users who generated events.
    /// </summary>
    public int UniqueUsers { get; init; }

    /// <summary>
    /// Failed authentication attempts.
    /// </summary>
    public long FailedAuthAttempts { get; init; }

    /// <summary>
    /// Authorization denials.
    /// </summary>
    public long AuthorizationDenials { get; init; }

    /// <summary>
    /// Security events (PII detected, injection attempts, etc.).
    /// </summary>
    public long SecurityEvents { get; init; }

    /// <summary>
    /// Rate limit violations.
    /// </summary>
    public long RateLimitViolations { get; init; }
}
