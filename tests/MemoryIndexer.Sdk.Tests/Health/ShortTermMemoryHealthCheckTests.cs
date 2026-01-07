using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class ShortTermMemoryHealthCheckTests
{
    private readonly Mock<IShortTermMemory> _mockWorkingMemory;
    private readonly ShortTermMemoryHealthCheck _healthCheck;

    public ShortTermMemoryHealthCheckTests()
    {
        _mockWorkingMemory = new Mock<IShortTermMemory>();
        _healthCheck = new ShortTermMemoryHealthCheck(_mockWorkingMemory.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_LowUtilization_ReturnsHealthy()
    {
        // Arrange
        _mockWorkingMemory.Setup(wm => wm.Count).Returns(3);
        _mockWorkingMemory.Setup(wm => wm.Capacity).Returns(7);
        _mockWorkingMemory.Setup(wm => wm.IsFull).Returns(false);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("utilizationRatio");
        result.Data.Should().ContainKey("count");
        result.Data["count"].Should().Be(3);
    }

    [Fact]
    public async Task CheckHealthAsync_HighUtilization_ReturnsDegraded()
    {
        // Arrange
        _mockWorkingMemory.Setup(wm => wm.Count).Returns(6);
        _mockWorkingMemory.Setup(wm => wm.Capacity).Returns(7);
        _mockWorkingMemory.Setup(wm => wm.IsFull).Returns(false);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("high utilization");
    }

    [Fact]
    public async Task CheckHealthAsync_CriticalUtilization_ReturnsUnhealthy()
    {
        // Arrange
        _mockWorkingMemory.Setup(wm => wm.Count).Returns(7);
        _mockWorkingMemory.Setup(wm => wm.Capacity).Returns(7);
        _mockWorkingMemory.Setup(wm => wm.IsFull).Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("critically full");
    }

    [Fact]
    public async Task CheckHealthAsync_Exception_ReturnsUnhealthy()
    {
        // Arrange
        _mockWorkingMemory.Setup(wm => wm.Count).Throws(new Exception("Test exception"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
