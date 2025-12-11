namespace MemoryIndexer.Intelligence.Security.MultiTenant;

/// <summary>
/// Provides the current tenant context for scoped operations.
/// Implements ambient tenant isolation for memory operations.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant ID.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Gets the current user ID within the tenant.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets whether a tenant context is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the tenant-specific configuration.
    /// </summary>
    TenantConfiguration? Configuration { get; }
}

/// <summary>
/// Allows setting the tenant context (typically by middleware).
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Gets or sets the current tenant context.
    /// </summary>
    TenantContextData? Context { get; set; }
}

/// <summary>
/// Tenant context data.
/// </summary>
public sealed class TenantContextData
{
    /// <summary>
    /// Tenant ID.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// User ID within the tenant.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// User's roles within the tenant.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// User's permissions (resolved from roles).
    /// </summary>
    public IReadOnlySet<Permission> Permissions { get; init; } = new HashSet<Permission>();

    /// <summary>
    /// Tenant-specific configuration.
    /// </summary>
    public TenantConfiguration? Configuration { get; init; }

    /// <summary>
    /// When the context was established.
    /// </summary>
    public DateTimeOffset EstablishedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Tenant-specific configuration.
/// </summary>
public sealed class TenantConfiguration
{
    /// <summary>
    /// Maximum memories allowed for this tenant.
    /// </summary>
    public int MaxMemories { get; set; } = 100_000;

    /// <summary>
    /// Maximum storage size in bytes.
    /// </summary>
    public long MaxStorageBytes { get; set; } = 1_073_741_824; // 1 GB

    /// <summary>
    /// Rate limit overrides for this tenant.
    /// </summary>
    public RateLimitOptions? RateLimitOverrides { get; set; }

    /// <summary>
    /// Whether PII detection is required for this tenant.
    /// </summary>
    public bool RequirePiiDetection { get; set; } = true;

    /// <summary>
    /// Allowed memory types for this tenant.
    /// </summary>
    public HashSet<string>? AllowedMemoryTypes { get; set; }

    /// <summary>
    /// Custom metadata fields allowed for this tenant.
    /// </summary>
    public HashSet<string>? AllowedMetadataFields { get; set; }

    /// <summary>
    /// Encryption key ID for this tenant (if using per-tenant encryption).
    /// </summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>
    /// Data retention period in days (0 = indefinite).
    /// </summary>
    public int DataRetentionDays { get; set; }

    /// <summary>
    /// Whether audit logging is enabled for this tenant.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;
}

/// <summary>
/// Default implementation of tenant context accessor using AsyncLocal.
/// </summary>
public sealed class AsyncLocalTenantContextAccessor : ITenantContextAccessor, ITenantContext
{
    private static readonly AsyncLocal<TenantContextData?> _context = new();

    public TenantContextData? Context
    {
        get => _context.Value;
        set => _context.Value = value;
    }

    public string? TenantId => Context?.TenantId;
    public string? UserId => Context?.UserId;
    public bool IsActive => Context != null;
    public TenantConfiguration? Configuration => Context?.Configuration;
}

/// <summary>
/// Service for resolving tenant information from various sources.
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// Resolves tenant information from a tenant identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant information if found.</returns>
    Task<TenantInfo?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves tenant information from an API key.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant information if the API key is valid.</returns>
    Task<TenantInfo?> ResolveFromApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tenant information.
/// </summary>
public sealed class TenantInfo
{
    /// <summary>
    /// Tenant ID.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Tenant display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the tenant is active.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Tenant configuration.
    /// </summary>
    public TenantConfiguration Configuration { get; init; } = new();

    /// <summary>
    /// When the tenant was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
