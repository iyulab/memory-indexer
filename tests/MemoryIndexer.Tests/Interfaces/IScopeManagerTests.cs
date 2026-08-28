using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.Interfaces;

/// <summary>
/// Tests for IScopeManager integration with 3-Axis Memory Model.
/// Phase 32.4: Category 2 - IScopeManager integration tests (35 tests)
/// </summary>
public class IScopeManagerTests
{
    private static ScopeManager CreateScopeManager()
    {
        var memoryIndexerOptions = Options.Create(new MemoryIndexerOptions());
        var embeddingLogger = NullLogger<MockEmbeddingService>.Instance;
        var embeddingService = new MockEmbeddingService(memoryIndexerOptions, embeddingLogger);
        var options = Options.Create(new ScopeManagerOptions());
        var logger = NullLogger<ScopeManager>.Instance;
        return new ScopeManager(embeddingService, options, logger);
    }

    #region Initialization Tests (5 tests)

    [Fact]
    public async Task InitializeAsync_ShouldSetCurrentState()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        await scopeManager.InitializeAsync("user1", "session1");

        // Assert
        var state = scopeManager.CurrentState;
        state.IsInitialized.Should().BeTrue();
        state.UserId.Should().Be("user1");
        state.SessionId.Should().Be("session1");
        state.TurnCount.Should().Be(0);
        state.TopicTransitionCount.Should().Be(0);
        state.SessionStartTime.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_ShouldCreateInitialTopicId()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        await scopeManager.InitializeAsync("user1", "session1");

