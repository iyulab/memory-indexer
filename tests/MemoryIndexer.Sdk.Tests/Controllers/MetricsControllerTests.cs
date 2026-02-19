using FluentAssertions;
using McpServer.Controllers;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Controllers;

/// <summary>
/// Unit tests for MetricsController REST API.
/// </summary>
public class MetricsControllerTests
{
    private readonly IMemoryPressureMonitor _mockPressureMonitor;
    private readonly ILatencyProfiler _mockLatencyProfiler;
    private readonly IMemoryGrowthMonitor _mockGrowthMonitor;
    private readonly ILogger<MetricsController> _mockLogger;
    private readonly MetricsController _controller;

    public MetricsControllerTests()
    {
        _mockPressureMonitor = Substitute.For<IMemoryPressureMonitor>();
        _mockLatencyProfiler = Substitute.For<ILatencyProfiler>();
        _mockGrowthMonitor = Substitute.For<IMemoryGrowthMonitor>();
        _mockLogger = Substitute.For<ILogger<MetricsController>>();

        _controller = new MetricsController(
            _mockPressureMonitor,
            _mockLatencyProfiler,
            _mockGrowthMonitor,
            _mockLogger);
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

        _mockPressureMonitor.GetMemoryInfo().Returns(pressureInfo);
        _mockPressureMonitor.IsUnderPressure().Returns(false);

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

        _mockPressureMonitor.GetMemoryInfo().Returns(pressureInfo);
        _mockPressureMonitor.IsUnderPressure().Returns(true);

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

        _mockLatencyProfiler.GetMetricsAsync("default", null, Arg.Any<CancellationToken>())
            .Returns(metrics);

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

        _mockLatencyProfiler.GetMetricsAsync("default", "Session", Arg.Any<CancellationToken>())
            .Returns(metrics);

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

        _mockGrowthMonitor.GetGrowthMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(growthMetrics);

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

        _mockGrowthMonitor.GetGrowthMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(growthMetrics);

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

        _mockPressureMonitor.GetMemoryInfo().Returns(pressureInfo);
        _mockPressureMonitor.IsUnderPressure().Returns(false);
        _mockLatencyProfiler.GetMetricsAsync("default", null, Arg.Any<CancellationToken>())
            .Returns(latencyMetrics);
        _mockGrowthMonitor.GetGrowthMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(growthMetrics);

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

        _mockPressureMonitor.GetMemoryInfo().Returns(pressureInfo);
        _mockPressureMonitor.IsUnderPressure().Returns(false);
        _mockLatencyProfiler.GetMetricsAsync("default", null, Arg.Any<CancellationToken>())
            .Returns(latencyMetrics);
        _mockGrowthMonitor.GetGrowthMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(growthMetrics);

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

        _mockPressureMonitor.GetMemoryInfo().Returns(pressureInfo);
        _mockPressureMonitor.IsUnderPressure().Returns(true);
        _mockLatencyProfiler.GetMetricsAsync("default", null, Arg.Any<CancellationToken>())
            .Returns(latencyMetrics);
        _mockGrowthMonitor.GetGrowthMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(growthMetrics);

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
        _mockLatencyProfiler.ResetMetricsAsync("default", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ResetLatencyMetrics(cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        await _mockLatencyProfiler.Received(1).ResetMetricsAsync("default", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetLatencyMetrics_WithCustomUser_ResetsForCorrectUser()
    {
        // Arrange
        _mockLatencyProfiler.ResetMetricsAsync("custom-user", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ResetLatencyMetrics("custom-user", CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        await _mockLatencyProfiler.Received(1).ResetMetricsAsync("custom-user", Arg.Any<CancellationToken>());
    }

    #endregion
}
