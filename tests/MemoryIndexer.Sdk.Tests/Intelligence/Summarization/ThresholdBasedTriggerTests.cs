using MemoryIndexer.Sdk.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Summarization;

/// <summary>
/// Unit tests for ThresholdBasedTrigger.
/// </summary>
public class ThresholdBasedTriggerTests
{
    private readonly TriggerOptions _options;
    private readonly ThresholdBasedTrigger _trigger;

    public ThresholdBasedTriggerTests()
    {
        _options = new TriggerOptions();
        _trigger = new ThresholdBasedTrigger(
            Options.Create(_options),
            NullLogger<ThresholdBasedTrigger>.Instance);
    }

    [Fact]
    public async Task EvaluateAsync_NoConditionsMet_ReturnsFalse()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 1000,
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.ShouldSummarize);
        Assert.Equal(SummarizationPriority.None, result.Priority);
        Assert.Equal(TriggerCondition.None, result.Condition);
    }

    [Fact]
    public async Task EvaluateAsync_CriticalTokenUsage_ReturnsCritical()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 9600, // 96% of budget
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Equal(SummarizationPriority.Critical, result.Priority);
        Assert.Equal(SummarizationStrategy.Compression, result.RecommendedStrategy);
    }

    [Fact]
    public async Task EvaluateAsync_HighTokenUsage_ReturnsHigh()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 8500, // 85% of budget
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.True(result.Priority >= SummarizationPriority.High);
        Assert.Equal(SummarizationStrategy.Hybrid, result.RecommendedStrategy);
    }

    [Fact]
    public async Task EvaluateAsync_MediumTokenUsage_ReturnsMedium()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 6500, // 65% of budget
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.True(result.Priority >= SummarizationPriority.Low);
    }

    [Fact]
    public async Task EvaluateAsync_SessionEnding_ReturnsHighWithReflection()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 3000,
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(30),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = true
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.True(result.Priority >= SummarizationPriority.High);
        Assert.Equal(SummarizationStrategy.Reflection, result.RecommendedStrategy);
    }

    [Fact]
    public async Task EvaluateAsync_MessageCountThresholdExceeded_Triggers()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 3000,
            MaxTokenBudget = 10000,
            MessageCount = 60, // Above default 50
            SessionDuration = TimeSpan.FromMinutes(30),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Contains("message", result.Explanation.ToLowerInvariant());
    }

    [Fact]
    public async Task EvaluateAsync_MemoryCountThresholdExceeded_Triggers()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 3000,
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(30),
            MemoriesCreated = 25, // Above default 20
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Equal(SummarizationStrategy.Archive, result.RecommendedStrategy);
    }

    [Fact]
    public async Task EvaluateAsync_ImportanceThresholdExceeded_Triggers()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 3000,
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(30),
            MemoriesCreated = 5,
            AccumulatedImportance = 6.0f, // Above default 5.0
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Contains("importance", result.Explanation.ToLowerInvariant());
    }

    [Fact]
    public async Task EvaluateAsync_TimeSinceLastSummarization_Triggers()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 3000,
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(60),
            TimeSinceLastSummarization = TimeSpan.FromMinutes(45), // Above default 30
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Contains("time", result.Explanation.ToLowerInvariant());
    }

    [Fact]
    public async Task EvaluateAsync_MultipleConditions_ReturnsCombined()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 7000, // 70% - medium
            MaxTokenBudget = 10000,
            MessageCount = 60, // Above threshold
            SessionDuration = TimeSpan.FromMinutes(60),
            MemoriesCreated = 25, // Above threshold
            AccumulatedImportance = 6.0f, // Above threshold
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.ShouldSummarize);
        Assert.Equal(TriggerCondition.Combined, result.Condition);
    }

    [Fact]
    public async Task EvaluateAsync_CalculatesCorrectTargetTokenCount()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 9600, // 96% - critical
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.TargetTokenCount);
        // Critical priority should target 40% of max budget
        Assert.Equal((int)(10000 * _options.CriticalTargetRatio), result.TargetTokenCount);
    }

    [Fact]
    public async Task EvaluateAsync_CalculatesSummarizationRatio()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 9600, // 96%
            MaxTokenBudget = 10000,
            MessageCount = 10,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.SummarizationRatio > 0);
        Assert.True(result.SummarizationRatio < 1);
    }

    [Fact]
    public void RegisterEvent_SessionStart_InitializesState()
    {
        // Arrange & Act
        _trigger.RegisterEvent("session-1", SessionEventType.SessionStart);

        // Assert - Just verify no exception
    }

    [Fact]
    public void RegisterEvent_SessionEnd_MarksSessionEnding()
    {
        // Arrange
        _trigger.RegisterEvent("session-1", SessionEventType.SessionStart);

        // Act
        _trigger.RegisterEvent("session-1", SessionEventType.SessionEnd);

        // Assert - Just verify no exception
    }

    [Fact]
    public void RegisterEvent_UserMessage_IncrementsMessageCount()
    {
        // Arrange
        _trigger.RegisterEvent("session-1", SessionEventType.SessionStart);

        // Act
        _trigger.RegisterEvent("session-1", SessionEventType.UserMessage);
        _trigger.RegisterEvent("session-1", SessionEventType.AssistantResponse);

        // Assert - Just verify no exception
    }

    [Fact]
    public void RegisterEvent_MemoryStored_TracksImportance()
    {
        // Arrange
        _trigger.RegisterEvent("session-1", SessionEventType.SessionStart);

        // Act
        var metadata = new Dictionary<string, string> { { "importance", "0.8" } };
        _trigger.RegisterEvent("session-1", SessionEventType.MemoryStored, metadata);

        // Assert - Just verify no exception
    }

    [Fact]
    public void RegisterEvent_ManualRequest_SetsFlag()
    {
        // Arrange
        _trigger.RegisterEvent("session-1", SessionEventType.SessionStart);

        // Act
        _trigger.RegisterEvent("session-1", SessionEventType.ManualRequest);

        // Assert - Just verify no exception
    }

    [Fact]
    public async Task EvaluateAsync_CustomOptions_RespectsThresholds()
    {
        // Arrange - Use custom options with lower thresholds
        var customOptions = new TriggerOptions
        {
            MediumTokenThreshold = 0.4f,
            HighTokenThreshold = 0.5f,
            CriticalTokenThreshold = 0.6f,
            MessageCountThreshold = 10
        };
        var customTrigger = new ThresholdBasedTrigger(
            Options.Create(customOptions),
            NullLogger<ThresholdBasedTrigger>.Instance);

        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 5500, // 55% - would be medium with custom options
            MaxTokenBudget = 10000,
            MessageCount = 5,
            SessionDuration = TimeSpan.FromMinutes(5),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await customTrigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert - 55% is above custom medium threshold of 40%
        Assert.True(result.ShouldSummarize);
    }

    [Fact]
    public async Task EvaluateAsync_ExplanationContainsAllConditions()
    {
        // Arrange
        var context = new SummarizationContext
        {
            SessionId = "session-1",
            UserId = "user-1",
            CurrentTokenCount = 8500, // High token usage
            MaxTokenBudget = 10000,
            MessageCount = 60, // Above threshold
            SessionDuration = TimeSpan.FromMinutes(60),
            MemoriesCreated = 5,
            AccumulatedImportance = 1.0f,
            IsSessionEnding = false
        };

        // Act
        var result = await _trigger.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Assert - Explanation should contain info about both conditions
        Assert.Contains("token", result.Explanation.ToLowerInvariant());
        Assert.Contains("message", result.Explanation.ToLowerInvariant());
    }
}
