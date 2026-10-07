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
        _embeddingServiceMock.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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

    // Recall the caller cancelled is not an empty recall: each tier used to log the cancellation as a store failure and
    // move on, so a cancelled caller got a context with nothing in it instead of an exception.
    [Fact]
    public async Task GetRecentTurnsAsync_CallerCancelsDuringTheBufferRead_Throws()
    {
        using var cts = new CancellationTokenSource();
        _bufferMock.GetPendingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<SensoryMemory>>>(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => _builder.GetRecentTurnsAsync(UserId, SessionId, maxTokens: 1000, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }


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

        await _embeddingServiceMock.Received(1).GenerateQueryEmbeddingAsync(query, Arg.Any<CancellationToken>());
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

    // A user asks, in one conversation, to have something remembered; a later conversation of the same user in the same
    // namespace asks about it. The utterance is stored Episodic (the classifier's reading of a "User: ..." turn).
    [Fact]
    public async Task BuildAsync_FromALaterSession_RecallsWhatAnEarlierSessionStored_WithinTheNamespaceOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var (builder, store) = BuilderOverInMemoryStore();
        await store.StoreAsync(Stored("User: 내 차 번호는 12가 3456이야. 기억해 줘.", MemoryType.Episodic, "session-1", "desk-a"), ct);
        await store.StoreAsync(Stored("User: 회의실 예약은 금요일 오후 세 시야.", MemoryType.Episodic, "session-1", "desk-b"), ct);

        var inA = await builder.BuildAsync(
            new ContextRequest(UserId, "session-2", "내 차 번호가 뭐였지?", new ContextBudget(1000)) { Namespace = "desk-a" }, ct: ct);
        var inC = await builder.BuildAsync(
            new ContextRequest(UserId, "session-2", "내 차 번호가 뭐였지?", new ContextBudget(1000)) { Namespace = "desk-c" }, ct: ct);

        inA.Items.Select(i => i.Content).Should().Equal("User: 내 차 번호는 12가 3456이야. 기억해 줘.");
        inA.Items[0].MemoryType.Should().Be(MemoryType.Episodic);
        inC.Items.Should().BeEmpty("another namespace's memories never cross over");
    }

    [Fact]
    public async Task BuildAsync_CurrentSessionEpisodic_IsReturnedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var (builder, store) = BuilderOverInMemoryStore();
        await store.StoreAsync(Stored("User: 오늘 회의는 3시야.", MemoryType.Episodic, SessionId, null), ct);

        var bundle = await builder.BuildAsync(new ContextRequest(UserId, SessionId, "회의 몇 시?", new ContextBudget(1000)), ct: ct);

        bundle.Items.Should().ContainSingle().Which.Source.Should().Be(ContextItemSource.Episodic,
            "the session slot owns this session's experience; the query slot must not repeat it");
    }

    private (ContextBuilder Builder, MemoryIndexer.InMemory.InMemoryMemoryStore Store) BuilderOverInMemoryStore()
    {
        var store = new MemoryIndexer.InMemory.InMemoryMemoryStore(NullLogger<MemoryIndexer.InMemory.InMemoryMemoryStore>.Instance);
        var embedder = Substitute.For<IEmbeddingService>();
        embedder.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UnitVector());
        embedder.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(UnitVector());
        _bufferMock.GetPendingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<SensoryMemory>());
        _shortTermMemoryMock.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<MemoryUnit>());
        return (new ContextBuilder(_bufferMock, _shortTermMemoryMock, store, embedder, _tokenCounter,
            NullLogger<ContextBuilder>.Instance), store);
    }

    private static MemoryUnit Stored(string content, MemoryType type, string sessionId, string? ns) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SessionId = sessionId,
        Namespace = ns,
        Content = content,
        Type = type,
        Tier = Tier.Long,
        Scope = Scope.Topic, // what the consumer's stored row carried
        Role = "user",
        CreatedAt = DateTime.UtcNow,
        Embedding = UnitVector(),
    };

    private static float[] UnitVector()
    {
        var v = new float[1024];
        v[0] = 1f;
        return v;
    }


    [Fact]
    public async Task BuildAsync_WithANamespace_PassesItToEverySemanticEpisodicAndFactQuery()
    {
        SetupEmptyMocks();
        var request = new ContextRequest(UserId, SessionId, "test query", new ContextBudget(1000)) { Namespace = "workspace-b" };

        await _builder.BuildAsync(request, ct: TestContext.Current.CancellationToken);

        await _memoryStoreMock.Received(1).SearchAsync(
            Arg.Any<ReadOnlyMemory<float>>(), Arg.Is<MemorySearchOptions>(o => o.Namespace == "workspace-b"), Arg.Any<CancellationToken>());
        await _memoryStoreMock.Received(2).GetAllAsync(
            UserId, Arg.Is<MemoryFilterOptions>(o => o.Namespace == "workspace-b"), Arg.Any<CancellationToken>());
        await _memoryStoreMock.DidNotReceive().GetAllAsync(
            Arg.Any<string>(), Arg.Is<MemoryFilterOptions>(o => o.Namespace != "workspace-b"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserFactsAsync_WithANamespace_DoesNotReturnAnotherNamespacesFacts()
    {
        var store = new MemoryIndexer.InMemory.InMemoryMemoryStore(NullLogger<MemoryIndexer.InMemory.InMemoryMemoryStore>.Instance);
        var inA = CreateMemory("Budget owner is Kim", MemoryType.Fact);
        inA.Namespace = "workspace-a";
        var inB = CreateMemory("Budget owner is Lee", MemoryType.Fact);
        inB.Namespace = "workspace-b";
        await store.StoreAsync(inA, TestContext.Current.CancellationToken);
        await store.StoreAsync(inB, TestContext.Current.CancellationToken);
        var builder = new ContextBuilder(_bufferMock, _shortTermMemoryMock, store, _embeddingServiceMock, _tokenCounter,
            NullLogger<ContextBuilder>.Instance);

        var facts = await builder.GetUserFactsAsync(UserId, 1000, "workspace-b", TestContext.Current.CancellationToken);

        facts.Select(f => f.Content).Should().Equal("Budget owner is Lee");
    }

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