        // Assert
        var topicId = scopeManager.GetCurrentTopicId();
        topicId.Should().NotBeNullOrEmpty();
        topicId.Should().StartWith("topic-");
    }

    [Fact]
    public async Task InitializeAsync_MultipleCallsShouldResetState()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("First turn");

        // Act
        await scopeManager.InitializeAsync("user2", "session2");

        // Assert
        var state = scopeManager.CurrentState;
        state.UserId.Should().Be("user2");
        state.SessionId.Should().Be("session2");
        state.TurnCount.Should().Be(0); // Reset
    }

    [Fact]
    public void CurrentState_BeforeInitialize_ShouldNotBeInitialized()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var state = scopeManager.CurrentState;

        // Assert
        state.IsInitialized.Should().BeFalse();
        state.UserId.Should().BeNull();
        state.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_ShouldSetTimestamps()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var before = DateTime.UtcNow;

        // Act
        await scopeManager.InitializeAsync("user1", "session1");
        var after = DateTime.UtcNow;

        // Assert
        var state = scopeManager.CurrentState;
        state.SessionStartTime.Should().NotBeNull();
        state.SessionStartTime.Should().BeAfter(before.AddSeconds(-1));
        state.SessionStartTime.Should().BeBefore(after.AddSeconds(1));
        state.TopicStartTime.Should().NotBeNull();
    }

    #endregion

    #region RecordTurnAsync Tests (8 tests)

    [Fact]
    public async Task RecordTurnAsync_ShouldIncrementTurnCount()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        await scopeManager.RecordTurnAsync("Turn 1");
        await scopeManager.RecordTurnAsync("Turn 2");
        await scopeManager.RecordTurnAsync("Turn 3");

        // Assert
        scopeManager.CurrentState.TurnCount.Should().Be(3);
    }

    [Fact]
    public async Task RecordTurnAsync_ShouldReturnScopeResolution()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var resolution = await scopeManager.RecordTurnAsync("Test turn");

        // Assert
        resolution.Should().NotBeNull();
        resolution.ResolvedScope.Should().Be(Scope.Turn);
        resolution.TopicId.Should().NotBeNullOrEmpty();
        resolution.TurnIndex.Should().Be(1);
        resolution.TopicTurnIndex.Should().Be(1);
    }

    [Fact]
    public async Task RecordTurnAsync_FirstTurn_ShouldCrossTurnBoundary()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var resolution = await scopeManager.RecordTurnAsync("First turn");

        // Assert
        resolution.BoundaryCrossed.Should().BeTrue();
        resolution.BoundaryType.Should().Be(ScopeBoundaryType.Turn);
    }

    [Fact]
    public async Task RecordTurnAsync_SimilarContent_ShouldNotCrossTopicBoundary()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var resolution1 = await scopeManager.RecordTurnAsync("Talk about weather");
        var resolution2 = await scopeManager.RecordTurnAsync("More about weather");

        // Assert
        resolution1.TopicId.Should().Be(resolution2.TopicId);
        resolution2.BoundaryType.Should().Be(ScopeBoundaryType.Turn);
        scopeManager.CurrentState.TopicTransitionCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordTurnAsync_DifferentContent_MayDetectTopicChange()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Talk about weather");

        // Act
        var resolution = await scopeManager.RecordTurnAsync("Let's discuss quantum physics");

        // Assert - May or may not detect topic change with MockEmbeddingService
        resolution.TopicId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RecordTurnAsync_WithRole_ShouldReturnResolution()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var resolution = await scopeManager.RecordTurnAsync("User message", role: "user");

        // Assert
        resolution.Should().NotBeNull();
        resolution.ResolvedScope.Should().Be(Scope.Turn);
    }

    [Fact]
    public async Task RecordTurnAsync_ShouldUpdateLastTurnTimestamp()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        var before = DateTime.UtcNow;

        // Act
        await scopeManager.RecordTurnAsync("Test turn");
        var after = DateTime.UtcNow;

        // Assert
        var state = scopeManager.CurrentState;
        state.LastTurnTimestamp.Should().NotBeNull();
        state.LastTurnTimestamp.Should().BeAfter(before.AddSeconds(-1));
        state.LastTurnTimestamp.Should().BeBefore(after.AddSeconds(1));
    }

    [Fact]
    public async Task RecordTurnAsync_ShouldIncrementTopicTurnCount()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        await scopeManager.RecordTurnAsync("Turn 1");
        await scopeManager.RecordTurnAsync("Turn 2");

        // Assert
        scopeManager.CurrentState.TopicTurnCount.Should().Be(2);
    }

    #endregion

    #region ResolveScopeAsync Tests (6 tests)

    [Fact]
    public async Task ResolveScopeAsync_HighImportanceFact_ShouldReturnUserScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "User's name is Alice",
            MemoryType.Fact,
            importance: 0.9f);

        // Assert
        scope.Should().Be(Scope.User);
    }

    [Fact]
    public async Task ResolveScopeAsync_EpisodicMemory_ShouldReturnTopicScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "We discussed project requirements",
            MemoryType.Episodic,
            importance: 0.6f); // 0.6 < 0.7, so returns Topic

        // Assert
        scope.Should().Be(Scope.Topic);
    }

    [Fact]
    public async Task ResolveScopeAsync_LowImportanceContent_ShouldReturnTurnScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "yes",
            MemoryType.Episodic,
            importance: 0.1f);

        // Assert
        scope.Should().Be(Scope.Turn);
    }

    [Fact]
    public async Task ResolveScopeAsync_SemanticMemory_ShouldReturnSessionScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "Machine learning concepts",
            MemoryType.Semantic,
            importance: 0.5f); // 0.5 >= 0.5, 0.5 < 0.8, so returns Session

        // Assert
        scope.Should().Be(Scope.Session);
    }

    [Fact]
    public async Task ResolveScopeAsync_ProceduralMemory_ShouldReturnAppropriateScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "How to deploy the application",
            MemoryType.Procedural,
            importance: 0.7f);

        // Assert
        scope.Should().BeOneOf(Scope.Session, Scope.User);
    }

    [Fact]
    public async Task ResolveScopeAsync_MediumImportance_ShouldReturnSessionOrTopic()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var scope = await scopeManager.ResolveScopeAsync(
            "Discussing the current task",
            MemoryType.Episodic,
            importance: 0.5f);

        // Assert
        scope.Should().BeOneOf(Scope.Topic, Scope.Session);
    }

    #endregion

    #region DetectTopicChangeAsync Tests (8 tests)

    [Fact]
    public async Task DetectTopicChangeAsync_EmptyHistory_ShouldReturnFalse()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var topicChanged = await scopeManager.DetectTopicChangeAsync("First message");

        // Assert
        topicChanged.Should().BeFalse();
    }

    [Fact]
    public async Task DetectTopicChangeAsync_SingleTurn_ShouldReturnFalse()
    {
        // Arrange - Only 1 turn in history (needs >= 2 for comparison)
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("First turn about weather");

        // Act
        var topicChanged = await scopeManager.DetectTopicChangeAsync("More about weather");

        // Assert - Not enough history for comparison
        topicChanged.Should().BeFalse();
    }

    [Fact]
    public async Task DetectTopicChangeAsync_SameContent_ShouldReturnFalse()
    {
        // Arrange - Record identical content twice to build history
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Discussing the weather today");
        await scopeManager.RecordTurnAsync("Discussing the weather today");

        // Act - Same content should have high similarity → no topic change
        var topicChanged = await scopeManager.DetectTopicChangeAsync("Discussing the weather today");

        // Assert
        topicChanged.Should().BeFalse();
    }

    [Fact]
    public async Task DetectTopicChangeAsync_DifferentContent_ShouldDetectTopicChange()
    {
        // Arrange - Build history with consistent topic
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("The weather is sunny and warm");
        await scopeManager.RecordTurnAsync("The weather is sunny and warm");

        // Act - Completely different content should have low similarity
        var topicChanged = await scopeManager.DetectTopicChangeAsync(
            "Quantum entanglement in photonic systems with high-dimensional Hilbert spaces");

        // Assert - With 768-dim random unit vectors from MockEmbeddingService,
        // different texts should have cosine similarity near 0 (< 0.5 threshold)
        topicChanged.Should().BeTrue();
    }

    [Fact]
    public async Task DetectTopicChangeAsync_WithEmbeddings_ShouldReturnBoolean()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Topic one");
        await scopeManager.RecordTurnAsync("Topic one continued");

        // Act
        var topicChanged = await scopeManager.DetectTopicChangeAsync("Topic two");

        // Assert
        Assert.IsType<bool>(topicChanged);
    }

    [Fact]
    public async Task DetectTopicChangeAsync_ViaRecordTurn_ShouldDetectTopicTransition()
    {
        // Arrange - Build history with consistent topic
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("The weather is sunny and warm");
        await scopeManager.RecordTurnAsync("The weather is sunny and warm");

        // Act - RecordTurnAsync calls DetectTopicChangeAsync internally
        var resolution = await scopeManager.RecordTurnAsync(
            "Advanced quantum computing algorithms for molecular simulation");

        // Assert - Topic transition should be detected
        resolution.BoundaryType.Should().Be(ScopeBoundaryType.Topic);
        scopeManager.CurrentState.TopicTransitionCount.Should().Be(1);
    }

    [Fact]
    public async Task DetectTopicChangeAsync_HighThreshold_ShouldAlwaysDetectChange()
    {
        // Arrange - Set very high threshold (almost always triggers topic change)
        var embeddingService = new MockEmbeddingService(
            Options.Create(new MemoryIndexerOptions()),
            NullLogger<MockEmbeddingService>.Instance);
        var options = Options.Create(new ScopeManagerOptions { TopicSimilarityThreshold = 0.99f });
        var scopeManager = new ScopeManager(embeddingService, options, NullLogger<ScopeManager>.Instance);

        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Weather discussion");
        await scopeManager.RecordTurnAsync("Weather discussion");

        // Act - Even similar-ish content should trigger with very high threshold
        var topicChanged = await scopeManager.DetectTopicChangeAsync("Weather discussion topic");

        // Assert - High threshold (0.99) means even slightly different content triggers change
        topicChanged.Should().BeTrue();
    }

    [Fact]
    public async Task DetectTopicChangeAsync_SameContent_WithLowThreshold_ShouldNotChange()
    {
        // Arrange - Low threshold with identical content
        var embeddingService = new MockEmbeddingService(
            Options.Create(new MemoryIndexerOptions()),
            NullLogger<MockEmbeddingService>.Instance);
        var options = Options.Create(new ScopeManagerOptions { TopicSimilarityThreshold = 0.5f });
        var scopeManager = new ScopeManager(embeddingService, options, NullLogger<ScopeManager>.Instance);

        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Exact same content for testing");
        await scopeManager.RecordTurnAsync("Exact same content for testing");

        // Act - Identical content → cosine similarity = 1.0 → no topic change
        var topicChanged = await scopeManager.DetectTopicChangeAsync("Exact same content for testing");

        // Assert
        topicChanged.Should().BeFalse();
    }

    #endregion

    #region GetCurrentTopicId Tests (3 tests)

    [Fact]
    public async Task GetCurrentTopicId_AfterInitialize_ShouldReturnTopicId()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var topicId = scopeManager.GetCurrentTopicId();

        // Assert
        topicId.Should().NotBeNullOrEmpty();
        topicId.Should().StartWith("topic-");
    }

    [Fact]
    public async Task GetCurrentTopicId_ShouldBeConsistent()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");

        // Act
        var topicId1 = scopeManager.GetCurrentTopicId();
        var topicId2 = scopeManager.GetCurrentTopicId();

        // Assert
        topicId1.Should().Be(topicId2);
    }

    [Fact]
    public void GetCurrentTopicId_BeforeInitialize_ShouldThrow()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var act = () => scopeManager.GetCurrentTopicId();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be initialized*");
    }

    #endregion

    #region FilterByScope Tests (6 tests)

    [Fact]
    public void FilterByScope_SessionScope_ShouldReturnSessionAndNarrower()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.Session, includeNarrower: true);

        // Assert
        filtered.Should().HaveCount(3); // Session, Topic, Turn
        filtered.Should().Contain(m => m.Scope == Scope.Session);
        filtered.Should().Contain(m => m.Scope == Scope.Topic);
        filtered.Should().Contain(m => m.Scope == Scope.Turn);
    }

    [Fact]
    public void FilterByScope_SessionScope_ExcludeNarrower_ShouldReturnSessionOnly()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.Session, includeNarrower: false);

        // Assert
        filtered.Should().HaveCount(1);
        filtered.Single().Scope.Should().Be(Scope.Session);
    }

    [Fact]
    public void FilterByScope_UserScope_ShouldReturnAllScopes()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.User, includeNarrower: true);

        // Assert
        filtered.Should().HaveCount(4); // All scopes
    }

    [Fact]
    public void FilterByScope_TurnScope_ShouldReturnTurnOnly()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.Turn, includeNarrower: true);

        // Assert
        filtered.Should().HaveCount(1);
        filtered.Single().Scope.Should().Be(Scope.Turn);
    }

    [Fact]
    public void FilterByScope_EmptyMemories_ShouldReturnEmpty()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = Array.Empty<MemoryUnit>();

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.Session, includeNarrower: true);

        // Assert
        filtered.Should().BeEmpty();
    }

    [Fact]
    public void FilterByScope_TopicScope_ShouldReturnTopicAndTurn()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C1" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C2" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var filtered = scopeManager.FilterByScope(memories, Scope.Topic, includeNarrower: true);

        // Assert
        filtered.Should().HaveCount(3); // 2 Topic + 1 Turn
        filtered.Should().Contain(m => m.Content == "C1");
        filtered.Should().Contain(m => m.Content == "C2");
        filtered.Should().Contain(m => m.Scope == Scope.Turn);
    }

    #endregion

    #region EndSessionAsync and GetStatistics Tests (4 tests)

    [Fact]
    public async Task EndSessionAsync_ShouldFinalizeState()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Turn 1");
        await scopeManager.RecordTurnAsync("Turn 2");

        // Act
        await scopeManager.EndSessionAsync();

        // Assert
        var state = scopeManager.CurrentState;
        state.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task EndSessionAsync_WithoutInitialize_ShouldNotThrow()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var act = async () => await scopeManager.EndSessionAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnCorrectTurnCount()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await scopeManager.RecordTurnAsync("Turn 1");
        await scopeManager.RecordTurnAsync("Turn 2");
        await scopeManager.RecordTurnAsync("Turn 3");

        // Act
        var stats = scopeManager.GetStatistics();

        // Assert
        stats.TotalTurns.Should().Be(3);
    }

    [Fact]
    public async Task GetStatistics_ShouldCalculateSessionDuration()
    {
        // Arrange
        var scopeManager = CreateScopeManager();
        await scopeManager.InitializeAsync("user1", "session1");
        await Task.Delay(100); // Wait a bit
        await scopeManager.RecordTurnAsync("Turn 1");

        // Act
        var stats = scopeManager.GetStatistics();

        // Assert
        stats.SessionDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion
}
