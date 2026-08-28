using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services.ContextStrategies;
using Xunit;

namespace MemoryIndexer.Tests.Services.ContextBuilding;

public class ContextStrategyTests
{
    [Fact]
    public void BalancedStrategy_ShouldAllocate30_25_25_20()
    {
        // Arrange
        var strategy = new BalancedStrategy();

        // Act
        var allocation = strategy.GetAllocation();

        // Assert
        allocation.Recent.Should().BeApproximately(0.30, 0.01);
        allocation.Semantic.Should().BeApproximately(0.25, 0.01);
        allocation.Episodic.Should().BeApproximately(0.25, 0.01);
        allocation.Fact.Should().BeApproximately(0.20, 0.01);
    }

    [Fact]
    public void RecentHeavyStrategy_ShouldAllocate45_10_35_10()
    {
        // Arrange
        var strategy = new RecentHeavyStrategy();

        // Act
        var allocation = strategy.GetAllocation();

        // Assert
        allocation.Recent.Should().BeApproximately(0.45, 0.01);
        allocation.Semantic.Should().BeApproximately(0.10, 0.01);
        allocation.Episodic.Should().BeApproximately(0.35, 0.01);
        allocation.Fact.Should().BeApproximately(0.10, 0.01);
    }

    [Fact]
    public void SemanticHeavyStrategy_ShouldAllocate15_45_15_25()
    {
        // Arrange
        var strategy = new SemanticHeavyStrategy();

        // Act
        var allocation = strategy.GetAllocation();

        // Assert
        allocation.Recent.Should().BeApproximately(0.15, 0.01);
        allocation.Semantic.Should().BeApproximately(0.45, 0.01);
        allocation.Episodic.Should().BeApproximately(0.15, 0.01);
        allocation.Fact.Should().BeApproximately(0.25, 0.01);
    }

    [Fact]
    public void AllocateBudget_WithBalanced_ShouldDistributeTokens()
    {
        // Arrange
        var strategy = new BalancedStrategy();
        const int totalBudget = 1000;

        // Act
        var breakdown = strategy.AllocateBudget(totalBudget);

        // Assert
        breakdown.RecentTokens.Should().Be(300);     // 30%
        breakdown.SemanticTokens.Should().Be(250);   // 25%
        breakdown.EpisodicTokens.Should().Be(250);   // 25%
        breakdown.FactTokens.Should().Be(200);       // 20%
        breakdown.Total.Should().Be(totalBudget);
    }

    [Fact]
    public void AllocateBudget_WithRecentHeavy_ShouldPrioritizeSessionContext()
    {
        // Arrange
        var strategy = new RecentHeavyStrategy();
        const int totalBudget = 2000;

        // Act
        var breakdown = strategy.AllocateBudget(totalBudget);

        // Assert
        breakdown.RecentTokens.Should().Be(900);     // 45%
        breakdown.SemanticTokens.Should().Be(200);   // 10%
        breakdown.EpisodicTokens.Should().Be(700);   // 35%
        breakdown.FactTokens.Should().Be(200);       // 10%
        breakdown.Total.Should().Be(totalBudget);
    }

    [Fact]
    public void AllocateBudget_WithSemanticHeavy_ShouldPrioritizeUserKnowledge()
    {
        // Arrange
        var strategy = new SemanticHeavyStrategy();
        const int totalBudget = 2000;

        // Act
        var breakdown = strategy.AllocateBudget(totalBudget);

        // Assert
        breakdown.RecentTokens.Should().Be(300);     // 15%
        breakdown.SemanticTokens.Should().Be(900);   // 45%
        breakdown.EpisodicTokens.Should().Be(300);   // 15%
        breakdown.FactTokens.Should().Be(500);       // 25%
        breakdown.Total.Should().Be(totalBudget);
    }

    [Fact]
    public void CustomStrategy_ShouldAllowArbitraryAllocation()
    {
        // Arrange
        var strategy = new CustomStrategy(
            name: "TestCustom",
            recentPercent: 0.25,
            semanticPercent: 0.25,
            episodicPercent: 0.25,
            factPercent: 0.25);

        // Act
        var breakdown = strategy.AllocateBudget(1000);

        // Assert
        breakdown.RecentTokens.Should().Be(250);
        breakdown.SemanticTokens.Should().Be(250);
        breakdown.EpisodicTokens.Should().Be(250);
        breakdown.FactTokens.Should().Be(250);
    }

    [Fact]
    public void CustomStrategy_WithInvalidPercentages_ShouldThrow()
    {
        // Act & Assert
        var act = () => new CustomStrategy(
            name: "Invalid",
            recentPercent: 0.5,
            semanticPercent: 0.5,
            episodicPercent: 0.5,  // Total = 1.5, invalid
            factPercent: 0.0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must sum to 1.0*");
    }

    [Fact]
    public void CustomStrategy_From_ShouldCloneAllocation()
    {
        // Arrange
        var source = new RecentHeavyStrategy();

        // Act
        var cloned = CustomStrategy.From(source, "ClonedRecent");

        // Assert
        cloned.Name.Should().Be("ClonedRecent");
        var (recent, semantic, episodic, fact) = cloned.GetAllocation();
        recent.Should().BeApproximately(0.45, 0.01);
        semantic.Should().BeApproximately(0.10, 0.01);
        episodic.Should().BeApproximately(0.35, 0.01);
        fact.Should().BeApproximately(0.10, 0.01);
    }

    [Fact]
    public void Strategy_Name_ShouldBeCorrect()
    {
        new BalancedStrategy().Name.Should().Be("Balanced");
        new RecentHeavyStrategy().Name.Should().Be("RecentHeavy");
        new SemanticHeavyStrategy().Name.Should().Be("SemanticHeavy");
    }
}
