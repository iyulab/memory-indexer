using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Intelligence.SelfEditing;
using MemoryIndexer.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class MemGPTStyleMemoryManagerTests
{
    private readonly MemGPTStyleMemoryManager _manager;
    private readonly Mock<MemoryService> _memoryServiceMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<ISummarizationService> _summarizationServiceMock;

    public MemGPTStyleMemoryManagerTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => GenerateMockEmbedding(text));

        _summarizationServiceMock = new Mock<ISummarizationService>();
        _summarizationServiceMock
            .Setup(x => x.SummarizeAsync(
                It.IsAny<IEnumerable<MemoryUnit>>(),
                It.IsAny<SummarizationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<MemoryUnit> memories, SummarizationOptions? _, CancellationToken __) =>
            {
                var content = string.Join(" ", memories.Select(m => m.Content));
                return new MemorySummary
                {
                    Content = $"Summary of: {content[..Math.Min(100, content.Length)]}...",
                    KeyPoints = ["Key point 1", "Key point 2"]
                };
            });

        var memoryStoreMock = new Mock<IMemoryStore>();
        memoryStoreMock
            .Setup(x => x.StoreAsync(It.IsAny<MemoryUnit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemoryUnit m, CancellationToken _) => m);
        memoryStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var scoringServiceMock = new Mock<IScoringService>();

        _memoryServiceMock = new Mock<MemoryService>(
            memoryStoreMock.Object,
            _embeddingServiceMock.Object,
            scoringServiceMock.Object) { CallBase = true };

        _manager = new MemGPTStyleMemoryManager(
            _memoryServiceMock.Object,
            _embeddingServiceMock.Object,
            _summarizationServiceMock.Object,
            NullLogger<MemGPTStyleMemoryManager>.Instance);
    }

    private static ReadOnlyMemory<float> GenerateMockEmbedding(string text)
    {
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[1024];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)random.NextDouble() * 2 - 1;
        }
        return embedding;
    }

    [Fact]
    public async Task ReplaceWorkingMemoryAsync_CoreMemory_ShouldSucceed()
    {
        // Arrange
        var newContent = "This is the core memory content.";

        // Act
        var result = await _manager.ReplaceWorkingMemoryAsync("core", newContent);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("core", result.Message);
    }

    [Fact]
    public async Task ReplaceWorkingMemoryAsync_ContextMemory_ShouldSucceed()
    {
        // Arrange
        var newContent = "This is the conversation context.";

        // Act
        var result = await _manager.ReplaceWorkingMemoryAsync("context", newContent);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("context", result.Message);
    }

    [Fact]
    public async Task ReplaceWorkingMemoryAsync_InvalidLocation_ShouldFail()
    {
        // Act
        var result = await _manager.ReplaceWorkingMemoryAsync("invalid_location", "content");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unknown memory location", result.Message);
    }

    [Fact]
    public async Task ReplaceWorkingMemoryAsync_ShouldReturnPreviousContent()
    {
        // Arrange
        var firstContent = "First content";
        var secondContent = "Second content";

        // Act
        await _manager.ReplaceWorkingMemoryAsync("core", firstContent);
        var result = await _manager.ReplaceWorkingMemoryAsync("core", secondContent);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(firstContent, result.PreviousContent);
    }

    [Fact]
    public async Task InsertArchivalMemoryAsync_ShouldSucceed()
    {
        // Arrange
        var content = "This is archival content.";

        // Act
        var result = await _manager.InsertArchivalMemoryAsync(content);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.MemoryId);
        Assert.True(result.TokenCount > 0);
    }

    [Fact]
    public async Task InsertArchivalMemoryAsync_WithMetadata_ShouldIncludeMetadata()
    {
        // Arrange
        var content = "Content with metadata.";
        var metadata = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };

        // Act
        var result = await _manager.InsertArchivalMemoryAsync(content, metadata);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetWorkingMemoryAsync_NewSession_ShouldReturnEmptySnapshot()
    {
        // Act
        var snapshot = await _manager.GetWorkingMemoryAsync("new_session");

        // Assert
        Assert.Equal("new_session", snapshot.SessionId);
        Assert.Equal(string.Empty, snapshot.CoreMemory);
        Assert.Equal(string.Empty, snapshot.ConversationContext);
        Assert.Empty(snapshot.RecentSummaries);
    }

    [Fact]
    public async Task GetWorkingMemoryAsync_AfterUpdate_ShouldReflectChanges()
    {
        // Arrange
        await _manager.ReplaceWorkingMemoryAsync("core", "Core content");

        // Act
        var snapshot = await _manager.GetWorkingMemoryAsync("default");

        // Assert
        Assert.Equal("Core content", snapshot.CoreMemory);
    }

    [Fact]
    public async Task UpdateWorkingMemoryAsync_ShouldAppendContent()
    {
        // Arrange
        var sessionId = "append_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "First context.");

        // Act
        var result = await _manager.UpdateWorkingMemoryAsync(sessionId, "Second context.");

        // Assert
        Assert.True(result.Success);
        var snapshot = await _manager.GetWorkingMemoryAsync(sessionId);
        Assert.Contains("First context", snapshot.ConversationContext);
        Assert.Contains("Second context", snapshot.ConversationContext);
    }

    [Fact]
    public async Task UpdateWorkingMemoryAsync_ShouldUpdateTokenCount()
    {
        // Arrange
        var sessionId = "token_test";

        // Act
        var result = await _manager.UpdateWorkingMemoryAsync(sessionId, "Some content here.");

        // Assert
        Assert.True(result.NewTokenCount > 0);
    }

    [Fact]
    public async Task ShouldTriggerReflectionAsync_BelowThreshold_ShouldReturnFalse()
    {
        // Arrange
        var sessionId = "reflection_test_below";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Short content.");

        // Act
        var result = await _manager.ShouldTriggerReflectionAsync(sessionId);

        // Assert
        Assert.False(result.ShouldReflect);
        Assert.True(result.AccumulatedImportance < result.Threshold);
    }

    [Fact]
    public async Task ShouldTriggerReflectionAsync_AboveThreshold_ShouldReturnTrue()
    {
        // Arrange
        var sessionId = "reflection_test_above";

        // Add lots of important content to exceed threshold
        for (int i = 0; i < 20; i++)
        {
            await _manager.UpdateWorkingMemoryAsync(sessionId,
                $"This is important critical urgent content number {i}. Remember this key information that is essential and must be prioritized. function class code");
        }

        // Act
        var result = await _manager.ShouldTriggerReflectionAsync(sessionId);

        // Assert
        Assert.True(result.ShouldReflect);
    }

    [Fact]
    public async Task PerformReflectionAsync_WithContent_ShouldSucceed()
    {
        // Arrange
        var sessionId = "perform_reflection_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Content to reflect on. This is important information.");

        // Act
        var result = await _manager.PerformReflectionAsync(sessionId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.MemoriesConsolidated > 0);
    }

    [Fact]
    public async Task PerformReflectionAsync_EmptyContent_ShouldFail()
    {
        // Arrange
        var sessionId = "empty_reflection_test";

        // Act
        var result = await _manager.PerformReflectionAsync(sessionId);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PerformReflectionAsync_ShouldResetAccumulatedImportance()
    {
        // Arrange
        var sessionId = "importance_reset_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Important content to build up importance score.");

        // Act
        await _manager.PerformReflectionAsync(sessionId);
        var check = await _manager.ShouldTriggerReflectionAsync(sessionId);

        // Assert
        Assert.Equal(0, check.AccumulatedImportance);
    }

    [Fact]
    public async Task ManageContextWindowAsync_BelowThreshold_ShouldDoNothing()
    {
        // Arrange
        var sessionId = "manage_context_below";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Short content.");

        // Act
        var result = await _manager.ManageContextWindowAsync(sessionId, 100000);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(ContextManagementAction.None, result.ActionTaken);
    }

    [Fact]
    public async Task ManageContextWindowAsync_ShouldSetMaxTokenCapacity()
    {
        // Arrange
        var sessionId = "manage_context_capacity";

        // Act
        await _manager.ManageContextWindowAsync(sessionId, 50000);
        var snapshot = await _manager.GetWorkingMemoryAsync(sessionId);

        // Assert
        Assert.Equal(50000, snapshot.MaxTokenCapacity);
    }

    [Fact]
    public async Task WorkingMemory_DifferentSessions_ShouldBeIsolated()
    {
        // Arrange
        var sessionId1 = "session_1";
        var sessionId2 = "session_2";

        // Act
        await _manager.UpdateWorkingMemoryAsync(sessionId1, "Content for session 1.");
        await _manager.UpdateWorkingMemoryAsync(sessionId2, "Different content for session 2.");

        var snapshot1 = await _manager.GetWorkingMemoryAsync(sessionId1);
        var snapshot2 = await _manager.GetWorkingMemoryAsync(sessionId2);

        // Assert
        Assert.Contains("session 1", snapshot1.ConversationContext);
        Assert.Contains("session 2", snapshot2.ConversationContext);
        Assert.DoesNotContain("session 2", snapshot1.ConversationContext);
        Assert.DoesNotContain("session 1", snapshot2.ConversationContext);
    }
}
