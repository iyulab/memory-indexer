using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Chunking;

public class TopicSegmenterTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;

    public TopicSegmenterTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[1024]));
        _embeddingServiceMock
            .Setup(x => x.GenerateBatchEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                texts.Select(_ => new ReadOnlyMemory<float>(new float[1024])).ToList());
    }

    [Fact]
    public async Task SegmentConversationAsync_IncludeRoleInContent_True_FormatsWithRole()
    {
        // Arrange
        var segmenter = new TopicSegmenter(
            _embeddingServiceMock.Object,
            NullLogger<TopicSegmenter>.Instance)
        {
            IncludeRoleInContent = true,
            MinSegmentLength = 1
        };

        var messages = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Hello", Timestamp = DateTime.UtcNow },
            new() { Role = "assistant", Content = "Hi there", Timestamp = DateTime.UtcNow.AddSeconds(1) }
        };

        // Act
        var segments = await segmenter.SegmentConversationAsync(messages);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Content.Should().Contain("[user] Hello");
        segments[0].Content.Should().Contain("[assistant] Hi there");
    }

    [Fact]
    public async Task SegmentConversationAsync_IncludeRoleInContent_False_FormatsWithoutRole()
    {
        // Arrange
        var segmenter = new TopicSegmenter(
            _embeddingServiceMock.Object,
            NullLogger<TopicSegmenter>.Instance)
        {
            IncludeRoleInContent = false,
            MinSegmentLength = 1
        };

        var messages = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Hello", Timestamp = DateTime.UtcNow },
            new() { Role = "assistant", Content = "Hi there", Timestamp = DateTime.UtcNow.AddSeconds(1) }
        };

        // Act
        var segments = await segmenter.SegmentConversationAsync(messages);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Content.Should().NotContain("[user]");
        segments[0].Content.Should().NotContain("[assistant]");
        segments[0].Content.Should().Contain("Hello");
        segments[0].Content.Should().Contain("Hi there");
    }

    [Fact]
    public async Task SegmentConversationAsync_IncludeRoleInContent_DefaultIsFalse()
    {
        // Arrange
        var segmenter = new TopicSegmenter(
            _embeddingServiceMock.Object,
            NullLogger<TopicSegmenter>.Instance)
        {
            MinSegmentLength = 1
        };

        var messages = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Test message", Timestamp = DateTime.UtcNow }
        };

        // Act
        var segments = await segmenter.SegmentConversationAsync(messages);

        // Assert
        segments.Should().HaveCount(1);
        // Default should NOT include role prefix
        segments[0].Content.Should().NotContain("[user]");
        segments[0].Content.Should().Be("Test message");
    }
}
