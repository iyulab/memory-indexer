using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using MemoryIndexer.Configuration;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for resource management and usage tracking.
/// Phase v0.6.0-γ: Resource Management.
/// </summary>
[McpServerToolType]
public class ResourceManagementTools(
    IResourceLimitEnforcer enforcer,
    IUsageTracker usageTracker,
    IOptions<MemoryIndexerOptions> options)
{

    /// <summary>
    /// Get current resource usage for a user.
    /// </summary>
    [McpServerTool]
    [Description("Get current resource usage statistics including memory count, storage size, and breakdown by tier and type.")]
    public async Task<UsageResult> GetUsage(
        [Description("User ID to get usage for (optional, defaults to current user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? options.Value.DefaultUserId;
        var usage = await enforcer.GetUsageAsync(targetUserId, cancellationToken);
        var limits = enforcer.GetLimits(targetUserId);

        return new UsageResult
        {
            Success = true,
            UserId = usage.UserId,
            TenantId = usage.TenantId,
            MemoryCount = usage.MemoryCount,
            StorageSizeBytes = usage.StorageSizeBytes,
            StorageSizeMB = usage.StorageSizeBytes / (1024.0 * 1024.0),
            ByTier = usage.ByTier?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ByType = usage.ByType?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            MemoryCountPercent = usage.MemoryCountPercentage(limits),
            StoragePercent = usage.StoragePercentage(limits),
            CalculatedAt = usage.CalculatedAt.ToString("O"),
            Message = $"Usage: {usage.MemoryCount} memories, {usage.StorageSizeBytes / (1024.0 * 1024.0):F2} MB"
        };
    }

    /// <summary>
    /// Get resource limits for a user.
    /// </summary>
    [McpServerTool]
    [Description("Get the resource limits applicable to a user, including maximum memories and storage size.")]
    public LimitsResult GetLimits(
        [Description("User ID to get limits for (optional, defaults to current user)")] string? userId = null)
    {
        var targetUserId = userId ?? options.Value.DefaultUserId;
        var limits = enforcer.GetLimits(targetUserId);

        return new LimitsResult
        {
            Success = true,
            UserId = targetUserId,
            MaxMemories = limits.MaxMemories,
            MaxStorageBytes = limits.MaxStorageBytes,
            MaxStorageMB = limits.MaxStorageBytes / (1024.0 * 1024.0),
            MaxStorageGB = limits.MaxStorageBytes / (1024.0 * 1024.0 * 1024.0),
            EnforcementEnabled = limits.EnforcementEnabled,
            WarningThresholdPercent = limits.WarningThresholdPercent,
            Source = limits.Source,
            Message = $"Limits: {limits.MaxMemories} memories, {limits.MaxStorageBytes / (1024.0 * 1024.0 * 1024.0):F2} GB (from {limits.Source})"
        };
    }

    /// <summary>
    /// Check if a store operation is allowed.
    /// </summary>
    [McpServerTool]
    [Description("Check if a store operation would be allowed within current resource limits.")]
    public async Task<EnforcementCheckResult> CanStore(
        [Description("Estimated size in bytes of the memory to store")] long estimatedSizeBytes = 0,
        [Description("User ID to check (optional, defaults to current user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? options.Value.DefaultUserId;
        var result = await enforcer.CanStoreAsync(targetUserId, estimatedSizeBytes, cancellationToken);

        return new EnforcementCheckResult
        {
            Success = true,
            IsAllowed = result.IsAllowed,
            DenialReason = result.DenialReason,
            ExceededLimit = result.ExceededLimit?.ToString(),
            CurrentMemoryCount = result.CurrentUsage?.MemoryCount,
            CurrentStorageBytes = result.CurrentUsage?.StorageSizeBytes,
            MaxMemories = result.Limits?.MaxMemories,
            MaxStorageBytes = result.Limits?.MaxStorageBytes,
            Message = result.IsAllowed
                ? "Operation allowed"
                : $"Operation denied: {result.DenialReason}"
        };
    }

    /// <summary>
    /// Check if a batch store operation is allowed.
    /// </summary>
    [McpServerTool]
    [Description("Check if a batch store operation would be allowed within current resource limits.")]
    public async Task<EnforcementCheckResult> CanStoreBatch(
        [Description("Number of memories to store")] int count,
        [Description("Estimated total size in bytes")] long estimatedTotalSizeBytes = 0,
        [Description("User ID to check (optional, defaults to current user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? options.Value.DefaultUserId;
        var result = await enforcer.CanStoreBatchAsync(targetUserId, count, estimatedTotalSizeBytes, cancellationToken);

        return new EnforcementCheckResult
        {
            Success = true,
            IsAllowed = result.IsAllowed,
            DenialReason = result.DenialReason,
            ExceededLimit = result.ExceededLimit?.ToString(),
            CurrentMemoryCount = result.CurrentUsage?.MemoryCount,
            CurrentStorageBytes = result.CurrentUsage?.StorageSizeBytes,
            MaxMemories = result.Limits?.MaxMemories,
            MaxStorageBytes = result.Limits?.MaxStorageBytes,
            Message = result.IsAllowed
                ? $"Batch of {count} memories allowed"
                : $"Batch denied: {result.DenialReason}"
        };
    }

    /// <summary>
    /// Get tenant-level usage aggregation.
    /// </summary>
    [McpServerTool]
    [Description("Get aggregated resource usage for a tenant, including all users and breakdown by tier and type.")]
    public TenantUsageResult GetTenantUsage(
        [Description("Tenant ID to get usage for")] string tenantId)
    {
        var usage = usageTracker.GetTenantUsage(tenantId);

        return new TenantUsageResult
        {
            Success = true,
            TenantId = usage.TenantId,
            ActiveUsers = usage.ActiveUsers,
            TotalMemories = usage.TotalMemories,
            TotalStorageBytes = usage.TotalStorageBytes,
            TotalStorageMB = usage.TotalStorageBytes / (1024.0 * 1024.0),
            ByTier = usage.ByTier?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ByType = usage.ByType?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            CalculatedAt = usage.CalculatedAt.ToString("O"),
            Message = $"Tenant {tenantId}: {usage.ActiveUsers} users, {usage.TotalMemories} memories, {usage.TotalStorageBytes / (1024.0 * 1024.0):F2} MB"
        };
    }

    /// <summary>
    /// Get global usage summary across all users.
    /// </summary>
    [McpServerTool]
    [Description("Get global resource usage summary across all users and tenants.")]
    public GlobalUsageResult GetGlobalSummary()
    {
        var summary = usageTracker.GetGlobalSummary();

        return new GlobalUsageResult
        {
            Success = true,
            TotalUsers = summary.TotalUsers,
            TotalTenants = summary.TotalTenants,
            TotalMemories = summary.TotalMemories,
            TotalStorageBytes = summary.TotalStorageBytes,
            TotalStorageMB = summary.TotalStorageBytes / (1024.0 * 1024.0),
            AverageMemoriesPerUser = summary.AverageMemoriesPerUser,
            AverageStoragePerUserMB = summary.AverageStoragePerUser / (1024.0 * 1024.0),
            ByTier = summary.ByTier?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            ByType = summary.ByType?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            TopUsersByCount = summary.TopUsersByCount?.Select(x => new UserStat { UserId = x.UserId, Value = x.Count }).ToList(),
            TopUsersByStorage = summary.TopUsersByStorage?.Select(x => new UserStat { UserId = x.UserId, Value = x.Bytes }).ToList(),
            CalculatedAt = summary.CalculatedAt.ToString("O"),
            Message = $"Global: {summary.TotalUsers} users, {summary.TotalMemories} memories, {summary.TotalStorageBytes / (1024.0 * 1024.0):F2} MB"
        };
    }

    /// <summary>
    /// Refresh usage from store for a user.
    /// </summary>
    [McpServerTool]
    [Description("Refresh and recalculate usage from the actual memory store for a user.")]
    public async Task<RefreshResult> RefreshUsage(
        [Description("User ID to refresh (optional, defaults to current user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? options.Value.DefaultUserId;
        await usageTracker.RefreshFromStoreAsync(targetUserId, cancellationToken);
        var usage = usageTracker.GetUsage(targetUserId);

        return new RefreshResult
        {
            Success = true,
            UserId = targetUserId,
            MemoryCount = usage.MemoryCount,
            StorageSizeBytes = usage.StorageSizeBytes,
            Message = $"Refreshed usage for {targetUserId}: {usage.MemoryCount} memories, {usage.StorageSizeBytes / (1024.0 * 1024.0):F2} MB"
        };
    }
}

/// <summary>
/// Result of a usage query.
/// </summary>
public class UsageResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public string? TenantId { get; set; }
    public long MemoryCount { get; set; }
    public long StorageSizeBytes { get; set; }
    public double StorageSizeMB { get; set; }
    public Dictionary<string, long>? ByTier { get; set; }
    public Dictionary<string, long>? ByType { get; set; }
    public double MemoryCountPercent { get; set; }
    public double StoragePercent { get; set; }
    public string? CalculatedAt { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Result of a limits query.
/// </summary>
public class LimitsResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public long MaxMemories { get; set; }
    public long MaxStorageBytes { get; set; }
    public double MaxStorageMB { get; set; }
    public double MaxStorageGB { get; set; }
    public bool EnforcementEnabled { get; set; }
    public int WarningThresholdPercent { get; set; }
    public string? Source { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Result of an enforcement check.
/// </summary>
public class EnforcementCheckResult
{
    public bool Success { get; set; }
    public bool IsAllowed { get; set; }
    public string? DenialReason { get; set; }
    public string? ExceededLimit { get; set; }
    public long? CurrentMemoryCount { get; set; }
    public long? CurrentStorageBytes { get; set; }
    public long? MaxMemories { get; set; }
    public long? MaxStorageBytes { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Result of a tenant usage query.
/// </summary>
public class TenantUsageResult
{
    public bool Success { get; set; }
    public string? TenantId { get; set; }
    public int ActiveUsers { get; set; }
    public long TotalMemories { get; set; }
    public long TotalStorageBytes { get; set; }
    public double TotalStorageMB { get; set; }
    public Dictionary<string, long>? ByTier { get; set; }
    public Dictionary<string, long>? ByType { get; set; }
    public string? CalculatedAt { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Result of a global usage query.
/// </summary>
public class GlobalUsageResult
{
    public bool Success { get; set; }
    public int TotalUsers { get; set; }
    public int TotalTenants { get; set; }
    public long TotalMemories { get; set; }
    public long TotalStorageBytes { get; set; }
    public double TotalStorageMB { get; set; }
    public double AverageMemoriesPerUser { get; set; }
    public double AverageStoragePerUserMB { get; set; }
    public Dictionary<string, long>? ByTier { get; set; }
    public Dictionary<string, long>? ByType { get; set; }
    public List<UserStat>? TopUsersByCount { get; set; }
    public List<UserStat>? TopUsersByStorage { get; set; }
    public string? CalculatedAt { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// User statistic entry.
/// </summary>
public class UserStat
{
    public string? UserId { get; set; }
    public long Value { get; set; }
}

/// <summary>
/// Result of a usage refresh.
/// </summary>
public class RefreshResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public long MemoryCount { get; set; }
    public long StorageSizeBytes { get; set; }
    public string? Message { get; set; }
}
