using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Summarization;

/// <summary>
/// Unit tests for SummarizationOrchestrator.
/// </summary>
public class SummarizationOrchestratorTests
{
    private readonly ISummarizationTrigger _triggerMock;
    private readonly ISummarizationService _summarizerMock;
    private readonly IMemoryStore _memoryStoreMock;
    private readonly SummarizationOrchestrator _orchestrator;

    public SummarizationOrchestratorTests()
    {
        _triggerMock = Substitute.For<ISummarizationTrigger>();
        _summarizerMock = Substitute.For<ISummarizationService>();
        _memoryStoreMock = Substitute.For<IMemoryStore>();

        _orchestrator = new SummarizationOrchestrator(
            _triggerMock,
            _summarizerMock,
            _memoryStoreMock,
            NullLogger<SummarizationOrchestrator>.Instance);
    }

    [Fact]
    public void StartSession_NewSession_TracksSession()
    {
        // Act
        _orchestrator.StartSession("session-1", "user-1", 100000);

        // Assert
        var state = _orchestrator.GetSessionState("session-1");
        Assert.NotNull(state);
        Assert.Equal("session-1", state.SessionId);
        Assert.Equal("user-1", state.UserId);
        Assert.Equal(100000, state.MaxTokenBudget);
    }

    [Fact]
    public void StartSession_RegistersSessionStartEvent()
    {
        // Act
        _orchestrator.StartSession("session-1", "user-1");

        // Assert
        _triggerMock.Received(1).RegisterEvent("session-1", SessionEventType.SessionStart, null);
    }

    [Fact]
    public void GetActiveSessionIds_ReturnsAllSessions()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");
        _orchestrator.StartSession("session-2", "user-1");
        _orchestrator.StartSession("session-3", "user-2");

        // Act
        var sessionIds = _orchestrator.GetActiveSessionIds();

