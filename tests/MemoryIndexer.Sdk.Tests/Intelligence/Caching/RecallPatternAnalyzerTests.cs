using MemoryIndexer.Sdk.Intelligence.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Caching;

public class RecallPatternAnalyzerTests
{
    private readonly RecallPatternAnalyzer _analyzer;

    public RecallPatternAnalyzerTests()
    {
        _analyzer = new RecallPatternAnalyzer(
            NullLogger<RecallPatternAnalyzer>.Instance,
            new RecallPatternOptions
            {
                HighDuplicateThreshold = 0.3f,
                ExcessiveDuplicateThreshold = 5,
                RapidFireWindowMs = 1000,
                RapidFireThreshold = 5
            });
    }

    [Fact]
    public void RecordRecall_FirstQuery_ShouldTrackAsUnique()
    {
        // Act
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        var stats = _analyzer.GetStatistics("user1");

        // Assert
        Assert.Equal(1, stats.TotalRecalls);
        Assert.Equal(0, stats.DuplicateRecalls);
        Assert.Equal(1, stats.UniqueQueries);
        Assert.Equal(0f, stats.DuplicateRatio);
    }

    [Fact]
    public void RecordRecall_DuplicateQuery_ShouldTrackAsDuplicate()
    {
        // Act
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        var stats = _analyzer.GetStatistics("user1");

        // Assert
        Assert.Equal(3, stats.TotalRecalls);
        Assert.Equal(2, stats.DuplicateRecalls); // 2nd and 3rd are duplicates
        Assert.Equal(1, stats.UniqueQueries);
        Assert.True(stats.DuplicateRatio > 0.6f);
    }

    [Fact]
    public void RecordRecall_DifferentTiers_ShouldTrackSeparately()
    {
        // Act
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        _analyzer.RecordRecall("user1", "test query", "Session", 5);
        var stats = _analyzer.GetStatistics("user1");

        // Assert
        Assert.Equal(2, stats.TotalRecalls);
        Assert.Equal(0, stats.DuplicateRecalls);
        Assert.Equal(2, stats.UniqueQueries);
    }

    [Fact]
    public void RecordRecall_DifferentLimits_ShouldTrackSeparately()
    {
        // Act
        _analyzer.RecordRecall("user1", "test query", "Working", 5);
        _analyzer.RecordRecall("user1", "test query", "Working", 10);
        var stats = _analyzer.GetStatistics("user1");

        // Assert
        Assert.Equal(2, stats.TotalRecalls);
        Assert.Equal(0, stats.DuplicateRecalls);
        Assert.Equal(2, stats.UniqueQueries);
    }

    [Fact]
    public void GetStatistics_NoUserId_ShouldReturnGlobalStats()
    {
        // Arrange
        _analyzer.RecordRecall("user1", "query1", "Working", 5);
        _analyzer.RecordRecall("user2", "query2", "Working", 5);
        _analyzer.RecordRecall("user3", "query3", "Working", 5);

        // Act
        var stats = _analyzer.GetStatistics();

        // Assert
        Assert.Null(stats.UserId);
        Assert.Equal(3, stats.TotalRecalls);
        Assert.Equal(3, stats.UniqueUsers);
    }

    [Fact]
    public void GetAlerts_HighDuplicateRatio_ShouldGenerateAlert()
    {
        // Arrange - Create high duplicate ratio (>30%)
        _analyzer.RecordRecall("user1", "same query", "Working", 5);
        _analyzer.RecordRecall("user1", "same query", "Working", 5);
        _analyzer.RecordRecall("user1", "same query", "Working", 5);
        _analyzer.RecordRecall("user1", "same query", "Working", 5);

        // Act
        var alerts = _analyzer.GetAlerts("user1");

        // Assert
        Assert.Contains(alerts, a => a.AlertType == RecallPatternAlertType.HighDuplicateRatio);
    }

