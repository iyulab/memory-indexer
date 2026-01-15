using MemoryIndexer.Sdk.Evaluation;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Evaluation;

public class NiahTestConfigTests
{
    [Fact]
    public void NiahTestConfig_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new NiahTestConfig
        {
            Needle = "secret code",
            NeedleQuery = "code"
        };

        // Assert
        Assert.Equal(0.5, config.NeedlePosition);
        Assert.Equal(100_000, config.TargetHaystackTokens);
        Assert.Equal(100, config.SegmentSize);
        Assert.Equal(5, config.RecallLimit);
        Assert.Equal(0.01, config.TargetCcr);
        Assert.Null(config.UserId);
        Assert.Null(config.SessionId);
        Assert.Null(config.HaystackContent);
    }

    [Theory]
    [InlineData(0.0)]   // Start
    [InlineData(0.25)]  // 25%
    [InlineData(0.5)]   // Middle
    [InlineData(0.75)]  // 75%
    [InlineData(1.0)]   // End
    public void NiahTestConfig_NeedlePosition_AcceptsValidValues(double position)
    {
        // Arrange & Act
        var config = new NiahTestConfig
        {
            Needle = "test needle",
            NeedleQuery = "needle",
            NeedlePosition = position
        };

        // Assert
        Assert.Equal(position, config.NeedlePosition);
    }

    [Fact]
    public void NiahTestConfig_WithCustomHaystack_StoresCorrectly()
    {
        // Arrange
        var customHaystack = "This is a long text that serves as the haystack for testing...";

        // Act
        var config = new NiahTestConfig
        {
            Needle = "secret information",
            NeedleQuery = "secret",
            HaystackContent = customHaystack
        };

        // Assert
        Assert.Equal(customHaystack, config.HaystackContent);
    }
}

public class NiahTestResultTests
{
    [Fact]
    public void NiahTestResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new NiahTestResult();

        // Assert
        Assert.False(result.Success);
        Assert.False(result.NeedleFound);
        Assert.Null(result.NeedleRank);
        Assert.Equal(0, result.Ccr);
        Assert.Equal(0, result.HaystackTokens);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NiahTestResult_SuccessfulTest_HasCorrectValues()
    {
        // Arrange & Act
        var result = new NiahTestResult
        {
            Success = true,
            NeedleFound = true,
            NeedleRank = 1,
            Ccr = 0.005,
            RecallAtK = 1.0,
            HaystackTokens = 100_000,
            RecalledTokens = 500,
            MemoriesStored = 1000,
            StoreLatencyMs = 5000,
            RecallLatencyMs = 50
        };

        // Assert
        Assert.True(result.Success);
        Assert.True(result.NeedleFound);
        Assert.Equal(1, result.NeedleRank);
        Assert.True(result.Ccr < 0.01);  // Below 1% target
    }

    [Fact]
    public void NiahTestResult_FailedTest_HasErrorMessage()
    {
        // Arrange & Act
        var result = new NiahTestResult
        {
            Success = false,
            NeedleFound = false,
            Error = "Needle not found in recalled memories"
        };

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}

public class NiahTestSuiteTests
{
    [Fact]
    public void NiahTestSuite_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var suite = new NiahTestSuite();

        // Assert
        Assert.Empty(suite.Results);
        Assert.Equal(0, suite.OverallSuccessRate);
        Assert.Equal(0, suite.AverageCcr);
        Assert.Equal(0, suite.AverageRecallLatencyMs);
    }

    [Fact]
    public void NiahTestSuite_WithResults_CalculatesAggregates()
    {
        // Arrange
        var results = new List<NiahTestResult>
        {
            new() { Success = true, Ccr = 0.005, RecallLatencyMs = 50 },
            new() { Success = true, Ccr = 0.008, RecallLatencyMs = 60 },
            new() { Success = false, Ccr = 0.02, RecallLatencyMs = 70 }
        };

        // Act
        var suite = new NiahTestSuite
        {
            Results = results,
            OverallSuccessRate = 2.0 / 3.0,
            AverageCcr = (0.005 + 0.008 + 0.02) / 3.0,
            AverageRecallLatencyMs = (50 + 60 + 70) / 3.0
        };

        // Assert
        Assert.Equal(3, suite.Results.Count);
        Assert.Equal(2.0 / 3.0, suite.OverallSuccessRate, precision: 5);
        Assert.Equal(0.011, suite.AverageCcr, precision: 5);
        Assert.Equal(60, suite.AverageRecallLatencyMs, precision: 5);
    }
}

#region Multi-Needle Test Types (RULER-style)

/// <summary>
/// Tests for multi-needle NIAH configuration.
/// Phase v0.11.0: RULER-style multi-needle retrieval.
/// </summary>
public class MultiNeedleTestConfigTests
{
    [Fact]
    public void MultiNeedleTestConfig_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new MultiNeedleTestConfig
        {
            Needles =
            [
                new NeedleInfo { Content = "First secret", Query = "first" },
                new NeedleInfo { Content = "Second secret", Query = "second" }
            ]
        };

