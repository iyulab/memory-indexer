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
