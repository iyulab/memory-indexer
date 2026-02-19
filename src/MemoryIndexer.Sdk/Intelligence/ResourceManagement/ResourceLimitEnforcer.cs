using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Security.MultiTenant;
using MemoryIndexer.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MemoryIndexer.Configuration;

namespace MemoryIndexer.Sdk.Intelligence.ResourceManagement;

/// <summary>
/// Enforces resource limits based on tenant configuration.
/// </summary>
/// <remarks>
/// Phase v0.6.0-γ: Resource Management
/// Integrates with IUsageTracker and ITenantContext for limit enforcement.
/// </remarks>
public sealed partial class ResourceLimitEnforcer : IResourceLimitEnforcer
{
    private readonly IUsageTracker _usageTracker;
    private readonly ITenantContext _tenantContext;
    private readonly MemoryIndexerOptions _options;
    private readonly ILogger<ResourceLimitEnforcer> _logger;

    public ResourceLimitEnforcer(
        IUsageTracker usageTracker,
        ITenantContext tenantContext,
        IOptions<MemoryIndexerOptions> options,
        ILogger<ResourceLimitEnforcer> logger)
    {
        _usageTracker = usageTracker;
        _tenantContext = tenantContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EnforcementResult> CanStoreAsync(
        string userId,
        long estimatedSize = 0,
        CancellationToken cancellationToken = default)
    {
        return await CanStoreBatchAsync(userId, 1, estimatedSize, cancellationToken);
    }

    public Task<EnforcementResult> CanStoreBatchAsync(
        string userId,
        int count,
        long estimatedTotalSize = 0,
        CancellationToken cancellationToken = default)
    {
        var limits = GetLimits(userId);

        // If enforcement is disabled, allow all operations
        if (!limits.EnforcementEnabled)
        {
            LogEnforcementDisabled(_logger, userId);
            return Task.FromResult(EnforcementResult.Allowed(null, limits));
        }

        var usage = _usageTracker.GetUsage(userId, _tenantContext.TenantId);

        // Check memory count limit
        var projectedCount = usage.MemoryCount + count;
        if (projectedCount > limits.MaxMemories)
        {
            var result = EnforcementResult.Denied(
                LimitType.MemoryCount,
                $"Memory count limit exceeded: {projectedCount} > {limits.MaxMemories}",
                usage,
                limits);

            MemoryIndexerTelemetry.RecordResourceLimitExceeded(
                userId, _tenantContext.TenantId, "memory_count", usage.MemoryCount, limits.MaxMemories);

            LogMemoryCountLimitExceeded(_logger, userId, usage.MemoryCount, count, limits.MaxMemories);

            return Task.FromResult(result);
        }

        // Check storage size limit
        var projectedSize = usage.StorageSizeBytes + estimatedTotalSize;
        if (projectedSize > limits.MaxStorageBytes)
        {
            var result = EnforcementResult.Denied(
                LimitType.StorageSize,
                $"Storage size limit exceeded: {projectedSize} > {limits.MaxStorageBytes}",
                usage,
                limits);

            MemoryIndexerTelemetry.RecordResourceLimitExceeded(
                userId, _tenantContext.TenantId, "storage_size", usage.StorageSizeBytes, limits.MaxStorageBytes);

            LogStorageSizeLimitExceeded(_logger, userId, usage.StorageSizeBytes, estimatedTotalSize, limits.MaxStorageBytes);

            return Task.FromResult(result);
        }

        // Check warning thresholds and emit telemetry
        var countPercent = usage.MemoryCountPercentage(limits);
        var storagePercent = usage.StoragePercentage(limits);

        if (countPercent >= limits.WarningThresholdPercent)
        {
            MemoryIndexerTelemetry.RecordResourceWarning(
                userId, _tenantContext.TenantId, "memory_count", countPercent);

            LogMemoryCountWarning(_logger, userId, countPercent);
        }

        if (storagePercent >= limits.WarningThresholdPercent)
        {
            MemoryIndexerTelemetry.RecordResourceWarning(
                userId, _tenantContext.TenantId, "storage_size", storagePercent);

            LogStorageSizeWarning(_logger, userId, storagePercent);
        }

        return Task.FromResult(EnforcementResult.Allowed(usage, limits));
    }

    public async Task<ResourceUsage> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // First try cached usage
        var usage = _usageTracker.GetUsage(userId, _tenantContext.TenantId);

        // If no cached data, refresh from store
        if (usage.MemoryCount == 0)
        {
            await _usageTracker.RefreshFromStoreAsync(userId, cancellationToken);
            usage = _usageTracker.GetUsage(userId, _tenantContext.TenantId);
        }

        return usage;
    }

    public ResourceLimits GetLimits(string userId)
    {
        // Priority: Tenant config > Global options > Default

        // 1. Check tenant configuration
        if (_tenantContext.Configuration is { } tenantConfig)
        {
            return new ResourceLimits
            {
                MaxMemories = tenantConfig.MaxMemories,
                MaxStorageBytes = tenantConfig.MaxStorageBytes,
                EnforcementEnabled = true,
                WarningThresholdPercent = 80,
                Source = $"Tenant:{_tenantContext.TenantId}"
            };
        }

        // 2. Check global options
        if (_options.ResourceLimits is { } globalLimits)
        {
            return new ResourceLimits
            {
                MaxMemories = globalLimits.MaxMemoriesPerUser,
                MaxStorageBytes = globalLimits.MaxStorageBytesPerUser,
                EnforcementEnabled = globalLimits.EnforcementEnabled,
                WarningThresholdPercent = globalLimits.WarningThresholdPercent,
                Source = "Configuration"
            };
        }

        // 3. Default limits
        return ResourceLimits.Default;
    }

    [LoggerMessage(Level = LogLevel.Trace, Message = "Enforcement disabled for user {UserId}")]
    private static partial void LogEnforcementDisabled(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory count limit exceeded for user {UserId}: {Current} + {New} > {Limit}")]
    private static partial void LogMemoryCountLimitExceeded(ILogger logger, string userId, long current, int @new, long limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Storage size limit exceeded for user {UserId}: {Current} + {New} > {Limit}")]
    private static partial void LogStorageSizeLimitExceeded(ILogger logger, string userId, long current, long @new, long limit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Memory count warning for user {UserId}: {Percent:F1}% of limit")]
    private static partial void LogMemoryCountWarning(ILogger logger, string userId, double percent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage size warning for user {UserId}: {Percent:F1}% of limit")]
    private static partial void LogStorageSizeWarning(ILogger logger, string userId, double percent);
}
