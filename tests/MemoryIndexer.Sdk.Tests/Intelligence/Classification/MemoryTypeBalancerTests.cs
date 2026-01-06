using FluentAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Classification;

/// <summary>
/// Tests for MemoryTypeBalancer (Phase 23.1).
/// </summary>
public class MemoryTypeBalancerTests
{
    private readonly Mock<IMemoryStore> _mockStore;
    private readonly IMemoryTypeBalancer _balancer;
    private readonly TypeBalancerOptions _options;

    public MemoryTypeBalancerTests()
    {
        _mockStore = new Mock<IMemoryStore>();

        _options = new TypeBalancerOptions
        {
            Enabled = true,
            TargetDistribution = new Dictionary<MemoryType, float>
            {
                [MemoryType.Episodic] = 0.40f,
                [MemoryType.Semantic] = 0.30f,
                [MemoryType.Procedural] = 0.20f,
                [MemoryType.Fact] = 0.10f,
                [MemoryType.Reflection] = 0.0f
            },
            BoostSensitivity = 2.0f,
            MaxBoost = 0.5f,
            MinMemoriesForBalancing = 20
        };

        var indexerOptions = new MemoryIndexerOptions
        {
            TypeBalancing = _options
        };

        _balancer = new MemoryTypeBalancer(
            _mockStore.Object,
            Options.Create(indexerOptions),
            NullLogger<MemoryTypeBalancer>.Instance);
    }

    #region GetTypeBoostAsync Tests

    [Fact]
    public async Task GetTypeBoostAsync_UnderrepresentedType_ReturnsPositiveBoost()
    {
        // Arrange - Procedural is severely underrepresented (5% vs 20% target)
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 80,  // 80%
            [MemoryType.Semantic] = 10,  // 10%
            [MemoryType.Procedural] = 5, // 5%
            [MemoryType.Fact] = 5        // 5%
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await _balancer.GetTypeBoostAsync(MemoryType.Procedural, "user1");

        // Assert
        // Target=0.20, Current=0.05, Deviation=0.15
        // Boost = 0.15 * 2.0 = 0.30
        boost.Should().BeGreaterThan(0.2f);
        boost.Should().BeLessThanOrEqualTo(_options.MaxBoost);
    }

    [Fact]
    public async Task GetTypeBoostAsync_OverrepresentedType_ReturnsZeroBoost()
    {
        // Arrange - Episodic is overrepresented (80% vs 40% target)
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 80,
            [MemoryType.Semantic] = 10,
            [MemoryType.Procedural] = 5,
            [MemoryType.Fact] = 5
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await _balancer.GetTypeBoostAsync(MemoryType.Episodic, "user1");

        // Assert
        boost.Should().Be(0f, "overrepresented types should not get boost");
    }

    [Fact]
    public async Task GetTypeBoostAsync_AtTarget_ReturnsMinimalBoost()
    {
        // Arrange - Types exactly at target distribution
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 40,
            [MemoryType.Semantic] = 30,
            [MemoryType.Procedural] = 20,
            [MemoryType.Fact] = 10
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await _balancer.GetTypeBoostAsync(MemoryType.Procedural, "user1");

        // Assert
        boost.Should().Be(0f, "types at target should have zero boost");
    }

    [Fact]
    public async Task GetTypeBoostAsync_InsufficientMemories_ReturnsZero()
    {
        // Arrange - Only 10 memories (below MinMemoriesForBalancing=20)
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 8,
            [MemoryType.Semantic] = 1,
            [MemoryType.Procedural] = 1,
            [MemoryType.Fact] = 0
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await _balancer.GetTypeBoostAsync(MemoryType.Procedural, "user1");

        // Assert
        boost.Should().Be(0f, "should not apply balancing with insufficient data");
    }

    [Fact]
    public async Task GetTypeBoostAsync_Disabled_ReturnsZero()
    {
        // Arrange
        var disabledOptions = new MemoryIndexerOptions
        {
            TypeBalancing = new TypeBalancerOptions { Enabled = false }
        };

        var disabledBalancer = new MemoryTypeBalancer(
            _mockStore.Object,
            Options.Create(disabledOptions),
            NullLogger<MemoryTypeBalancer>.Instance);

        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 80,
            [MemoryType.Procedural] = 5
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await disabledBalancer.GetTypeBoostAsync(MemoryType.Procedural, "user1");

        // Assert
        boost.Should().Be(0f, "disabled balancer should return zero boost");
    }

    [Fact]
    public async Task GetTypeBoostAsync_MaxBoostClamping_RespectsLimit()
    {
        // Arrange - Extremely underrepresented (1% vs 40% target for Episodic)
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 1,   // 1%
            [MemoryType.Semantic] = 49,  // 49%
            [MemoryType.Procedural] = 40, // 40%
            [MemoryType.Fact] = 10       // 10%
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var boost = await _balancer.GetTypeBoostAsync(MemoryType.Episodic, "user1");

        // Assert
        // Target=0.40, Current=0.01, Deviation=0.39
        // Raw boost = 0.39 * 2.0 = 0.78, clamped to MaxBoost=0.5
        boost.Should().Be(_options.MaxBoost, "should clamp to MaxBoost");
    }

