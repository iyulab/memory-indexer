using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Enforces resource limits for memory operations.
/// Prevents exceeding configured quotas for memory count and storage size.
/// </summary>
/// <remarks>
/// Phase v0.6.0-γ: Resource Management
/// Integrates with TenantConfiguration for tenant-specific limits.
/// </remarks>
public interface IResourceLimitEnforcer
{
    /// <summary>
    /// Checks if a store operation is allowed within resource limits.
    /// </summary>
    /// <param name="userId">The user attempting the operation.</param>
    /// <param name="estimatedSize">Estimated size in bytes of the new memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enforcement result indicating if operation is allowed.</returns>
    Task<EnforcementResult> CanStoreAsync(
        string userId,
        long estimatedSize = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a batch store operation is allowed within resource limits.
    /// </summary>
    /// <param name="userId">The user attempting the operation.</param>
    /// <param name="count">Number of memories to store.</param>
    /// <param name="estimatedTotalSize">Estimated total size in bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enforcement result indicating if operation is allowed.</returns>
    Task<EnforcementResult> CanStoreBatchAsync(
        string userId,
        int count,
        long estimatedTotalSize = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current resource usage for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current usage statistics.</returns>
    Task<ResourceUsage> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the applicable limits for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Resource limits configuration.</returns>
    ResourceLimits GetLimits(string userId);
}

/// <summary>
/// Result of an enforcement check.
/// </summary>
public sealed class EnforcementResult
{
    /// <summary>
    /// Whether the operation is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Reason for denial (if not allowed).
    /// </summary>
    public string? DenialReason { get; init; }

    /// <summary>
    /// Which limit would be exceeded.
    /// </summary>
    public LimitType? ExceededLimit { get; init; }

    /// <summary>
    /// Current usage at time of check.
    /// </summary>
    public ResourceUsage? CurrentUsage { get; init; }

    /// <summary>
    /// Applicable limits.
    /// </summary>
    public ResourceLimits? Limits { get; init; }

    /// <summary>
    /// Creates an allowed result.
    /// </summary>
    public static EnforcementResult Allowed(ResourceUsage? usage = null, ResourceLimits? limits = null) =>
        new() { IsAllowed = true, CurrentUsage = usage, Limits = limits };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static EnforcementResult Denied(LimitType limitType, string reason, ResourceUsage? usage = null, ResourceLimits? limits = null) =>
        new()
        {
            IsAllowed = false,
            ExceededLimit = limitType,
            DenialReason = reason,
            CurrentUsage = usage,
            Limits = limits
        };
}

/// <summary>
/// Type of resource limit.
/// </summary>
public enum LimitType
{
    /// <summary>
    /// Maximum number of memories.
    /// </summary>
    MemoryCount,

    /// <summary>
    /// Maximum storage size in bytes.
    /// </summary>
    StorageSize,

    /// <summary>
    /// Rate limit exceeded.
    /// </summary>
    RateLimit
}

/// <summary>
/// Current resource usage statistics.
/// </summary>
public sealed class ResourceUsage
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Tenant ID (if multi-tenant).
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Current number of memories.
    /// </summary>
    public long MemoryCount { get; init; }

    /// <summary>
    /// Current storage size in bytes.
    /// </summary>
    public long StorageSizeBytes { get; init; }

    /// <summary>
    /// Memory count breakdown by tier.
    /// </summary>
    public IReadOnlyDictionary<Tier, long>? ByTier { get; init; }

    /// <summary>
    /// Memory count breakdown by type.
    /// </summary>
    public IReadOnlyDictionary<MemoryType, long>? ByType { get; init; }

    /// <summary>
    /// When usage was last calculated.
    /// </summary>
    public DateTime CalculatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Percentage of memory count limit used.
    /// </summary>
    public double MemoryCountPercentage(ResourceLimits limits) =>
        limits.MaxMemories > 0 ? (double)MemoryCount / limits.MaxMemories * 100 : 0;

    /// <summary>
    /// Percentage of storage limit used.
    /// </summary>
    public double StoragePercentage(ResourceLimits limits) =>
        limits.MaxStorageBytes > 0 ? (double)StorageSizeBytes / limits.MaxStorageBytes * 100 : 0;
}

/// <summary>
/// Resource limits configuration.
/// </summary>
public sealed class ResourceLimits
{
    /// <summary>
    /// Default limits for users without specific configuration.
    /// </summary>
    public static readonly ResourceLimits Default = new()
    {
        MaxMemories = 100_000,
        MaxStorageBytes = 1_073_741_824, // 1 GB
        EnforcementEnabled = true
    };

    /// <summary>
    /// Unlimited resources (for system operations).
    /// </summary>
    public static readonly ResourceLimits Unlimited = new()
    {
        MaxMemories = long.MaxValue,
        MaxStorageBytes = long.MaxValue,
        EnforcementEnabled = false
    };

    /// <summary>
    /// Maximum number of memories allowed.
    /// </summary>
    public long MaxMemories { get; init; } = 100_000;

    /// <summary>
    /// Maximum storage size in bytes.
    /// </summary>
    public long MaxStorageBytes { get; init; } = 1_073_741_824;

    /// <summary>
    /// Whether enforcement is enabled.
    /// </summary>
    public bool EnforcementEnabled { get; init; } = true;

    /// <summary>
    /// Warning threshold percentage (0-100).
    /// </summary>
    public int WarningThresholdPercent { get; init; } = 80;

    /// <summary>
    /// Source of these limits (Default, Tenant, User).
    /// </summary>
    public string Source { get; init; } = "Default";
}
