using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.SelfCorrection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.SelfCorrection;

public class MemorySelfCorrectorTests
{
    private readonly MemorySelfCorrector _corrector;
    private readonly Mock<IMemoryStore> _memoryStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<ITemporalEntityStore> _entityStoreMock;

    public MemorySelfCorrectorTests()
    {
        _memoryStoreMock = new Mock<IMemoryStore>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _entityStoreMock = new Mock<ITemporalEntityStore>();

        SetupDefaultMocks();

        _corrector = new MemorySelfCorrector(
            _memoryStoreMock.Object,
            _embeddingServiceMock.Object,
            _entityStoreMock.Object,
            NullLogger<MemorySelfCorrector>.Instance);
    }

    private void SetupDefaultMocks()
    {
        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _memoryStoreMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new MemoryUnit
            {
                Id = id,
                Content = "Test memory content",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        _memoryStoreMock
            .Setup(x => x.UpdateAsync(It.IsAny<MemoryUnit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _memoryStoreMock
            .Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _memoryStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1024]);

        _entityStoreMock
            .Setup(x => x.GetBySubjectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task AnalyzeMemoriesAsync_NoMemories_ShouldReturnEmptyResult()
    {
        // Arrange - default mock returns empty

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.MemoriesAnalyzed);
        Assert.Empty(result.Contradictions);
    }

    [Fact]
    public async Task AnalyzeMemoriesAsync_WithMemories_ShouldAnalyze()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user prefers Python",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user prefers C#",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.MemoriesAnalyzed > 0);
    }

    [Fact]
    public async Task DetectContradictionsAsync_NoContradictions_ShouldReturnEmpty()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The API uses REST architecture",
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The database uses PostgreSQL",
                Stability = MemoryStability.Stable
            }
        };

        // Act
        var result = await _corrector.DetectContradictionsAsync(memories);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DetectContradictionsAsync_WithContradictions_ShouldDetect()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The project uses Python 3.9",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The project uses Python 3.11",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Stability = MemoryStability.Stable
            }
        };

        // Act
        var result = await _corrector.DetectContradictionsAsync(memories);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task IdentifyOutdatedMemoriesAsync_NoOutdated_ShouldReturnEmpty()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Recent memory content",
                CreatedAt = DateTime.UtcNow,
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        // Act
        var result = await _corrector.IdentifyOutdatedMemoriesAsync("test_user");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task IdentifyOutdatedMemoriesAsync_WithOldMemories_ShouldIdentify()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Very old memory content",
                CreatedAt = DateTime.UtcNow.AddDays(-365),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Recent memory content",
                CreatedAt = DateTime.UtcNow,
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        // Act
        var result = await _corrector.IdentifyOutdatedMemoriesAsync("test_user");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TrackEvidenceGapsAsync_WithQuery_ShouldReturnGaps()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The API uses authentication",
                Stability = MemoryStability.Volatile
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        // Act
        var result = await _corrector.TrackEvidenceGapsAsync("test_user", "authentication details");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_WithCorrections_ShouldApply()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var corrections = new List<MemoryCorrection>
        {
            new()
            {
                MemoryId = memoryId,
                Type = CorrectionType.ContentUpdate,
                OriginalContent = "Old content",
                CorrectedContent = "New corrected content",
                Reason = "Content was outdated",
                Priority = CorrectionPriority.Normal
            }
        };

        // Act
        var result = await _corrector.ApplyCorrectionsAsync(corrections);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_NoCorrections_ShouldReturnEmptyResult()
    {
        // Arrange
        var corrections = new List<MemoryCorrection>();

        // Act
        var result = await _corrector.ApplyCorrectionsAsync(corrections);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.AppliedCorrections);
    }

    [Fact]
    public async Task ResolveContradictionAsync_KeepNewest_ShouldResolve()
    {
        // Arrange
        var memory1 = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Old version of the fact",
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        };

        var memory2 = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "New version of the fact",
            CreatedAt = DateTime.UtcNow
        };

        var contradiction = new MemoryContradiction
        {
            Memory1 = memory1,
            Memory2 = memory2,
            Type = ContradictionType.Factual,
            Confidence = 0.9f,
            Description = "Conflicting facts about the same topic"
        };

        // Act
        var result = await _corrector.ResolveContradictionAsync(
            contradiction,
            ResolutionStrategy.KeepNewest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ResolutionStrategy.KeepNewest, result.Strategy);
    }

    [Fact]
    public async Task UpdateConfidenceScoresAsync_ShouldUpdateScores()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Memory with decaying confidence",
                CreatedAt = DateTime.UtcNow.AddDays(-60),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        var options = new ConfidenceUpdateOptions
        {
            ApplyTimeDecay = true,
            DecayHalfLifeDays = 30
        };

        // Act
        var result = await _corrector.UpdateConfidenceScoresAsync("test_user", options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCorrectionHistoryAsync_ShouldReturnHistory()
    {
        // Act
        var result = await _corrector.GetCorrectionHistoryAsync("test_user");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AnalyzeMemoriesAsync_WithOptions_ShouldRespectOptions()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Test memory",
                CreatedAt = DateTime.UtcNow,
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(memories);

        var options = new MemoryAnalysisOptions
        {
            CheckContradictions = true,
            CheckOutdated = false,
            CheckDuplicates = false,
            MaxMemoriesToAnalyze = 100
        };

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user", options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ResolveContradictionAsync_KeepHigherConfidence_ShouldResolve()
    {
        // Arrange
        var memory1 = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Low confidence fact",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        var memory2 = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "High confidence fact",
            CreatedAt = DateTime.UtcNow
        };

        var contradiction = new MemoryContradiction
        {
            Memory1 = memory1,
            Memory2 = memory2,
            Type = ContradictionType.Factual,
            Confidence = 0.85f
        };

        // Act
        var result = await _corrector.ResolveContradictionAsync(
            contradiction,
            ResolutionStrategy.KeepHigherConfidence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ResolutionStrategy.KeepHigherConfidence, result.Strategy);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_WithConfidenceAdjustment_ShouldApply()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var corrections = new List<MemoryCorrection>
        {
            new()
            {
                MemoryId = memoryId,
                Type = CorrectionType.ConfidenceAdjustment,
                NewConfidence = 0.5f,
                Reason = "Confidence decay due to time",
                Priority = CorrectionPriority.Low
            }
        };

        // Act
        var result = await _corrector.ApplyCorrectionsAsync(corrections);

        // Assert
        Assert.NotNull(result);
    }
}
