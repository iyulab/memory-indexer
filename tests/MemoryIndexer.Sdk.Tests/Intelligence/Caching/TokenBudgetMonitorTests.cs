using MemoryIndexer.Sdk.Intelligence.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Caching;

public class TokenBudgetMonitorTests
{
    private readonly TokenBudgetMonitor _monitor;

    public TokenBudgetMonitorTests()
    {
        _monitor = new TokenBudgetMonitor(NullLogger<TokenBudgetMonitor>.Instance);
    }

    [Fact]
    public void StartSession_ShouldCreateSession()
    {
        // Act
        _monitor.StartSession("session1", "user1", 10000);
        var status = _monitor.GetSessionStatus("session1");

        // Assert
        Assert.NotNull(status);
        Assert.Equal("session1", status.SessionId);
        Assert.Equal("user1", status.UserId);
        Assert.Equal(0, status.TotalTokens);
        Assert.Equal(10000, status.MaxBudget);
    }

    [Fact]
    public void RecordTokenUsage_ShouldTrackUsage()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 10000);

        // Act
        _monitor.RecordTokenUsage("session1", 1000, "recall");
        _monitor.RecordTokenUsage("session1", 500, "store");
        var status = _monitor.GetSessionStatus("session1");

        // Assert
        Assert.NotNull(status);
        Assert.Equal(1500, status.TotalTokens);
        Assert.Equal(8500, status.RemainingTokens);
        Assert.Equal(0.15f, status.UsageRatio, 2);
    }

    [Fact]
    public void RecordTokenUsage_ShouldTrackOperationBreakdown()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 10000);

        // Act
        _monitor.RecordTokenUsage("session1", 1000, "recall");
        _monitor.RecordTokenUsage("session1", 500, "recall");
        _monitor.RecordTokenUsage("session1", 200, "store");
        var status = _monitor.GetSessionStatus("session1");

        // Assert
        Assert.NotNull(status);
        Assert.Equal(1500, status.OperationBreakdown["recall"]);
        Assert.Equal(200, status.OperationBreakdown["store"]);
    }

    [Fact]
    public void OnBudgetWarning_ShouldFireWhenThresholdReached()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000, warningThreshold: 0.8f);
        TokenBudgetEventArgs? firedArgs = null;
        _monitor.OnBudgetWarning += (_, args) => firedArgs = args;

        // Act
        _monitor.RecordTokenUsage("session1", 850, "recall"); // 85% > 80% threshold

        // Assert
        Assert.NotNull(firedArgs);
        Assert.Equal("session1", firedArgs.SessionId);
        Assert.Equal(TokenBudgetEventType.Warning, firedArgs.EventType);
        Assert.True(firedArgs.UsageRatio >= 0.8f);
    }

    [Fact]
    public void OnBudgetExceeded_ShouldFireWhenBudgetExceeded()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);
        TokenBudgetEventArgs? firedArgs = null;
        _monitor.OnBudgetExceeded += (_, args) => firedArgs = args;

        // Act
        _monitor.RecordTokenUsage("session1", 1100, "recall"); // Exceeds 1000

        // Assert
        Assert.NotNull(firedArgs);
        Assert.Equal("session1", firedArgs.SessionId);
        Assert.Equal(TokenBudgetEventType.Exceeded, firedArgs.EventType);
        Assert.True(firedArgs.UsageRatio > 1.0f);
    }

    [Fact]
    public void OnBudgetWarning_ShouldFireOnlyOnce()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000, warningThreshold: 0.5f);
        int fireCount = 0;
        _monitor.OnBudgetWarning += (_, _) => fireCount++;

        // Act
        _monitor.RecordTokenUsage("session1", 600, "recall"); // 60% > 50%
        _monitor.RecordTokenUsage("session1", 200, "recall"); // 80% - should not fire again

        // Assert
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void CanAfford_ShouldCheckBudget()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);
        _monitor.RecordTokenUsage("session1", 800, "recall");

        // Act & Assert
        Assert.True(_monitor.CanAfford("session1", 100));
        Assert.True(_monitor.CanAfford("session1", 200));
        Assert.False(_monitor.CanAfford("session1", 300));
    }

    [Fact]
    public void GetRecommendation_ShouldReturnAppropriateLevel()
    {
        // Arrange - healthy
        _monitor.StartSession("healthy", "user1", 1000);
        _monitor.RecordTokenUsage("healthy", 300, "recall"); // 30%
        var healthyRec = _monitor.GetRecommendation("healthy");

        // Arrange - moderate
        _monitor.StartSession("moderate", "user1", 1000);
        _monitor.RecordTokenUsage("moderate", 650, "recall"); // 65%
        var moderateRec = _monitor.GetRecommendation("moderate");

        // Arrange - warning
        _monitor.StartSession("warning", "user1", 1000);
        _monitor.RecordTokenUsage("warning", 850, "recall"); // 85%
        var warningRec = _monitor.GetRecommendation("warning");

        // Arrange - critical
        _monitor.StartSession("critical", "user1", 1000);
        _monitor.RecordTokenUsage("critical", 950, "recall"); // 95%
        var criticalRec = _monitor.GetRecommendation("critical");

        // Arrange - exceeded
        _monitor.StartSession("exceeded", "user1", 1000);
        _monitor.RecordTokenUsage("exceeded", 1100, "recall"); // 110%
        var exceededRec = _monitor.GetRecommendation("exceeded");

        // Assert
        Assert.Equal(TokenRecommendationType.Continue, healthyRec.Type);
        Assert.Equal(TokenRecommendationType.ReduceScope, moderateRec.Type);
        Assert.Equal(TokenRecommendationType.Compress, warningRec.Type);
        Assert.Equal(TokenRecommendationType.Conserve, criticalRec.Type);
        Assert.Equal(TokenRecommendationType.Stop, exceededRec.Type);
    }

    [Fact]
    public void EstimateTokens_ShouldEstimateReasonably()
    {
        // Act
        var estimate = _monitor.EstimateTokens("Hello, world!"); // 13 chars

        // Assert - roughly 4 chars per token
        Assert.InRange(estimate, 2, 5);
    }

    [Fact]
    public void EndSession_ShouldReturnSummary()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000, warningThreshold: 0.5f);
        _monitor.RecordTokenUsage("session1", 300, "recall");
        _monitor.RecordTokenUsage("session1", 200, "store");
        _monitor.RecordTokenUsage("session1", 600, "recall"); // Triggers warning

        // Act
        var summary = _monitor.EndSession("session1");

        // Assert
        Assert.NotNull(summary);
        Assert.Equal("session1", summary.SessionId);
        Assert.Equal(1100, summary.TotalTokens);
        Assert.Equal(1000, summary.MaxBudget);
        Assert.Equal(3, summary.OperationCount);
        Assert.True(summary.WasExceeded);
        Assert.Equal(1, summary.WarningCount);
        Assert.True(summary.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public void OnSessionEnded_ShouldFireWithSummary()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);
        _monitor.RecordTokenUsage("session1", 500, "recall");
        SessionTokenSummary? firedSummary = null;
        _monitor.OnSessionEnded += (_, args) => firedSummary = args.Summary;

        // Act
        _monitor.EndSession("session1");

        // Assert
        Assert.NotNull(firedSummary);
        Assert.Equal("session1", firedSummary.SessionId);
        Assert.Equal(500, firedSummary.TotalTokens);
    }

    [Fact]
    public void GetSessionStatus_UnknownSession_ShouldReturnNull()
    {
        // Act
        var status = _monitor.GetSessionStatus("unknown");

        // Assert
        Assert.Null(status);
    }

    [Fact]
    public void GetGlobalStats_ShouldAggregateAllSessions()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);
        _monitor.StartSession("session2", "user2", 2000);
        _monitor.RecordTokenUsage("session1", 500, "recall");
        _monitor.RecordTokenUsage("session2", 1000, "recall");
        _monitor.RecordTokenUsage("session2", 300, "store");

        // Act
        var stats = _monitor.GetGlobalStats();

        // Assert
        Assert.Equal(2, stats.ActiveSessions);
        Assert.Equal(2, stats.TotalSessions);
        Assert.Equal(1800, stats.TotalTokens);
        Assert.Equal("recall", stats.TopOperation);
    }

    [Fact]
    public void EndSession_ShouldDecrementActiveSessions()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);
        _monitor.StartSession("session2", "user2", 1000);

        // Act
        _monitor.EndSession("session1");
        var stats = _monitor.GetGlobalStats();

        // Assert
        Assert.Equal(1, stats.ActiveSessions);
        Assert.Equal(2, stats.TotalSessions);
    }

    [Fact]
    public void RecordTokenUsage_UnknownSession_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        _monitor.RecordTokenUsage("unknown", 100, "recall");
    }

    [Fact]
    public void PeakUsageRatio_ShouldTrackMaximum()
    {
        // Arrange
        _monitor.StartSession("session1", "user1", 1000);

        // Act - usage goes up and down (conceptually)
        _monitor.RecordTokenUsage("session1", 900, "recall"); // 90%
        var summary = _monitor.EndSession("session1");

        // Assert
        Assert.NotNull(summary);
        Assert.Equal(0.9f, summary.PeakUsageRatio, 2);
    }
}
