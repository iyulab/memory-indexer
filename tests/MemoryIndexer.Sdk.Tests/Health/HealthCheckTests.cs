using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Health;

public class HealthCheckTests
{
    #region BufferHealthCheck Tests

    [Fact]
    public async Task BufferHealthCheck_Healthy_WhenNormalOperation()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Returns(new SensoryBufferStats
            {
                ItemCount = 5,
                TotalTokens = 100,
                TurnCount = 2,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-10),
                TriggerSatisfied = false
            });

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BufferHealthCheck_Unhealthy_WhenProcessingLagCritical()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Returns(new SensoryBufferStats
            {
                ItemCount = 10,
                TotalTokens = 200,
                TurnCount = 5,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-150), // 150s lag (>120s critical)
                TriggerSatisfied = true
            });

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("critical", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BufferHealthCheck_Unhealthy_WhenTokenAccumulationCritical()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Returns(new SensoryBufferStats
            {
                ItemCount = 50,
                TotalTokens = 6000, // >5000 critical threshold
                TurnCount = 20,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-30),
                TriggerSatisfied = true
            });

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("token accumulation", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BufferHealthCheck_Degraded_WhenApproachingLimits()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Returns(new SensoryBufferStats
            {
                ItemCount = 20,
                TotalTokens = 2500, // >2000 warning, <5000 critical
                TurnCount = 10,
                OldestItemTimestamp = DateTime.UtcNow.AddSeconds(-30),
                TriggerSatisfied = false
            });

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("approaching", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BufferHealthCheck_Unhealthy_WhenExceptionThrown()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Buffer error"));

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    #endregion

    #region ShortTermMemoryHealthCheck Tests

    [Fact]
    public async Task ShortTermMemoryHealthCheck_Healthy_WhenNormalUtilization()
    {
        // Arrange
        var mockMemory = new Mock<IShortTermMemory>();
        mockMemory.Setup(m => m.Count).Returns(3);
        mockMemory.Setup(m => m.Capacity).Returns(7);
        mockMemory.Setup(m => m.IsFull).Returns(false);

        var healthCheck = new ShortTermMemoryHealthCheck(mockMemory.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShortTermMemoryHealthCheck_Unhealthy_WhenFull()
    {
        // Arrange
        var mockMemory = new Mock<IShortTermMemory>();
        mockMemory.Setup(m => m.Count).Returns(7);
        mockMemory.Setup(m => m.Capacity).Returns(7);
        mockMemory.Setup(m => m.IsFull).Returns(true);

        var healthCheck = new ShortTermMemoryHealthCheck(mockMemory.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("critically full", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShortTermMemoryHealthCheck_Degraded_WhenHighUtilization()
    {
        // Arrange
        var mockMemory = new Mock<IShortTermMemory>();
        mockMemory.Setup(m => m.Count).Returns(6); // 6/7 = 85.7% (>85% warning)
        mockMemory.Setup(m => m.Capacity).Returns(7);
        mockMemory.Setup(m => m.IsFull).Returns(false);

        var healthCheck = new ShortTermMemoryHealthCheck(mockMemory.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("high utilization", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShortTermMemoryHealthCheck_Unhealthy_WhenExceptionThrown()
    {
        // Arrange
        var mockMemory = new Mock<IShortTermMemory>();
        mockMemory.Setup(m => m.Count).Throws(new InvalidOperationException("Memory error"));

        var healthCheck = new ShortTermMemoryHealthCheck(mockMemory.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    #endregion

    #region LongTermStoreHealthCheck Tests

    [Fact]
    public async Task LongTermStoreHealthCheck_Healthy_WhenQueryFast()
    {
        // Arrange
        var mockStore = new Mock<ILongTermStore>();
        mockStore.Setup(s => s.GetOrCreateActiveSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = Guid.NewGuid(), UserId = "__health_check_test__" });
        mockStore.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = new LongTermStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LongTermStoreHealthCheck_Unhealthy_WhenExceptionThrown()
    {
        // Arrange
        var mockStore = new Mock<ILongTermStore>();
        mockStore.Setup(s => s.GetOrCreateActiveSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var healthCheck = new LongTermStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        Assert.Contains("Failed to check", result.Description);
    }

    #endregion

    #region VectorDbHealthCheck Tests

    [Fact]
    public async Task VectorDbHealthCheck_Healthy_WhenQuerySucceeds()
    {
        // Arrange
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        var healthCheck = new VectorDbHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VectorDbHealthCheck_Unhealthy_WhenExceptionThrown()
    {
        // Arrange
        var mockStore = new Mock<IMemoryStore>();
        mockStore.Setup(s => s.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vector DB error"));

        var healthCheck = new VectorDbHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    #endregion

    #region EmbeddingServiceHealthCheck Tests

    [Fact]
    public async Task EmbeddingServiceHealthCheck_Healthy_WhenGenerationSucceeds()
    {
        // Arrange
        var mockService = new Mock<IEmbeddingService>();
        mockService.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1024]);

        var healthCheck = new EmbeddingServiceHealthCheck(mockService.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddingServiceHealthCheck_Unhealthy_WhenExceptionThrown()
    {
        // Arrange
        var mockService = new Mock<IEmbeddingService>();
        mockService.Setup(s => s.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Embedding service unavailable"));

        var healthCheck = new EmbeddingServiceHealthCheck(mockService.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    #endregion

    #region ArchiveStoreHealthCheck Tests

    [Fact]
    public async Task ArchiveStoreHealthCheck_Healthy_WhenNormalEntries()
    {
        // Arrange
        var mockStore = new Mock<IArchiveStore>();
        mockStore.Setup(s => s.GetStats(It.IsAny<string>()))
            .Returns(new SemanticStoreStats
            {
                UserId = "__health_check_test__",
                TotalEntries = 100,
                ConfirmedEntries = 80,
                AverageConfidence = 0.85f,
                EntriesByCategory = new Dictionary<SemanticStoreCategory, int>
                {
                    { SemanticStoreCategory.Preference, 50 },
                    { SemanticStoreCategory.Fact, 50 }
                }
            });

        var healthCheck = new ArchiveStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("healthy", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveStoreHealthCheck_Unhealthy_WhenCriticalEntryCount()
    {
        // Arrange
        var mockStore = new Mock<IArchiveStore>();
        mockStore.Setup(s => s.GetStats(It.IsAny<string>()))
            .Returns(new SemanticStoreStats
            {
                UserId = "__health_check_test__",
                TotalEntries = 1500, // >1000 critical threshold
                ConfirmedEntries = 1000,
                AverageConfidence = 0.7f
            });

        var healthCheck = new ArchiveStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("critical", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveStoreHealthCheck_Degraded_WhenApproachingLimit()
    {
        // Arrange
        var mockStore = new Mock<IArchiveStore>();
        mockStore.Setup(s => s.GetStats(It.IsAny<string>()))
            .Returns(new SemanticStoreStats
            {
                UserId = "__health_check_test__",
                TotalEntries = 800, // >750 warning, <1000 critical
                ConfirmedEntries = 600,
                AverageConfidence = 0.8f
            });

        var healthCheck = new ArchiveStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("approaching", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveStoreHealthCheck_Degraded_WhenLowConfirmationRatio()
    {
        // Arrange
        var mockStore = new Mock<IArchiveStore>();
        mockStore.Setup(s => s.GetStats(It.IsAny<string>()))
            .Returns(new SemanticStoreStats
            {
                UserId = "__health_check_test__",
                TotalEntries = 100,
                ConfirmedEntries = 5, // 5% confirmation (<10% threshold)
                AverageConfidence = 0.6f
            });

        var healthCheck = new ArchiveStoreHealthCheck(mockStore.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("confirmation ratio", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchiveStoreHealthCheck_ThrowsOnNullStore()
    {
        Assert.Throws<ArgumentNullException>(() => new ArchiveStoreHealthCheck(null!));
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void BufferHealthCheck_ThrowsOnNullBuffer()
    {
        Assert.Throws<ArgumentNullException>(() => new BufferHealthCheck(null!));
    }

    [Fact]
    public void ShortTermMemoryHealthCheck_ThrowsOnNullMemory()
    {
        Assert.Throws<ArgumentNullException>(() => new ShortTermMemoryHealthCheck(null!));
    }

    [Fact]
    public void LongTermStoreHealthCheck_ThrowsOnNullStore()
    {
        Assert.Throws<ArgumentNullException>(() => new LongTermStoreHealthCheck(null!));
    }

    [Fact]
    public void VectorDbHealthCheck_ThrowsOnNullStore()
    {
        Assert.Throws<ArgumentNullException>(() => new VectorDbHealthCheck(null!));
    }

    [Fact]
    public void EmbeddingServiceHealthCheck_ThrowsOnNullService()
    {
        Assert.Throws<ArgumentNullException>(() => new EmbeddingServiceHealthCheck(null!));
    }

    #endregion

    #region Data Population Tests

    [Fact]
    public async Task BufferHealthCheck_PopulatesDataCorrectly()
    {
        // Arrange
        var mockBuffer = new Mock<IBuffer>();
        mockBuffer.Setup(b => b.GetStats(It.IsAny<string>()))
            .Returns(new SensoryBufferStats
            {
                ItemCount = 5,
                TotalTokens = 100,
                TurnCount = 2,
                OldestItemTimestamp = DateTime.UtcNow,
                TriggerSatisfied = false
            });

        var healthCheck = new BufferHealthCheck(mockBuffer.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Contains("itemCount", result.Data.Keys);
        Assert.Contains("totalTokens", result.Data.Keys);
        Assert.Contains("turnCount", result.Data.Keys);
        Assert.Contains("processingLag", result.Data.Keys);
        Assert.Equal(5, result.Data["itemCount"]);
        Assert.Equal(100, result.Data["totalTokens"]);
    }

    [Fact]
    public async Task ShortTermMemoryHealthCheck_PopulatesDataCorrectly()
    {
        // Arrange
        var mockMemory = new Mock<IShortTermMemory>();
        mockMemory.Setup(m => m.Count).Returns(5);
        mockMemory.Setup(m => m.Capacity).Returns(7);
        mockMemory.Setup(m => m.IsFull).Returns(false);

        var healthCheck = new ShortTermMemoryHealthCheck(mockMemory.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Contains("count", result.Data.Keys);
        Assert.Contains("capacity", result.Data.Keys);
        Assert.Contains("utilizationRatio", result.Data.Keys);
        Assert.Contains("isFull", result.Data.Keys);
        Assert.Equal(5, result.Data["count"]);
        Assert.Equal(7, result.Data["capacity"]);
    }

    #endregion
}
