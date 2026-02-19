using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class VectorDbHealthCheckTests
{
    private readonly IMemoryStore _mockMemoryStore;
    private readonly VectorDbHealthCheck _healthCheck;

    public VectorDbHealthCheckTests()
    {
        _mockMemoryStore = Substitute.For<IMemoryStore>();
        _healthCheck = new VectorDbHealthCheck(_mockMemoryStore);
    }

    [Fact]
    public async Task CheckHealthAsync_FastQuery_ReturnsHealthy()
    {
        // Arrange
        _mockMemoryStore.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<MemorySearchResult>());

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
        _mockMemoryStore.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Connection failed"));

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Failed to connect");
        result.Exception.Should().NotBeNull();
    }
}
