using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Promotion;

public class ShortTermMemoryOrchestratorServiceTests
{
    private readonly IShortTermMemory _workingMemoryMock;
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly WorkingMemoryOrchestratorOptions _options;
    private readonly ShortTermMemoryOrchestratorService _orchestrator;

    public ShortTermMemoryOrchestratorServiceTests()
    {
        _workingMemoryMock = Substitute.For<IShortTermMemory>();
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();

        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[768].AsMemory());

        _options = new WorkingMemoryOrchestratorOptions
        {
            IdleTimeout = TimeSpan.FromMinutes(10),
            TokenThreshold = 2000,
            TurnThreshold = 10,
            EnableTopicChangeDetection = true,
            TopicChangeSimilarityThreshold = 0.5f,
            SummarizeBeforeArchival = true
        };

        _orchestrator = new ShortTermMemoryOrchestratorService(
            _workingMemoryMock,
            _embeddingServiceMock,
            Options.Create(_options),
            NullLogger<ShortTermMemoryOrchestratorService>.Instance);
    }

    #region RecordActivityAsync Tests

    [Fact]
    public async Task RecordActivityAsync_NewUser_CreatesState()
    {
        // Arrange
        const string userId = "user-1";
        var memory = CreateTestMemory(userId);

        // Act
        await _orchestrator.RecordActivityAsync(userId, "session-1", memory);

        // Assert
        var state = _orchestrator.GetState(userId);
        state.UserId.Should().Be(userId);
        state.TurnCount.Should().Be(1);
        state.MemoryCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordActivityAsync_MultipleActivities_AccumulatesState()
    {
        // Arrange
        const string userId = "user-1";

        // Act
        for (int i = 0; i < 5; i++)
        {
            var memory = CreateTestMemory(userId, $"Content {i}");
            await _orchestrator.RecordActivityAsync(userId, "session-1", memory);
        }

        // Assert
        var state = _orchestrator.GetState(userId);
        state.TurnCount.Should().Be(5);
        state.MemoryCount.Should().Be(5);
        state.TotalTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RecordActivityAsync_NullUserId_ThrowsArgumentNullException()
    {
        // Arrange
        var memory = CreateTestMemory("user-1");

        // Act & Assert
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _orchestrator.RecordActivityAsync(null!, "session-1", memory));
    }

    [Fact]
    public async Task RecordActivityAsync_NullMemory_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _orchestrator.RecordActivityAsync("user-1", "session-1", null!));
    }

    #endregion

    #region CheckArchivalTriggerAsync Tests

    [Fact]
    public async Task CheckArchivalTriggerAsync_NoState_ReturnsNull()
    {
        // Act
        var trigger = await _orchestrator.CheckArchivalTriggerAsync("nonexistent-user");

        // Assert
        trigger.Should().BeNull();
    }

    [Fact]
    public async Task CheckArchivalTriggerAsync_BelowAllThresholds_ReturnsNull()
    {
        // Arrange
        const string userId = "user-1";
        var memory = CreateTestMemory(userId, "Short content");
        await _orchestrator.RecordActivityAsync(userId, "session-1", memory);

        // Act
        var trigger = await _orchestrator.CheckArchivalTriggerAsync(userId);

        // Assert
        trigger.Should().BeNull();
    }

    [Fact]
    public async Task CheckArchivalTriggerAsync_TurnThresholdMet_ReturnsTurnThreshold()
    {
        // Arrange - use low threshold for testing
        var options = new WorkingMemoryOrchestratorOptions { TurnThreshold = 3 };
        var orchestrator = CreateOrchestratorWithOptions(options);

        const string userId = "user-1";
        for (int i = 0; i < 3; i++)
        {
            await orchestrator.RecordActivityAsync(userId, "session-1", CreateTestMemory(userId));
        }

        // Act
        var trigger = await orchestrator.CheckArchivalTriggerAsync(userId);

        // Assert
        trigger.Should().Be(WorkingPromotionTrigger.TurnThreshold);
    }

    [Fact]
    public async Task CheckArchivalTriggerAsync_TokenThresholdMet_ReturnsTokenThreshold()
    {
        // Arrange - use low threshold for testing
        var options = new WorkingMemoryOrchestratorOptions { TokenThreshold = 100 };
        var orchestrator = CreateOrchestratorWithOptions(options);

        const string userId = "user-1";
        // Content with ~100 tokens (400 characters)
        var longContent = new string('a', 400);
        await orchestrator.RecordActivityAsync(userId, "session-1", CreateTestMemory(userId, longContent));

        // Act
        var trigger = await orchestrator.CheckArchivalTriggerAsync(userId);

        // Assert
        trigger.Should().Be(WorkingPromotionTrigger.TokenThreshold);
    }

    #endregion

    #region ArchiveToSessionAsync Tests

    [Fact]
    public async Task ArchiveToSessionAsync_NoState_ReturnsEmpty()
    {
        // Act
        var result = await _orchestrator.ArchiveToSessionAsync(
            "nonexistent-user",
            WorkingPromotionTrigger.Manual);

        // Assert
        result.Success.Should().BeTrue();
        result.MemoriesArchived.Should().Be(0);
    }

    [Fact]
    public async Task ArchiveToSessionAsync_WithMemories_ArchivesSuccessfully()
    {
        // Arrange
        const string userId = "user-1";
        for (int i = 0; i < 3; i++)
        {
            await _orchestrator.RecordActivityAsync(
                userId, "session-1", CreateTestMemory(userId, $"Content {i}"));
        }

        // Act
        var result = await _orchestrator.ArchiveToSessionAsync(
            userId, WorkingPromotionTrigger.Manual);

        // Assert
        result.Success.Should().BeTrue();
        result.MemoriesArchived.Should().Be(3);
        result.Trigger.Should().Be(WorkingPromotionTrigger.Manual);
    }

    [Fact]
    public async Task ArchiveToSessionAsync_WithSummarization_CreatesSummary()
    {
        // Arrange
        const string userId = "user-1";
        for (int i = 0; i < 3; i++)
        {
            await _orchestrator.RecordActivityAsync(
                userId, "session-1", CreateTestMemory(userId, $"Content {i}"));
        }

        // Act
        var result = await _orchestrator.ArchiveToSessionAsync(
            userId, WorkingPromotionTrigger.Manual, summarize: true);

        // Assert
        result.Success.Should().BeTrue();
        result.SummaryId.Should().NotBeNull();
        // Embedding should be generated for the summary
        await _embeddingServiceMock.Received().GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ArchiveToSessionAsync_WithoutSummarization_NoSummary()
    {
        // Arrange
        const string userId = "user-1";
        await _orchestrator.RecordActivityAsync(
            userId, "session-1", CreateTestMemory(userId));

        // Act
        var result = await _orchestrator.ArchiveToSessionAsync(
            userId, WorkingPromotionTrigger.Manual, summarize: false);

        // Assert
        result.Success.Should().BeTrue();
        result.SummaryId.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveToSessionAsync_ClearsState()
    {
        // Arrange
        const string userId = "user-1";
        await _orchestrator.RecordActivityAsync(
            userId, "session-1", CreateTestMemory(userId));

        // Act
        await _orchestrator.ArchiveToSessionAsync(
            userId, WorkingPromotionTrigger.Manual);

        // Assert
        var state = _orchestrator.GetState(userId);
        state.MemoryCount.Should().Be(0);
        state.TurnCount.Should().Be(0);
        state.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task ArchiveToSessionAsync_DemotesFromWorkingMemory()
    {
        // Arrange
        const string userId = "user-1";
        var memory = CreateTestMemory(userId);
        await _orchestrator.RecordActivityAsync(userId, "session-1", memory);

        // Act
        await _orchestrator.ArchiveToSessionAsync(
            userId, WorkingPromotionTrigger.Manual);

        // Assert
        await _workingMemoryMock.Received().DemoteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetState Tests

    [Fact]
    public void GetState_NoState_ReturnsEmptyState()
    {
        // Act
        var state = _orchestrator.GetState("nonexistent-user");

        // Assert
        state.UserId.Should().Be("nonexistent-user");
        state.MemoryCount.Should().Be(0);
        state.TurnCount.Should().Be(0);
        state.TotalTokens.Should().Be(0);
        state.TriggerSatisfied.Should().BeFalse();
    }

    [Fact]
    public async Task GetState_WithActivity_ReturnsCorrectState()
    {
        // Arrange
        const string userId = "user-1";
        await _orchestrator.RecordActivityAsync(
            userId, "session-1", CreateTestMemory(userId, "Test content here"));

        // Act
        var state = _orchestrator.GetState(userId);

        // Assert
        state.UserId.Should().Be(userId);
        state.SessionId.Should().Be("session-1");
        state.MemoryCount.Should().Be(1);
        state.TurnCount.Should().Be(1);
        state.TotalTokens.Should().BeGreaterThan(0);
        state.LastActivityTime.Should().NotBeNull();
    }

    [Fact]
    public async Task GetState_TriggerSatisfied_ShowsTrigger()
    {
        // Arrange
        var options = new WorkingMemoryOrchestratorOptions { TurnThreshold = 2 };
        var orchestrator = CreateOrchestratorWithOptions(options);

        const string userId = "user-1";
        await orchestrator.RecordActivityAsync(userId, "session-1", CreateTestMemory(userId));
        await orchestrator.RecordActivityAsync(userId, "session-1", CreateTestMemory(userId));

        // Act
        var state = orchestrator.GetState(userId);

        // Assert
        state.TriggerSatisfied.Should().BeTrue();
        state.SatisfiedTrigger.Should().Be(WorkingPromotionTrigger.TurnThreshold);
    }

    #endregion

    #region GetActiveUserIds Tests

    [Fact]
    public void GetActiveUserIds_NoUsers_ReturnsEmpty()
    {
        // Act
        var userIds = _orchestrator.GetActiveUserIds();

        // Assert
        userIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveUserIds_WithUsers_ReturnsAllUserIds()
    {
        // Arrange
        await _orchestrator.RecordActivityAsync("user-1", "s1", CreateTestMemory("user-1"));
        await _orchestrator.RecordActivityAsync("user-2", "s2", CreateTestMemory("user-2"));
        await _orchestrator.RecordActivityAsync("user-3", "s3", CreateTestMemory("user-3"));

        // Act
        var userIds = _orchestrator.GetActiveUserIds();

        // Assert
        userIds.Should().HaveCount(3);
        userIds.Should().Contain(["user-1", "user-2", "user-3"]);
    }

    #endregion

    #region ClearState Tests

    [Fact]
    public async Task ClearState_ExistingUser_ClearsState()
    {
        // Arrange
        const string userId = "user-1";
        await _orchestrator.RecordActivityAsync(userId, "session-1", CreateTestMemory(userId));

        // Act
        _orchestrator.ClearState(userId);

        // Assert
        var state = _orchestrator.GetState(userId);
        state.MemoryCount.Should().Be(0);
    }

    [Fact]
    public void ClearState_NonexistentUser_NoError()
    {
        // Act & Assert (should not throw)
        _orchestrator.ClearState("nonexistent-user");
    }

    #endregion

    #region Helper Methods

    private static MemoryUnit CreateTestMemory(string userId, string content = "Test content")
    {
        return new MemoryUnit
        {
            Content = content,
            UserId = userId,
            Embedding = new float[768].AsMemory(),
            Type = MemoryType.Episodic,
            Tier = Tier.Short,
            Stability = MemoryStability.Volatile
        };
    }

    private ShortTermMemoryOrchestratorService CreateOrchestratorWithOptions(WorkingMemoryOrchestratorOptions options)
    {
        return new ShortTermMemoryOrchestratorService(
            _workingMemoryMock,
            _embeddingServiceMock,
            Options.Create(options),
            NullLogger<ShortTermMemoryOrchestratorService>.Instance);
    }

    #endregion
}
