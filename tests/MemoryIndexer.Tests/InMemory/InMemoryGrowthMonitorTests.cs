using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.InMemory;

/// <summary>
/// Tests for InMemoryGrowthMonitor.
/// Phase 22.1: Memory Growth Rate Control.
/// </summary>
public sealed class InMemoryGrowthMonitorTests
{
    private readonly InMemoryGrowthMonitor _monitor;

    public InMemoryGrowthMonitorTests()
    {
        var options = Options.Create(new MemoryIndexerOptions
        {
            MemoryGrowth = new MemoryGrowthOptions
            {
                MaxGrowthRatePerRound = 4.0f,
                MinImportanceForStorage = 0.3f
            }
        });

        _monitor = new InMemoryGrowthMonitor(options);
    }

    [Fact]
    public async Task GetGrowthMetricsAsync_ShouldReturnInitialMetrics()
    {
        // Act
        var metrics = await _monitor.GetGrowthMetricsAsync("user1");

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal("user1", metrics.UserId);
        Assert.Equal(1, metrics.CurrentRound);
        Assert.Equal(0, metrics.MemoriesStoredThisRound);
        Assert.Equal(0, metrics.MemoriesFilteredThisRound);
        Assert.Equal(0f, metrics.AverageMemoriesPerRound);
        Assert.Equal(0f, metrics.CurrentGrowthRate);
        Assert.False(metrics.ExceedsThreshold);
        Assert.Equal(4.0f, metrics.MaxAllowedGrowthRate);
    }

    [Fact]
    public async Task RecordMemoryStorageAsync_ShouldTrackStoredMemories()
    {
        // Arrange
        var userId = "user1";

        // Act
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);

        var metrics = await _monitor.GetGrowthMetricsAsync(userId);

        // Assert
        Assert.Equal(3, metrics.MemoriesStoredThisRound);
        Assert.Equal(0, metrics.MemoriesFilteredThisRound);
        Assert.Equal(3f, metrics.CurrentGrowthRate);
        Assert.False(metrics.ExceedsThreshold); // 3 < 4.0
    }

    [Fact]
    public async Task RecordMemoryStorageAsync_ShouldTrackFilteredMemories()
    {
        // Arrange
        var userId = "user1";

        // Act
        await _monitor.RecordMemoryStorageAsync(userId, filtered: true, "Low importance");
        await _monitor.RecordMemoryStorageAsync(userId, filtered: true, "Duplicate topic");
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);

        var metrics = await _monitor.GetGrowthMetricsAsync(userId);

        // Assert
        Assert.Equal(1, metrics.MemoriesStoredThisRound);
        Assert.Equal(2, metrics.MemoriesFilteredThisRound);
        Assert.Equal(2, metrics.FilterReasons.Count);
        Assert.Equal(1, metrics.FilterReasons["Low importance"]);
        Assert.Equal(1, metrics.FilterReasons["Duplicate topic"]);
    }

    [Fact]
    public async Task RecordMemoryStorageAsync_ShouldDetectThresholdExceeded()
    {
        // Arrange
        var userId = "user1";

        // Act - Store 5 memories (exceeds 4.0 threshold)
        for (int i = 0; i < 5; i++)
        {
            await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        }

        var metrics = await _monitor.GetGrowthMetricsAsync(userId);

        // Assert
        Assert.Equal(5, metrics.MemoriesStoredThisRound);
        Assert.Equal(5f, metrics.CurrentGrowthRate);
        Assert.True(metrics.ExceedsThreshold); // 5 > 4.0
    }

    [Fact]
    public async Task EndRoundAsync_ShouldResetCurrentRound()
    {
        // Arrange
        var userId = "user1";

        // Act - Round 1
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: true, "Low importance");

        var metricsRound1 = await _monitor.GetGrowthMetricsAsync(userId);

        // End round 1
        await _monitor.EndRoundAsync(userId);

        var metricsRound2 = await _monitor.GetGrowthMetricsAsync(userId);

        // Assert
        Assert.Equal(1, metricsRound1.CurrentRound);
        Assert.Equal(2, metricsRound1.MemoriesStoredThisRound);
        Assert.Equal(1, metricsRound1.MemoriesFilteredThisRound);

        Assert.Equal(2, metricsRound2.CurrentRound);
        Assert.Equal(0, metricsRound2.MemoriesStoredThisRound);
        Assert.Equal(0, metricsRound2.MemoriesFilteredThisRound);
        Assert.Empty(metricsRound2.FilterReasons);
    }

    [Fact]
    public async Task EndRoundAsync_ShouldCalculateAverageMemoriesPerRound()
    {
        // Arrange
        var userId = "user1";

        // Act - Round 1: 3 memories
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.EndRoundAsync(userId);

        // Round 2: 5 memories
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.RecordMemoryStorageAsync(userId, filtered: false);
        await _monitor.EndRoundAsync(userId);

        var metrics = await _monitor.GetGrowthMetricsAsync(userId);

        // Assert
        Assert.Equal(3, metrics.CurrentRound);
        Assert.Equal(4.0f, metrics.AverageMemoriesPerRound); // (3 + 5) / 2 = 4.0
    }

    [Fact]
    public async Task MultipleUsers_ShouldTrackIndependently()
    {
        // Arrange
        var user1 = "user1";
        var user2 = "user2";

        // Act
        await _monitor.RecordMemoryStorageAsync(user1, filtered: false);
        await _monitor.RecordMemoryStorageAsync(user1, filtered: false);

        await _monitor.RecordMemoryStorageAsync(user2, filtered: false);
        await _monitor.RecordMemoryStorageAsync(user2, filtered: false);
        await _monitor.RecordMemoryStorageAsync(user2, filtered: false);
        await _monitor.RecordMemoryStorageAsync(user2, filtered: false);

        var metrics1 = await _monitor.GetGrowthMetricsAsync(user1);
        var metrics2 = await _monitor.GetGrowthMetricsAsync(user2);

        // Assert
        Assert.Equal(2, metrics1.MemoriesStoredThisRound);
        Assert.Equal(4, metrics2.MemoriesStoredThisRound);
    }
}
