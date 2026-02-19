using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.ResourceManagement;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.ResourceManagement;

/// <summary>
/// Tests for InMemoryUsageTracker (Phase v0.6.0-γ: Resource Management).
/// </summary>
public class InMemoryUsageTrackerTests
{
    private readonly IMemoryStore _mockMemoryStore;
    private readonly InMemoryUsageTracker _tracker;

    public InMemoryUsageTrackerTests()
    {
        _mockMemoryStore = Substitute.For<IMemoryStore>();
        _tracker = new InMemoryUsageTracker(
            _mockMemoryStore,
            NullLogger<InMemoryUsageTracker>.Instance);
    }

    #region RecordStore Tests

    [Fact]
    public void RecordStore_ShouldIncrementMemoryCount()
    {
        // Act
        _tracker.RecordStore("user1", 1000, Tier.Long, MemoryType.Episodic);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(1, usage.MemoryCount);
        Assert.Equal(1000, usage.StorageSizeBytes);
    }

    [Fact]
    public void RecordStore_MultipleCalls_ShouldAccumulate()
    {
        // Act
        _tracker.RecordStore("user1", 500, Tier.Short, MemoryType.Semantic);
        _tracker.RecordStore("user1", 700, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 300, Tier.Long, MemoryType.Fact);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(3, usage.MemoryCount);
        Assert.Equal(1500, usage.StorageSizeBytes);
    }

    [Fact]
    public void RecordStore_ShouldTrackByTier()
    {
        // Act
        _tracker.RecordStore("user1", 100, Tier.Buffer, MemoryType.Episodic);
        _tracker.RecordStore("user1", 200, Tier.Short, MemoryType.Episodic);
        _tracker.RecordStore("user1", 300, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 400, Tier.Long, MemoryType.Episodic);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.NotNull(usage.ByTier);
        Assert.Equal(1, usage.ByTier[Tier.Buffer]);
        Assert.Equal(1, usage.ByTier[Tier.Short]);
        Assert.Equal(2, usage.ByTier[Tier.Long]);
    }

