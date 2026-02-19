using FluentAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using MemoryIndexer.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Promotion;

public class SensoryPromoterServiceTests
{
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly BufferService _recentlyBuffer;
    private readonly ShortTermMemoryService _workingMemory;
    private readonly TopicSegmenter _topicSegmenter;
    private readonly SensoryPromoterService _promoter;

    public SensoryPromoterServiceTests()
    {
        // Setup mocks
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[768].AsMemory());
        _embeddingServiceMock.GenerateBatchEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.ArgAt<IEnumerable<string>>(0);
    var embeddings = texts.Select(_ => (ReadOnlyMemory<float>)new float[768].AsMemory()).ToList();
                return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(embeddings);
            });

        // Setup sensory buffer
        var bufferOptions = new MemoryIndexerOptions
        {
            SensoryBuffer = new SensoryBufferOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(60),
                TokenThreshold = 500,
                TurnThreshold = 3
            }
        };
        _recentlyBuffer = new BufferService(
            Options.Create(bufferOptions),
            NullLogger<BufferService>.Instance);

        // Setup working memory
        var workingOptions = new MemoryIndexer.Services.WorkingMemoryOptions { Capacity = 7 };
        _workingMemory = new ShortTermMemoryService(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(workingOptions));

        // Setup topic segmenter
        _topicSegmenter = new TopicSegmenter(
            _embeddingServiceMock,
            NullLogger<TopicSegmenter>.Instance);

        // Create service under test
        _promoter = new SensoryPromoterService(
            _recentlyBuffer,
            _workingMemory,
            _embeddingServiceMock,
            _topicSegmenter,
            NullLogger<SensoryPromoterService>.Instance);
    }

    #region PromoteAsync Tests

    [Fact]
    public async Task PromoteAsync_EmptyBuffer_ReturnsEmptyResult()
    {
        // Act
        var result = await _promoter.PromoteAsync("user-1", PromotionTriggerType.Manual);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ItemsProcessed.Should().Be(0);
        result.TopicGroupsCreated.Should().Be(0);
        result.CreatedMemories.Should().BeEmpty();
    }

    [Fact]
    public async Task PromoteAsync_SingleItem_CreatesOneMemory()
    {
        // Arrange
        const string userId = "user-1";
        await _recentlyBuffer.EnqueueAsync("Hello, world!", userId);

        // Act
        var result = await _promoter.PromoteAsync(userId, PromotionTriggerType.Manual);

        // Assert
        result.Success.Should().BeTrue();
        result.ItemsProcessed.Should().Be(1);
        result.TopicGroupsCreated.Should().Be(1);
        result.CreatedMemories.Should().HaveCount(1);
        result.CreatedMemories[0].Content.Should().Be("Hello, world!");
        result.CreatedMemories[0].UserId.Should().Be(userId);
        result.CreatedMemories[0].Tier.Should().Be(Tier.Short);
    }

    [Fact]
    public async Task PromoteAsync_MultipleItems_GroupsByTopic()
    {
        // Arrange
        const string userId = "user-1";
        // Use short content so topic segmenter creates single segment
        await _recentlyBuffer.EnqueueAsync("First", userId);
        await _recentlyBuffer.EnqueueAsync("Second", userId);
        await _recentlyBuffer.EnqueueAsync("Third", userId);

        // Act
        var result = await _promoter.PromoteAsync(userId, PromotionTriggerType.TurnThreshold);

        // Assert
        result.Success.Should().BeTrue(because: result.Error ?? "no error");
        result.ItemsProcessed.Should().Be(3);
        result.TopicGroupsCreated.Should().BeGreaterThanOrEqualTo(1);
        result.CreatedMemories.Should().NotBeEmpty();
        result.Trigger.Should().Be(PromotionTriggerType.TurnThreshold);
    }

    [Fact]
    public async Task PromoteAsync_DrainsBuffer()
    {
        // Arrange
        const string userId = "user-1";
        await _recentlyBuffer.EnqueueAsync("Content 1", userId);
        await _recentlyBuffer.EnqueueAsync("Content 2", userId);

        // Act
        await _promoter.PromoteAsync(userId, PromotionTriggerType.Manual);

        // Assert - buffer should be empty
        _recentlyBuffer.GetCount(userId).Should().Be(0);
    }

    [Fact]
    public async Task PromoteAsync_SetsCorrectMetadata()
    {
        // Arrange
        const string userId = "user-1";
        await _recentlyBuffer.EnqueueAsync("Test content", userId);

        // Act
        var result = await _promoter.PromoteAsync(userId, PromotionTriggerType.TokenThreshold);

        // Assert
        var memory = result.CreatedMemories[0];
        memory.Metadata.Should().ContainKey("source");
        memory.Metadata["source"].Should().Be("buffer_promotion");
        memory.Metadata.Should().ContainKey("promotion_trigger");
        memory.Metadata["promotion_trigger"].Should().Be("TokenThreshold");
    }

    [Fact]
    public async Task PromoteAsync_AddsToWorkingMemory()
    {
        // Arrange
        const string userId = "user-1";
        await _recentlyBuffer.EnqueueAsync("Test content", userId);

        // Act
        var result = await _promoter.PromoteAsync(userId, PromotionTriggerType.Manual);

        // Assert
        _workingMemory.Count.Should().Be(1);
        var memoryInWorking = await _workingMemory.GetAsync(result.CreatedMemories[0].Id);
        memoryInWorking.Should().NotBeNull();
    }

    #endregion

    #region PromoteItemsAsync Tests

    [Fact]
    public async Task PromoteItemsAsync_EmptyList_ReturnsEmptyResult()
    {
        // Act
        var result = await _promoter.PromoteItemsAsync([]);

        // Assert
        result.Success.Should().BeTrue();
        result.ItemsProcessed.Should().Be(0);
    }

    [Fact]
    public async Task PromoteItemsAsync_WithItems_CreatesMemories()
    {
        // Arrange
        var items = new List<SensoryMemory>
        {
            new SensoryMemory
            {
                Content = "Item 1",
                UserId = "user-1",
                Role = "user"
            },
            new SensoryMemory
            {
                Content = "Item 2",
                UserId = "user-1",
                Role = "assistant"
            }
        };

        // Act
        var result = await _promoter.PromoteItemsAsync(items);

        // Assert
        result.Success.Should().BeTrue();
        result.Trigger.Should().Be(PromotionTriggerType.Manual);
        result.CreatedMemories.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PromoteItemsAsync_PreservesRoleInMemoryUnit()
    {
        // Arrange - all items have same role (dominantRole case)
        var items = new List<SensoryMemory>
        {
            new SensoryMemory
            {
                Content = "User question 1",
                UserId = "user-1",
                Role = "user"
            },
            new SensoryMemory
            {
                Content = "User question 2",
                UserId = "user-1",
                Role = "user"
            }
        };

        // Act
        var result = await _promoter.PromoteItemsAsync(items);

        // Assert - Role should be preserved in MemoryUnit
        result.Success.Should().BeTrue();
        result.CreatedMemories.Should().NotBeEmpty();
        result.CreatedMemories[0].Role.Should().Be("user");
    }

    [Fact]
    public async Task PromoteItemsAsync_MultipleRoles_StoresDominantRole()
    {
        // Arrange - mixed roles, "assistant" is dominant (2 vs 1)
        var items = new List<SensoryMemory>
        {
            new SensoryMemory
            {
                Content = "User question",
                UserId = "user-1",
                Role = "user"
            },
            new SensoryMemory
            {
                Content = "Assistant response 1",
                UserId = "user-1",
                Role = "assistant"
            },
            new SensoryMemory
            {
                Content = "Assistant response 2",
                UserId = "user-1",
                Role = "assistant"
            }
        };

        // Act
        var result = await _promoter.PromoteItemsAsync(items);

        // Assert - dominant role (assistant) should be stored
        result.Success.Should().BeTrue();
        result.CreatedMemories.Should().NotBeEmpty();
        result.CreatedMemories[0].Role.Should().Be("assistant");
        // Also verify roles metadata contains all unique roles
        result.CreatedMemories[0].Metadata.Should().NotBeNull();
        result.CreatedMemories[0].Metadata!.Should().ContainKey("roles");
        result.CreatedMemories[0].Metadata!["roles"].Should().Contain("user");
        result.CreatedMemories[0].Metadata!["roles"].Should().Contain("assistant");
    }

    #endregion

    #region CheckPendingPromotionsAsync Tests

    [Fact]
    public async Task CheckPendingPromotionsAsync_NoActiveUsers_ReturnsEmpty()
    {
        // Act
        var result = await _promoter.CheckPendingPromotionsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPendingPromotionsAsync_BelowThreshold_ReturnsEmpty()
    {
        // Arrange
        await _recentlyBuffer.EnqueueAsync("Short", "user-1");

        // Act
        var result = await _promoter.CheckPendingPromotionsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPendingPromotionsAsync_TurnThresholdMet_ReturnsUser()
    {
        // Arrange - 3 turns meets threshold
        await _recentlyBuffer.EnqueueAsync("First", "user-1");
        await _recentlyBuffer.EnqueueAsync("Second", "user-1");
        await _recentlyBuffer.EnqueueAsync("Third", "user-1");

        // Act
        var result = await _promoter.CheckPendingPromotionsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].UserId.Should().Be("user-1");
        result[0].Trigger.Should().Be(PromotionTriggerType.TurnThreshold);
        result[0].PendingItems.Should().Be(3);
    }

    [Fact]
    public async Task CheckPendingPromotionsAsync_MultipleUsers_ReturnsAllTriggered()
    {
        // Arrange
        await _recentlyBuffer.EnqueueAsync("First", "user-1");
        await _recentlyBuffer.EnqueueAsync("Second", "user-1");
        await _recentlyBuffer.EnqueueAsync("Third", "user-1");

        await _recentlyBuffer.EnqueueAsync("First", "user-2");
        await _recentlyBuffer.EnqueueAsync("Second", "user-2");
        await _recentlyBuffer.EnqueueAsync("Third", "user-2");

        // Act
        var result = await _promoter.CheckPendingPromotionsAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.UserId).Should().Contain(["user-1", "user-2"]);
    }

    #endregion

    #region Short-Term Memory Eviction Tests

    [Fact]
    public async Task PromoteAsync_AtCapacity_EvictsOldest()
    {
        // Arrange - fill working memory to capacity
        var smallWorkingOptions = new MemoryIndexer.Services.WorkingMemoryOptions { Capacity = 2 };
        var smallWorkingMemory = new ShortTermMemoryService(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(smallWorkingOptions));

        var promoter = new SensoryPromoterService(
            _recentlyBuffer,
            smallWorkingMemory,
            _embeddingServiceMock,
            _topicSegmenter,
            NullLogger<SensoryPromoterService>.Instance);

        // Pre-fill working memory
        await smallWorkingMemory.PromoteAsync(new MemoryUnit
        {
            Content = "Pre-existing 1",
            UserId = "user-1"
        });
        await smallWorkingMemory.PromoteAsync(new MemoryUnit
        {
            Content = "Pre-existing 2",
            UserId = "user-1"
        });

        // Add item to buffer
        await _recentlyBuffer.EnqueueAsync("New content", "user-1");

        // Act
        var result = await promoter.PromoteAsync("user-1", PromotionTriggerType.Manual);

        // Assert
        result.Success.Should().BeTrue();
        result.EvictedMemories.Should().HaveCount(1);
        result.EvictedMemories[0].Content.Should().Be("Pre-existing 1");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task PromoteAsync_EmbeddingFailure_ReturnsFailure()
    {
        // Arrange
        var failingEmbeddingService = Substitute.For<IEmbeddingService>();
        failingEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Embedding failed"));
        failingEmbeddingService.GenerateBatchEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Batch embedding failed"));

        var segmenter = new TopicSegmenter(
            failingEmbeddingService,
            NullLogger<TopicSegmenter>.Instance);

        var promoter = new SensoryPromoterService(
            _recentlyBuffer,
            _workingMemory,
            failingEmbeddingService,
            segmenter,
            NullLogger<SensoryPromoterService>.Instance);

        await _recentlyBuffer.EnqueueAsync("Content", "user-1");

        // Act
        var result = await promoter.PromoteAsync("user-1", PromotionTriggerType.Manual);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Duration Tracking Tests

    [Fact]
    public async Task PromoteAsync_TracksDuration()
    {
        // Arrange
        await _recentlyBuffer.EnqueueAsync("Content", "user-1");

        // Act
        var result = await _promoter.PromoteAsync("user-1", PromotionTriggerType.Manual);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion
}
