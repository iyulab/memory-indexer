using FluentAssertions;
using McpServer.Controllers;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Controllers;

/// <summary>
/// Unit tests for MetricsController REST API.
/// </summary>
public class MetricsControllerTests
{
    private readonly Mock<IMemoryPressureMonitor> _mockPressureMonitor;
    private readonly Mock<ILatencyProfiler> _mockLatencyProfiler;
    private readonly Mock<IMemoryGrowthMonitor> _mockGrowthMonitor;
    private readonly Mock<ILogger<MetricsController>> _mockLogger;
    private readonly MetricsController _controller;

    public MetricsControllerTests()
    {
        _mockPressureMonitor = new Mock<IMemoryPressureMonitor>();
        _mockLatencyProfiler = new Mock<ILatencyProfiler>();
        _mockGrowthMonitor = new Mock<IMemoryGrowthMonitor>();
        _mockLogger = new Mock<ILogger<MetricsController>>();

        _controller = new MetricsController(
            _mockPressureMonitor.Object,
            _mockLatencyProfiler.Object,
            _mockGrowthMonitor.Object,
            _mockLogger.Object);
    }

    #region GetMemoryPressure Tests

    [Fact]
    public void GetMemoryPressure_ReturnsMemoryPressureInfo()
    {
        // Arrange
        var pressureInfo = new MemoryPressureInfo
        {
            Level = MemoryPressureLevel.Low,
            TotalAvailableMemoryBytes = 16L * 1024 * 1024 * 1024,
            MemoryLoadBytes = 4L * 1024 * 1024 * 1024,
            HeapSizeBytes = 100 * 1024 * 1024,
            UtilizationPercentage = 0.25f,
            Gen0Collections = 10,
            Gen1Collections = 5,
            Gen2Collections = 1
        };

        _mockPressureMonitor.Setup(m => m.GetMemoryInfo()).Returns(pressureInfo);
        _mockPressureMonitor.Setup(m => m.IsUnderPressure()).Returns(false);

        // Act
        var result = _controller.GetMemoryPressure();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<MemoryPressureResponse>().Subject;
        response.Level.Should().Be("Low");
        response.IsUnderPressure.Should().BeFalse();
        response.Gen0Collections.Should().Be(10);
    }

    [Fact]
    public void GetMemoryPressure_WhenUnderPressure_ReturnsCorrectStatus()
    {
        // Arrange
        var pressureInfo = new MemoryPressureInfo
        {
            Level = MemoryPressureLevel.High,
            TotalAvailableMemoryBytes = 16L * 1024 * 1024 * 1024,
            MemoryLoadBytes = 14L * 1024 * 1024 * 1024,
            HeapSizeBytes = 2L * 1024 * 1024 * 1024,
            UtilizationPercentage = 0.875f
        };

        _mockPressureMonitor.Setup(m => m.GetMemoryInfo()).Returns(pressureInfo);
        _mockPressureMonitor.Setup(m => m.IsUnderPressure()).Returns(true);

        // Act
        var result = _controller.GetMemoryPressure();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<MemoryPressureResponse>().Subject;
        response.Level.Should().Be("High");
        response.IsUnderPressure.Should().BeTrue();
    }

    #endregion

    #region GetLatencyMetrics Tests

