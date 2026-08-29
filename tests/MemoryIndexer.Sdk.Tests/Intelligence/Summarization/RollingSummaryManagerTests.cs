using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Summarization;

/// <summary>
/// Unit tests for RollingSummaryManager.
/// </summary>
public class RollingSummaryManagerTests
{
    private readonly ISummarizationService _summarizerMock;
    private readonly RollingSummaryManager _manager;

    public RollingSummaryManagerTests()
    {
        _summarizerMock = Substitute.For<ISummarizationService>();
        _manager = new RollingSummaryManager(
            _summarizerMock,
            NullLogger<RollingSummaryManager>.Instance);
    }

    [Fact]
    public void Initialize_NewSession_CreatesState()
    {
        // Act
        _manager.Initialize("session-1", "user-1");

        // Assert
        var state = _manager.GetState("session-1");
        Assert.NotNull(state);
        Assert.Equal("session-1", state.SessionId);
        Assert.Equal("user-1", state.UserId);
        Assert.NotNull(state.Config);
    }

    [Fact]
    public void Initialize_WithConfig_UsesProvidedConfig()
    {
        // Arrange
        var config = new RollingSummaryConfig
        {
            TurnInterval = 10,
            MaxWindowSize = 50,
            TokenThreshold = 8000
        };

        // Act
        _manager.Initialize("session-1", "user-1", config);

        // Assert
        var state = _manager.GetState("session-1");
        Assert.NotNull(state);
        Assert.Equal(10, state.Config.TurnInterval);
        Assert.Equal(50, state.Config.MaxWindowSize);
        Assert.Equal(8000, state.Config.TokenThreshold);
    }

    [Fact]
    public async Task RecordAsync_BelowThreshold_ReturnsNull()
    {
        // Arrange
        _manager.Initialize("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Short content",
            UserId = "user-1"
        };

        // Act
        var result = await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        var state = _manager.GetState("session-1");
        Assert.NotNull(state);
        Assert.Single(state.WindowMemories);
        Assert.Equal(1, state.TotalMemoriesProcessed);
    }

    [Fact]
    public async Task RecordAsync_ExceedsWindowSize_TriggersSummary()
    {
        // Arrange
        var config = new RollingSummaryConfig { MaxWindowSize = 3 };
        _manager.Initialize("session-1", "user-1", config);

        var expectedSummary = new MemorySummary
        {
            Content = "Summarized content",
            OriginalTokenCount = 100,
            SummarizedTokenCount = 30
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Add memories up to threshold
        for (int i = 0; i < 3; i++)
        {
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = $"Memory content {i}",
                UserId = "user-1"
            };
            var result = await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

            if (i < 2)
            {
                Assert.Null(result);
            }
            else
            {
                // Last one should trigger
                Assert.NotNull(result);
                Assert.Equal("Summarized content", result.Content);
            }
        }

        // Assert final state
        var state = _manager.GetState("session-1");
        Assert.NotNull(state);
        Assert.Empty(state.WindowMemories); // Cleared after summarization
        Assert.Equal(1, state.TotalSummariesGenerated);
    }

    [Fact]
    public async Task RecordTurnAsync_ExceedsTurnInterval_TriggersSummary()
    {
        // Arrange
        var config = new RollingSummaryConfig { TurnInterval = 2 };
        _manager.Initialize("session-1", "user-1", config);

        // Add a memory first so there's something to summarize
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test memory",
            UserId = "user-1"
        };
        await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        var expectedSummary = new MemorySummary
        {
            Content = "Turn-based summary",
            OriginalTokenCount = 50,
            SummarizedTokenCount = 15
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Act - record turns
        var result1 = await _manager.RecordTurnAsync("session-1", 100, TestContext.Current.CancellationToken);
        Assert.Null(result1); // First turn, no trigger

        var result2 = await _manager.RecordTurnAsync("session-1", 100, TestContext.Current.CancellationToken);
        Assert.NotNull(result2); // Second turn triggers

        // Assert
        Assert.Equal("Turn-based summary", result2!.Content);
    }

    [Fact]
    public async Task RecordAsync_ExceedsTokenThreshold_TriggersSummary()
    {
        // Arrange
        var config = new RollingSummaryConfig { TokenThreshold = 50 };
        _manager.Initialize("session-1", "user-1", config);

        var expectedSummary = new MemorySummary
        {
            Content = "Token threshold summary",
            OriginalTokenCount = 200,
            SummarizedTokenCount = 60
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Create memory with content that exceeds token threshold (4 chars ≈ 1 token)
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = new string('x', 250), // ~62 tokens, exceeds 50
            UserId = "user-1"
        };

        // Act
        var result = await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Token threshold summary", result.Content);
    }

