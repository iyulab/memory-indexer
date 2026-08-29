using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.Services;

public class BufferServiceTests
{
    private readonly BufferService _buffer;
    private readonly SensoryBufferOptions _options;

    public BufferServiceTests()
    {
        _options = new SensoryBufferOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(60),
            TokenThreshold = 500,
            TurnThreshold = 3,
            MaxBufferSize = 100,
            MaxBufferTokens = 10000
        };

        var memoryOptions = new MemoryIndexerOptions
        {
            SensoryBuffer = _options
        };

        _buffer = new BufferService(
            Options.Create(memoryOptions),
            NullLogger<BufferService>.Instance);
    }

    #region Enqueue Tests

    [Fact]
    public async Task EnqueueAsync_ShouldAddItemToBuffer()
    {
        // Arrange
        const string userId = "user-1";
        const string content = "Hello, world!";

        // Act
        var result = await _buffer.EnqueueAsync(content, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Content.Should().Be(content);
        result.UserId.Should().Be(userId);
        result.Id.Should().NotBeEmpty();
        result.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        result.TokenCount.Should().BeGreaterThan(0);
        result.TurnIndex.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleCalls_ShouldIncrementTurnIndex()
    {
        // Arrange
        const string userId = "user-1";

        // Act
        var item1 = await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        var item2 = await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);
        var item3 = await _buffer.EnqueueAsync("Third", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        item1.TurnIndex.Should().Be(1);
        item2.TurnIndex.Should().Be(2);
        item3.TurnIndex.Should().Be(3);
    }

    [Fact]
    public async Task EnqueueAsync_WithOptionalParameters_ShouldStoreAll()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string role = "user";
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        // Act
        var result = await _buffer.EnqueueAsync("content", userId, sessionId, role, metadata, TestContext.Current.CancellationToken);

        // Assert
        result.SessionId.Should().Be(sessionId);
        result.Role.Should().Be(role);
        result.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public async Task EnqueueAsync_DifferentUsers_ShouldHaveSeparateBuffers()
    {
        // Arrange & Act
        await _buffer.EnqueueAsync("User1 content", "user-1", cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("User2 content", "user-2", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _buffer.GetCount("user-1").Should().Be(1);
        _buffer.GetCount("user-2").Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_NullContent_ShouldThrow()
    {
        // Act & Assert
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _buffer.EnqueueAsync(null!, "user-1", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnqueueAsync_EmptyUserId_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _buffer.EnqueueAsync("content", "", cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region GetCount/GetTokenCount Tests

    [Fact]
    public async Task GetCount_WithItems_ShouldReturnCorrectCount()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("Item 1", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Item 2", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Item 3", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var count = _buffer.GetCount(userId);

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public void GetCount_NonExistentUser_ShouldReturnZero()
    {
        // Act
        var count = _buffer.GetCount("non-existent");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetTokenCount_ShouldAccumulateTokens()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("Short", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("A longer piece of content", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var tokens = _buffer.GetTokenCount(userId);

        // Assert
        tokens.Should().BeGreaterThan(0);
    }

    #endregion

    #region GetPending Tests

    [Fact]
    public async Task GetPendingAsync_ShouldReturnAllItems()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var pending = await _buffer.GetPendingAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        pending.Should().HaveCount(2);
        pending[0].Content.Should().Be("First");
        pending[1].Content.Should().Be("Second");
    }

    [Fact]
    public async Task GetPendingAsync_ShouldNotRemoveItems()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("Content", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _buffer.GetPendingAsync(userId, TestContext.Current.CancellationToken);
        var countAfter = _buffer.GetCount(userId);

        // Assert
        countAfter.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingAsync_NonExistentUser_ShouldReturnEmpty()
    {
        // Act
        var pending = await _buffer.GetPendingAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        pending.Should().BeEmpty();
    }

    #endregion

    #region Trigger Tests

    [Fact]
    public async Task CheckTriggerAsync_BelowThresholds_ShouldReturnNull()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("Short", userId, cancellationToken: TestContext.Current.CancellationToken); // 1 turn, few tokens

        // Act
        var trigger = await _buffer.CheckTriggerAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        trigger.Should().BeNull();
    }

    [Fact]
    public async Task CheckTriggerAsync_TokenThresholdExceeded_ShouldReturnTokenTrigger()
    {
        // Arrange
        const string userId = "user-1";
        // Each item ~125 tokens (500 chars / 4), need 4 items to exceed 500 threshold
        var longContent = new string('x', 500);
        await _buffer.EnqueueAsync(longContent, userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync(longContent, userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync(longContent, userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync(longContent, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var trigger = await _buffer.CheckTriggerAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        trigger.Should().Be(PromotionTriggerType.TokenThreshold);
    }

    [Fact]
    public async Task CheckTriggerAsync_TurnThresholdExceeded_ShouldReturnTurnTrigger()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("a", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("b", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("c", userId, cancellationToken: TestContext.Current.CancellationToken); // 3 turns = threshold

        // Act
        var trigger = await _buffer.CheckTriggerAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        trigger.Should().Be(PromotionTriggerType.TurnThreshold);
    }

    [Fact]
    public async Task CheckTriggerAsync_EmptyBuffer_ShouldReturnNull()
    {
        // Act
        var trigger = await _buffer.CheckTriggerAsync("user-1", TestContext.Current.CancellationToken);

        // Assert
        trigger.Should().BeNull();
    }

    #endregion

    #region Drain Tests

    [Fact]
    public async Task DrainAsync_ShouldReturnAndRemoveAllItems()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var drained = await _buffer.DrainAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        drained.Should().HaveCount(2);
        _buffer.GetCount(userId).Should().Be(0);
    }

    [Fact]
    public async Task DrainAsync_WithMaxItems_ShouldRespectLimit()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Third", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var drained = await _buffer.DrainAsync(userId, maxItems: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        drained.Should().HaveCount(2);
        _buffer.GetCount(userId).Should().Be(1);
    }

    [Fact]
    public async Task DrainAsync_ShouldResetTurnCounter()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _buffer.DrainAsync(userId, TestContext.Current.CancellationToken);
        var newItem = await _buffer.EnqueueAsync("New", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        newItem.TurnIndex.Should().Be(1); // Reset after full drain
    }

    [Fact]
    public async Task DrainAsync_NonExistentUser_ShouldReturnEmpty()
    {
        // Act
        var drained = await _buffer.DrainAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        drained.Should().BeEmpty();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task ClearAsync_ShouldRemoveAllItems()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var cleared = await _buffer.ClearAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        cleared.Should().Be(2);
        _buffer.GetCount(userId).Should().Be(0);
    }

    [Fact]
    public async Task ClearAsync_NonExistentUser_ShouldReturnZero()
    {
        // Act
        var cleared = await _buffer.ClearAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        cleared.Should().Be(0);
    }

    #endregion

    #region Stats Tests

    [Fact]
    public async Task GetStats_ShouldReturnCorrectStatistics()
    {
        // Arrange
        const string userId = "user-1";
        await _buffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var stats = _buffer.GetStats(userId);

        // Assert
        stats.ItemCount.Should().Be(2);
        stats.TurnCount.Should().Be(2);
        stats.TotalTokens.Should().BeGreaterThan(0);
        stats.OldestItemTimestamp.Should().NotBeNull();
        stats.NewestItemTimestamp.Should().NotBeNull();
        stats.IdleDuration.Should().NotBeNull();
    }

    [Fact]
    public void GetStats_NonExistentUser_ShouldReturnEmpty()
    {
        // Act
        var stats = _buffer.GetStats("non-existent");

        // Assert
        stats.ItemCount.Should().Be(0);
        stats.TotalTokens.Should().Be(0);
    }

    #endregion

    #region GetActiveUserIds Tests

    [Fact]
    public async Task GetActiveUserIds_ShouldReturnUsersWithPendingItems()
    {
        // Arrange
        await _buffer.EnqueueAsync("Content", "user-1", cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.EnqueueAsync("Content", "user-2", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var activeUsers = _buffer.GetActiveUserIds();

        // Assert
        activeUsers.Should().Contain("user-1");
        activeUsers.Should().Contain("user-2");
    }

    [Fact]
    public async Task GetActiveUserIds_AfterDrain_ShouldNotIncludeUser()
    {
        // Arrange
        await _buffer.EnqueueAsync("Content", "user-1", cancellationToken: TestContext.Current.CancellationToken);
        await _buffer.DrainAsync("user-1", TestContext.Current.CancellationToken);

        // Act
        var activeUsers = _buffer.GetActiveUserIds();

        // Assert
        activeUsers.Should().NotContain("user-1");
    }

    #endregion

    #region Token Estimation Tests

    [Fact]
    public async Task EnqueueAsync_TokenEstimation_ShouldBeApproximatelyAccurate()
    {
        // Arrange
        const string userId = "user-1";
        // 100 characters should be ~25 tokens (100/4)
        var content = new string('a', 100);

        // Act
        var item = await _buffer.EnqueueAsync(content, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        item.TokenCount.Should().BeInRange(20, 30);
    }

    #endregion

    #region Buffer Overflow Tests

    [Fact]
    public async Task EnqueueAsync_ExceedsMaxBufferSize_ShouldRemoveOldest()
    {
        // Arrange
        var smallOptions = new MemoryIndexerOptions
        {
            SensoryBuffer = new SensoryBufferOptions
            {
                MaxBufferSize = 3
            }
        };
        var smallBuffer = new BufferService(
            Options.Create(smallOptions),
            NullLogger<BufferService>.Instance);

        const string userId = "user-1";

        // Act
        await smallBuffer.EnqueueAsync("First", userId, cancellationToken: TestContext.Current.CancellationToken);
        await smallBuffer.EnqueueAsync("Second", userId, cancellationToken: TestContext.Current.CancellationToken);
        await smallBuffer.EnqueueAsync("Third", userId, cancellationToken: TestContext.Current.CancellationToken);
        await smallBuffer.EnqueueAsync("Fourth", userId, cancellationToken: TestContext.Current.CancellationToken); // Should remove "First"

        // Assert
        var pending = await smallBuffer.GetPendingAsync(userId, TestContext.Current.CancellationToken);
        pending.Should().HaveCount(3);
        pending[0].Content.Should().Be("Second"); // First was removed
    }

    #endregion
}
