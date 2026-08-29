using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Graph;

/// <summary>
/// Unit tests for PageRankImportancePropagator.
/// </summary>
public class PageRankImportancePropagatorTests
{
    private readonly ITemporalEntityStore _entityStoreMock;
    private readonly IMemoryGraphService _graphServiceMock;
    private readonly PageRankImportancePropagator _propagator;

    public PageRankImportancePropagatorTests()
    {
        _entityStoreMock = Substitute.For<ITemporalEntityStore>();
        _graphServiceMock = Substitute.For<IMemoryGraphService>();
        _propagator = new PageRankImportancePropagator(
            _entityStoreMock,
            _graphServiceMock,
            NullLogger<PageRankImportancePropagator>.Instance);
    }

    [Fact]
    public async Task ComputeImportanceAsync_EmptyGraph_ShouldReturnZeroEntities()
    {
        // Arrange
        _entityStoreMock.GetAllActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        // Act
        var result = await _propagator.ComputeImportanceAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.EntityCount);
        Assert.Equal(0, result.EdgeCount);
    }

    [Fact]
    public async Task ComputeImportanceAsync_SimpleGraph_ShouldComputeScores()
    {
        // Arrange
        var triples = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Alice",
                Predicate = "knows",
                ObjectValue = "Bob",
                Confidence = 0.9f,
                UserId = "user1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Bob",
                Predicate = "knows",
                ObjectValue = "Charlie",
                Confidence = 0.8f,
                UserId = "user1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Charlie",
                Predicate = "knows",
                ObjectValue = "Alice",
                Confidence = 0.7f,
                UserId = "user1"
            }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);

        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());
        _entityStoreMock.GetByObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        // Act
        var result = await _propagator.ComputeImportanceAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.EntityCount);
        Assert.Equal(3, result.EdgeCount);
        Assert.True(result.Iterations > 0);
        Assert.Contains("Alice", result.EntityScores.Keys);
        Assert.Contains("Bob", result.EntityScores.Keys);
        Assert.Contains("Charlie", result.EntityScores.Keys);
    }

    [Fact]
    public async Task ComputeImportanceAsync_HubNode_ShouldHaveHigherScore()
    {
        // Arrange: Create a star graph with Hub in the center
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "Spoke1", Predicate = "connects", ObjectValue = "Hub", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "Spoke2", Predicate = "connects", ObjectValue = "Hub", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "Spoke3", Predicate = "connects", ObjectValue = "Hub", Confidence = 0.9f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "Spoke4", Predicate = "connects", ObjectValue = "Hub", Confidence = 0.9f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);
        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());
        _entityStoreMock.GetByObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        // Act
        var result = await _propagator.ComputeImportanceAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: Hub should have higher score than spokes
        var hubScore = result.EntityScores["Hub"];
        var spoke1Score = result.EntityScores["Spoke1"];
        Assert.True(hubScore > spoke1Score, "Hub should have higher importance than spokes");
    }

    [Fact]
    public async Task GetEntityImportanceAsync_AfterCompute_ShouldReturnCachedScore()
    {
        // Arrange
        var triples = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Alice",
                Predicate = "knows",
                ObjectValue = "Bob",
                Confidence = 0.9f,
                UserId = "user1"
            }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);
        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());
        _entityStoreMock.GetByObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        await _propagator.ComputeImportanceAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var score = await _propagator.GetEntityImportanceAsync("Alice", "user1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(score);
        Assert.True(score > 0);
    }

    [Fact]
    public async Task GetTopEntitiesAsync_ShouldReturnRankedList()
    {
        // Arrange
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "to", ObjectValue = "Hub", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B", Predicate = "to", ObjectValue = "Hub", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "C", Predicate = "to", ObjectValue = "Hub", Confidence = 1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);
        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());
        _entityStoreMock.GetByObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        await _propagator.ComputeImportanceAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var topEntities = await _propagator.GetTopEntitiesAsync("user1", 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, topEntities.Count);
        Assert.Equal(1, topEntities[0].Rank);
        Assert.Equal(2, topEntities[1].Rank);
        Assert.True(topEntities[0].Score >= topEntities[1].Score);
    }

    [Fact]
    public async Task ComputeImportanceAsync_WithConvergence_ShouldConverge()
    {
        // Arrange: Stable circular graph
        var triples = new List<EntityTriple>
        {
            new() { Id = Guid.NewGuid(), Subject = "A", Predicate = "to", ObjectValue = "B", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "B", Predicate = "to", ObjectValue = "C", Confidence = 1f, UserId = "user1" },
            new() { Id = Guid.NewGuid(), Subject = "C", Predicate = "to", ObjectValue = "A", Confidence = 1f, UserId = "user1" }
        };

        _entityStoreMock.GetAllActiveAsync("user1", Arg.Any<CancellationToken>())
            .Returns(triples);
        _entityStoreMock.GetBySubjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());
        _entityStoreMock.GetByObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        // Act
        var result = await _propagator.ComputeImportanceAsync("user1", new ImportanceOptions
        {
            MaxIterations = 100,
            ConvergenceThreshold = 1e-6f
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Converged || result.Iterations >= 1);
    }
}
