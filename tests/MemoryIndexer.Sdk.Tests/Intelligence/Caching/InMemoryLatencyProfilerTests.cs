using MemoryIndexer.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MemoryIndexer.Configuration;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Caching;

public class InMemoryLatencyProfilerTests
{
    private readonly InMemoryLatencyProfiler _profiler;
    private readonly MemoryIndexerOptions _options;

    public InMemoryLatencyProfilerTests()
    {
        _options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                ProfilingEnabled = true,
                WorkingMemoryBudgetMs = 100.0,
                SessionMemoryBudgetMs = 300.0,
                UserProfileBudgetMs = 500.0
            }
        };

        _profiler = new InMemoryLatencyProfiler(Options.Create(_options));
    }

    [Fact]
    public async Task RecordLatencyAsync_ShouldRecordSingleMeasurement()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Working";
        const double latencyMs = 75.5;

        // Act
        await _profiler.RecordLatencyAsync(userId, tier, latencyMs, cancellationToken: TestContext.Current.CancellationToken);
        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(metrics);
        var metric = metrics[0];
        Assert.Equal(userId, metric.UserId);
        Assert.Equal(tier, metric.Tier);
        Assert.Equal(1, metric.TotalQueries);
        Assert.Equal(latencyMs, metric.AverageLatencyMs);
        Assert.Equal(latencyMs, metric.MinLatencyMs);
        Assert.Equal(latencyMs, metric.MaxLatencyMs);
        Assert.Equal(latencyMs, metric.P50LatencyMs);
        Assert.Equal(latencyMs, metric.P95LatencyMs);
        Assert.Equal(latencyMs, metric.P99LatencyMs);
    }

    [Fact]
    public async Task RecordLatencyAsync_ShouldTrackMultipleMeasurements()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Session";
        var latencies = new[] { 100.0, 200.0, 300.0, 400.0, 500.0 };

        // Act
        foreach (var latency in latencies)
        {
            await _profiler.RecordLatencyAsync(userId, tier, latency, cancellationToken: TestContext.Current.CancellationToken);
        }
        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(metrics);
        var metric = metrics[0];
        Assert.Equal(5, metric.TotalQueries);
        Assert.Equal(300.0, metric.AverageLatencyMs);
        Assert.Equal(100.0, metric.MinLatencyMs);
        Assert.Equal(500.0, metric.MaxLatencyMs);
        Assert.Equal(300.0, metric.P50LatencyMs); // Median
    }

    [Fact]
    public async Task RecordLatencyAsync_ShouldCalculatePercentilesCorrectly()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "User";

        // Create 100 measurements from 1 to 100
        for (int i = 1; i <= 100; i++)
        {
            await _profiler.RecordLatencyAsync(userId, tier, i, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act
        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        var metric = metrics[0];
        Assert.Equal(100, metric.TotalQueries);
        Assert.Equal(50.5, metric.AverageLatencyMs);
        Assert.Equal(50.0, metric.P50LatencyMs, 1.0); // Allow 1ms tolerance
        Assert.InRange(metric.P95LatencyMs, 94.0, 96.0);
        Assert.InRange(metric.P99LatencyMs, 98.0, 100.0);
    }

    [Fact]
    public async Task RecordLatencyAsync_WithComponentLatencies_ShouldTrackComponents()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Working";
        var componentLatencies = new Dictionary<string, double>
        {
            ["Embedding"] = 50.0,
            ["Search"] = 30.0,
            ["Scoring"] = 20.0
        };

        // Act
        await _profiler.RecordLatencyAsync(userId, tier, 100.0, componentLatencies, TestContext.Current.CancellationToken);
        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        var metric = metrics[0];
        Assert.Equal(3, metric.ComponentLatencies.Count);
        Assert.Equal(50.0, metric.ComponentLatencies["Embedding"]);
        Assert.Equal(30.0, metric.ComponentLatencies["Search"]);
        Assert.Equal(20.0, metric.ComponentLatencies["Scoring"]);
    }

    [Fact]
    public async Task RecordLatencyAsync_ShouldTrackBudgetExceeded()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Working";
        const double overBudgetLatency = 150.0; // Budget is 100ms

        // Act
        await _profiler.RecordLatencyAsync(userId, tier, overBudgetLatency, cancellationToken: TestContext.Current.CancellationToken);
        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        var metric = metrics[0];
        Assert.Equal(1, metric.BudgetExceededCount);
    }

    [Fact]
    public async Task RecordLatencyAsync_ShouldTrackBudgetForDifferentTiers()
    {
        // Arrange
        const string userId = "user1";

        // Act & Assert - Short-Term Memory budget is 100ms
        await _profiler.RecordLatencyAsync(userId, "Working", 120.0, cancellationToken: TestContext.Current.CancellationToken);
        var workingMetrics = await _profiler.GetMetricsAsync(userId, "Working", TestContext.Current.CancellationToken);
        Assert.Equal(1, workingMetrics[0].BudgetExceededCount);

        // Act & Assert - Session Memory budget is 300ms
        await _profiler.RecordLatencyAsync(userId, "Session", 350.0, cancellationToken: TestContext.Current.CancellationToken);
        var sessionMetrics = await _profiler.GetMetricsAsync(userId, "Session", TestContext.Current.CancellationToken);
        Assert.Equal(1, sessionMetrics[0].BudgetExceededCount);

        // Act & Assert - User Profile budget is 500ms
        await _profiler.RecordLatencyAsync(userId, "User", 600.0, cancellationToken: TestContext.Current.CancellationToken);
        var userMetrics = await _profiler.GetMetricsAsync(userId, "User", TestContext.Current.CancellationToken);
        Assert.Equal(1, userMetrics[0].BudgetExceededCount);
    }

    [Fact]
    public async Task RecordCacheAccessAsync_ShouldTrackCacheHits()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Working";

        // Record some latencies first
        await _profiler.RecordLatencyAsync(userId, tier, 100.0, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _profiler.RecordCacheAccessAsync(userId, "Embedding", hit: true, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordCacheAccessAsync(userId, "Embedding", hit: true, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordCacheAccessAsync(userId, "Embedding", hit: false, cancellationToken: TestContext.Current.CancellationToken);

        var metrics = await _profiler.GetMetricsAsync(userId, tier, TestContext.Current.CancellationToken);

        // Assert
        var metric = metrics[0];
        Assert.Equal(2, metric.EmbeddingCacheHits);
        Assert.Equal(1, metric.TotalQueries); // Only latency records count as queries
        Assert.Equal(2.0 / 3.0, metric.CacheHitRate, 0.01); // 2 hits out of 3 cache accesses
    }

    [Fact]
    public async Task GetMetricsAsync_WithoutTierFilter_ShouldReturnAllTiers()
    {
        // Arrange
        const string userId = "user1";
        await _profiler.RecordLatencyAsync(userId, "Working", 50.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(userId, "Session", 200.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(userId, "User", 400.0, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var metrics = await _profiler.GetMetricsAsync(userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, metrics.Count);
        Assert.Contains(metrics, m => m.Tier == "Working");
        Assert.Contains(metrics, m => m.Tier == "Session");
        Assert.Contains(metrics, m => m.Tier == "User");
    }

    [Fact]
    public async Task GetMetricsAsync_WithTierFilter_ShouldReturnOnlyMatchingTier()
    {
        // Arrange
        const string userId = "user1";
        await _profiler.RecordLatencyAsync(userId, "Working", 50.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(userId, "Session", 200.0, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var metrics = await _profiler.GetMetricsAsync(userId, "Working", TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(metrics);
        Assert.Equal("Working", metrics[0].Tier);
    }

    [Fact]
    public async Task GetMetricsAsync_ForNonExistentUser_ShouldReturnEmptyList()
    {
        // Act
        var metrics = await _profiler.GetMetricsAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(metrics);
    }

    [Fact]
    public async Task ResetMetricsAsync_ShouldClearUserMetrics()
    {
        // Arrange
        const string userId = "user1";
        await _profiler.RecordLatencyAsync(userId, "Working", 50.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(userId, "Session", 200.0, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _profiler.ResetMetricsAsync(userId, TestContext.Current.CancellationToken);
        var metrics = await _profiler.GetMetricsAsync(userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(metrics);
    }

    [Fact]
    public async Task ResetMetricsAsync_ShouldClearAllTiersForUser()
    {
        // Arrange
        const string userId = "user1";
        const string otherUser = "user2";
        await _profiler.RecordLatencyAsync(userId, "Working", 50.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(userId, "Session", 200.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(otherUser, "Working", 100.0, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _profiler.ResetMetricsAsync(userId, TestContext.Current.CancellationToken);
        var userMetrics = await _profiler.GetMetricsAsync(userId, cancellationToken: TestContext.Current.CancellationToken);
        var otherMetrics = await _profiler.GetMetricsAsync(otherUser, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(userMetrics);
        Assert.Single(otherMetrics); // Other user metrics unchanged
    }

    [Fact]
    public void GetLatencyBudget_ShouldReturnCorrectBudgets()
    {
        // Act & Assert
        Assert.Equal(100.0, _profiler.GetLatencyBudget("Working"));
        Assert.Equal(300.0, _profiler.GetLatencyBudget("Session"));
        Assert.Equal(500.0, _profiler.GetLatencyBudget("User"));
        Assert.Equal(500.0, _profiler.GetLatencyBudget("Unknown")); // Default to max
    }

    [Fact]
    public async Task RecordLatencyAsync_MultipleUsers_ShouldKeepSeparateState()
    {
        // Arrange
        const string user1 = "user1";
        const string user2 = "user2";
        const string tier = "Working";

        // Act
        await _profiler.RecordLatencyAsync(user1, tier, 50.0, cancellationToken: TestContext.Current.CancellationToken);
        await _profiler.RecordLatencyAsync(user2, tier, 150.0, cancellationToken: TestContext.Current.CancellationToken);

        var user1Metrics = await _profiler.GetMetricsAsync(user1, tier, TestContext.Current.CancellationToken);
        var user2Metrics = await _profiler.GetMetricsAsync(user2, tier, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(user1Metrics);
        Assert.Single(user2Metrics);
        Assert.Equal(50.0, user1Metrics[0].AverageLatencyMs);
        Assert.Equal(150.0, user2Metrics[0].AverageLatencyMs);
    }
}
