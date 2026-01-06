using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class VectorDbHealthCheckTests
{
    private readonly Mock<IMemoryStore> _mockMemoryStore;
    private readonly VectorDbHealthCheck _healthCheck;

    public VectorDbHealthCheckTests()
    {
        _mockMemoryStore = new Mock<IMemoryStore>();
        _healthCheck = new VectorDbHealthCheck(_mockMemoryStore.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_FastQuery_ReturnsHealthy()
    {
        // Arrange
        _mockMemoryStore.Setup(ms => ms.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("queryLatencyMs");
        result.Data.Should().ContainKey("storeType");
    }

    [Fact]
    public async Task CheckHealthAsync_ConnectionFailure_ReturnsUnhealthy()
    {
        // Arrange
        _mockMemoryStore.Setup(ms => ms.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Failed to connect");
        result.Exception.Should().NotBeNull();
    }
}
