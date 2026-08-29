using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Intelligence.SelfEditing;
using MemoryIndexer.Sdk.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence;

public class MemGPTStyleMemoryManagerTests
{
    private readonly MemGPTStyleMemoryManager _manager;
    private readonly MemoryService _memoryServiceMock;
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly ISummarizationService _summarizationServiceMock;

    public MemGPTStyleMemoryManagerTests()
    {
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => GenerateMockEmbedding(callInfo.ArgAt<string>(0)));

        _summarizationServiceMock = Substitute.For<ISummarizationService>();
        _summarizationServiceMock.SummarizeAsync(
                Arg.Any<IEnumerable<MemoryUnit>>(),
                Arg.Any<SummarizationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var memories = callInfo.ArgAt<IEnumerable<MemoryUnit>>(0);
    var content = string.Join(" ", memories.Select(m => m.Content));
                return new MemorySummary
                {
                    Content = $"Summary of: {content[..Math.Min(100, content.Length)]}...",
                    KeyPoints = ["Key point 1", "Key point 2"]
                };
            });

        var memoryStoreMock = Substitute.For<IMemoryStore>();
        memoryStoreMock.StoreAsync(Arg.Any<MemoryUnit>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<MemoryUnit>(0));
        memoryStoreMock.SearchAsync(
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<MemorySearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        memoryStoreMock.GetAllAsync(
                Arg.Any<string>(),
                Arg.Any<MemoryFilterOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var scoringServiceMock = Substitute.For<IScoringService>();

        var deduplicationServiceMock = Substitute.For<IDeduplicationService>();
        deduplicationServiceMock.CheckForDuplicateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<float?>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new DuplicateCheckResult
            {
                IsDuplicate = false,
                DuplicateType = DuplicateType.None,
                RecommendedAction = DuplicateAction.Add,
                SimilarityScore = 0f
            });

        var pressureMonitorMock = Substitute.For<IMemoryPressureMonitor>();
        pressureMonitorMock.GetMemoryInfo()
            .Returns(new MemoryPressureInfo
            {
                Level = MemoryPressureLevel.Low,
                UtilizationPercentage = 0.5,
                TotalAvailableMemoryBytes = 1000000000,
                MemoryLoadBytes = 500000000,
                HeapSizeBytes = 500000000,
                Gen0Collections = 0,
                Gen1Collections = 0,
                Gen2Collections = 0
            });

        var growthMonitorMock = Substitute.For<IMemoryGrowthMonitor>();
        growthMonitorMock.RecordMemoryStorageAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var optionsMock = Options.Create(new MemoryIndexerOptions
        {
            MemoryGrowth = new MemoryGrowthOptions()
        });

        _memoryServiceMock = new MemoryService(
            memoryStoreMock,
            _embeddingServiceMock,
            scoringServiceMock,
            deduplicationServiceMock,
            pressureMonitorMock,
            growthMonitorMock,
            null!, // IMemoryConflictResolver (optional, null is valid)
            optionsMock);

        var managerOptions = Options.Create(new MemoryIndexerOptions());
        _manager = new MemGPTStyleMemoryManager(
            _memoryServiceMock,
            _embeddingServiceMock,
            _summarizationServiceMock,
            managerOptions,
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
        var result = await _manager.ReplaceWorkingMemoryAsync("core", newContent, TestContext.Current.CancellationToken);

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
        var result = await _manager.ReplaceWorkingMemoryAsync("context", newContent, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("context", result.Message);
    }

    [Fact]
    public async Task ReplaceWorkingMemoryAsync_InvalidLocation_ShouldFail()
    {
        // Act
        var result = await _manager.ReplaceWorkingMemoryAsync("invalid_location", "content", TestContext.Current.CancellationToken);

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
        await _manager.ReplaceWorkingMemoryAsync("core", firstContent, TestContext.Current.CancellationToken);
        var result = await _manager.ReplaceWorkingMemoryAsync("core", secondContent, TestContext.Current.CancellationToken);

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
        var result = await _manager.InsertArchivalMemoryAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _manager.InsertArchivalMemoryAsync(content, metadata, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetWorkingMemoryAsync_NewSession_ShouldReturnEmptySnapshot()
    {
        // Act
        var snapshot = await _manager.GetWorkingMemoryAsync("new_session", TestContext.Current.CancellationToken);

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
        await _manager.ReplaceWorkingMemoryAsync("core", "Core content", TestContext.Current.CancellationToken);

        // Act
        var snapshot = await _manager.GetWorkingMemoryAsync("default", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Core content", snapshot.CoreMemory);
    }

    [Fact]
    public async Task UpdateWorkingMemoryAsync_ShouldAppendContent()
    {
        // Arrange
        var sessionId = "append_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "First context.", TestContext.Current.CancellationToken);

        // Act
        var result = await _manager.UpdateWorkingMemoryAsync(sessionId, "Second context.", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var snapshot = await _manager.GetWorkingMemoryAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.Contains("First context", snapshot.ConversationContext);
        Assert.Contains("Second context", snapshot.ConversationContext);
    }

    [Fact]
    public async Task UpdateWorkingMemoryAsync_ShouldUpdateTokenCount()
    {
        // Arrange
        var sessionId = "token_test";

        // Act
        var result = await _manager.UpdateWorkingMemoryAsync(sessionId, "Some content here.", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.NewTokenCount > 0);
    }

    [Fact]
    public async Task ShouldTriggerReflectionAsync_BelowThreshold_ShouldReturnFalse()
    {
        // Arrange
        var sessionId = "reflection_test_below";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Short content.", TestContext.Current.CancellationToken);

        // Act
        var result = await _manager.ShouldTriggerReflectionAsync(sessionId, TestContext.Current.CancellationToken);

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
            await _manager.UpdateWorkingMemoryAsync(sessionId, $"This is important critical urgent content number {i}. Remember this key information that is essential and must be prioritized. function class code", TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _manager.ShouldTriggerReflectionAsync(sessionId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldReflect);
    }

    [Fact]
    public async Task PerformReflectionAsync_WithContent_ShouldSucceed()
    {
        // Arrange
        var sessionId = "perform_reflection_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Content to reflect on. This is important information.", TestContext.Current.CancellationToken);

        // Act
        var result = await _manager.PerformReflectionAsync(sessionId, TestContext.Current.CancellationToken);

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
        var result = await _manager.PerformReflectionAsync(sessionId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PerformReflectionAsync_ShouldResetAccumulatedImportance()
    {
        // Arrange
        var sessionId = "importance_reset_test";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Important content to build up importance score.", TestContext.Current.CancellationToken);

        // Act
        await _manager.PerformReflectionAsync(sessionId, TestContext.Current.CancellationToken);
        var check = await _manager.ShouldTriggerReflectionAsync(sessionId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, check.AccumulatedImportance);
    }

    [Fact]
    public async Task ManageContextWindowAsync_BelowThreshold_ShouldDoNothing()
    {
        // Arrange
        var sessionId = "manage_context_below";
        await _manager.UpdateWorkingMemoryAsync(sessionId, "Short content.", TestContext.Current.CancellationToken);

        // Act
        var result = await _manager.ManageContextWindowAsync(sessionId, 100000, TestContext.Current.CancellationToken);

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
        await _manager.ManageContextWindowAsync(sessionId, 50000, TestContext.Current.CancellationToken);
        var snapshot = await _manager.GetWorkingMemoryAsync(sessionId, TestContext.Current.CancellationToken);

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
        await _manager.UpdateWorkingMemoryAsync(sessionId1, "Content for session 1.", TestContext.Current.CancellationToken);
        await _manager.UpdateWorkingMemoryAsync(sessionId2, "Different content for session 2.", TestContext.Current.CancellationToken);

        var snapshot1 = await _manager.GetWorkingMemoryAsync(sessionId1, TestContext.Current.CancellationToken);
        var snapshot2 = await _manager.GetWorkingMemoryAsync(sessionId2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("session 1", snapshot1.ConversationContext);
        Assert.Contains("session 2", snapshot2.ConversationContext);
        Assert.DoesNotContain("session 2", snapshot1.ConversationContext);
        Assert.DoesNotContain("session 1", snapshot2.ConversationContext);
    }
}
