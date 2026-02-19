using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class BufferHealthCheckTests
{
    private readonly IBuffer _mockBuffer;
    private readonly BufferHealthCheck _healthCheck;

    public BufferHealthCheckTests()
    {
        _mockBuffer = Substitute.For<IBuffer>();
        _healthCheck = new BufferHealthCheck(_mockBuffer);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthyBuffer_ReturnsHealthy()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Returns(new SensoryBufferStats
            {
                ItemCount = 5,
                TotalTokens = 500,
                TurnCount = 3,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-10),
                TriggerSatisfied = false
            });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("itemCount");
        result.Data.Should().ContainKey("totalTokens");
    }

    [Fact]
    public async Task CheckHealthAsync_HighTokenCount_ReturnsDegraded()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Returns(new SensoryBufferStats
            {
                ItemCount = 10,
                TotalTokens = 2500, // Warning threshold
                TurnCount = 5,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-30),
                TriggerSatisfied = false
            });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("approaching capacity");
    }

    [Fact]
    public async Task CheckHealthAsync_CriticalTokenCount_ReturnsUnhealthy()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Returns(new SensoryBufferStats
            {
                ItemCount = 20,
                TotalTokens = 6000, // Critical threshold
                TurnCount = 10,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-30),
                TriggerSatisfied = false
            });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("critical token accumulation");
    }

    [Fact]
    public async Task CheckHealthAsync_HighProcessingLag_ReturnsDegraded()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Returns(new SensoryBufferStats
            {
                ItemCount = 5,
                TotalTokens = 500,
                TurnCount = 3,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-70), // Warning lag
                TriggerSatisfied = false
            });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_CriticalProcessingLag_ReturnsUnhealthy()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Returns(new SensoryBufferStats
            {
                ItemCount = 10,
                TotalTokens = 1000,
                TurnCount = 5,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-150), // Critical lag
                TriggerSatisfied = false
            });

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("critical processing lag");
    }

    [Fact]
    public async Task CheckHealthAsync_Exception_ReturnsUnhealthy()
    {
        // Arrange
        _mockBuffer.GetStats(Arg.Any<string>())
            .Throws(new InvalidOperationException("Test exception"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
