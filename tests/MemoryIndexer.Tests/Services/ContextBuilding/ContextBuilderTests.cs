using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services.ContextBuilding;
using MemoryIndexer.Services.ContextStrategies;
using MemoryIndexer.Services.TokenCounting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Tests.Services.ContextBuilding;

public class ContextBuilderTests
{
    private readonly IBuffer _bufferMock;
    private readonly IShortTermMemory _shortTermMemoryMock;
    private readonly IMemoryStore _memoryStoreMock;
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly ITokenCounter _tokenCounter;
    private readonly ContextBuilder _builder;

    private const string UserId = "test-user";
    private const string SessionId = "test-session";

    public ContextBuilderTests()
    {
        _bufferMock = Substitute.For<IBuffer>();
        _shortTermMemoryMock = Substitute.For<IShortTermMemory>();
        _memoryStoreMock = Substitute.For<IMemoryStore>();
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _tokenCounter = new ApproximateTokenCounter();

        // Setup default embedding service behavior
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[1024]);

        _builder = new ContextBuilder(
            _bufferMock,
            _shortTermMemoryMock,
            _memoryStoreMock,
            _embeddingServiceMock,
            _tokenCounter,
            NullLogger<ContextBuilder>.Instance);
    }

    #region GetRecentTurnsAsync Tests

    [Fact]
    public async Task GetRecentTurnsAsync_WithBufferItems_ShouldReturnRecentContext()
    {
        // Arrange
        var bufferItems = new List<SensoryMemory>
        {
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "Hello from user",
                Role = "user",
                Timestamp = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "Hello from assistant",
                Role = "assistant",
                Timestamp = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(bufferItems);
        _shortTermMemoryMock.GetAllAsync(TestContext.Current.CancellationToken).Returns(Array.Empty<MemoryUnit>());

        // Act
        var result = await _builder.GetRecentTurnsAsync(UserId, SessionId, maxTokens: 1000, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(2);
        result.All(i => i.Source == ContextItemSource.Recent).Should().BeTrue();
        result[0].Role.Should().Be("user");
        result[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task GetRecentTurnsAsync_WithTokenLimit_ShouldRespectBudget()
    {
        // Arrange - create items that exceed token limit
        // Note: Items are ordered by Timestamp descending (newest first)
        // "Short" = 5 chars / 4 = 2 tokens (ceiling) - NEWEST (processed first)
        // "This is a much longer message..." = 88 chars / 4 = 22 tokens - OLDER (processed second, skipped)
        var bufferItems = new List<SensoryMemory>
        {
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "This is a much longer message that should be excluded due to token limits being exceeded",
                Role = "user",
                Timestamp = DateTime.UtcNow.AddMinutes(-2)  // OLDER - will be processed second
            },
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "Short",  // 2 tokens
                Role = "assistant",
                Timestamp = DateTime.UtcNow.AddMinutes(-1)  // NEWER - will be processed first
            }
        };

        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(bufferItems);
        _shortTermMemoryMock.GetAllAsync(TestContext.Current.CancellationToken).Returns(Array.Empty<MemoryUnit>());

        // Act - only 5 tokens allowed (2 for first item fits, 22 for second doesn't fit)
        var result = await _builder.GetRecentTurnsAsync(UserId, SessionId, maxTokens: 5, ct: TestContext.Current.CancellationToken);

        // Assert - only the newer (Short) item should fit
        result.Should().HaveCount(1);
        result[0].Content.Should().Be("Short");
    }

    [Fact]
    public async Task GetRecentTurnsAsync_FiltersToCorrectSession()
    {
        // Arrange
        var bufferItems = new List<SensoryMemory>
        {
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "Same session",
                Role = "user",
                Timestamp = DateTime.UtcNow
            },
            new()
            {
                UserId = UserId,
                SessionId = "other-session",
                Content = "Different session",
                Role = "user",
                Timestamp = DateTime.UtcNow
            }
        };

        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(bufferItems);
        _shortTermMemoryMock.GetAllAsync(TestContext.Current.CancellationToken).Returns(Array.Empty<MemoryUnit>());

        // Act
        var result = await _builder.GetRecentTurnsAsync(UserId, SessionId, maxTokens: 1000, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
        result[0].Content.Should().Be("Same session");
    }

    [Fact]
    public async Task GetRecentTurnsAsync_ShortTermMemory_FiltersToCorrectSession()
    {
        // Arrange - This test verifies session isolation for ShortTermMemory (T1)
        // BUG FIX: Previously, ShortTermMemory items from other sessions were included
        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SensoryMemory>());

        var shortTermItems = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SessionId = SessionId,  // Same session
                Content = "Game question from current session",
                Type = MemoryType.Episodic,
                Tier = Tier.Short,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                SessionId = "other-session",  // Different session - should be excluded!
                Content = "Game question from previous session",
                Type = MemoryType.Episodic,
                Tier = Tier.Short,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        _shortTermMemoryMock.GetAllAsync(TestContext.Current.CancellationToken).Returns(shortTermItems);

        // Act
        var result = await _builder.GetRecentTurnsAsync(UserId, SessionId, maxTokens: 1000, ct: TestContext.Current.CancellationToken);

        // Assert - Only items from current session should be included
        result.Should().HaveCount(1);
        result[0].Content.Should().Be("Game question from current session");
    }

    #endregion

    #region GetSemanticContextAsync Tests

    [Fact]
    public async Task GetSemanticContextAsync_ShouldGenerateEmbeddingAndSearch()
    {
        // Arrange
        const string query = "What is the weather?";
        var searchResults = new List<MemorySearchResult>
        {
            new()
            {
                Memory = CreateMemory("Weather is sunny", MemoryType.Semantic),
                Score = 0.9f
            }
        };

        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(searchResults);

        // Act
        var result = await _builder.GetSemanticContextAsync(UserId, query, maxTokens: 1000, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
        result[0].Source.Should().Be(ContextItemSource.Semantic);
        result[0].Score.Should().Be(0.9f);

        await _embeddingServiceMock.Received(1).GenerateEmbeddingAsync(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSemanticContextAsync_WithTokenLimit_ShouldRespectBudget()
    {
        // Arrange
        var searchResults = new List<MemorySearchResult>
        {
            new()
            {
                Memory = CreateMemory("Short", MemoryType.Semantic),
                Score = 0.9f
            },
            new()
            {
                Memory = CreateMemory("This is a very long memory that should be skipped due to token limits", MemoryType.Semantic),
                Score = 0.8f
            }
        };

        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(searchResults);

        // Act
        var result = await _builder.GetSemanticContextAsync(UserId, "query", maxTokens: 5, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Should().HaveCount(1);
        result[0].Content.Should().Be("Short");
    }

    #endregion

    #region BuildAsync Tests

    [Fact]
    public async Task BuildAsync_WithDefaultStrategy_ShouldUseBalanced()
    {
        // Arrange
        SetupEmptyMocks();

        var request = new ContextRequest(UserId, SessionId, "test query", new ContextBudget(1000));

        // Act
        var result = await _builder.BuildAsync(request, ct: TestContext.Current.CancellationToken);

        // Assert - Balanced strategy used, verify proportional allocation
        result.Should().NotBeNull();
        result.TotalTokens.Should().Be(0); // No items returned from mocks
    }

    [Fact]
    public async Task BuildAsync_WithStrategyName_ShouldUseCorrectStrategy()
    {
        // Arrange
        SetupEmptyMocks();

        var request = new ContextRequest(UserId, SessionId, "test query", new ContextBudget(1000));

        // Act
        var result = await _builder.BuildAsync(request, "RecentHeavy", TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAsync_WithInvalidStrategyName_ShouldFallbackToBalanced()
    {
        // Arrange
        SetupEmptyMocks();

        var request = new ContextRequest(UserId, SessionId, "test query", new ContextBudget(1000));

        // Act
        var result = await _builder.BuildAsync(request, "NonExistentStrategy", TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAsync_WithExplicitBudget_ShouldOverrideStrategy()
    {
        // Arrange
        SetupEmptyMocks();

        var budget = new ContextBudget(
            TotalTokens: 1000,
            RecentTokens: 500,
            SemanticTokens: 300,
            EpisodicTokens: 100,
            FactTokens: 100);
        var request = new ContextRequest(UserId, SessionId, "test query", budget);

        // Act
        var result = await _builder.BuildAsync(request, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAsync_ShouldCombineAllSources()
    {
        // Arrange
        var bufferItems = new List<SensoryMemory>
        {
            new()
            {
                UserId = UserId,
                SessionId = SessionId,
                Content = "Recent message",
                Role = "user",
                Timestamp = DateTime.UtcNow
            }
        };

        var searchResults = new List<MemorySearchResult>
        {
            new()
            {
                Memory = CreateMemory("Semantic result", MemoryType.Semantic),
                Score = 0.8f
            }
        };

        var userFacts = new List<MemoryUnit>
        {
            CreateMemory("User fact", MemoryType.Fact)
        };

        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(bufferItems);
        _shortTermMemoryMock.GetAllAsync(TestContext.Current.CancellationToken).Returns(Array.Empty<MemoryUnit>());
        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(searchResults);
        _memoryStoreMock.GetAllAsync(UserId, Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(userFacts);

        var request = new ContextRequest(UserId, SessionId, "test query", new ContextBudget(1000));

        // Act
        var result = await _builder.BuildAsync(request, ct: TestContext.Current.CancellationToken);

        // Assert
        result.Items.Should().HaveCountGreaterThan(0);
        result.TotalTokens.Should().BeGreaterThan(0);
    }

    #endregion

    #region RegisterStrategy Tests

    [Fact]
    public async Task RegisterStrategy_ShouldAllowCustomStrategy()
    {
        // Arrange
        SetupEmptyMocks();

        var customStrategy = new CustomStrategy("MyCustom", 0.5, 0.3, 0.1, 0.1);
        _builder.RegisterStrategy(customStrategy);

        var request = new ContextRequest(UserId, SessionId, "test", new ContextBudget(1000));

        // Act
        var result = await _builder.BuildAsync(request, "MyCustom", TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region ContextBundle Tests

    [Fact]
    public void ContextBundle_ItemCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var items = new List<ContextItem>
        {
            new("a", 1, ContextItemSource.Recent, null, DateTime.UtcNow),
            new("b", 1, ContextItemSource.Semantic, 0.5f, DateTime.UtcNow)
        };

        var bundle = new ContextBundle(
            Content: "test",
            TotalTokens: 2,
            Breakdown: new ContextBreakdown(1, 1, 0, 0),
            Items: items);

        // Act & Assert
        bundle.ItemCount.Should().Be(2);
    }

    [Fact]
    public void ContextBreakdown_Total_ShouldSumAll()
    {
        // Arrange
        var breakdown = new ContextBreakdown(100, 200, 50, 150);

        // Act & Assert
        breakdown.Total.Should().Be(500);
    }

    #endregion

    #region Helper Methods

    private void SetupEmptyMocks()
    {
        _bufferMock.GetPendingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SensoryMemory>());
        _shortTermMemoryMock.GetAllAsync()
            .Returns(Array.Empty<MemoryUnit>());
        _memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<MemorySearchResult>());
        _memoryStoreMock.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());
    }

    private static MemoryUnit CreateMemory(string content, MemoryType type)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Content = content,
            Type = type,
            Tier = Tier.Long,
            Scope = Scope.Session,
            CreatedAt = DateTime.UtcNow,
            Embedding = new float[1024]
        };
    }

    #endregion
}