    [Fact]
    public void GetAlerts_ExcessiveDuplicates_ShouldGenerateAlert()
    {
        // Arrange - Repeat same query more than threshold (5)
        for (int i = 0; i < 7; i++)
        {
            _analyzer.RecordRecall("user1", "excessive query", "Working", 5);
        }

        // Act
        var alerts = _analyzer.GetAlerts("user1");

        // Assert
        Assert.Contains(alerts, a => a.AlertType == RecallPatternAlertType.ExcessiveDuplicates);
    }

    [Fact]
    public void GetRecommendations_HighDuplicateRatio_ShouldRecommendCaching()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _analyzer.RecordRecall("user1", "query", "Working", 5);
        }

        // Act
        var recommendations = _analyzer.GetRecommendations("user1");

        // Assert
        Assert.Contains(recommendations, r => r.RecommendationType == RecommendationType.EnableCaching);
    }

    [Fact]
    public void Reset_WithUserId_ShouldResetOnlyThatUser()
    {
        // Arrange
        _analyzer.RecordRecall("user1", "query", "Working", 5);
        _analyzer.RecordRecall("user2", "query", "Working", 5);

        // Act
        _analyzer.Reset("user1");
        var user1Stats = _analyzer.GetStatistics("user1");
        var user2Stats = _analyzer.GetStatistics("user2");

        // Assert
        Assert.Equal(0, user1Stats.TotalRecalls);
        Assert.Equal(1, user2Stats.TotalRecalls);
    }

    [Fact]
    public void Reset_NoUserId_ShouldResetAll()
    {
        // Arrange
        _analyzer.RecordRecall("user1", "query", "Working", 5);
        _analyzer.RecordRecall("user2", "query", "Working", 5);

        // Act
        _analyzer.Reset();
        var globalStats = _analyzer.GetStatistics();

        // Assert
        Assert.Equal(0, globalStats.TotalRecalls);
        Assert.Equal(0, globalStats.UniqueUsers);
    }

    [Fact]
    public void GetStatistics_TopDuplicateQueries_ShouldReturnTopN()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
            _analyzer.RecordRecall("user1", "most duplicated", "Working", 5);
        for (int i = 0; i < 3; i++)
            _analyzer.RecordRecall("user1", "second most", "Working", 5);
        for (int i = 0; i < 2; i++)
            _analyzer.RecordRecall("user1", "third most", "Working", 5);

        // Act
        var stats = _analyzer.GetStatistics("user1");

        // Assert
        Assert.NotNull(stats.TopDuplicateQueries);
        Assert.Equal(3, stats.TopDuplicateQueries!.Count);
        Assert.Equal("most duplicated", stats.TopDuplicateQueries[0].Query);
        Assert.Equal(5, stats.TopDuplicateQueries[0].Count);
    }

    [Fact]
    public void GetAlerts_NoAlerts_ShouldReturnEmpty()
    {
        // Arrange - Single unique query
        _analyzer.RecordRecall("user1", "unique query", "Working", 5);

        // Act
        var alerts = _analyzer.GetAlerts("user1");

        // Assert
        Assert.Empty(alerts);
    }

    [Fact]
    public void GetRecommendations_NoIssues_ShouldReturnEmpty()
    {
        // Arrange - Few unique queries
        _analyzer.RecordRecall("user1", "query1", "Working", 5);
        _analyzer.RecordRecall("user1", "query2", "Working", 5);

        // Act
        var recommendations = _analyzer.GetRecommendations("user1");

        // Assert
        Assert.Empty(recommendations);
    }

    [Fact]
    public void RecordRecall_MultipleUsers_ShouldTrackSeparately()
    {
        // Act
        _analyzer.RecordRecall("user1", "query", "Working", 5);
        _analyzer.RecordRecall("user1", "query", "Working", 5);
        _analyzer.RecordRecall("user2", "query", "Working", 5);

        var user1Stats = _analyzer.GetStatistics("user1");
        var user2Stats = _analyzer.GetStatistics("user2");

        // Assert
        Assert.Equal(2, user1Stats.TotalRecalls);
        Assert.Equal(1, user1Stats.DuplicateRecalls);
        Assert.Equal(1, user2Stats.TotalRecalls);
        Assert.Equal(0, user2Stats.DuplicateRecalls);
    }
}
