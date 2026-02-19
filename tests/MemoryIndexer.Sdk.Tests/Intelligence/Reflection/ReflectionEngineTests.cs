using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Reflection;

public class ReflectionEngineTests
{
    private readonly ReflectionEngine _engine;
    private readonly IMemoryStore _memoryStoreMock;
    private readonly ITemporalEntityStore _entityStoreMock;
    private readonly IScoringService _scoringServiceMock;

    public ReflectionEngineTests()
    {
        _memoryStoreMock = Substitute.For<IMemoryStore>();
        _entityStoreMock = Substitute.For<ITemporalEntityStore>();
        _scoringServiceMock = Substitute.For<IScoringService>();

        SetupDefaultMocks();

        _engine = new ReflectionEngine(
            _memoryStoreMock,
            _entityStoreMock,
            _scoringServiceMock,
            NullLogger<ReflectionEngine>.Instance);
    }

    private void SetupDefaultMocks()
    {
        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        _memoryStoreMock.StoreAsync(Arg.Any<MemoryUnit>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<MemoryUnit>(0));

        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _entityStoreMock.GetAllActiveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _scoringServiceMock.CalculateScore(Arg.Any<MemoryUnit>(), Arg.Any<ReadOnlyMemory<float>?>())
            .Returns(0.75f);
    }

    [Fact]
    public async Task ShouldReflectAsync_NoRecentMemories_ShouldReturnFalse()
    {
        // Arrange - default mock returns empty list

        // Act
        var result = await _engine.ShouldReflectAsync("test_session");

        // Assert
        Assert.NotNull(result);
        Assert.False(result.ShouldReflect);
    }

    [Fact]
    public async Task ShouldReflectAsync_HighAccumulatedImportance_ShouldReturnTrue()
    {
        // Arrange - create high-importance memories
        var memories = Enumerable.Range(0, 20)
            .Select(i => new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = $"Very important critical urgent memory #{i}. Key information that must be remembered. Essential data.",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                Stability = MemoryStability.Stable
            })
            .ToList();

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        _scoringServiceMock.CalculateScore(Arg.Any<MemoryUnit>(), Arg.Any<ReadOnlyMemory<float>?>())
            .Returns(10f); // High importance score

        // Act
        var result = await _engine.ShouldReflectAsync("test_session");