    [Fact]
    public async Task ForceUpdateAsync_GeneratesSummaryImmediately()
    {
        // Arrange
        _manager.Initialize("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test memory",
            UserId = "user-1"
        };
        await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        var expectedSummary = new MemorySummary
        {
            Content = "Forced summary",
            OriginalTokenCount = 20,
            SummarizedTokenCount = 8
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Act
        var result = await _manager.ForceUpdateAsync("session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Forced summary", result.Content);
        var state = _manager.GetState("session-1");
        Assert.NotNull(state);
        Assert.Empty(state.WindowMemories);
        Assert.Equal(1, state.TotalSummariesGenerated);
    }

    [Fact]
    public async Task ForceUpdateAsync_UnknownSession_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.ForceUpdateAsync("unknown-session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetCurrentSummary_NoSummary_ReturnsNull()
    {
        // Arrange
        _manager.Initialize("session-1", "user-1");

        // Act
        var summary = _manager.GetCurrentSummary("session-1");

        // Assert
        Assert.Null(summary);
    }

    [Fact]
    public async Task GetCurrentSummary_AfterSummarization_ReturnsSummary()
    {
        // Arrange
        var config = new RollingSummaryConfig { MaxWindowSize = 1 };
        _manager.Initialize("session-1", "user-1", config);

        var expectedSummary = new MemorySummary
        {
            Content = "Current summary",
            OriginalTokenCount = 50,
            SummarizedTokenCount = 15
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test",
            UserId = "user-1"
        };
        await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        // Act
        var summary = _manager.GetCurrentSummary("session-1");

        // Assert
        Assert.NotNull(summary);
        Assert.Equal("Current summary", summary.Content);
    }

    [Fact]
    public async Task FinalizeAsync_GeneratesFinalSummary()
    {
        // Arrange
        _manager.Initialize("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Final memory",
            UserId = "user-1"
        };
        await _manager.RecordAsync("session-1", memory, TestContext.Current.CancellationToken);

        var finalSummary = new MemorySummary
        {
            Content = "Final session summary",
            OriginalTokenCount = 30,
            SummarizedTokenCount = 10
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(finalSummary);

        // Act
        var result = await _manager.FinalizeAsync("session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Final session summary", result.Content);

        // State should be removed after finalization
        Assert.Null(_manager.GetState("session-1"));
    }

    [Fact]
    public void Remove_RemovesState()
    {
        // Arrange
        _manager.Initialize("session-1", "user-1");
        Assert.NotNull(_manager.GetState("session-1"));

        // Act
        _manager.Remove("session-1");

        // Assert
        Assert.Null(_manager.GetState("session-1"));
    }

    [Fact]
    public async Task IncrementalUpdate_UsedWhenConfigured()
    {
        // Arrange
        var config = new RollingSummaryConfig
        {
            UseIncrementalUpdates = true,
            MaxWindowSize = 2
        };
        _manager.Initialize("session-1", "user-1", config);

        var memory1 = new MemoryUnit { Id = Guid.NewGuid(), Content = "First", UserId = "user-1" };
        var memory2 = new MemoryUnit { Id = Guid.NewGuid(), Content = "Second", UserId = "user-1" };
        var memory3 = new MemoryUnit { Id = Guid.NewGuid(), Content = "Third", UserId = "user-1" };

        var firstSummary = new MemorySummary
        {
            Content = "First summary",
            OriginalTokenCount = 20,
            SummarizedTokenCount = 8
        };

        var incrementalSummary = new MemorySummary
        {
            Content = "Incremental summary",
            OriginalTokenCount = 30,
            SummarizedTokenCount = 12
        };

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(firstSummary);

        _summarizerMock.IncrementalUpdateAsync(
            Arg.Any<MemorySummary>(),
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<CancellationToken>())
            .Returns(incrementalSummary);

        // First batch
        await _manager.RecordAsync("session-1", memory1, TestContext.Current.CancellationToken);
        await _manager.RecordAsync("session-1", memory2, TestContext.Current.CancellationToken);

        // Second batch should use incremental update
        await _manager.RecordAsync("session-1", memory3, TestContext.Current.CancellationToken);
        var result = await _manager.ForceUpdateAsync("session-1", TestContext.Current.CancellationToken);

        // Assert - incremental update was used
        await _summarizerMock.Received(1).IncrementalUpdateAsync(
            Arg.Any<MemorySummary>(),
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RollingSummaryState_ShouldTriggerSummary_TurnBased()
    {
        // Arrange
        var config = new RollingSummaryConfig { TurnInterval = 5 };
        var state = new RollingSummaryState
        {
            SessionId = "test",
            UserId = "user",
            Config = config
        };

        // Act & Assert
        state.TurnsSinceLastSummary = 4;
        Assert.False(state.ShouldTriggerSummary());

        state.TurnsSinceLastSummary = 5;
        Assert.True(state.ShouldTriggerSummary());
    }

    [Fact]
    public void RollingSummaryState_ShouldTriggerSummary_WindowSize()
    {
        // Arrange
        var config = new RollingSummaryConfig { MaxWindowSize = 10 };
        var state = new RollingSummaryState
        {
            SessionId = "test",
            UserId = "user",
            Config = config
        };

        // Act & Assert
        for (int i = 0; i < 9; i++)
        {
            state.WindowMemories.Add(new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = $"Memory {i}",
                UserId = "user"
            });
        }
        Assert.False(state.ShouldTriggerSummary());

        state.WindowMemories.Add(new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Memory 10",
            UserId = "user"
        });
        Assert.True(state.ShouldTriggerSummary());
    }

    [Fact]
    public void RollingSummaryState_ShouldTriggerSummary_TokenThreshold()
    {
        // Arrange
        var config = new RollingSummaryConfig { TokenThreshold = 1000 };
        var state = new RollingSummaryState
        {
            SessionId = "test",
            UserId = "user",
            Config = config
        };

        // Act & Assert
        state.WindowTokenCount = 999;
        Assert.False(state.ShouldTriggerSummary());

        state.WindowTokenCount = 1000;
        Assert.True(state.ShouldTriggerSummary());
    }

    [Fact]
    public void RollingSummaryConfig_Default_HasExpectedValues()
    {
        // Act
        var config = RollingSummaryConfig.Default;

        // Assert
        Assert.Equal(5, config.TurnInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), config.TimeInterval);
        Assert.Equal(20, config.MaxWindowSize);
        Assert.Equal(4000, config.TokenThreshold);
        Assert.True(config.UseIncrementalUpdates);
        Assert.Equal(0.3f, config.TargetCompressionRatio, 0.001f);
    }
}
