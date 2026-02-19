using FluentAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.Interfaces;

/// <summary>
/// Tests for ITierManager promotion logic in 4-tier architecture.
/// Phase 32.4: Category 3 - ITierManager promotion logic tests (40+ tests)
/// </summary>
public class ITierManagerTests
{
    private static TierManager CreateTierManager()
    {
        var options = Options.Create(new MemoryIndexerOptions
        {
            SensoryBuffer = new SensoryBufferOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(60),
                TokenThreshold = 500,
                TurnThreshold = 3
            }
        });
        var logger = NullLogger<TierManager>.Instance;
        return new TierManager(options, logger);
    }

    private static TierEvaluationContext CreateContext(
        int turnCount = 0,
        int tokenCount = 0,
        TimeSpan? timeElapsed = null,
        bool topicChangeDetected = false,
        bool sessionEnding = false)
    {
        return new TierEvaluationContext
        {
            UserId = "user1",
            SessionId = "session1",
            TurnCount = turnCount,
            TokenCount = tokenCount,
            TimeElapsed = timeElapsed ?? TimeSpan.Zero,
            TopicChangeDetected = topicChangeDetected,
            TopicId = "topic-123",
            SessionEnding = sessionEnding
        };
    }

    #region EvaluatePromotionAsync Tests (10 tests)

    [Fact]
    public async Task EvaluatePromotionAsync_BufferTier_WithIdleTimeout_ShouldRecommendPromotion()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };
        var context = CreateContext(timeElapsed: TimeSpan.FromSeconds(61)); // > 60s threshold

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeTrue();
        recommendation.TargetTier.Should().Be(Tier.Short);
        recommendation.Reason.Should().Be(PromotionReason.AutomaticTrigger);
        recommendation.SatisfiedTriggers.Should().Contain("IdleTimeout");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_BufferTier_WithTokenThreshold_ShouldRecommendPromotion()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };
        var context = CreateContext(tokenCount: 501); // > 500 threshold

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeTrue();
        recommendation.TargetTier.Should().Be(Tier.Short);
        recommendation.SatisfiedTriggers.Should().Contain("TokenThreshold");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_BufferTier_WithTurnThreshold_ShouldRecommendPromotion()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };
        var context = CreateContext(turnCount: 3); // >= 3 threshold

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeTrue();
        recommendation.TargetTier.Should().Be(Tier.Short);
        recommendation.SatisfiedTriggers.Should().Contain("TurnThreshold");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_BufferTier_NoTriggersSatisfied_ShouldNotRecommend()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };
        var context = CreateContext(turnCount: 1, tokenCount: 100, timeElapsed: TimeSpan.FromSeconds(10));

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeFalse();
        recommendation.SatisfiedTriggers.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluatePromotionAsync_ShortTier_WithTopicChange_ShouldRecommendPromotion()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };
        var context = CreateContext(topicChangeDetected: true);

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeTrue();
        recommendation.TargetTier.Should().Be(Tier.Long);
        recommendation.Reason.Should().Be(PromotionReason.TopicBoundary);
        recommendation.SatisfiedTriggers.Should().Contain("TopicChange");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_ShortTier_NoTriggersSatisfied_ShouldNotRecommend()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };
        var context = CreateContext();

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluatePromotionAsync_LongTier_WithConfidenceAndConfirmations_ShouldRecommendPromotion()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit
        {
            Tier = Tier.Long,
            Content = "Important fact",
            Confidence = 0.85f,
            ConfirmCount = 3
        };
        var context = CreateContext();

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeTrue();
        recommendation.TargetTier.Should().Be(Tier.Archive);
        recommendation.Reason.Should().Be(PromotionReason.ThresholdMet);
        recommendation.Confidence.Should().BeGreaterThan(0.9f);
    }

    [Fact]
    public async Task EvaluatePromotionAsync_LongTier_LowConfidence_ShouldNotRecommend()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit
        {
            Tier = Tier.Long,
            Content = "Uncertain fact",
            Confidence = 0.5f, // < 0.8 threshold
            ConfirmCount = 3
        };
        var context = CreateContext();

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeFalse();
        recommendation.Explanation.Should().Contain("confidence");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_LongTier_LowConfirmations_ShouldNotRecommend()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit
        {
            Tier = Tier.Long,
            Content = "Unconfirmed fact",
            Confidence = 0.9f,
            ConfirmCount = 1 // < 3 threshold
        };
        var context = CreateContext();

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeFalse();
        recommendation.Explanation.Should().Contain("confirmations");
    }

    [Fact]
    public async Task EvaluatePromotionAsync_ArchiveTier_ShouldNotRecommend()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Archive, Content = "Archive memory" };
        var context = CreateContext();

        // Act
        var recommendation = await tierManager.EvaluatePromotionAsync(memory, context);

        // Assert
        recommendation.ShouldPromote.Should().BeFalse();
        recommendation.Explanation.Should().Contain("highest tier");
    }

    #endregion

    #region CheckPromotionTriggersAsync Tests (12 tests)

    [Fact]
    public async Task CheckPromotionTriggersAsync_BufferTier_IdleTimeout_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(timeElapsed: TimeSpan.FromSeconds(61));

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.LogicType.Should().Be(TriggerLogicType.Or);
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.IdleTimeout);
        status.PrimaryTrigger.Should().NotBeNull();
        status.PrimaryTrigger!.Type.Should().Be(PromotionTriggerType.IdleTimeout);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_BufferTier_TokenThreshold_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(tokenCount: 500);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.TokenThreshold);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_BufferTier_TurnThreshold_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(turnCount: 3);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.TurnThreshold);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_BufferTier_NoTriggers_ShouldNotBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(turnCount: 1, tokenCount: 100, timeElapsed: TimeSpan.FromSeconds(10));

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        status.IsTriggered.Should().BeFalse();
        status.SatisfiedTriggers.Should().BeEmpty();
        status.AllTriggers.Should().HaveCount(3); // Idle, Token, Turn
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_BufferTier_MultipleTriggers_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(turnCount: 3, tokenCount: 500, timeElapsed: TimeSpan.FromSeconds(61));

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.SatisfiedTriggers.Should().HaveCount(3); // All 3 triggers satisfied
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_ShortTier_TopicChange_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(topicChangeDetected: true);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Short, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.LogicType.Should().Be(TriggerLogicType.Or);
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.TopicChange);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_ShortTier_SessionEnd_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(sessionEnding: true);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Short, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.SessionEnd);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_ShortTier_NoTriggers_ShouldNotBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext();

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Short, context);

        // Assert
        status.IsTriggered.Should().BeFalse();
        status.AllTriggers.Should().HaveCount(5); // Idle, Token, Turn, TopicChange, SessionEnd
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_LongTier_SessionEnd_ShouldBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(sessionEnding: true);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Long, context);

        // Assert
        status.IsTriggered.Should().BeTrue();
        status.LogicType.Should().Be(TriggerLogicType.And); // Archive uses AND logic
        status.SatisfiedTriggers.Should().ContainSingle(t => t.Type == PromotionTriggerType.SessionEnd);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_LongTier_NoSessionEnd_ShouldNotBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(sessionEnding: false);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Long, context);

        // Assert
        status.IsTriggered.Should().BeFalse();
        status.AllTriggers.Should().HaveCount(1); // SessionEnd only
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_ArchiveTier_ShouldNotBeTriggered()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext();

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Archive, context);

        // Assert
        status.IsTriggered.Should().BeFalse();
        status.LogicType.Should().Be(TriggerLogicType.Or);
    }

    [Fact]
    public async Task CheckPromotionTriggersAsync_TriggerDetails_ShouldIncludeCurrentAndThreshold()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var context = CreateContext(turnCount: 5, tokenCount: 600);

        // Act
        var status = await tierManager.CheckPromotionTriggersAsync(Tier.Buffer, context);

        // Assert
        var turnTrigger = status.AllTriggers.First(t => t.Type == PromotionTriggerType.TurnThreshold);
        turnTrigger.CurrentValue.Should().Be(5);
        turnTrigger.ThresholdValue.Should().Be(3);
        turnTrigger.IsSatisfied.Should().BeTrue();
        turnTrigger.Description.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region PromoteAsync Tests (8 tests)

    [Fact]
    public async Task PromoteAsync_BufferToShort_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test memory" };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Short, PromotionReason.AutomaticTrigger);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Buffer);
        result.NewTier.Should().Be(Tier.Short);
        result.UpdatedMemory.Should().NotBeNull();
        result.UpdatedMemory!.Tier.Should().Be(Tier.Short);
        result.Reason.Should().Be(PromotionReason.AutomaticTrigger);
    }

    [Fact]
    public async Task PromoteAsync_ShortToLong_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test memory" };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Long, PromotionReason.TopicBoundary);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Short);
        result.NewTier.Should().Be(Tier.Long);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Long);
    }

    [Fact]
    public async Task PromoteAsync_LongToArchive_EpisodicMemory_ShouldConvertToSemantic()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit
        {
            Tier = Tier.Long,
            Type = MemoryType.Episodic,
            Content = "User loves coffee"
        };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Archive, PromotionReason.ThresholdMet);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Long);
        result.NewTier.Should().Be(Tier.Archive);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Archive);
        result.UpdatedMemory.Type.Should().Be(MemoryType.Semantic); // Converted to semantic
    }

    [Fact]
    public async Task PromoteAsync_LongToArchive_SemanticMemory_ShouldRemainSemantic()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit
        {
            Tier = Tier.Long,
            Type = MemoryType.Semantic,
            Content = "Paris is the capital of France"
        };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Archive, PromotionReason.ThresholdMet);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdatedMemory!.Type.Should().Be(MemoryType.Semantic); // Remains semantic
    }

    [Fact]
    public async Task PromoteAsync_TargetTierNotHigher_ShouldFail()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Buffer, PromotionReason.Manual);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must be higher");
        result.UpdatedMemory.Should().BeNull();
    }

    [Fact]
    public async Task PromoteAsync_SameTier_ShouldFail()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Short, PromotionReason.Manual);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must be higher");
    }

    [Fact]
    public async Task PromoteAsync_BufferToArchive_SkippingTiers_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Critical memory" };

        // Act
        var result = await tierManager.PromoteAsync(memory, Tier.Archive, PromotionReason.Manual);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Buffer);
        result.NewTier.Should().Be(Tier.Archive);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Archive);
    }

    [Fact]
    public async Task PromoteAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };
        var originalTimestamp = memory.UpdatedAt;

        // Act
        await Task.Delay(10); // Ensure time difference
        var result = await tierManager.PromoteAsync(memory, Tier.Short, PromotionReason.AutomaticTrigger);

        // Assert
        result.UpdatedMemory!.UpdatedAt.Should().BeAfter(originalTimestamp);
    }

    #endregion

    #region DemoteAsync Tests (6 tests)

    [Fact]
    public async Task DemoteAsync_LongToShort_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Long, Content = "Demote test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Short, PromotionReason.LowRetention);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Long);
        result.NewTier.Should().Be(Tier.Short);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Short);
        result.Reason.Should().Be(PromotionReason.LowRetention);
    }

    [Fact]
    public async Task DemoteAsync_ArchiveToLong_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Archive, Content = "Demote test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Long, PromotionReason.CapacityEviction);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Archive);
        result.NewTier.Should().Be(Tier.Long);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Long);
    }

    [Fact]
    public async Task DemoteAsync_TargetTierNotLower_ShouldFail()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Long, PromotionReason.LowRetention);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must be lower");
        result.UpdatedMemory.Should().BeNull();
    }

    [Fact]
    public async Task DemoteAsync_SameTier_ShouldFail()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Short, Content = "Test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Short, PromotionReason.LowRetention);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must be lower");
    }

    [Fact]
    public async Task DemoteAsync_ArchiveToBuffer_SkippingTiers_ShouldSucceed()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Archive, Content = "Evict test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Buffer, PromotionReason.CapacityEviction);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalTier.Should().Be(Tier.Archive);
        result.NewTier.Should().Be(Tier.Buffer);
        result.UpdatedMemory!.Tier.Should().Be(Tier.Buffer);
    }

    [Fact]
    public async Task DemoteAsync_BufferTier_CannotDemote()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Buffer, Content = "Test" };

        // Act
        var result = await tierManager.DemoteAsync(memory, Tier.Short, PromotionReason.LowRetention);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("must be lower");
    }

    #endregion

    #region GetTierStatistics Tests (4 tests)

    [Fact]
    public void GetTierStatistics_BufferTier_ShouldReturnStatistics()
    {
        // Arrange
        var tierManager = CreateTierManager();

        // Act
        var stats = tierManager.GetTierStatistics(Tier.Buffer);

        // Assert
        stats.Should().NotBeNull();
        stats.Tier.Should().Be(Tier.Buffer);
        stats.TotalPromotions.Should().Be(0); // No promotions yet
        stats.TotalDemotions.Should().Be(0);
    }

    [Fact]
    public async Task GetTierStatistics_AfterPromotion_ShouldTrackPromotions()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory1 = new MemoryUnit { Tier = Tier.Buffer, Content = "Test 1" };
        var memory2 = new MemoryUnit { Tier = Tier.Buffer, Content = "Test 2" };

        // Act
        await tierManager.PromoteAsync(memory1, Tier.Short, PromotionReason.AutomaticTrigger);
        await tierManager.PromoteAsync(memory2, Tier.Short, PromotionReason.AutomaticTrigger);
        var stats = tierManager.GetTierStatistics(Tier.Buffer);

        // Assert
        stats.TotalPromotions.Should().Be(2);
    }

    [Fact]
    public async Task GetTierStatistics_AfterDemotion_ShouldTrackDemotions()
    {
        // Arrange
        var tierManager = CreateTierManager();
        var memory = new MemoryUnit { Tier = Tier.Long, Content = "Test" };

        // Act
        await tierManager.DemoteAsync(memory, Tier.Short, PromotionReason.LowRetention);
        var sourceStats = tierManager.GetTierStatistics(Tier.Long); // Source tier tracks demotion (consistent with promotion tracking on source)

        // Assert
        sourceStats.TotalDemotions.Should().Be(1);
    }

    [Fact]
    public void GetTierStatistics_AllTiers_ShouldReturnForEachTier()
    {
        // Arrange
        var tierManager = CreateTierManager();

        // Act & Assert
        var bufferStats = tierManager.GetTierStatistics(Tier.Buffer);
        var shortStats = tierManager.GetTierStatistics(Tier.Short);
        var longStats = tierManager.GetTierStatistics(Tier.Long);
        var archiveStats = tierManager.GetTierStatistics(Tier.Archive);

        bufferStats.Tier.Should().Be(Tier.Buffer);
        shortStats.Tier.Should().Be(Tier.Short);
        longStats.Tier.Should().Be(Tier.Long);
        archiveStats.Tier.Should().Be(Tier.Archive);
    }

    #endregion
}