        // Assert
        Assert.Equal(3, sessionIds.Count);
        Assert.Contains("session-1", sessionIds);
        Assert.Contains("session-2", sessionIds);
        Assert.Contains("session-3", sessionIds);
    }

    [Fact]
    public void RecordMessage_UpdatesSessionState()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        // Act
        _orchestrator.RecordMessage("session-1", 100, isUserMessage: true);
        _orchestrator.RecordMessage("session-1", 200, isUserMessage: false);

        // Assert
        var state = _orchestrator.GetSessionState("session-1");
        Assert.NotNull(state);
        Assert.Equal(2, state.MessageCount);
        Assert.Equal(300, state.CurrentTokenCount);
    }

    [Fact]
    public async Task RecordMemoryAsync_UpdatesSessionState()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new TriggerEvaluation
            {
                ShouldSummarize = false,
                Explanation = "No trigger"
            });

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test memory content",
            UserId = "user-1",
            ImportanceScore = 0.8f
        };

        // Act
        var result = await _orchestrator.RecordMemoryAsync("session-1", memory, TestContext.Current.CancellationToken);

        // Assert
        var state = _orchestrator.GetSessionState("session-1");
        Assert.NotNull(state);
        Assert.Equal(1, state.MemoriesCreated);
        Assert.Contains(memory.Id, state.MemoryIds);
        Assert.Equal(0.8f, state.AccumulatedImportance, 0.01f);
        Assert.NotNull(result);
        Assert.False(result.Summarized);
    }

    [Fact]
    public async Task RecordMemoryAsync_WhenTriggerFires_ExecutesSummarization()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test memory content that triggers summarization",
            UserId = "user-1",
            ImportanceScore = 0.9f
        };

        var expectedSummary = new MemorySummary
        {
            Content = "Summary content",
            SourceMemoryIds = [memory.Id],
            OriginalTokenCount = 100,
            SummarizedTokenCount = 30
        };

        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new TriggerEvaluation
            {
                ShouldSummarize = true,
                Priority = SummarizationPriority.High,
                RecommendedStrategy = SummarizationStrategy.Extractive,
                Explanation = "Token threshold exceeded"
            });

        _memoryStoreMock.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit> { memory });

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Act
        var result = await _orchestrator.RecordMemoryAsync("session-1", memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Summarized);
        Assert.Equal(expectedSummary, result.Summary);
        Assert.Equal(SummarizationStrategy.Extractive, result.Strategy);
    }

    [Fact]
    public async Task EvaluateTriggerAsync_ReturnsEvaluation()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        var expectedEvaluation = new TriggerEvaluation
        {
            ShouldSummarize = true,
            Priority = SummarizationPriority.Medium,
            RecommendedStrategy = SummarizationStrategy.Hybrid,
            Explanation = "Test evaluation"
        };

        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(expectedEvaluation);

        // Act
        var result = await _orchestrator.EvaluateTriggerAsync("session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedEvaluation.ShouldSummarize, result.ShouldSummarize);
        Assert.Equal(expectedEvaluation.Priority, result.Priority);
        Assert.Equal(expectedEvaluation.RecommendedStrategy, result.RecommendedStrategy);
    }

    [Fact]
    public async Task TriggerSummarizationAsync_ManualTrigger_ExecutesSummarization()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test memory",
            UserId = "user-1"
        };

        // Pre-record a memory
        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new TriggerEvaluation { ShouldSummarize = false, Explanation = "No trigger" });
        await _orchestrator.RecordMemoryAsync("session-1", memory, TestContext.Current.CancellationToken);

        var expectedSummary = new MemorySummary
        {
            Content = "Manual summary",
            SourceMemoryIds = [memory.Id],
            OriginalTokenCount = 50,
            SummarizedTokenCount = 15
        };

        _memoryStoreMock.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit> { memory });

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedSummary);

        // Act
        var result = await _orchestrator.TriggerSummarizationAsync("session-1", SummarizationStrategy.Compression, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Summarized);
        Assert.Equal(SummarizationStrategy.Compression, result.Strategy);
    }

    [Fact]
    public async Task EndSessionAsync_GeneratesFinalSummary()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Final memory",
            UserId = "user-1"
        };

        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new TriggerEvaluation
            {
                ShouldSummarize = true,
                RecommendedStrategy = SummarizationStrategy.Reflection,
                Explanation = "Session ending"
            });

        await _orchestrator.RecordMemoryAsync("session-1", memory, TestContext.Current.CancellationToken);

        var finalSummary = new MemorySummary
        {
            Content = "Final session summary",
            SourceMemoryIds = [memory.Id]
        };

        _memoryStoreMock.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit> { memory });

        _summarizerMock.SummarizeAsync(
            Arg.Any<IEnumerable<MemoryUnit>>(),
            Arg.Any<SummarizationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(finalSummary);

        // Act
        var result = await _orchestrator.EndSessionAsync("session-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Final session summary", result.Content);

        // Verify session is removed
        Assert.Null(_orchestrator.GetSessionState("session-1"));
    }

    [Fact]
    public async Task EndSessionAsync_RegistersSessionEndEvent()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1");

        _triggerMock.EvaluateAsync(Arg.Any<SummarizationContext>(), Arg.Any<CancellationToken>())
            .Returns(new TriggerEvaluation { ShouldSummarize = false, Explanation = "Empty session" });

        // Act
        await _orchestrator.EndSessionAsync("session-1", TestContext.Current.CancellationToken);

        // Assert
        _triggerMock.Received(1).RegisterEvent("session-1", SessionEventType.SessionEnd, null);
    }

    [Fact]
    public void GetSessionState_UnknownSession_ReturnsNull()
    {
        // Act
        var state = _orchestrator.GetSessionState("unknown-session");

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public async Task RecordMemoryAsync_UnknownSession_ReturnsNull()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Test",
            UserId = "user-1"
        };

        // Act
        var result = await _orchestrator.RecordMemoryAsync("unknown-session", memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SessionState_ToContext_MapsCorrectly()
    {
        // Arrange
        _orchestrator.StartSession("session-1", "user-1", 50000);
        _orchestrator.RecordMessage("session-1", 1000, isUserMessage: true);

        // Act
        var state = _orchestrator.GetSessionState("session-1");
        var context = state!.ToContext();

        // Assert
        Assert.Equal("session-1", context.SessionId);
        Assert.Equal("user-1", context.UserId);
        Assert.Equal(1000, context.CurrentTokenCount);
        Assert.Equal(50000, context.MaxTokenBudget);
        Assert.Equal(1, context.MessageCount);
    }
}