    [Fact]
    public void RecordStore_ShouldTrackByType()
    {
        // Act
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 200, Tier.Long, MemoryType.Semantic);
        _tracker.RecordStore("user1", 300, Tier.Long, MemoryType.Semantic);
        _tracker.RecordStore("user1", 400, Tier.Long, MemoryType.Procedural);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.NotNull(usage.ByType);
        Assert.Equal(1, usage.ByType[MemoryType.Episodic]);
        Assert.Equal(2, usage.ByType[MemoryType.Semantic]);
        Assert.Equal(1, usage.ByType[MemoryType.Procedural]);
    }

    [Fact]
    public void RecordStore_WithTenant_ShouldTrackTenantUsers()
    {
        // Act
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic, "tenant1");
        _tracker.RecordStore("user2", 200, Tier.Long, MemoryType.Episodic, "tenant1");
        _tracker.RecordStore("user3", 300, Tier.Long, MemoryType.Episodic, "tenant2");

        var tenant1Usage = _tracker.GetTenantUsage("tenant1");
        var tenant2Usage = _tracker.GetTenantUsage("tenant2");

        // Assert
        Assert.Equal(2, tenant1Usage.ActiveUsers);
        Assert.Equal(300, tenant1Usage.TotalStorageBytes);
        Assert.Equal(1, tenant2Usage.ActiveUsers);
        Assert.Equal(300, tenant2Usage.TotalStorageBytes);
    }

    #endregion

    #region RecordDelete Tests

    [Fact]
    public void RecordDelete_ShouldDecrementMemoryCount()
    {
        // Arrange
        _tracker.RecordStore("user1", 1000, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 500, Tier.Long, MemoryType.Semantic);

        // Act
        _tracker.RecordDelete("user1", 500, Tier.Long, MemoryType.Semantic);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(1, usage.MemoryCount);
        Assert.Equal(1000, usage.StorageSizeBytes); // 1000 + 500 - 500 = 1000
    }

    [Fact]
    public void RecordDelete_ShouldNotGoNegative()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);

        // Act - delete more than we have
        _tracker.RecordDelete("user1", 500, Tier.Long, MemoryType.Episodic);
        _tracker.RecordDelete("user1", 500, Tier.Long, MemoryType.Episodic);
        var usage = _tracker.GetUsage("user1");

        // Assert - should be 0, not negative
        Assert.True(usage.MemoryCount >= 0);
        Assert.True(usage.StorageSizeBytes >= 0);
    }

    [Fact]
    public void RecordDelete_UnknownUser_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _tracker.RecordDelete("unknown-user", 100, Tier.Long, MemoryType.Episodic);
    }

    [Fact]
    public void RecordDelete_ShouldDecrementTierAndType()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user1", 100, Tier.Short, MemoryType.Semantic);

        // Act
        _tracker.RecordDelete("user1", 100, Tier.Long, MemoryType.Episodic);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(1, usage.ByTier![Tier.Long]);
        Assert.Equal(1, usage.ByTier[Tier.Short]);
        Assert.Equal(1, usage.ByType![MemoryType.Episodic]);
        Assert.Equal(1, usage.ByType[MemoryType.Semantic]);
    }

    #endregion

    #region RecordTierPromotion Tests

    [Fact]
    public void RecordTierPromotion_ShouldUpdateTierCounts()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Buffer, MemoryType.Episodic);
        _tracker.RecordStore("user1", 100, Tier.Buffer, MemoryType.Episodic);

        // Act - promote one from Buffer to Short
        _tracker.RecordTierPromotion("user1", Tier.Buffer, Tier.Short);
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(1, usage.ByTier![Tier.Buffer]);
        Assert.Equal(1, usage.ByTier[Tier.Short]);
    }

    [Fact]
    public void RecordTierPromotion_UnknownUser_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _tracker.RecordTierPromotion("unknown-user", Tier.Buffer, Tier.Short);
    }

    #endregion

    #region GetUsage Tests

    [Fact]
    public void GetUsage_NewUser_ShouldReturnEmptyUsage()
    {
        // Act
        var usage = _tracker.GetUsage("new-user");

        // Assert
        Assert.Equal("new-user", usage.UserId);
        Assert.Equal(0, usage.MemoryCount);
        Assert.Equal(0, usage.StorageSizeBytes);
        Assert.Empty(usage.ByTier!);
        Assert.Empty(usage.ByType!);
    }

    [Fact]
    public void GetUsage_ShouldReturnCalculatedAt()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);

        // Act
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.True(usage.CalculatedAt >= DateTime.UtcNow.AddSeconds(-1));
    }

    #endregion

    #region GetTenantUsage Tests

    [Fact]
    public void GetTenantUsage_UnknownTenant_ShouldReturnEmptyUsage()
    {
        // Act
        var usage = _tracker.GetTenantUsage("unknown-tenant");

        // Assert
        Assert.Equal("unknown-tenant", usage.TenantId);
        Assert.Equal(0, usage.ActiveUsers);
        Assert.Equal(0, usage.TotalMemories);
        Assert.Equal(0, usage.TotalStorageBytes);
    }

    [Fact]
    public void GetTenantUsage_ShouldAggregateTierAndType()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic, "tenant1");
        _tracker.RecordStore("user1", 100, Tier.Short, MemoryType.Semantic, "tenant1");
        _tracker.RecordStore("user2", 100, Tier.Long, MemoryType.Episodic, "tenant1");

        // Act
        var usage = _tracker.GetTenantUsage("tenant1");

        // Assert
        Assert.Equal(2, usage.ByTier![Tier.Long]);
        Assert.Equal(1, usage.ByTier[Tier.Short]);
        Assert.Equal(2, usage.ByType![MemoryType.Episodic]);
        Assert.Equal(1, usage.ByType[MemoryType.Semantic]);
    }

    [Fact]
    public void GetTenantUsage_ShouldIncludeUserBreakdown()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic, "tenant1");
        _tracker.RecordStore("user2", 200, Tier.Long, MemoryType.Semantic, "tenant1");

        // Act
        var usage = _tracker.GetTenantUsage("tenant1");

        // Assert
        Assert.NotNull(usage.UserBreakdown);
        Assert.Contains("user1", usage.UserBreakdown.Keys);
        Assert.Contains("user2", usage.UserBreakdown.Keys);
        Assert.Equal(100, usage.UserBreakdown["user1"].StorageSizeBytes);
        Assert.Equal(200, usage.UserBreakdown["user2"].StorageSizeBytes);
    }

    #endregion

    #region GetGlobalSummary Tests

    [Fact]
    public void GetGlobalSummary_ShouldAggregateAllUsers()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user2", 200, Tier.Long, MemoryType.Semantic);
        _tracker.RecordStore("user3", 300, Tier.Short, MemoryType.Procedural);

        // Act
        var summary = _tracker.GetGlobalSummary();

        // Assert
        Assert.Equal(3, summary.TotalUsers);
        Assert.Equal(3, summary.TotalMemories);
        Assert.Equal(600, summary.TotalStorageBytes);
    }

    [Fact]
    public void GetGlobalSummary_ShouldIncludeTopUsers()
    {
        // Arrange
        _tracker.RecordStore("small-user", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("big-user", 1000, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("big-user", 1000, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("big-user", 1000, Tier.Long, MemoryType.Episodic);

        // Act
        var summary = _tracker.GetGlobalSummary();

        // Assert
        Assert.NotNull(summary.TopUsersByCount);
        Assert.NotNull(summary.TopUsersByStorage);
        Assert.Equal("big-user", summary.TopUsersByCount[0].UserId);
        Assert.Equal("big-user", summary.TopUsersByStorage[0].UserId);
    }

    [Fact]
    public void GetGlobalSummary_ShouldAggregateTierAndType()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user2", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user3", 100, Tier.Short, MemoryType.Semantic);

        // Act
        var summary = _tracker.GetGlobalSummary();

        // Assert
        Assert.Equal(2, summary.ByTier![Tier.Long]);
        Assert.Equal(1, summary.ByTier[Tier.Short]);
        Assert.Equal(2, summary.ByType![MemoryType.Episodic]);
        Assert.Equal(1, summary.ByType[MemoryType.Semantic]);
    }

    #endregion

    #region RefreshFromStoreAsync Tests

    [Fact]
    public async Task RefreshFromStoreAsync_ShouldUpdateFromStore()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = "user1",
                Content = "Test memory 1",
                Tier = Tier.Long,
                Type = MemoryType.Episodic
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = "user1",
                Content = "Test memory 2",
                Tier = Tier.Short,
                Type = MemoryType.Semantic,
                Embedding = new float[1024]
            }
        };

        _mockMemoryStore.GetAllAsync(
                "user1",
                Arg.Any<MemoryFilterOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        await _tracker.RefreshFromStoreAsync("user1");
        var usage = _tracker.GetUsage("user1");

        // Assert
        Assert.Equal(2, usage.MemoryCount);
        Assert.True(usage.StorageSizeBytes > 0);
        Assert.Equal(1, usage.ByTier![Tier.Long]);
        Assert.Equal(1, usage.ByTier[Tier.Short]);
    }

    #endregion

    #region ClearUser Tests

    [Fact]
    public void ClearUser_ShouldRemoveUserData()
    {
        // Arrange
        _tracker.RecordStore("user1", 1000, Tier.Long, MemoryType.Episodic, "tenant1");

        // Act
        _tracker.ClearUser("user1");
        var usage = _tracker.GetUsage("user1");
        var trackedUsers = _tracker.GetTrackedUsers();

        // Assert
        Assert.Equal(0, usage.MemoryCount);
        Assert.DoesNotContain("user1", trackedUsers);
    }

    #endregion

    #region GetTrackedUsers Tests

    [Fact]
    public void GetTrackedUsers_ShouldReturnAllUsers()
    {
        // Arrange
        _tracker.RecordStore("user1", 100, Tier.Long, MemoryType.Episodic);
        _tracker.RecordStore("user2", 100, Tier.Long, MemoryType.Semantic);
        _tracker.RecordStore("user3", 100, Tier.Short, MemoryType.Procedural);

        // Act
        var users = _tracker.GetTrackedUsers();

        // Assert
        Assert.Equal(3, users.Count);
        Assert.Contains("user1", users);
        Assert.Contains("user2", users);
        Assert.Contains("user3", users);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task RecordStore_ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        const int operationsPerThread = 100;
        const int threadCount = 10;

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    _tracker.RecordStore("concurrent-user", 10, Tier.Long, MemoryType.Episodic);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var usage = _tracker.GetUsage("concurrent-user");

        // Assert
        Assert.Equal(operationsPerThread * threadCount, usage.MemoryCount);
        Assert.Equal(operationsPerThread * threadCount * 10, usage.StorageSizeBytes);
    }

    #endregion
}