    [Fact]
    public async Task GetLatencyMetrics_WithDefaultUser_ReturnsMetrics()
    {
        // Arrange
        var metrics = new List<LatencyMetrics>
        {
            new()
            {
                UserId = "default",
                Tier = "Session",
                TotalQueries = 100,
                AverageLatencyMs = 50,
                P50LatencyMs = 45,
                P95LatencyMs = 80,
                P99LatencyMs = 120,
                CacheHitRate = 0.75,
                LastUpdated = DateTime.UtcNow
            }
        };

        _mockLatencyProfiler.Setup(l => l.GetMetricsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        // Act
        var result = await _controller.GetLatencyMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<LatencyMetricsResponse>().Subject;
        response.UserId.Should().Be("default");
        response.MetricsCount.Should().Be(1);
        response.Metrics.Should().HaveCount(1);
        response.Metrics![0].Tier.Should().Be("Session");
    }

    [Fact]
    public async Task GetLatencyMetrics_WithTierFilter_FiltersCorrectly()
    {
        // Arrange
        var metrics = new List<LatencyMetrics>
        {
            new() { UserId = "default", Tier = "Session", TotalQueries = 50 }
        };

        _mockLatencyProfiler.Setup(l => l.GetMetricsAsync("default", "Session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        // Act
        var result = await _controller.GetLatencyMetrics(tier: "Session", cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<LatencyMetricsResponse>().Subject;
        response.Tier.Should().Be("Session");
    }

    #endregion

    #region GetGrowthMetrics Tests

    [Fact]
    public async Task GetGrowthMetrics_ReturnsGrowthInfo()
    {
        // Arrange
        var growthMetrics = new MemoryGrowthMetrics
        {
            UserId = "default",
            CurrentRound = 5,
            MemoriesStoredThisRound = 10,
            MemoriesFilteredThisRound = 2,
            AverageMemoriesPerRound = 8.5f,
            CurrentGrowthRate = 1.2f,
            MaxAllowedGrowthRate = 2.0f,
            ExceedsThreshold = false,
            Timestamp = DateTime.UtcNow
        };

        _mockGrowthMonitor.Setup(g => g.GetGrowthMetricsAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growthMetrics);

        // Act
        var result = await _controller.GetGrowthMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<MemoryGrowthResponse>().Subject;
        response.CurrentRound.Should().Be(5);
        response.ExceedsThreshold.Should().BeFalse();
        response.CurrentGrowthRate.Should().Be(1.2f);
    }

    [Fact]
    public async Task GetGrowthMetrics_WhenExceedsThreshold_ReturnsWarning()
    {
        // Arrange
        var growthMetrics = new MemoryGrowthMetrics
        {
            UserId = "default",
            CurrentGrowthRate = 3.0f,
            MaxAllowedGrowthRate = 2.0f,
            ExceedsThreshold = true,
            Timestamp = DateTime.UtcNow
        };

        _mockGrowthMonitor.Setup(g => g.GetGrowthMetricsAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growthMetrics);

        // Act
        var result = await _controller.GetGrowthMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<MemoryGrowthResponse>().Subject;
        response.ExceedsThreshold.Should().BeTrue();
    }

    #endregion

    #region GetDashboardMetrics Tests

    [Fact]
    public async Task GetDashboardMetrics_ReturnsAggregatedMetrics()
    {
        // Arrange
        var pressureInfo = new MemoryPressureInfo
        {
            Level = MemoryPressureLevel.Low,
            UtilizationPercentage = 0.3f,
            HeapSizeBytes = 100 * 1024 * 1024
        };

        var latencyMetrics = new List<LatencyMetrics>
        {
            new() { UserId = "default", Tier = "Session", AverageLatencyMs = 50, TotalQueries = 100, CacheHitRate = 0.8 }
        };

        var growthMetrics = new MemoryGrowthMetrics
        {
            UserId = "default",
            CurrentRound = 3,
            CurrentGrowthRate = 1.0f,
            MaxAllowedGrowthRate = 2.0f,
            ExceedsThreshold = false,
            Timestamp = DateTime.UtcNow
        };

        _mockPressureMonitor.Setup(m => m.GetMemoryInfo()).Returns(pressureInfo);
        _mockPressureMonitor.Setup(m => m.IsUnderPressure()).Returns(false);
        _mockLatencyProfiler.Setup(l => l.GetMetricsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latencyMetrics);
        _mockGrowthMonitor.Setup(g => g.GetGrowthMetricsAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growthMetrics);

        // Act
        var result = await _controller.GetDashboardMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DashboardResponse>().Subject;

        response.MemoryPressure.Should().NotBeNull();
        response.MemoryPressure!.Level.Should().Be("Low");

        response.Latency.Should().NotBeNull();
        response.Latency!.TotalQueries.Should().Be(100);

        response.Growth.Should().NotBeNull();
        response.Growth!.CurrentRound.Should().Be(3);

        response.Health.Should().NotBeNull();
        response.Health!.OverallStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetDashboardMetrics_WithHighLatency_ReturnsDegradedStatus()
    {
        // Arrange
        var pressureInfo = new MemoryPressureInfo
        {
            Level = MemoryPressureLevel.Low,
            UtilizationPercentage = 0.3f
        };

        var latencyMetrics = new List<LatencyMetrics>
        {
            new() { UserId = "default", Tier = "Session", AverageLatencyMs = 600, TotalQueries = 100, CacheHitRate = 0.5 }
        };

        var growthMetrics = new MemoryGrowthMetrics
        {
            UserId = "default",
            CurrentGrowthRate = 1.0f,
            MaxAllowedGrowthRate = 2.0f,
            ExceedsThreshold = false,
            Timestamp = DateTime.UtcNow
        };

        _mockPressureMonitor.Setup(m => m.GetMemoryInfo()).Returns(pressureInfo);
        _mockPressureMonitor.Setup(m => m.IsUnderPressure()).Returns(false);
        _mockLatencyProfiler.Setup(l => l.GetMetricsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latencyMetrics);
        _mockGrowthMonitor.Setup(g => g.GetGrowthMetricsAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growthMetrics);

        // Act
        var result = await _controller.GetDashboardMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DashboardResponse>().Subject;
        response.Health!.OverallStatus.Should().Be("Degraded");
    }

    [Fact]
    public async Task GetDashboardMetrics_WithCriticalPressure_ReturnsCriticalStatus()
    {
        // Arrange
        var pressureInfo = new MemoryPressureInfo
        {
            Level = MemoryPressureLevel.Critical,
            UtilizationPercentage = 0.95f
        };

        var latencyMetrics = new List<LatencyMetrics>();

        var growthMetrics = new MemoryGrowthMetrics
        {
            UserId = "default",
            CurrentGrowthRate = 1.0f,
            MaxAllowedGrowthRate = 2.0f,
            ExceedsThreshold = false,
            Timestamp = DateTime.UtcNow
        };

        _mockPressureMonitor.Setup(m => m.GetMemoryInfo()).Returns(pressureInfo);
        _mockPressureMonitor.Setup(m => m.IsUnderPressure()).Returns(true);
        _mockLatencyProfiler.Setup(l => l.GetMetricsAsync("default", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latencyMetrics);
        _mockGrowthMonitor.Setup(g => g.GetGrowthMetricsAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growthMetrics);

        // Act
        var result = await _controller.GetDashboardMetrics(cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<DashboardResponse>().Subject;
        response.Health!.OverallStatus.Should().Be("Critical");
    }

    #endregion

    #region ResetLatencyMetrics Tests

    [Fact]
    public async Task ResetLatencyMetrics_WithDefaultUser_ResetsSuccessfully()
    {
        // Arrange
        _mockLatencyProfiler.Setup(l => l.ResetMetricsAsync("default", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ResetLatencyMetrics(cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockLatencyProfiler.Verify(l => l.ResetMetricsAsync("default", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetLatencyMetrics_WithCustomUser_ResetsForCorrectUser()
    {
        // Arrange
        _mockLatencyProfiler.Setup(l => l.ResetMetricsAsync("custom-user", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ResetLatencyMetrics("custom-user", CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _mockLatencyProfiler.Verify(l => l.ResetMetricsAsync("custom-user", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
