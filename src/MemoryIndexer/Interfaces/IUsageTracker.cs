using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Tracks resource usage for users and tenants.
/// Provides real-time and historical usage statistics.
/// </summary>
/// <remarks>
/// Phase v0.6.0-γ: Resource Management
/// Supports both per-user and tenant-level tracking.
/// </remarks>
public interface IUsageTracker
{
    /// <summary>
    /// Records a memory store operation.
    /// </summary>
    /// <param name="userId">User performing the operation.</param>
    /// <param name="sizeBytes">Size of stored content in bytes.</param>
    /// <param name="tier">Memory tier.</param>
    /// <param name="type">Memory type.</param>
    /// <param name="tenantId">Optional tenant ID.</param>
    void RecordStore(string userId, long sizeBytes, Tier tier, MemoryType type, string? tenantId = null);

    /// <summary>
    /// Records a memory delete operation.
    /// </summary>
    /// <param name="userId">User performing the operation.</param>
    /// <param name="sizeBytes">Size of deleted content in bytes.</param>
    /// <param name="tier">Memory tier.</param>
    /// <param name="type">Memory type.</param>
    /// <param name="tenantId">Optional tenant ID.</param>
    void RecordDelete(string userId, long sizeBytes, Tier tier, MemoryType type, string? tenantId = null);

    /// <summary>
    /// Records a tier promotion (counts decrease in source, increase in target).
    /// </summary>
    /// <param name="userId">User whose memory was promoted.</param>
    /// <param name="fromTier">Source tier.</param>
    /// <param name="toTier">Destination tier.</param>
    /// <param name="tenantId">Optional tenant ID.</param>
    void RecordTierPromotion(string userId, Tier fromTier, Tier toTier, string? tenantId = null);

    /// <summary>
    /// Gets current usage for a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="tenantId">Optional tenant ID.</param>
    /// <returns>Current usage statistics.</returns>
    ResourceUsage GetUsage(string userId, string? tenantId = null);

    /// <summary>
    /// Gets aggregated usage for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <returns>Aggregated tenant usage statistics.</returns>
    TenantUsage GetTenantUsage(string tenantId);

    /// <summary>
    /// Gets all tracked users.
    /// </summary>
    /// <returns>List of tracked user IDs.</returns>
    IReadOnlyList<string> GetTrackedUsers();

    /// <summary>
    /// Gets usage summary across all users.
    /// </summary>
    /// <returns>Global usage summary.</returns>
    GlobalUsageSummary GetGlobalSummary();

    /// <summary>
    /// Refreshes usage from the actual store (reconciliation).
    /// </summary>
    /// <param name="userId">User ID to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshFromStoreAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears tracking data for a user (e.g., after ForgetUser).
    /// </summary>
    /// <param name="userId">User ID to clear.</param>
    void ClearUser(string userId);
}

/// <summary>
/// Aggregated usage for a tenant.
/// </summary>
public sealed class TenantUsage
{
    /// <summary>
    /// Tenant ID.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Number of active users.
    /// </summary>
    public int ActiveUsers { get; init; }

    /// <summary>
    /// Total memories across all users.
    /// </summary>
    public long TotalMemories { get; init; }

    /// <summary>
    /// Total storage across all users.
    /// </summary>
    public long TotalStorageBytes { get; init; }

    /// <summary>
    /// Per-user breakdown.
    /// </summary>
    public IReadOnlyDictionary<string, ResourceUsage>? UserBreakdown { get; init; }

    /// <summary>
    /// Memories by tier across tenant.
    /// </summary>
    public IReadOnlyDictionary<Tier, long>? ByTier { get; init; }

    /// <summary>
    /// Memories by type across tenant.
    /// </summary>
    public IReadOnlyDictionary<MemoryType, long>? ByType { get; init; }

    /// <summary>
    /// When usage was calculated.
    /// </summary>
    public DateTime CalculatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Global usage summary across all users and tenants.
/// </summary>
public sealed class GlobalUsageSummary
{
    /// <summary>
    /// Total number of users.
    /// </summary>
    public int TotalUsers { get; init; }

    /// <summary>
    /// Total number of tenants.
    /// </summary>
    public int TotalTenants { get; init; }

    /// <summary>
    /// Total memories across all users.
    /// </summary>
    public long TotalMemories { get; init; }

    /// <summary>
    /// Total storage across all users.
    /// </summary>
    public long TotalStorageBytes { get; init; }

    /// <summary>
    /// Average memories per user.
    /// </summary>
    public double AverageMemoriesPerUser => TotalUsers > 0 ? (double)TotalMemories / TotalUsers : 0;

    /// <summary>
    /// Average storage per user.
    /// </summary>
    public double AverageStoragePerUser => TotalUsers > 0 ? (double)TotalStorageBytes / TotalUsers : 0;

    /// <summary>
    /// Memories by tier.
    /// </summary>
    public IReadOnlyDictionary<Tier, long>? ByTier { get; init; }

    /// <summary>
    /// Memories by type.
    /// </summary>
    public IReadOnlyDictionary<MemoryType, long>? ByType { get; init; }

    /// <summary>
    /// Top users by memory count.
    /// </summary>
    public IReadOnlyList<(string UserId, long Count)>? TopUsersByCount { get; init; }

    /// <summary>
    /// Top users by storage size.
    /// </summary>
    public IReadOnlyList<(string UserId, long Bytes)>? TopUsersByStorage { get; init; }

    /// <summary>
    /// When summary was calculated.
    /// </summary>
    public DateTime CalculatedAt { get; init; } = DateTime.UtcNow;
}
