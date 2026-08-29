using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.ResourceManagement;
using MemoryIndexer.Sdk.Intelligence.Security.MultiTenant;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.ResourceManagement;

/// <summary>
/// Tests for ResourceLimitEnforcer (Phase v0.6.0-γ: Resource Management).
/// </summary>
public class ResourceLimitEnforcerTests
{
    private readonly IUsageTracker _mockUsageTracker;
    private readonly ITenantContext _mockTenantContext;
    private readonly MemoryIndexerOptions _options;
    private readonly ResourceLimitEnforcer _enforcer;

    public ResourceLimitEnforcerTests()
    {
        _mockUsageTracker = Substitute.For<IUsageTracker>();
        _mockTenantContext = Substitute.For<ITenantContext>();
        _options = new MemoryIndexerOptions
        {
            ResourceLimits = new ResourceLimitOptions
            {
                MaxMemoriesPerUser = 1000,
                MaxStorageBytesPerUser = 10_000_000, // 10 MB
                EnforcementEnabled = true,
                WarningThresholdPercent = 80
            }
        };

        _mockTenantContext.TenantId.Returns((string?)null);
        _mockTenantContext.Configuration.Returns((TenantConfiguration?)null);

        _enforcer = new ResourceLimitEnforcer(
            _mockUsageTracker,
            _mockTenantContext,
            Options.Create(_options),
            NullLogger<ResourceLimitEnforcer>.Instance);
    }

    #region CanStoreAsync Tests

