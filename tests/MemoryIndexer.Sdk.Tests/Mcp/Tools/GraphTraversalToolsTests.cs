using FluentAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Unit tests for GraphTraversalTools MCP endpoints.
/// </summary>
public class GraphTraversalToolsTests
{
    private readonly IMemoryGraphService _mockGraphService;
    private readonly IImportancePropagator _mockImportancePropagator;
    private readonly ICommunityDetector _mockCommunityDetector;
    private readonly GraphTraversalTools _tools;

    private const string DefaultUserId = "default";

    public GraphTraversalToolsTests()
    {
        _mockGraphService = Substitute.For<IMemoryGraphService>();
        _mockImportancePropagator = Substitute.For<IImportancePropagator>();
        _mockCommunityDetector = Substitute.For<ICommunityDetector>();

        _tools = new GraphTraversalTools(
            _mockGraphService,
            _mockImportancePropagator,
            _mockCommunityDetector,
            Options.Create(new MemoryIndexerOptions()));
    }

    #region DetectCommunities Tests

    [Fact]
    public async Task DetectCommunities_ReturnsClusterInfo()
    {
        // Arrange
        var detectionResult = new CommunityDetectionResult
        {
            CommunityCount = 3,
            MemoryAssignments = new Dictionary<Guid, int>
            {
                [Guid.NewGuid()] = 0,
                [Guid.NewGuid()] = 1,
                [Guid.NewGuid()] = 2
            },
            CommunitySizes = new Dictionary<int, int>
            {
                [0] = 5,
                [1] = 3,
                [2] = 2
            },
            Modularity = 0.65f,
            IterationsToConverge = 15
        };

        _mockCommunityDetector.DetectCommunitiesAsync(
            DefaultUserId,
            Arg.Any<CommunityDetectionOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _tools.DetectCommunities();

        // Assert
        result.Success.Should().BeTrue();
        result.CommunityCount.Should().Be(3);
        result.TotalMemories.Should().Be(3);
        result.ConvergedIterations.Should().Be(15);
        result.Modularity.Should().Be(0.65f);
        result.Communities.Should().HaveCount(3);
    }

    [Fact]
    public async Task DetectCommunities_WithCustomParameters_PassesOptions()
    {
        // Arrange
        var detectionResult = new CommunityDetectionResult
        {
            CommunityCount = 2,
            MemoryAssignments = new Dictionary<Guid, int>(),
            CommunitySizes = new Dictionary<int, int>(),
            IterationsToConverge = 10
        };

        _mockCommunityDetector.DetectCommunitiesAsync(
            DefaultUserId,
            Arg.Is<CommunityDetectionOptions>(o =>
                o.MaxIterations == 50 && o.MinCommunitySize == 5),
            Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _tools.DetectCommunities(maxIterations: 50, minCommunitySize: 5);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region GetCommunityMemories Tests

    [Fact]
    public async Task GetCommunityMemories_ReturnsMemoriesInCommunity()
    {
        // Arrange
        var communityId = 1;
        var memories = new List<MemoryUnit>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Memory 1 in community",
                Type = MemoryType.Fact,
                Tier = Tier.Long,
                ImportanceScore = 0.8f
            },
            new()
            {
                Id = Guid.NewGuid(),
                Content = "Memory 2 in community",
                Type = MemoryType.Episodic,
                Tier = Tier.Archive,
                ImportanceScore = 0.9f
            }
        };

        _mockCommunityDetector.GetCommunityMemoriesAsync(
            communityId,
            DefaultUserId,
            Arg.Any<CancellationToken>())
            .Returns(memories);

        // Act
        var result = await _tools.GetCommunityMemories(communityId);

        // Assert
        result.Success.Should().BeTrue();
        result.CommunityId.Should().Be(communityId);
        result.Count.Should().Be(2);
        result.Memories.Should().HaveCount(2);
        result.Memories![0].Content.Should().Be("Memory 1 in community");
    }

    #endregion

    #region GetCommunitySummary Tests

    [Fact]
    public async Task GetCommunitySummary_ReturnsSummaryInfo()
    {
        // Arrange
        var communityId = 1;
        var summary = new CommunitySummary
        {
            CommunityId = communityId,
            TopicLabel = "Technology and Programming",
            KeyEntities = ["Python", "Machine Learning", "AI"],
            MemoryCount = 15,
            EntityCount = 10,
            CommonPredicates = ["uses", "implements", "builds"]
        };

        _mockCommunityDetector.GetCommunitySummaryAsync(
            communityId,
            DefaultUserId,
            Arg.Any<CancellationToken>())
            .Returns(summary);

        // Act
        var result = await _tools.GetCommunitySummary(communityId);

        // Assert
        result.Success.Should().BeTrue();
        result.CommunityId.Should().Be(communityId);
        result.TopicLabel.Should().Be("Technology and Programming");
        result.KeyEntities.Should().Contain("Python");
        result.MemoryCount.Should().Be(15);
        result.EntityCount.Should().Be(10);
    }

    #endregion

    #region ComputeImportance Tests

    [Fact]
    public async Task ComputeImportance_ReturnsEntityScores()
    {
        // Arrange
        var importanceResult = new ImportanceResult
        {
            EntityScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["Python"] = 0.95f,
                ["AI"] = 0.85f,
                ["Machine Learning"] = 0.80f
            },
            EntityCount = 3,
            EdgeCount = 5,
            Iterations = 25,
            FinalDifference = 0.0001f,
            Converged = true
        };

        _mockImportancePropagator.ComputeImportanceAsync(
            DefaultUserId,
            Arg.Any<ImportanceOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(importanceResult);

        // Act
        var result = await _tools.ComputeImportance();

        // Assert
        result.Success.Should().BeTrue();
        result.EntityCount.Should().Be(3);
        result.IterationsUsed.Should().Be(25);
        result.Converged.Should().BeTrue();
        result.TopEntities.Should().HaveCount(3);
        result.TopEntities![0].EntityName.Should().Be("Python");
        result.TopEntities[0].Score.Should().Be(0.95f);
    }

    [Fact]
    public async Task ComputeImportance_WithCustomParameters_PassesOptions()
    {
        // Arrange
        var importanceResult = new ImportanceResult
        {
            EntityScores = new Dictionary<string, float>(),
            Converged = true
        };

        _mockImportancePropagator.ComputeImportanceAsync(
            DefaultUserId,
            Arg.Is<ImportanceOptions>(o =>
                o.DampingFactor == 0.9f &&
                o.MaxIterations == 100 &&
                o.ConvergenceThreshold == 0.00001f),
            Arg.Any<CancellationToken>())
            .Returns(importanceResult);

        // Act
        var result = await _tools.ComputeImportance(
            dampingFactor: 0.9f,
            maxIterations: 100,
            tolerance: 0.00001f);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region GetEntityImportance Tests

    [Fact]
    public async Task GetEntityImportance_WhenEntityExists_ReturnsScore()
    {
        // Arrange
        var entityName = "Python";
        _mockImportancePropagator.GetEntityImportanceAsync(
            entityName,
            DefaultUserId,
            Arg.Any<CancellationToken>())
            .Returns(0.95f);

        // Act
        var result = await _tools.GetEntityImportance(entityName);

        // Assert
        result.Success.Should().BeTrue();
        result.EntityName.Should().Be(entityName);
        result.Score.Should().Be(0.95f);
        result.Found.Should().BeTrue();
    }

    [Fact]
    public async Task GetEntityImportance_WhenEntityNotFound_ReturnsNotFound()
    {
        // Arrange
        var entityName = "NonExistentEntity";
        _mockImportancePropagator.GetEntityImportanceAsync(
            entityName,
            DefaultUserId,
            Arg.Any<CancellationToken>())
            .Returns((float?)null);

        // Act
        var result = await _tools.GetEntityImportance(entityName);

        // Assert
        result.Success.Should().BeTrue();
        result.Found.Should().BeFalse();
        result.Score.Should().BeNull();
    }

    #endregion

    #region GetTopEntities Tests

    [Fact]
    public async Task GetTopEntities_ReturnsRankedList()
    {
        // Arrange
        var entities = new List<EntityImportance>
        {
            new() { EntityName = "Python", Score = 0.95f, Rank = 1, MemoryConnectionCount = 10 },
            new() { EntityName = "AI", Score = 0.85f, Rank = 2, MemoryConnectionCount = 8 },
            new() { EntityName = "Machine Learning", Score = 0.80f, Rank = 3, MemoryConnectionCount = 6 }
        };

        _mockImportancePropagator.GetTopEntitiesAsync(
            DefaultUserId,
            10,
            Arg.Any<CancellationToken>())
            .Returns(entities);

        // Act
        var result = await _tools.GetTopEntities(topK: 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Count.Should().Be(3);
        result.Entities.Should().HaveCount(3);
        result.Entities![0].EntityName.Should().Be("Python");
        result.Entities[0].Rank.Should().Be(1);
    }

    #endregion

    #region FindRelatedMemories Tests

    [Fact]
    public async Task FindRelatedMemories_WithValidId_ReturnsRelatedMemories()
    {
        // Arrange
        var sourceMemoryId = Guid.NewGuid();
        var relatedMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Related memory content"
        };

        var relatedMemories = new List<RelatedMemory>
        {
            new()
            {
                Memory = relatedMemory,
                GraphDistance = 1,
                SharedEntities = ["Python", "Programming"],
                RelationshipStrength = 0.8f,
                ConnectionPath = ["Python", "Programming"]
            }
        };

        _mockGraphService.FindRelatedMemoriesAsync(
            sourceMemoryId,
            2,
            10,
            Arg.Any<CancellationToken>())
            .Returns(relatedMemories);

        // Act
        var result = await _tools.FindRelatedMemories(sourceMemoryId.ToString());

        // Assert
        result.Success.Should().BeTrue();
        result.SourceMemoryId.Should().Be(sourceMemoryId.ToString());
        result.Count.Should().Be(1);
        result.RelatedMemories.Should().HaveCount(1);
        result.RelatedMemories![0].GraphDistance.Should().Be(1);
        result.RelatedMemories[0].SharedEntities.Should().Contain("Python");
    }

    [Fact]
    public async Task FindRelatedMemories_WithInvalidId_ReturnsError()
    {
        // Act
        var result = await _tools.FindRelatedMemories("not-a-valid-guid");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid memory ID");
    }

    #endregion

    #region ExtractSubgraph Tests

    [Fact]
    public async Task ExtractSubgraph_WithValidIds_ReturnsSubgraph()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var subgraph = new MemorySubgraph
        {
            CenterMemoryIds = [memoryId],
            MemoryNodes = [
                new MemoryGraphNode
                {
                    MemoryId = memoryId,
                    UserId = DefaultUserId,
                    ConnectedEntities = ["Python", "AI"]
                }
            ],
            Entities = ["Python", "AI", "Machine Learning"],
            Triples = [],
            MemoryEntityEdges = [],
            FormattedContext = "Python is related to AI and Machine Learning",
            Statistics = new SubgraphStatistics
            {
                MemoryCount = 1,
                EntityCount = 3,
                TripleCount = 2,
                EdgeCount = 4,
                MaxDepthReached = 2
            }
        };

        _mockGraphService.ExtractSubgraphAsync(
            Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<SubgraphOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(subgraph);

        // Act
        var result = await _tools.ExtractSubgraph(memoryId.ToString());

        // Assert
        result.Success.Should().BeTrue();
        result.CenterMemoryIds.Should().Contain(memoryId.ToString());
        result.MemoryNodeCount.Should().Be(1);
        result.EntityCount.Should().Be(3);
        result.FormattedContext.Should().Contain("Python");
        result.Statistics!.MemoryCount.Should().Be(1);
        result.Statistics.MaxDepthReached.Should().Be(2);
    }

    [Fact]
    public async Task ExtractSubgraph_WithNoValidIds_ReturnsError()
    {
        // Act
        var result = await _tools.ExtractSubgraph("invalid,also-invalid");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No valid memory IDs");
    }

    [Fact]
    public async Task ExtractSubgraph_WithMultipleIds_ParsesCorrectly()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var subgraph = new MemorySubgraph
        {
            CenterMemoryIds = [id1, id2],
            MemoryNodes = [],
            Entities = [],
            Triples = [],
            MemoryEntityEdges = [],
            Statistics = new SubgraphStatistics()
        };

        _mockGraphService.ExtractSubgraphAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
            Arg.Any<SubgraphOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(subgraph);

        // Act
        var result = await _tools.ExtractSubgraph($"{id1}, {id2}");

        // Assert
        result.Success.Should().BeTrue();
        result.CenterMemoryIds.Should().HaveCount(2);
    }

    #endregion
}