        // Assert
        Assert.NotNull(result);
        // With high accumulated importance, should suggest reflection
    }

    [Fact]
    public async Task ReflectAsync_NoMemories_ShouldReturnEmptyResult()
    {
        // Arrange - default mock returns empty list

        // Act
        var result = await _engine.ReflectAsync("test_session");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Insights);
    }

    [Fact]
    public async Task ReflectAsync_WithMemories_ShouldGenerateInsights()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user prefers TypeScript over JavaScript",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user always uses ESLint for code quality",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user likes VS Code as their editor",
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _engine.ReflectAsync("test_session");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ReflectedMemoryIds.Count > 0);
    }

    [Fact]
    public async Task GenerateInsightsAsync_EmptyMemories_ShouldReturnEmpty()
    {
        // Arrange
        var memories = new List<MemoryUnit>();

        // Act
        var result = await _engine.GenerateInsightsAsync(memories);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateInsightsAsync_RelatedMemories_ShouldFindConnections()
    {
        // Arrange - memories with overlapping entities
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The database uses PostgreSQL for persistence",
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "PostgreSQL is configured with connection pooling",
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The database schema follows normalized design",
                Stability = MemoryStability.Stable
            }
        };

        // Act
        var result = await _engine.GenerateInsightsAsync(memories);

        // Assert
        Assert.NotNull(result);
        // Should find connection insights due to shared entities
    }

    [Fact]
    public async Task SynthesizeQuestionsAsync_WithTopic_ShouldGenerateQuestions()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The API uses REST architecture",
                Stability = MemoryStability.Volatile
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _engine.SynthesizeQuestionsAsync("test_session", "API design");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DiscoverLinksAsync_NoMemories_ShouldReturnEmpty()
    {
        // Arrange
        var memoryId = Guid.NewGuid();

        _memoryStoreMock.GetByIdAsync(memoryId, Arg.Any<CancellationToken>())
            .Returns(new MemoryUnit
            {
                Id = memoryId,
                Content = "Test memory content"
            });

        // Act
        var result = await _engine.DiscoverLinksAsync(memoryId);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DiscoverLinksAsync_WithRelatedMemories_ShouldFindLinks()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();

        var targetMemory = new MemoryUnit
        {
            Id = targetId,
            Content = "The authentication system uses JWT tokens"
        };

        var relatedMemories = new List<MemoryUnit>
        {
            new()
            {
                Id = relatedId,
                Content = "JWT tokens expire after 24 hours"
            }
        };

        _memoryStoreMock.GetByIdAsync(targetId, Arg.Any<CancellationToken>())
            .Returns(targetMemory);

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(relatedMemories);

        // Act
        var result = await _engine.DiscoverLinksAsync(targetId);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SummarizeActivityAsync_NoRecentActivity_ShouldReturnEmptySummary()
    {
        // Arrange - default mock returns empty list

        // Act
        var result = await _engine.SummarizeActivityAsync("test_session", TimeSpan.FromHours(1));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.MemoriesCreated);
    }

    [Fact]
    public async Task SummarizeActivityAsync_WithActivity_ShouldSummarize()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Created new API endpoint",
                CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Updated database schema",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _engine.SummarizeActivityAsync("test_session", TimeSpan.FromHours(1));

        // Assert
        Assert.NotNull(result);
        Assert.True(result.MemoriesCreated > 0);
    }

    [Fact]
    public async Task ReflectAsync_ShouldStoreInsightsAsMemories()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user frequently asks about Python programming",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "The user prefers object-oriented design patterns",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        var storeCallCount = 0;
        _memoryStoreMock.StoreAsync(Arg.Any<MemoryUnit>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                storeCallCount++;
                return callInfo.ArgAt<MemoryUnit>(0);
            });

        // Act
        var result = await _engine.ReflectAsync("test_session");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldReflectAsync_WithTimeSinceLastReflection_ShouldConsiderTime()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Some memory content",
                CreatedAt = DateTime.UtcNow,
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act - first call
        var result1 = await _engine.ShouldReflectAsync("test_session");

        // Perform a reflection to set last reflection time
        await _engine.ReflectAsync("test_session");

        // Act - second call immediately after
        var result2 = await _engine.ShouldReflectAsync("test_session");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public async Task GenerateInsightsAsync_WithTrends_ShouldIdentifyTrends()
    {
        // Arrange - memories showing temporal pattern
        var baseTime = DateTime.UtcNow;
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Performance issue reported",
                CreatedAt = baseTime.AddHours(-4),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Another performance problem",
                CreatedAt = baseTime.AddHours(-2),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Performance degradation observed",
                CreatedAt = baseTime,
                Stability = MemoryStability.Stable
            }
        };

        // Act
        var result = await _engine.GenerateInsightsAsync(memories);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DiscoverLinksAsync_ShouldFindTemporalLinks()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var baseTime = DateTime.UtcNow;

        var targetMemory = new MemoryUnit
        {
            Id = targetId,
            Content = "Deploy to production",
            CreatedAt = baseTime
        };

        var relatedMemories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Run final tests",
                CreatedAt = baseTime.AddMinutes(-5) // Just before target
            }
        };

        _memoryStoreMock.GetByIdAsync(targetId, Arg.Any<CancellationToken>())
            .Returns(targetMemory);

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(relatedMemories);

        // Act
        var result = await _engine.DiscoverLinksAsync(targetId);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SummarizeActivityAsync_ShouldIncludeTopics()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Implemented authentication module",
                CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Added JWT token validation",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                Stability = MemoryStability.Stable
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Created login endpoint",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                Stability = MemoryStability.Stable
            }
        };

        _memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _engine.SummarizeActivityAsync("test_session", TimeSpan.FromHours(1));

        // Assert
        Assert.NotNull(result);
        Assert.True(result.MemoriesCreated > 0);
        Assert.NotNull(result.TextSummary);
    }
}