    [Fact]
    public async Task CanStoreAsync_UnderLimit_ShouldAllow()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 100,
                StorageSizeBytes = 1_000_000
            });

        // Act
        var result = await _enforcer.CanStoreAsync("user1", 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Null(result.DenialReason);
        Assert.NotNull(result.Limits);
    }

    [Fact]
    public async Task CanStoreAsync_ExceedsMemoryCount_ShouldDeny()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 1000, // At limit
                StorageSizeBytes = 1_000_000
            });

        // Act
        var result = await _enforcer.CanStoreAsync("user1", 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(LimitType.MemoryCount, result.ExceededLimit);
        Assert.Contains("Memory count limit exceeded", result.DenialReason);
    }

    [Fact]
    public async Task CanStoreAsync_ExceedsStorageSize_ShouldDeny()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 100,
                StorageSizeBytes = 9_500_000 // Close to 10MB limit
            });

        // Act
        var result = await _enforcer.CanStoreAsync("user1", 1_000_000, TestContext.Current.CancellationToken); // Adding 1MB would exceed

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(LimitType.StorageSize, result.ExceededLimit);
        Assert.Contains("Storage size limit exceeded", result.DenialReason);
    }

    [Fact]
    public async Task CanStoreAsync_EnforcementDisabled_ShouldAlwaysAllow()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            ResourceLimits = new ResourceLimitOptions
            {
                MaxMemoriesPerUser = 1000,
                MaxStorageBytesPerUser = 10_000_000,
                EnforcementEnabled = false,
                WarningThresholdPercent = 80
            }
        };

        var enforcer = new ResourceLimitEnforcer(
            _mockUsageTracker,
            _mockTenantContext,
            Options.Create(options),
            NullLogger<ResourceLimitEnforcer>.Instance);

        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 100000, // Way over limit
                StorageSizeBytes = 100_000_000
            });

        // Act
        var result = await enforcer.CanStoreAsync("user1", 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
    }

    #endregion

    #region CanStoreBatchAsync Tests

    [Fact]
    public async Task CanStoreBatchAsync_UnderLimit_ShouldAllow()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 100,
                StorageSizeBytes = 1_000_000
            });

        // Act
        var result = await _enforcer.CanStoreBatchAsync("user1", 10, 50000, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CanStoreBatchAsync_BatchExceedsMemoryCount_ShouldDeny()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 990, // Only 10 slots left
                StorageSizeBytes = 1_000_000
            });

        // Act - trying to add 20
        var result = await _enforcer.CanStoreBatchAsync("user1", 20, 50000, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(LimitType.MemoryCount, result.ExceededLimit);
    }

    [Fact]
    public async Task CanStoreBatchAsync_BatchExceedsStorageSize_ShouldDeny()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 100,
                StorageSizeBytes = 9_000_000 // Only 1MB left
            });

        // Act - trying to add 2MB total
        var result = await _enforcer.CanStoreBatchAsync("user1", 10, 2_000_000, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(LimitType.StorageSize, result.ExceededLimit);
    }

    #endregion

    #region GetLimits Tests

    [Fact]
    public void GetLimits_WithGlobalConfig_ShouldReturnConfigLimits()
    {
        // Act
        var limits = _enforcer.GetLimits("user1");

        // Assert
        Assert.Equal(1000, limits.MaxMemories);
        Assert.Equal(10_000_000, limits.MaxStorageBytes);
        Assert.True(limits.EnforcementEnabled);
        Assert.Equal("Configuration", limits.Source);
    }

    [Fact]
    public void GetLimits_WithTenantConfig_ShouldPrioritizeTenant()
    {
        // Arrange
        var tenantConfig = new TenantConfiguration
        {
            MaxMemories = 5000,
            MaxStorageBytes = 50_000_000L
        };

        _mockTenantContext.Configuration.Returns(tenantConfig);
        _mockTenantContext.TenantId.Returns("tenant1");

        // Act
        var limits = _enforcer.GetLimits("user1");

        // Assert
        Assert.Equal(5000, limits.MaxMemories);
        Assert.Equal(50_000_000, limits.MaxStorageBytes);
        Assert.Contains("Tenant:tenant1", limits.Source);
    }

    [Fact]
    public void GetLimits_NoConfig_ShouldReturnDefaults()
    {
        // Arrange
        var optionsWithoutLimits = new MemoryIndexerOptions
        {
            ResourceLimits = null
        };

        var enforcer = new ResourceLimitEnforcer(
            _mockUsageTracker,
            _mockTenantContext,
            Options.Create(optionsWithoutLimits),
            NullLogger<ResourceLimitEnforcer>.Instance);

        // Act
        var limits = enforcer.GetLimits("user1");

        // Assert
        Assert.Equal(ResourceLimits.Default.MaxMemories, limits.MaxMemories);
        Assert.Equal(ResourceLimits.Default.MaxStorageBytes, limits.MaxStorageBytes);
    }

    #endregion

    #region GetUsageAsync Tests

    [Fact]
    public async Task GetUsageAsync_WithCachedData_ShouldReturnCached()
    {
        // Arrange
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 50,
                StorageSizeBytes = 500_000
            });

        // Act
        var usage = await _enforcer.GetUsageAsync("user1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, usage.MemoryCount);
        Assert.Equal(500_000, usage.StorageSizeBytes);
    }

    [Fact]
    public async Task GetUsageAsync_NoCachedData_ShouldRefreshFromStore()
    {
        // Arrange - first call returns empty, second returns refreshed data
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(
                new ResourceUsage { UserId = "user1", MemoryCount = 0, StorageSizeBytes = 0 },
                new ResourceUsage { UserId = "user1", MemoryCount = 25, StorageSizeBytes = 250_000 });

        // Act
        var usage = await _enforcer.GetUsageAsync("user1", TestContext.Current.CancellationToken);

        // Assert
        await _mockUsageTracker.Received(1).RefreshFromStoreAsync("user1", Arg.Any<CancellationToken>());
        Assert.Equal(25, usage.MemoryCount);
    }

    #endregion

    #region Warning Threshold Tests

    [Fact]
    public async Task CanStoreAsync_AtWarningThreshold_ShouldStillAllow()
    {
        // Arrange - 80% of 1000 = 800
        _mockUsageTracker.GetUsage("user1", Arg.Any<string?>())
            .Returns(new ResourceUsage
            {
                UserId = "user1",
                MemoryCount = 800, // At 80% warning threshold
                StorageSizeBytes = 8_000_000 // Also at 80%
            });

        // Act
        var result = await _enforcer.CanStoreAsync("user1", 100, TestContext.Current.CancellationToken);

        // Assert - should still allow, warnings are just for telemetry
        Assert.True(result.IsAllowed);
    }

    #endregion

    #region ResourceUsage Helper Methods Tests

    [Fact]
    public void ResourceUsage_MemoryCountPercentage_ShouldCalculateCorrectly()
    {
        // Arrange
        var usage = new ResourceUsage { UserId = "test", MemoryCount = 250 };
        var limits = new ResourceLimits { MaxMemories = 1000 };

        // Act
        var percent = usage.MemoryCountPercentage(limits);

        // Assert
        Assert.Equal(25.0, percent);
    }

    [Fact]
    public void ResourceUsage_StoragePercentage_ShouldCalculateCorrectly()
    {
        // Arrange
        var usage = new ResourceUsage { UserId = "test", StorageSizeBytes = 5_000_000 };
        var limits = new ResourceLimits { MaxStorageBytes = 10_000_000 };

        // Act
        var percent = usage.StoragePercentage(limits);

        // Assert
        Assert.Equal(50.0, percent);
    }

    [Fact]
    public void ResourceUsage_Percentage_ZeroLimit_ShouldReturnZero()
    {
        // Arrange
        var usage = new ResourceUsage { UserId = "test", MemoryCount = 100, StorageSizeBytes = 1000 };
        var limits = new ResourceLimits { MaxMemories = 0, MaxStorageBytes = 0 };

        // Act
        var memPercent = usage.MemoryCountPercentage(limits);
        var storagePercent = usage.StoragePercentage(limits);

        // Assert - should not throw, return 0
        Assert.Equal(0, memPercent);
        Assert.Equal(0, storagePercent);
    }

    #endregion

    #region EnforcementResult Helper Tests

    [Fact]
    public void EnforcementResult_Allowed_ShouldHaveCorrectState()
    {
        // Arrange
        var usage = new ResourceUsage { UserId = "user1", MemoryCount = 50 };
        var limits = new ResourceLimits { MaxMemories = 1000 };

        // Act
        var result = EnforcementResult.Allowed(usage, limits);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Null(result.DenialReason);
        Assert.Null(result.ExceededLimit);
        Assert.Equal(usage, result.CurrentUsage);
        Assert.Equal(limits, result.Limits);
    }

    [Fact]
    public void EnforcementResult_Denied_ShouldHaveCorrectState()
    {
        // Arrange
        var usage = new ResourceUsage { UserId = "user1", MemoryCount = 1001 };
        var limits = new ResourceLimits { MaxMemories = 1000 };

        // Act
        var result = EnforcementResult.Denied(
            LimitType.MemoryCount,
            "Limit exceeded",
            usage,
            limits);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal("Limit exceeded", result.DenialReason);
        Assert.Equal(LimitType.MemoryCount, result.ExceededLimit);
        Assert.Equal(usage, result.CurrentUsage);
        Assert.Equal(limits, result.Limits);
    }

    #endregion

    #region ResourceLimits Default and Unlimited Tests

    [Fact]
    public void ResourceLimits_Default_ShouldHaveReasonableValues()
    {
        // Act
        var defaults = ResourceLimits.Default;

        // Assert
        Assert.True(defaults.MaxMemories > 0);
        Assert.True(defaults.MaxStorageBytes > 0);
        Assert.True(defaults.EnforcementEnabled);
        Assert.Equal("Default", defaults.Source);
    }

    [Fact]
    public void ResourceLimits_Unlimited_ShouldHaveMaxValues()
    {
        // Act
        var unlimited = ResourceLimits.Unlimited;

        // Assert
        Assert.Equal(long.MaxValue, unlimited.MaxMemories);
        Assert.Equal(long.MaxValue, unlimited.MaxStorageBytes);
        Assert.False(unlimited.EnforcementEnabled);
        // Source defaults to "Default" even for Unlimited preset
        Assert.Equal("Default", unlimited.Source);
    }

    #endregion
}
