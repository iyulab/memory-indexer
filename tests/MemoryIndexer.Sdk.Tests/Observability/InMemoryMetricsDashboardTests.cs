using AwesomeAssertions;
using MemoryIndexer.Sdk.Observability;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Observability;

/// <summary>
/// Tests for InMemoryMetricsDashboard.
/// Phase v0.11.0: Operational Metrics Dashboard.
/// </summary>
public class InMemoryMetricsDashboardTests
{
    private readonly InMemoryMetricsDashboard _dashboard;

    public InMemoryMetricsDashboardTests()
    {
        _dashboard = new InMemoryMetricsDashboard();
    }

    #region Health Summary Tests

    [Fact]
    public async Task GetHealthSummaryAsync_InitialState_ReturnsHealthy()
    {
        // Act
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        summary.Status.Should().Be(HealthStatus.Healthy);
        summary.HealthScore.Should().BeGreaterThanOrEqualTo(0.9f);
        summary.Components.Should().NotBeEmpty();
        summary.Uptime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetHealthSummaryAsync_WithUnhealthyComponent_ReturnsDegraded()
    {
        // Arrange
        _dashboard.UpdateComponentHealth("VectorStore", HealthStatus.Degraded, "High latency");

        // Act
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        summary.Status.Should().Be(HealthStatus.Degraded);
        summary.Components["VectorStore"].Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task GetHealthSummaryAsync_WithCriticalComponent_ReturnsUnhealthy()
    {
        // Arrange
        _dashboard.UpdateComponentHealth("EmbeddingService", HealthStatus.Unhealthy, "Service unavailable");

        // Act
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        summary.Status.Should().Be(HealthStatus.Unhealthy);
        summary.ActiveAlerts.Should().Contain(a => a.Source == "EmbeddingService");
    }

    [Fact]
    public async Task GetHealthSummaryAsync_WithAlert_ReturnsActiveAlerts()
    {
        // Arrange
        _dashboard.AddAlert(AlertSeverity.Warning, "Test Alert", "This is a test", "TestSource");

        // Act
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);

        // Assert
        summary.ActiveAlerts.Should().ContainSingle();
        summary.ActiveAlerts[0].Title.Should().Be("Test Alert");
        summary.ActiveAlerts[0].Severity.Should().Be(AlertSeverity.Warning);
    }

    #endregion

    #region Operation Statistics Tests

    [Fact]
    public async Task GetOperationStatisticsAsync_NoOperations_ReturnsZeros()
    {
        // Act
        var stats = await _dashboard.GetOperationStatisticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        stats.TotalOperations.Should().Be(0);
        stats.SuccessRate.Should().Be(1.0f);
    }

    [Fact]
    public async Task GetOperationStatisticsAsync_AfterOperations_ReturnsCorrectCounts()
    {
        // Arrange
        _dashboard.RecordOperation("Store", success: true, latencyMs: 10);
        _dashboard.RecordOperation("Store", success: true, latencyMs: 15);
        _dashboard.RecordOperation("Recall", success: false, latencyMs: 100);

        // Act
        var stats = await _dashboard.GetOperationStatisticsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        stats.TotalOperations.Should().Be(3);
        stats.SuccessfulOperations.Should().Be(2);
        stats.FailedOperations.Should().Be(1);
        stats.SuccessRate.Should().BeApproximately(2.0f / 3.0f, 0.01f);
    }

    #endregion

    #region Performance Metrics Tests

    [Fact]
    public async Task GetPerformanceMetricsAsync_NoData_ReturnsZeros()
    {
        // Act
        var metrics = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        metrics.StoreLatencyP50Ms.Should().Be(0);
        metrics.RecallLatencyP50Ms.Should().Be(0);
        metrics.EmbeddingCacheHitRate.Should().Be(0);
    }

    [Fact]
    public async Task GetPerformanceMetricsAsync_WithLatencies_CalculatesPercentiles()
    {
        // Arrange - Add multiple store operations with varying latencies
        for (int i = 1; i <= 100; i++)
        {
            _dashboard.RecordOperation("Store", true, i);
        }

        // Act
        var metrics = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        metrics.StoreLatencyP50Ms.Should().BeApproximately(50, 5);
        metrics.StoreLatencyP95Ms.Should().BeApproximately(95, 5);
        metrics.StoreLatencyP99Ms.Should().BeApproximately(99, 2);
    }

    [Fact]
    public async Task GetPerformanceMetricsAsync_WithEmbeddingCache_CalculatesHitRate()
    {
        // Arrange
        _dashboard.RecordEmbedding(10, cacheHit: true);
        _dashboard.RecordEmbedding(100, cacheHit: false);
        _dashboard.RecordEmbedding(5, cacheHit: true);
        _dashboard.RecordEmbedding(80, cacheHit: false);

        // Act
        var metrics = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        metrics.EmbeddingCacheHitRate.Should().Be(0.5f);
    }

    [Fact]
    public async Task GetPerformanceMetricsAsync_WithSimilarityScores_CalculatesAverage()
    {
        // Arrange
        _dashboard.RecordRecall(10, true, 0.9);
        _dashboard.RecordRecall(15, true, 0.8);
        _dashboard.RecordRecall(20, true, 0.7);

        // Act
        var metrics = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        metrics.AvgSimilarityScore.Should().BeApproximately(0.8, 0.01);
    }

    #endregion

    #region Storage Statistics Tests

    [Fact]
    public async Task GetStorageStatisticsAsync_NoData_ReturnsZeros()
    {
        // Act
        var stats = await _dashboard.GetStorageStatisticsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        stats.TotalMemories.Should().Be(0);
        stats.TotalSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task GetStorageStatisticsAsync_AfterStores_ReturnsCorrectStats()
    {
        // Arrange
        _dashboard.RecordStore(10, true, 1000, "Episodic", "user1");
        _dashboard.RecordStore(15, true, 2000, "Episodic", "user1");
        _dashboard.RecordStore(20, true, 1500, "Semantic", "user2");

        // Act
        var stats = await _dashboard.GetStorageStatisticsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        stats.TotalMemories.Should().Be(3);
        stats.TotalSizeBytes.Should().Be(4500);
        stats.AvgMemorySizeBytes.Should().Be(1500);
        stats.MemoriesByType["Episodic"].Should().Be(2);
        stats.MemoriesByType["Semantic"].Should().Be(1);
        stats.MemoriesByTenant["user1"].Should().Be(2);
        stats.MemoriesByTenant["user2"].Should().Be(1);
    }

    [Fact]
    public async Task GetStorageStatisticsAsync_WithTenantFilter_ReturnsFilteredStats()
    {
        // Arrange
        _dashboard.RecordStore(10, true, 1000, "Episodic", "tenant1");
        _dashboard.RecordStore(15, true, 2000, "Episodic", "tenant2");

        // Act
        var stats = await _dashboard.GetStorageStatisticsAsync("tenant1", TestContext.Current.CancellationToken);

        // Assert
        stats.MemoriesByTenant.Should().ContainKey("tenant1");
        stats.MemoriesByTenant.Should().NotContainKey("tenant2");
    }

    #endregion

    #region Security Metrics Tests

    [Fact]
    public async Task GetSecurityMetricsAsync_NoEvents_ReturnsEmpty()
    {
        // Act
        var metrics = await _dashboard.GetSecurityMetricsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        metrics.PiiDetections.Should().Be(0);
        metrics.InjectionAttempts.Should().Be(0);
        metrics.SecurityScore.Should().Be(1.0f);
    }

    [Fact]
    public async Task GetSecurityMetricsAsync_WithEvents_ReturnsCorrectCounts()
    {
        // Arrange
        _dashboard.RecordSecurityEvent("PII", "Email", "Low");
        _dashboard.RecordSecurityEvent("PII", "Phone", "Low");
        _dashboard.RecordSecurityEvent("Injection", "SQLi", "High");

        // Act
        var metrics = await _dashboard.GetSecurityMetricsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        metrics.PiiDetections.Should().Be(2);
        metrics.InjectionAttempts.Should().Be(1);
        metrics.PiiByType["Email"].Should().Be(1);
        metrics.PiiByType["Phone"].Should().Be(1);
        metrics.InjectionsByType["SQLi"].Should().Be(1);
    }

    [Fact]
    public async Task GetSecurityMetricsAsync_WithCriticalEvents_LowersSecurityScore()
    {
        // Arrange
        _dashboard.RecordSecurityEvent("Injection", "SQLi", "Critical");
        _dashboard.RecordSecurityEvent("PII", "Email", "Low");
        _dashboard.RecordSecurityEvent("PII", "Phone", "Low");

        // Act
        var metrics = await _dashboard.GetSecurityMetricsAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        // Assert
        metrics.SecurityScore.Should().BeLessThan(1.0f);
    }

    #endregion

    #region Time Series Tests

    [Fact]
    public async Task GetTimeSeriesAsync_NoData_ReturnsEmpty()
    {
        // Act
        var data = await _dashboard.GetTimeSeriesAsync("test.metric", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        // Assert
        data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimeSeriesAsync_WithData_ReturnsAggregatedPoints()
    {
        // Arrange - Record some operations which will create time series data
        _dashboard.RecordOperation("Store", true, 10);
        _dashboard.RecordOperation("Store", true, 20);
        await Task.Delay(10, TestContext.Current.CancellationToken); // Small delay to ensure separate timestamps

        // Act
        var data = await _dashboard.GetTimeSeriesAsync("operation.Store.latency", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        data.Should().NotBeEmpty();
    }

    #endregion

    #region Component Health Tests

    [Fact]
    public async Task UpdateComponentHealth_ValidComponent_UpdatesStatus()
    {
        // Act
        _dashboard.UpdateComponentHealth("VectorStore", HealthStatus.Degraded, "Slow response", 500);

        // Assert - verify via health summary
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);
        summary.Components["VectorStore"].Status.Should().Be(HealthStatus.Degraded);
        summary.Components["VectorStore"].Message.Should().Be("Slow response");
        summary.Components["VectorStore"].ResponseTimeMs.Should().Be(500);
    }

    [Fact]
    public async Task UpdateComponentHealth_Critical_CreatesAlert()
    {
        // Act
        _dashboard.UpdateComponentHealth("Buffer", HealthStatus.Critical, "Buffer overflow");

        // Assert
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);
        summary.ActiveAlerts.Should().Contain(a => a.Source == "Buffer" && a.Severity == AlertSeverity.Critical);
    }

    #endregion

    #region Active Operations Tests

    [Fact]
    public async Task BeginEndOperation_TracksActiveCount()
    {
        // Arrange
        _dashboard.BeginOperation();
        _dashboard.BeginOperation();

        // Act
        var metrics1 = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        _dashboard.EndOperation();
        var metrics2 = await _dashboard.GetPerformanceMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        metrics1.ActiveOperations.Should().Be(2);
        metrics2.ActiveOperations.Should().Be(1);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task GetOperationStatisticsAsync_FutureTimeRange_ReturnsEmpty()
    {
        // Arrange
        _dashboard.RecordOperation("Store", true, 10);

        // Act
        var stats = await _dashboard.GetOperationStatisticsAsync(DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2), TestContext.Current.CancellationToken);

        // Assert
        stats.OperationsByType.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordStore_FailedOperation_DoesNotIncrementMemoryCount()
    {
        // Act
        _dashboard.RecordStore(10, success: false, sizeBytes: 1000, "Episodic", "user1");

        // Assert
        var stats = await _dashboard.GetStorageStatisticsAsync(cancellationToken: TestContext.Current.CancellationToken);
        stats.TotalMemories.Should().Be(0);
        stats.TotalSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task AddAlert_MultipleAlerts_MaintainsOrder()
    {
        // Arrange & Act
        _dashboard.AddAlert(AlertSeverity.Info, "Alert 1", "First", "Source1");
        _dashboard.AddAlert(AlertSeverity.Warning, "Alert 2", "Second", "Source2");
        _dashboard.AddAlert(AlertSeverity.Error, "Alert 3", "Third", "Source3");

        // Assert
        var summary = await _dashboard.GetHealthSummaryAsync(TestContext.Current.CancellationToken);
        summary.ActiveAlerts.Should().HaveCount(3);
        summary.ActiveAlerts[0].Title.Should().Be("Alert 1");
        summary.ActiveAlerts[2].Title.Should().Be("Alert 3");
    }

    #endregion
}
