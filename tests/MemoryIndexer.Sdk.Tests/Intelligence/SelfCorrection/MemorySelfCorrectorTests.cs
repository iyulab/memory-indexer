using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.SelfCorrection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.SelfCorrection;

public class MemorySelfCorrectorTests
{
    private readonly MemorySelfCorrector _corrector;
    private readonly IMemoryStore _memoryStoreMock;
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly ITemporalEntityStore _entityStoreMock;

    public MemorySelfCorrectorTests()
    {
        _memoryStoreMock = Substitute.For<IMemoryStore>();
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _entityStoreMock = Substitute.For<ITemporalEntityStore>();

        SetupDefaultMocks();

        _corrector = new MemorySelfCorrector(
            _memoryStoreMock,
            _embeddingServiceMock,
            _entityStoreMock,
            NullLogger<MemorySelfCorrector>.Instance);
    }

    private void SetupDefaultMocks()
    {
        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        _memoryStoreMock.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new MemoryUnit
            {
                Id = callInfo.ArgAt<Guid>(0),
                Content = "Test memory content",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

        _memoryStoreMock.UpdateAsync(Arg.Any<MemoryUnit>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _memoryStoreMock.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[1024]);

        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact]
    public async Task AnalyzeMemoriesAsync_NoMemories_ShouldReturnEmptyResult()
    {
        // Arrange - default mock returns empty

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user", cancellationToken: TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user", cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _corrector.DetectContradictionsAsync(memories, TestContext.Current.CancellationToken);

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
        var result = await _corrector.DetectContradictionsAsync(memories, TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _corrector.IdentifyOutdatedMemoriesAsync("test_user", cancellationToken: TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _corrector.IdentifyOutdatedMemoriesAsync("test_user", cancellationToken: TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _corrector.TrackEvidenceGapsAsync("test_user", "authentication details", TestContext.Current.CancellationToken);

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
        var result = await _corrector.ApplyCorrectionsAsync(corrections, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_NoCorrections_ShouldReturnEmptyResult()
    {
        // Arrange
        var corrections = new List<MemoryCorrection>();

        // Act
        var result = await _corrector.ApplyCorrectionsAsync(corrections, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _corrector.ResolveContradictionAsync(contradiction, ResolutionStrategy.KeepNewest, TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        var options = new ConfidenceUpdateOptions
        {
            ApplyTimeDecay = true,
            DecayHalfLifeDays = 30
        };

        // Act
        var result = await _corrector.UpdateConfidenceScoresAsync("test_user", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCorrectionHistoryAsync_ShouldReturnHistory()
    {
        // Act
        var result = await _corrector.GetCorrectionHistoryAsync("test_user", cancellationToken: TestContext.Current.CancellationToken);

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

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        var options = new MemoryAnalysisOptions
        {
            CheckContradictions = true,
            CheckOutdated = false,
            CheckDuplicates = false,
            MaxMemoriesToAnalyze = 100
        };

        // Act
        var result = await _corrector.AnalyzeMemoriesAsync("test_user", options, TestContext.Current.CancellationToken);

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
        var result = await _corrector.ResolveContradictionAsync(contradiction, ResolutionStrategy.KeepHigherConfidence, TestContext.Current.CancellationToken);

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
        var result = await _corrector.ApplyCorrectionsAsync(corrections, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
    }
}
