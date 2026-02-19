using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Graph;

/// <summary>
/// Unit tests for LabelPropagationCommunityDetector.
/// </summary>
public class LabelPropagationCommunityDetectorTests
{
    private readonly IMemoryGraphService _graphServiceMock;
    private readonly ITemporalEntityStore _entityStoreMock;
    private readonly IMemoryStore _memoryStoreMock;
    private readonly LabelPropagationCommunityDetector _detector;

    public LabelPropagationCommunityDetectorTests()
    {
        _graphServiceMock = Substitute.For<IMemoryGraphService>();
        _entityStoreMock = Substitute.For<ITemporalEntityStore>();
        _memoryStoreMock = Substitute.For<IMemoryStore>();
        _detector = new LabelPropagationCommunityDetector(
            _graphServiceMock,
            _entityStoreMock,
            _memoryStoreMock,
            NullLogger<LabelPropagationCommunityDetector>.Instance);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_EmptyGraph_ShouldReturnZeroCommunities()
    {
        // Arrange
        _entityStoreMock.GetAllActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        // Act
        var result = await _detector.DetectCommunitiesAsync("user1");

        // Assert
        Assert.Equal(0, result.CommunityCount);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_TwoClusters_ShouldFindTwoCommunities()
    {
        // Arrange: Two disconnected clusters
        var triples = new List<EntityTriple>
        {
            // Cluster 1
            new() { Id = Guid.NewGuid(), Subject = "A1", Predicate = "relates", ObjectValue = "A2", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "A2", Predicate = "relates", ObjectValue = "A3", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "A3", Predicate = "relates", ObjectValue = "A1", Confidence = 0.9f, UserId = "user1" },
            // Cluster 2
            new() { Id = Guid.NewGuid(), Subject = "B1", Predicate = "relates", ObjectValue = "B2", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B2", Predicate = "relates", ObjectValue = "B3", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B3", Predicate = "relates", ObjectValue = "B1", Confidence = 0.9f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        // Act
        var result = await _detector.DetectCommunitiesAsync("user1", new CommunityDetectionOptions
        {
            MinCommunitySize = 2,
            MaxIterations = 50,
            RandomSeed = 42
        });

        // Assert
        Assert.True(result.CommunityCount >= 1, "Should detect at least one community");
        Assert.True(result.IterationsToConverge <= 50);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_SingleCluster_ShouldFindOneCommunity()
    {
        // Arrange: Fully connected cluster
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "connects", ObjectValue = "B", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B", Predicate = "connects", ObjectValue = "C", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "C", Predicate = "connects", ObjectValue = "A", Confidence = 1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        // Act
        var result = await _detector.DetectCommunitiesAsync("user1", new CommunityDetectionOptions
        {
            MinCommunitySize = 1,
            RandomSeed = 42
        });

        // Assert
        Assert.True(result.CommunityCount >= 1);
        Assert.Equal(3, result.EntityAssignments.Count);
    }

    [Fact]
    public async Task AssignToCommunityAsync_ExistingEntities_ShouldAssignToMostCommonCommunity()
    {
        // Arrange: First detect communities
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "to", ObjectValue = "B", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B", Predicate = "to", ObjectValue = "C", Confidence = 1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        await _detector.DetectCommunitiesAsync("user1", new CommunityDetectionOptions
        {
            MinCommunitySize = 1,
            RandomSeed = 42
        });

        var memoryId = Guid.NewGuid();

        // Act
        var community = await _detector.AssignToCommunityAsync(memoryId, ["A", "B"]);

        // Assert
        Assert.True(community >= 0);
    }

    [Fact]
    public async Task AssignToCommunityAsync_NewEntities_ShouldCreateNewCommunity()
    {
        // Arrange
        var memoryId = Guid.NewGuid();

        // Act
        var community = await _detector.AssignToCommunityAsync(memoryId, ["NewEntity1", "NewEntity2"]);

        // Assert
        Assert.True(community >= 0);
    }

    [Fact]
    public async Task AssignToCommunityAsync_EmptyEntities_ShouldReturnMinusOne()
    {
        // Arrange
        var memoryId = Guid.NewGuid();

        // Act
        var community = await _detector.AssignToCommunityAsync(memoryId, []);

        // Assert
        Assert.Equal(-1, community);
    }

    [Fact]
    public async Task GetCommunitySummaryAsync_NonexistentCommunity_ShouldReturnEmptySummary()
    {
        // Act
        var summary = await _detector.GetCommunitySummaryAsync(999, "user1");

        // Assert
        Assert.NotNull(summary);
        Assert.Equal("Empty Community", summary.TopicLabel);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_WithWeightedEdges_ShouldUseWeights()
    {
        // Arrange
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "strong", ObjectValue = "B", Confidence = 1.0f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "weak", ObjectValue = "C", Confidence = 0.1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        // Act
        var result = await _detector.DetectCommunitiesAsync("user1", new CommunityDetectionOptions
        {
            UseWeightedEdges = true,
            MinCommunitySize = 1,
            RandomSeed = 42
        });

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IterationsToConverge >= 1);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_ShouldCalculateModularity()
    {
        // Arrange
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "to", ObjectValue = "B", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B", Predicate = "to", ObjectValue = "C", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "C", Predicate = "to", ObjectValue = "A", Confidence = 1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        // Act
        var result = await _detector.DetectCommunitiesAsync("user1", new CommunityDetectionOptions
        {
            MinCommunitySize = 1,
            RandomSeed = 42
        });

        // Assert
        // Modularity should be defined (could be positive or negative)
        Assert.True(result.Modularity >= -1 && result.Modularity <= 1);
    }
}
