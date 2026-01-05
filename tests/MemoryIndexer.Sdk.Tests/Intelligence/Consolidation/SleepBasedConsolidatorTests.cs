using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Consolidation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Consolidation;

/// <summary>
/// Tests for <see cref="SleepBasedConsolidator"/>.
/// </summary>
public sealed class SleepBasedConsolidatorTests
{
    private readonly Mock<IMemoryStore> _memoryStore;
    private readonly Mock<IEmbeddingService> _embeddingService;
    private readonly Mock<ILogger<SleepBasedConsolidator>> _logger;
    private readonly SleepBasedConsolidator _sut;

    public SleepBasedConsolidatorTests()
    {
        _memoryStore = new Mock<IMemoryStore>();
        _embeddingService = new Mock<IEmbeddingService>();
        _logger = new Mock<ILogger<SleepBasedConsolidator>>();
        _sut = new SleepBasedConsolidator(
            _memoryStore.Object,
            _embeddingService.Object,
            _logger.Object);
    }

    #region ConsolidateAsync Tests

    [Fact]
    public async Task ConsolidateAsync_WithoutUserId_ReturnsSuccessWithZeroProcessed()
    {
        // Arrange
        var options = new ConsolidationOptions { UserId = null };

        // Act
        var result = await _sut.ConsolidateAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.MemoriesProcessed);
    }

    [Fact]
    public async Task ConsolidateAsync_WithEmptyMemories_ReturnsSuccessWithZeroProcessed()
    {
        // Arrange
        var options = new ConsolidationOptions { UserId = "user1" };
        _memoryStore.Setup(x => x.GetAllAsync("user1", It.IsAny<MemoryFilterOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _sut.ConsolidateAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.MemoriesProcessed);
    }

    [Fact]
    public async Task ConsolidateAsync_WithMemories_ProcessesSuccessfully()
    {
        // Arrange
        var options = new ConsolidationOptions
        {
            UserId = "user1",
            ApplyForgettingCurve = true,
            MinMemoriesForReflection = 5
        };

        var memories = CreateTestMemories(10);
        _memoryStore.Setup(x => x.GetAllAsync("user1", It.IsAny<MemoryFilterOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        _embeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1024]);

        // Act
        var result = await _sut.ConsolidateAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.MemoriesProcessed);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task ConsolidateAsync_WithForgettingCurve_UpdatesMemoryScores()
    {
        // Arrange
        var options = new ConsolidationOptions
        {
            UserId = "user1",
            ApplyForgettingCurve = true,
            MinMemoriesForReflection = 100 // High threshold to skip reflections
        };

        var oldMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Old memory",
            UserId = "user1",
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            LastAccessedAt = DateTime.UtcNow.AddDays(-3),
            ImportanceScore = 0.8f,
            AccessCount = 2
        };

        _memoryStore.Setup(x => x.GetAllAsync("user1", It.IsAny<MemoryFilterOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([oldMemory]);

        // Act
        var result = await _sut.ConsolidateAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.MemoriesDecayed > 0);
        _memoryStore.Verify(x => x.UpdateAsync(It.IsAny<MemoryUnit>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region GenerateReflectionsAsync Tests

    [Fact]
    public async Task GenerateReflectionsAsync_WithLessThan3Memories_ReturnsEmpty()
    {
        // Arrange
        var memories = CreateTestMemories(2);

        // Act
        var result = await _sut.GenerateReflectionsAsync(memories);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateReflectionsAsync_WithTopicGroups_GeneratesReflections()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new() { Id = Guid.NewGuid(), Content = "Auth memory 1", Topics = ["authentication"], UserId = "u1", ImportanceScore = 0.7f },
            new() { Id = Guid.NewGuid(), Content = "Auth memory 2", Topics = ["authentication"], UserId = "u1", ImportanceScore = 0.8f },
            new() { Id = Guid.NewGuid(), Content = "Auth memory 3", Topics = ["authentication"], UserId = "u1", ImportanceScore = 0.6f }
        };

        _embeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1024]);

        // Act
        var result = await _sut.GenerateReflectionsAsync(memories);

        // Assert
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.Equal(MemoryType.Reflection, r.Type));
    }

    [Fact]
    public async Task GenerateReflectionsAsync_WithImportantMemories_GeneratesCrossTopicReflection()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new() { Id = Guid.NewGuid(), Content = "Important 1", Topics = ["auth"], UserId = "u1", ImportanceScore = 0.9f },
            new() { Id = Guid.NewGuid(), Content = "Important 2", Topics = ["security"], UserId = "u1", ImportanceScore = 0.85f },
            new() { Id = Guid.NewGuid(), Content = "Important 3", Topics = ["access"], UserId = "u1", ImportanceScore = 0.8f }
        };

        _embeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1024]);

        // Act
        var result = await _sut.GenerateReflectionsAsync(memories);

        // Assert
        Assert.NotEmpty(result);
        var crossTopicReflection = result.FirstOrDefault(r =>
            r.Metadata.ContainsKey("source_type") && r.Metadata["source_type"] == "cross_topic_reflection");
        Assert.NotNull(crossTopicReflection);
    }

    #endregion

    #region ApplyForgettingCurveAsync Tests

    [Fact]
    public async Task ApplyForgettingCurveAsync_WithRecentMemory_MinimalDecay()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Recent memory",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            ImportanceScore = 0.8f,
            AccessCount = 5
        };

        // Act
        var results = await _sut.ApplyForgettingCurveAsync([memory]);

        // Assert
        var result = results.Single();
        Assert.True(result.NewScore >= result.PreviousScore * 0.95f);
        Assert.False(result.ShouldArchive);
    }

    [Fact]
    public async Task ApplyForgettingCurveAsync_WithOldMemory_SignificantDecay()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Old memory",
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastAccessedAt = DateTime.UtcNow.AddDays(-20),
            ImportanceScore = 0.5f,
            AccessCount = 1
        };

        // Act
        var results = await _sut.ApplyForgettingCurveAsync([memory]);

        // Assert
        var result = results.Single();
        Assert.True(result.NewScore < result.PreviousScore);
    }

    [Fact]
    public async Task ApplyForgettingCurveAsync_HighAccessCount_SlowerDecay()
    {
        // Arrange
        var lowAccessMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Low access",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            LastAccessedAt = DateTime.UtcNow.AddDays(-5),
            ImportanceScore = 0.6f,
            AccessCount = 1
        };

        var highAccessMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "High access",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            LastAccessedAt = DateTime.UtcNow.AddDays(-5),
            ImportanceScore = 0.6f,
            AccessCount = 100
        };

        // Act
        var results = await _sut.ApplyForgettingCurveAsync([lowAccessMemory, highAccessMemory]);

        // Assert
        var lowAccessResult = results.First(r => r.MemoryId == lowAccessMemory.Id);
        var highAccessResult = results.First(r => r.MemoryId == highAccessMemory.Id);

        // High access memory should retain more (higher new score)
        Assert.True(highAccessResult.NewScore > lowAccessResult.NewScore);
        Assert.True(highAccessResult.StrengthFactor > lowAccessResult.StrengthFactor);
    }

    [Fact]
    public async Task ApplyForgettingCurveAsync_BelowArchiveThreshold_MarksForArchive()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Forgotten memory",
            CreatedAt = DateTime.UtcNow.AddDays(-100),
            LastAccessedAt = DateTime.UtcNow.AddDays(-90),
            ImportanceScore = 0.15f,
            AccessCount = 0
        };

        // Act
        var results = await _sut.ApplyForgettingCurveAsync([memory]);

        // Assert
        var result = results.Single();
        Assert.True(result.ShouldArchive);
    }

    #endregion

    #region IdentifyMergeCandidatesAsync Tests

    [Fact]
    public async Task IdentifyMergeCandidatesAsync_WithLessThan2Memories_ReturnsEmpty()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Single memory",
            Embedding = new float[1024]
        };

        // Act
        var result = await _sut.IdentifyMergeCandidatesAsync([memory]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task IdentifyMergeCandidatesAsync_WithSimilarMemories_IdentifiesMergeCandidates()
    {
        // Arrange
        var embedding1 = CreateNormalizedEmbedding(1024, 0.1f);
        var embedding2 = CreateNormalizedEmbedding(1024, 0.1f); // Very similar
        var embedding3 = CreateNormalizedEmbedding(1024, 0.5f); // Different

        var memories = new List<MemoryUnit>
        {
            new() { Id = Guid.NewGuid(), Content = "Memory 1", Embedding = embedding1 },
            new() { Id = Guid.NewGuid(), Content = "Memory 2", Embedding = embedding2 },
            new() { Id = Guid.NewGuid(), Content = "Memory 3", Embedding = embedding3 }
        };

        // Act
        var result = await _sut.IdentifyMergeCandidatesAsync(memories, 0.85f);

        // Assert
        Assert.NotEmpty(result);
        var mergeOp = result.First();
        Assert.NotNull(mergeOp.PrimaryMemory);
        Assert.NotEmpty(mergeOp.MemoriesToMerge);
    }

    [Fact]
    public async Task IdentifyMergeCandidatesAsync_WithDissimilarMemories_ReturnsEmpty()
    {
        // Arrange - Create orthogonal embeddings
        var embedding1 = new float[1024];
        var embedding2 = new float[1024];
        embedding1[0] = 1.0f;
        embedding2[500] = 1.0f;

        var memories = new List<MemoryUnit>
        {
            new() { Id = Guid.NewGuid(), Content = "Memory 1", Embedding = embedding1 },
            new() { Id = Guid.NewGuid(), Content = "Memory 2", Embedding = embedding2 }
        };

        // Act
        var result = await _sut.IdentifyMergeCandidatesAsync(memories, 0.85f);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task IdentifyMergeCandidatesAsync_GeneratesSuggestedMergedContent()
    {
        // Arrange - Create identical embeddings
        var embedding = CreateNormalizedEmbedding(1024, 0.0f);

        var memories = new List<MemoryUnit>
        {
            new() { Id = Guid.NewGuid(), Content = "Primary content", Embedding = embedding },
            new() { Id = Guid.NewGuid(), Content = "Secondary content", Embedding = embedding }
        };

        // Act
        var result = await _sut.IdentifyMergeCandidatesAsync(memories, 0.99f);

        // Assert
        Assert.NotEmpty(result);
        var mergeOp = result.First();
        Assert.NotNull(mergeOp.SuggestedMergedContent);
        Assert.Contains("Primary content", mergeOp.SuggestedMergedContent);
        Assert.Contains("Consolidated from", mergeOp.SuggestedMergedContent);
    }

    #endregion

    #region Helper Methods

    private static List<MemoryUnit> CreateTestMemories(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = $"Test memory {i}",
                UserId = "user1",
                CreatedAt = DateTime.UtcNow.AddHours(-i),
                ImportanceScore = 0.5f + (i * 0.05f),
                Topics = [i % 2 == 0 ? "topic1" : "topic2"]
            })
            .ToList();
    }

    private static float[] CreateNormalizedEmbedding(int dimensions, float offset)
    {
        var embedding = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] = MathF.Sin(i * 0.01f + offset);
        }
        // Normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < dimensions; i++)
        {
            embedding[i] /= magnitude;
        }
        return embedding;
    }

    #endregion
}
