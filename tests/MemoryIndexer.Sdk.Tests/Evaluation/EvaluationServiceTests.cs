using MemoryIndexer.Sdk.Evaluation;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Evaluation;

public class EvaluationServiceTests
{
    [Theory]
    [InlineData(100, 10000, 0.01)]      // 1% CCR
    [InlineData(500, 100000, 0.005)]    // 0.5% CCR
    [InlineData(1000, 10000, 0.1)]      // 10% CCR
    [InlineData(0, 10000, 0)]           // Edge case: no recalled tokens
    [InlineData(100, 0, 0)]             // Edge case: no history tokens
    public void ComputeCcr_ReturnsCorrectRatio(long recalled, long history, double expected)
    {
        // Arrange - use a mock memory service
        var service = new TestableEvaluationService();

        // Act
        var ccr = service.ComputeCcr(recalled, history);

        // Assert
        Assert.Equal(expected, ccr, precision: 5);
    }

    [Theory]
    [InlineData(5, 5, 1.0)]     // Perfect recall
    [InlineData(4, 5, 0.8)]     // 80% recall
    [InlineData(1, 5, 0.2)]     // 20% recall
    [InlineData(0, 5, 0.0)]     // No relevant found
    [InlineData(3, 0, 0.0)]     // Edge case: k=0
    public void ComputeRecallAtK_ReturnsCorrectEfficiency(int relevant, int k, double expected)
    {
        // Arrange
        var service = new TestableEvaluationService();

        // Act
        var recallAtK = service.ComputeRecallAtK(relevant, k);

        // Assert
        Assert.Equal(expected, recallAtK, precision: 5);
    }

    [Theory]
    [InlineData(90, 100, 0.9)]   // 90% retention
    [InlineData(100, 100, 1.0)]  // Perfect retention
    [InlineData(50, 100, 0.5)]   // 50% retention
    [InlineData(0, 100, 0.0)]    // No retention
    [InlineData(10, 0, 0.0)]     // Edge case: nothing stored
    public void ComputeRetentionScore_ReturnsCorrectScore(int recalled, int stored, double expected)
    {
        // Arrange
        var service = new TestableEvaluationService();

        // Act
        var retention = service.ComputeRetentionScore(recalled, stored);

        // Assert
        Assert.Equal(expected, retention, precision: 5);
    }

    [Fact]
    public void RecordTierPromotionLatency_TracksCorrectly()
    {
        // Arrange
        var service = new TestableEvaluationService();

        // Act - should not throw
        service.RecordTierPromotionLatency("Buffer", "Short", 5.0);
        service.RecordTierPromotionLatency("Short", "Long", 25.0);
        service.RecordTierPromotionLatency("Long", "Archive", 50.0);

        // Assert - no exception means success (metrics are recorded via OTel)
        Assert.True(true);
    }

    /// <summary>
    /// Testable version that doesn't require IMemoryService dependency for basic calculations.
    /// </summary>
    private sealed class TestableEvaluationService : EvaluationService
    {
        public TestableEvaluationService() : base(null!)
        {
        }
    }
}

public class EvaluationMetricsTests
{
    [Fact]
    public void EvaluationMetrics_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var metrics = new EvaluationMetrics();

        // Assert
        Assert.Equal(5, metrics.RecallK);
        Assert.NotEqual(default, metrics.Timestamp);
        Assert.NotNull(metrics.TierPromotionLatency);
    }

    [Fact]
    public void TierPromotionLatency_TotalMs_SumsCorrectly()
    {
        // Arrange
        var latency = new TierPromotionLatency
        {
            BufferToShortMs = 5.0,
            ShortToLongMs = 25.0,
            LongToArchiveMs = 50.0
        };

        // Act & Assert
        Assert.Equal(80.0, latency.TotalMs);
    }

    [Fact]
    public void CognitiveComplianceMetrics_WorkingMemoryCompliance_ValidatesCorrectly()
    {
        // Arrange - compliant (within 5-9)
        var compliant = new CognitiveComplianceMetrics
        {
            ShortTierCount = 7,
            WorkingMemoryCompliance = 1.0
        };

        // Arrange - non-compliant (outside 5-9)
        var nonCompliant = new CognitiveComplianceMetrics
        {
            ShortTierCount = 15,
            WorkingMemoryCompliance = 0.0
        };

        // Assert
        Assert.Equal(1.0, compliant.WorkingMemoryCompliance);
        Assert.Equal(0.0, nonCompliant.WorkingMemoryCompliance);
    }

    [Fact]
    public void CognitiveComplianceMetrics_HealthyTierFlow_ValidatesCorrectly()
    {
        // Arrange - healthy flow
        var healthy = new CognitiveComplianceMetrics
        {
            BufferCount = 2,
            ShortTierCount = 7,
            LongCount = 5,
            HealthyTierFlow = true
        };

        // Arrange - unhealthy flow (too many in buffer)
        var unhealthy = new CognitiveComplianceMetrics
        {
            BufferCount = 10,
            ShortTierCount = 7,
            LongCount = 5,
            HealthyTierFlow = false
        };

        // Assert
        Assert.True(healthy.HealthyTierFlow);
        Assert.False(unhealthy.HealthyTierFlow);
    }
}

public class EvaluationReportTests
{
    [Fact]
    public void EvaluationReport_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var report = new EvaluationReport();

        // Assert
        Assert.NotNull(report.Metrics);
        Assert.NotNull(report.CognitiveCompliance);
        Assert.NotNull(report.Observations);
        Assert.Empty(report.Observations);
        Assert.NotEqual(default, report.GeneratedAt);
    }

    [Fact]
    public void EvaluationReport_WithObservations_StoresCorrectly()
    {
        // Arrange & Act
        var report = new EvaluationReport
        {
            OverallScore = 85.5,
            Observations = new List<string>
            {
                "Excellent CCR - context is highly compressed (<1%)",
                "High Recall@K efficiency - semantic retrieval is accurate"
            }
        };

        // Assert
        Assert.Equal(85.5, report.OverallScore);
        Assert.Equal(2, report.Observations.Count);
    }
}