    #endregion

    #region GetTypeDistributionAsync Tests

    [Fact]
    public async Task GetTypeDistributionAsync_ReturnsNormalizedPercentages()
    {
        // Arrange
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 50,
            [MemoryType.Semantic] = 30,
            [MemoryType.Procedural] = 15,
            [MemoryType.Fact] = 5
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var distribution = await _balancer.GetTypeDistributionAsync("user1");

        // Assert
        distribution.Should().HaveCount(4);
        distribution[MemoryType.Episodic].Should().BeApproximately(0.50f, 0.01f);
        distribution[MemoryType.Semantic].Should().BeApproximately(0.30f, 0.01f);
        distribution[MemoryType.Procedural].Should().BeApproximately(0.15f, 0.01f);
        distribution[MemoryType.Fact].Should().BeApproximately(0.05f, 0.01f);

        // Sum should be ~1.0
        var sum = distribution.Values.Sum();
        sum.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public async Task GetTypeDistributionAsync_NoMemories_ReturnsEmptyDictionary()
    {
        // Arrange
        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(new Dictionary<MemoryType, int>());

        // Act
        var distribution = await _balancer.GetTypeDistributionAsync("user1");

        // Assert
        distribution.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTypeDistributionAsync_SingleType_Returns100Percent()
    {
        // Arrange
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 100
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var distribution = await _balancer.GetTypeDistributionAsync("user1");

        // Assert
        distribution.Should().HaveCount(1);
        distribution[MemoryType.Episodic].Should().Be(1.0f);
    }

    #endregion

    #region GetTypeCountsAsync Tests

    [Fact]
    public async Task GetTypeCountsAsync_ReturnsRawCounts()
    {
        // Arrange
        var expectedCounts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 73,
            [MemoryType.Semantic] = 15,
            [MemoryType.Procedural] = 10,
            [MemoryType.Fact] = 2
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(expectedCounts);

        // Act
        var counts = await _balancer.GetTypeCountsAsync("user1");

        // Assert
        counts.Should().BeEquivalentTo(expectedCounts);
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public async Task Scenario_ImbalancedDistribution_RecommendsCorrectionBoosts()
    {
        // Arrange - Realistic imbalanced scenario (Episodic-heavy like Phase 22 data)
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 73,  // 73%
            [MemoryType.Semantic] = 22,  // 22%
            [MemoryType.Procedural] = 3, // 3%
            [MemoryType.Fact] = 2        // 2%
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var episodicBoost = await _balancer.GetTypeBoostAsync(MemoryType.Episodic, "user1");
        var semanticBoost = await _balancer.GetTypeBoostAsync(MemoryType.Semantic, "user1");
        var proceduralBoost = await _balancer.GetTypeBoostAsync(MemoryType.Procedural, "user1");
        var factBoost = await _balancer.GetTypeBoostAsync(MemoryType.Fact, "user1");

        // Assert
        // Episodic: 73% vs 40% target → overrepresented → no boost
        episodicBoost.Should().Be(0f);

        // Semantic: 22% vs 30% target → slightly underrepresented → small boost
        semanticBoost.Should().BeGreaterThan(0f);
        semanticBoost.Should().BeLessThan(0.3f);

        // Procedural: 3% vs 20% target → severely underrepresented → large boost
        proceduralBoost.Should().BeGreaterThan(0.25f);

        // Fact: 2% vs 10% target → underrepresented → moderate boost
        factBoost.Should().BeGreaterThan(0.1f);

        // Procedural boost should be highest
        proceduralBoost.Should().BeGreaterThan(semanticBoost);
        proceduralBoost.Should().BeGreaterThan(factBoost);
    }

    [Fact]
    public async Task Scenario_BalancedDistribution_MinimalBoosts()
    {
        // Arrange - Well-balanced distribution close to targets
        var counts = new Dictionary<MemoryType, int>
        {
            [MemoryType.Episodic] = 38,  // 38% (target 40%)
            [MemoryType.Semantic] = 32,  // 32% (target 30%)
            [MemoryType.Procedural] = 21, // 21% (target 20%)
            [MemoryType.Fact] = 9        // 9% (target 10%)
        };

        _mockStore.Setup(s => s.GetTypeCountsAsync("user1", default))
            .ReturnsAsync(counts);

        // Act
        var allBoosts = new Dictionary<MemoryType, float>();
        foreach (var type in Enum.GetValues<MemoryType>())
        {
            allBoosts[type] = await _balancer.GetTypeBoostAsync(type, "user1");
        }

        // Assert - All boosts should be minimal (< 0.1) since distribution is balanced
        allBoosts.Values.Should().AllSatisfy(boost => boost.Should().BeLessThan(0.1f));
    }

    #endregion
}
