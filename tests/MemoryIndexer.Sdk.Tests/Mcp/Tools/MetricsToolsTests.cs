using FluentAssertions;
using MemoryIndexer.Sdk.Mcp.Tools;
using MemoryIndexer.Sdk.Observability;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Tests for MetricsTools MCP integration.
/// Phase v0.11.0: Operational Metrics Dashboard.
/// </summary>
public class MetricsToolsTests
{
    private readonly Mock<IMetricsDashboard> _mockDashboard;
    private readonly MetricsTools _tools;

    public MetricsToolsTests()
    {
        _mockDashboard = new Mock<IMetricsDashboard>();
        _tools = new MetricsTools(_mockDashboard.Object);
    }

    #region GetHealthSummary Tests

    [Fact]
    public async Task GetHealthSummary_ReturnsHealthyStatus()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetHealthSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthSummary
            {
                Status = HealthStatus.Healthy,
                HealthScore = 1.0f,
                Components = new Dictionary<string, ComponentHealth>
                {
                    ["VectorStore"] = new() { Name = "VectorStore", Status = HealthStatus.Healthy }
                },
                ActiveAlerts = [],
                Uptime = TimeSpan.FromHours(5)
            });

        // Act
        var result = await _tools.GetHealthSummary();

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be("Healthy");
        result.HealthScore.Should().Be(1.0f);
        result.ComponentCount.Should().Be(1);
        result.AlertCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHealthSummary_WithAlerts_IncludesAlertInfo()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetHealthSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthSummary
            {
                Status = HealthStatus.Degraded,
                HealthScore = 0.8f,
                Components = new Dictionary<string, ComponentHealth>(),
                ActiveAlerts =
                [
                    new() { Severity = AlertSeverity.Warning, Title = "High Latency", Message = "P95 > 500ms", Source = "LatencyMonitor" }
                ],
                Uptime = TimeSpan.FromHours(10)
            });

        // Act
        var result = await _tools.GetHealthSummary();

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be("Degraded");
        result.AlertCount.Should().Be(1);
        result.Alerts![0].Title.Should().Be("High Latency");
    }

    [Fact]
    public async Task GetHealthSummary_OnException_ReturnsFailure()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetHealthSummaryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dashboard unavailable"));

        // Act
        var result = await _tools.GetHealthSummary();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Failed to get health summary");
    }

    #endregion

    #region GetOperationStats Tests

    [Fact]
    public async Task GetOperationStats_ReturnsCorrectStats()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetOperationStatisticsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationStatistics
            {
                TotalOperations = 1000,
                SuccessfulOperations = 950,
                FailedOperations = 50,
                SuccessRate = 0.95f,
                OperationsByType = new Dictionary<string, long> { ["Store"] = 500, ["Recall"] = 500 },
                OperationsPerSecond = 0.28,
                PeakOperationsPerSecond = 10.5,
                Period = TimeSpan.FromHours(1)
            });

        // Act
        var result = await _tools.GetOperationStats(hours: 1);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalOperations.Should().Be(1000);
        result.SuccessRate.Should().Be(0.95f);
        result.SuccessRatePercent.Should().Be("95.0%");
        result.OperationsByType.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOperationStats_WithCustomHours_UsesCorrectPeriod()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetOperationStatisticsAsync(
                It.Is<DateTimeOffset>(t => t < DateTimeOffset.UtcNow.AddHours(-11)),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationStatistics());

        // Act
        var result = await _tools.GetOperationStats(hours: 12);

        // Assert
        result.Success.Should().BeTrue();
        result.PeriodHours.Should().Be(12);
    }

    #endregion

    #region GetPerformanceMetrics Tests

    [Fact]
    public async Task GetPerformanceMetrics_ReturnsAllLatencies()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetPerformanceMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceMetrics
            {
                StoreLatencyP50Ms = 10,
                StoreLatencyP95Ms = 50,
                StoreLatencyP99Ms = 100,
                RecallLatencyP50Ms = 20,
                RecallLatencyP95Ms = 80,
                RecallLatencyP99Ms = 150,
                EmbeddingLatencyP95Ms = 200,
                EmbeddingCacheHitRate = 0.75f,
                ActiveOperations = 5,
                AvgSimilarityScore = 0.85
            });

        // Act
        var result = await _tools.GetPerformanceMetrics();

        // Assert
        result.Success.Should().BeTrue();
        result.StoreLatencyP50Ms.Should().Be(10);
        result.RecallLatencyP95Ms.Should().Be(80);
        result.EmbeddingCacheHitRate.Should().Be(0.75f);
        result.EmbeddingCacheHitRatePercent.Should().Be("75.0%");
        result.AvgSimilarityScore.Should().Be(0.85);
    }

    #endregion

    #region GetStorageStats Tests

    [Fact]
    public async Task GetStorageStats_ReturnsCorrectStats()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetStorageStatisticsAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageStatistics
            {
                TotalMemories = 10000,
                TotalSizeBytes = 1024 * 1024 * 50, // 50 MB
                AvgMemorySizeBytes = 5120,
                MemoriesByType = new Dictionary<string, long> { ["Episodic"] = 5000, ["Semantic"] = 5000 },
                MemoriesByTenant = new Dictionary<string, long> { ["user1"] = 10000 },
                GrowthRateBytesPerDay = 1024 * 1024,
                IndexStats = new IndexStatistics
                {
                    VectorCount = 10000,
                    IndexSizeBytes = 1024 * 1024 * 40,
                    BuildStatus = "Complete"
                }
            });

        // Act
        var result = await _tools.GetStorageStats();

        // Assert
        result.Success.Should().BeTrue();
        result.TotalMemories.Should().Be(10000);
        result.TotalSizeFormatted.Should().Be("50.0 MB");
        result.IndexStats.Should().NotBeNull();
        result.IndexStats!.VectorCount.Should().Be(10000);
    }

    [Fact]
    public async Task GetStorageStats_WithTenantFilter_PassesFilter()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetStorageStatisticsAsync(
                "tenant123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageStatistics());

        // Act
        var result = await _tools.GetStorageStats(tenantId: "tenant123");

        // Assert
        result.Success.Should().BeTrue();
        result.TenantFilter.Should().Be("tenant123");
    }

    #endregion

    #region GetSecurityMetrics Tests

    [Fact]
    public async Task GetSecurityMetrics_ReturnsSecurityInfo()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetSecurityMetricsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMetrics
            {
                PiiDetections = 10,
                PiiByType = new Dictionary<string, long> { ["Email"] = 5, ["Phone"] = 5 },
                InjectionAttempts = 2,
                InjectionsByType = new Dictionary<string, long> { ["SQLi"] = 2 },
                RateLimitViolations = 5,
                SecurityScore = 0.85f
            });

        // Act
        var result = await _tools.GetSecurityMetrics(hours: 24);

        // Assert
        result.Success.Should().BeTrue();
        result.PiiDetections.Should().Be(10);
        result.InjectionAttempts.Should().Be(2);
        result.SecurityScore.Should().Be(0.85f);
        result.SecurityScorePercent.Should().Be("85%");
    }

    #endregion

    #region GetMetricTimeSeries Tests

    [Fact]
    public async Task GetMetricTimeSeries_ReturnsDataPoints()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _mockDashboard.Setup(d => d.GetTimeSeriesAsync(
                "operation.Store.latency",
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TimeSeriesDataPoint>
            {
                new() { Timestamp = now.AddMinutes(-10), Value = 50 },
                new() { Timestamp = now.AddMinutes(-5), Value = 55 },
                new() { Timestamp = now, Value = 45 }
            });

        // Act
        var result = await _tools.GetMetricTimeSeries(
            metricName: "operation.Store.latency",
            hours: 1,
            intervalMinutes: 5);

        // Assert
        result.Success.Should().BeTrue();
        result.MetricName.Should().Be("operation.Store.latency");
        result.DataPointCount.Should().Be(3);
        result.Summary.Should().NotBeNull();
        result.Summary!.Min.Should().Be(45);
        result.Summary.Max.Should().Be(55);
        result.Summary.Avg.Should().Be(50);
    }

    [Fact]
    public async Task GetMetricTimeSeries_NoData_ReturnsEmptyWithMessage()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetTimeSeriesAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TimeSeriesDataPoint>());

        // Act
        var result = await _tools.GetMetricTimeSeries("unknown.metric", 1, 5);

        // Assert
        result.Success.Should().BeTrue();
        result.DataPointCount.Should().Be(0);
        result.Summary.Should().BeNull();
        result.Message.Should().Contain("No data available");
    }

    #endregion

    #region GetDashboardOverview Tests

    [Fact]
    public async Task GetDashboardOverview_CombinesAllMetrics()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetHealthSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthSummary
            {
                Status = HealthStatus.Healthy,
                HealthScore = 0.95f,
                Components = new Dictionary<string, ComponentHealth>(),
                ActiveAlerts = [],
                Uptime = TimeSpan.FromHours(24)
            });

        _mockDashboard.Setup(d => d.GetOperationStatisticsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationStatistics
            {
                TotalOperations = 5000,
                SuccessRate = 0.99f,
                OperationsPerSecond = 1.4
            });

        _mockDashboard.Setup(d => d.GetPerformanceMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceMetrics
            {
                RecallLatencyP95Ms = 100,
                EmbeddingCacheHitRate = 0.80f,
                AvgSimilarityScore = 0.85
            });

        _mockDashboard.Setup(d => d.GetStorageStatisticsAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageStatistics
            {
                TotalMemories = 50000,
                TotalSizeBytes = 1024 * 1024 * 100,
                GrowthRateBytesPerDay = 1024 * 1024 * 5
            });

        _mockDashboard.Setup(d => d.GetSecurityMetricsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMetrics
            {
                SecurityScore = 0.95f,
                PiiDetections = 5,
                InjectionAttempts = 0
            });

        // Act
        var result = await _tools.GetDashboardOverview();

        // Assert
        result.Success.Should().BeTrue();
        result.Health.Should().NotBeNull();
        result.Health!.Status.Should().Be("Healthy");
        result.Health.Score.Should().Be(0.95f);

        result.Operations.Should().NotBeNull();
        result.Operations!.TotalLastHour.Should().Be(5000);
        result.Operations.SuccessRate.Should().Be(0.99f);

        result.Performance.Should().NotBeNull();
        result.Performance!.RecallP95Ms.Should().Be(100);
        result.Performance.CacheHitRate.Should().Be(0.80f);

        result.Storage.Should().NotBeNull();
        result.Storage!.TotalMemories.Should().Be(50000);
        result.Storage.TotalSize.Should().Be("100.0 MB");

        result.Security.Should().NotBeNull();
        result.Security!.Score.Should().Be(0.95f);
    }

    [Fact]
    public async Task GetDashboardOverview_OnException_ReturnsFailure()
    {
        // Arrange
        _mockDashboard.Setup(d => d.GetHealthSummaryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        // Act
        var result = await _tools.GetDashboardOverview();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Failed to get dashboard overview");
    }

    #endregion

    #region Integration Test with InMemoryMetricsDashboard

    [Fact]
    public async Task MetricsTools_WithInMemoryDashboard_WorksEndToEnd()
    {
        // Arrange
        var dashboard = new InMemoryMetricsDashboard();
        var tools = new MetricsTools(dashboard);

        // Record some data
        dashboard.RecordStore(10, true, 1000, "Episodic", "user1");
        dashboard.RecordStore(15, true, 2000, "Semantic", "user1");
        dashboard.RecordRecall(50, true, 0.9);

        // Act
        var health = await tools.GetHealthSummary();
        var operations = await tools.GetOperationStats();
        var performance = await tools.GetPerformanceMetrics();
        var storage = await tools.GetStorageStats();
        var overview = await tools.GetDashboardOverview();

        // Assert
        health.Success.Should().BeTrue();
        health.Status.Should().Be("Healthy");

        operations.Success.Should().BeTrue();
        operations.TotalOperations.Should().Be(3);

        performance.Success.Should().BeTrue();
        performance.AvgSimilarityScore.Should().Be(0.9);

        storage.Success.Should().BeTrue();
        storage.TotalMemories.Should().Be(2);
        storage.TotalSizeBytes.Should().Be(3000);

        overview.Success.Should().BeTrue();
        overview.Health!.Status.Should().Be("Healthy");
    }

    #endregion
}
