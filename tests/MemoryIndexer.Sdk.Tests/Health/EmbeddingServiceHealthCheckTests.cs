using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class EmbeddingServiceHealthCheckTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly EmbeddingServiceHealthCheck _healthCheck;

    public EmbeddingServiceHealthCheckTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _healthCheck = new EmbeddingServiceHealthCheck(_mockEmbeddingService.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_ValidEmbedding_ReturnsHealthy()
    {
        // Arrange
        var validEmbedding = Enumerable.Repeat(0.5f, 1024).ToArray();
        _mockEmbeddingService.Setup(es => es.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(validEmbedding);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("embeddingLatencyMs");
        result.Data.Should().ContainKey("embeddingDimensions");
        result.Data["embeddingDimensions"].Should().Be(1024);
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyEmbedding_ReturnsUnhealthy()
    {
        // Arrange
        _mockEmbeddingService.Setup(es => es.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<float>());

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("empty embedding");
    }

    [Fact]
    public async Task CheckHealthAsync_NaNValues_ReturnsUnhealthy()
    {
        // Arrange
        var invalidEmbedding = new[] { 0.5f, float.NaN, 0.3f };
        _mockEmbeddingService.Setup(es => es.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidEmbedding);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("invalid values");
    }

    [Fact]
    public async Task CheckHealthAsync_ServiceFailure_ReturnsUnhealthy()
    {
        // Arrange
        _mockEmbeddingService.Setup(es => es.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Failed to connect");
        result.Exception.Should().NotBeNull();
    }
}