        // Assert
        Assert.Equal(2, config.Needles.Count);
        Assert.Equal(100_000, config.TargetHaystackTokens);
        Assert.Equal(100, config.SegmentSize);
        Assert.Equal(10, config.RecallLimit);
        Assert.Equal(0.05, config.TargetCcr);
        Assert.Equal(1.0, config.MinNeedleRecoveryRate);
        Assert.Equal(MultiNeedleQueryStrategy.CombinedQuery, config.QueryStrategy);
        Assert.Null(config.UserId);
        Assert.Null(config.SessionId);
    }

    [Fact]
    public void NeedleInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var needle = new NeedleInfo
        {
            Content = "Secret information",
            Query = "secret"
        };

        // Assert
        Assert.NotEmpty(needle.Id);
        Assert.Equal(8, needle.Id.Length); // Guid substring
        Assert.Equal("Secret information", needle.Content);
        Assert.Equal("secret", needle.Query);
        Assert.Null(needle.Position);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void NeedleInfo_WithPosition_StoresCorrectly(double position)
    {
        // Arrange & Act
        var needle = new NeedleInfo
        {
            Content = "Positioned secret",
            Query = "positioned",
            Position = position
        };

        // Assert
        Assert.Equal(position, needle.Position);
    }

    [Theory]
    [InlineData(MultiNeedleQueryStrategy.CombinedQuery)]
    [InlineData(MultiNeedleQueryStrategy.SeparateQueries)]
    [InlineData(MultiNeedleQueryStrategy.SequentialQueries)]
    public void MultiNeedleTestConfig_AcceptsAllQueryStrategies(MultiNeedleQueryStrategy strategy)
    {
        // Arrange & Act
        var config = new MultiNeedleTestConfig
        {
            Needles = [new NeedleInfo { Content = "test", Query = "test" }],
            QueryStrategy = strategy
        };

        // Assert
        Assert.Equal(strategy, config.QueryStrategy);
    }

    [Fact]
    public void MultiNeedleTestConfig_WithCustomRecoveryRate_StoresCorrectly()
    {
        // Arrange & Act
        var config = new MultiNeedleTestConfig
        {
            Needles =
            [
                new NeedleInfo { Content = "needle1", Query = "query1" },
                new NeedleInfo { Content = "needle2", Query = "query2" },
                new NeedleInfo { Content = "needle3", Query = "query3" }
            ],
            MinNeedleRecoveryRate = 0.67 // At least 2 out of 3 needles
        };

        // Assert
        Assert.Equal(3, config.Needles.Count);
        Assert.Equal(0.67, config.MinNeedleRecoveryRate);
    }
}

public class MultiNeedleTestResultTests
{
    [Fact]
    public void MultiNeedleTestResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new MultiNeedleTestResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.TotalNeedles);
        Assert.Equal(0, result.NeedlesFound);
        Assert.Equal(0, result.RecoveryRate);
        Assert.Empty(result.NeedleResults);
        Assert.Null(result.Error);
    }

    [Fact]
    public void MultiNeedleTestResult_RecoveryRate_CalculatesCorrectly()
    {
        // Arrange
        var result = new MultiNeedleTestResult
        {
            TotalNeedles = 4,
            NeedlesFound = 3
        };

        // Assert
        Assert.Equal(0.75, result.RecoveryRate, precision: 5);
    }

    [Fact]
    public void MultiNeedleTestResult_FullRecovery_IsSuccess()
    {
        // Arrange & Act
        var result = new MultiNeedleTestResult
        {
            Success = true,
            TotalNeedles = 3,
            NeedlesFound = 3,
            Ccr = 0.03,
            HaystackTokens = 100_000,
            RecalledTokens = 3000,
            NeedleResults =
            [
                new NeedleRecoveryResult { NeedleId = "n1", Found = true, Rank = 1 },
                new NeedleRecoveryResult { NeedleId = "n2", Found = true, Rank = 2 },
                new NeedleRecoveryResult { NeedleId = "n3", Found = true, Rank = 3 }
            ]
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1.0, result.RecoveryRate);
        Assert.Equal(3, result.NeedleResults.Count);
        Assert.All(result.NeedleResults, r => Assert.True(r.Found));
    }

    [Fact]
    public void MultiNeedleTestResult_PartialRecovery_CalculatesCorrectly()
    {
        // Arrange & Act
        var result = new MultiNeedleTestResult
        {
            TotalNeedles = 4,
            NeedlesFound = 2,
            NeedleResults =
            [
                new NeedleRecoveryResult { NeedleId = "n1", Found = true, Rank = 1 },
                new NeedleRecoveryResult { NeedleId = "n2", Found = false, Rank = null },
                new NeedleRecoveryResult { NeedleId = "n3", Found = true, Rank = 3 },
                new NeedleRecoveryResult { NeedleId = "n4", Found = false, Rank = null }
            ]
        };

        // Assert
        Assert.Equal(0.5, result.RecoveryRate);
        Assert.Equal(2, result.NeedleResults.Count(r => r.Found));
        Assert.Equal(2, result.NeedleResults.Count(r => !r.Found));
    }
}

public class NeedleRecoveryResultTests
{
    [Fact]
    public void NeedleRecoveryResult_Found_HasRank()
    {
        // Arrange & Act
        var result = new NeedleRecoveryResult
        {
            NeedleId = "test-needle",
            Found = true,
            Rank = 2,
            Query = "test query",
            RecallLatencyMs = 50
        };

        // Assert
        Assert.True(result.Found);
        Assert.Equal(2, result.Rank);
        Assert.Equal("test query", result.Query);
        Assert.Equal(50, result.RecallLatencyMs);
    }

    [Fact]
    public void NeedleRecoveryResult_NotFound_HasNullRank()
    {
        // Arrange & Act
        var result = new NeedleRecoveryResult
        {
            NeedleId = "missing-needle",
            Found = false,
            Rank = null,
            Query = "missing query",
            RecallLatencyMs = 100
        };

        // Assert
        Assert.False(result.Found);
        Assert.Null(result.Rank);
    }
}

#endregion
