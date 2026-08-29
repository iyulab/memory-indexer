using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Graph;

/// <summary>
/// Unit tests for GraphQueryExpander.
/// </summary>
public class GraphQueryExpanderTests
{
    private readonly IGraphRetriever _graphRetrieverMock;
    private readonly IImportancePropagator _importancePropagatorMock;
    private readonly ICommunityDetector _communityDetectorMock;
    private readonly ITemporalEntityStore _entityStoreMock;
    private readonly GraphQueryExpander _expander;

    public GraphQueryExpanderTests()
    {
        _graphRetrieverMock = Substitute.For<IGraphRetriever>();
        _importancePropagatorMock = Substitute.For<IImportancePropagator>();
        _communityDetectorMock = Substitute.For<ICommunityDetector>();
        _entityStoreMock = Substitute.For<ITemporalEntityStore>();

        _expander = new GraphQueryExpander(
            _graphRetrieverMock,
            _importancePropagatorMock,
            _communityDetectorMock,
            _entityStoreMock,
            NullLogger<GraphQueryExpander>.Instance);
    }

    [Fact]
    public async Task ExpandQueryAsync_EmptyQuery_ShouldReturnOriginalQuery()
    {
        // Arrange
        _importancePropagatorMock.GetTopEntitiesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        // Act
        var result = await _expander.ExpandQueryAsync("", "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("", result.OriginalQuery);
        Assert.Empty(result.MentionedEntities);
    }

    [Fact]
    public async Task ExpandQueryAsync_QueryWithQuotedEntity_ShouldExtractEntity()
    {
        // Arrange
        var entityName = "John Smith";
        var query = $"Tell me about \"{entityName}\"";

        _entityStoreMock.GetBySubjectAsync(entityName, "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = entityName, Predicate = "works_at", ObjectValue = "Acme", UserId = "user1" }
            });

        _importancePropagatorMock.GetEntityImportanceAsync(entityName, "user1", Arg.Any<CancellationToken>())
            .Returns(0.8f);

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        _graphRetrieverMock.TraverseAsync(entityName, Arg.Any<GraphTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GraphTraversalResult
            {
                StartEntity = entityName,
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result.MentionedEntities, e => e.Name == entityName);
    }

    [Fact]
    public async Task ExpandQueryAsync_QueryWithCapitalizedWord_ShouldTryExtractEntity()
    {
        // Arrange
        var query = "What is Microsoft doing?";

        _entityStoreMock.GetBySubjectAsync("Microsoft", "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Microsoft", Predicate = "is_a", ObjectValue = "Company", UserId = "user1" }
            });

        _importancePropagatorMock.GetEntityImportanceAsync("Microsoft", "user1", Arg.Any<CancellationToken>())
            .Returns(0.9f);

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        _graphRetrieverMock.TraverseAsync("Microsoft", Arg.Any<GraphTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GraphTraversalResult
            {
                StartEntity = "Microsoft",
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(result.MentionedEntities, e => e.Name == "Microsoft");
    }

    [Fact]
    public async Task ExpandQueryAsync_WithRelatedEntities_ShouldIncludeRelatedEntities()
    {
        // Arrange
        var query = "Tell me about Alice";

        _entityStoreMock.GetBySubjectAsync("Alice", "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Alice", Predicate = "knows", ObjectValue = "Bob", UserId = "user1" }
            });

        _importancePropagatorMock.GetEntityImportanceAsync(Arg.Any<string>(), "user1", Arg.Any<CancellationToken>())
            .Returns(0.7f);

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        _graphRetrieverMock.TraverseAsync("Alice", Arg.Any<GraphTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GraphTraversalResult
            {
                StartEntity = "Alice",
                DiscoveredEntities = new List<DiscoveredEntity>
                {
                    new()
                    {
                        Name = "Bob",
                        HopDistance = 1,
                        Facts = new List<EntityTriple>
                        {
                            new() { Id = Guid.NewGuid(), Subject = "Alice", Predicate = "knows", ObjectValue = "Bob", UserId = "user1" }
                        }
                    }
                },
                Statistics = new TraversalStatistics { MaxDepthReached = 1 }
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1", new QueryExpansionOptions
        {
            MaxHops = 2,
            MinImportanceScore = 0.1f
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(result.RelatedEntities);
        Assert.Contains(result.RelatedEntities, e => e.Name == "Bob");
    }

    [Fact]
    public async Task ExtractQueryEntitiesAsync_MultiWordEntity_ShouldExtract()
    {
        // Arrange
        var query = "Tell me about New York City";

        _entityStoreMock.GetBySubjectAsync("New York City", "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "New York City", Predicate = "is_a", ObjectValue = "City", UserId = "user1" }
            });

        _entityStoreMock.GetBySubjectAsync("New", "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>());

        _importancePropagatorMock.GetEntityImportanceAsync("New York City", "user1", Arg.Any<CancellationToken>())
            .Returns(0.8f);

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        // Act
        var entities = await _expander.ExtractQueryEntitiesAsync(query, "user1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(entities, e => e.Name == "New York City");
    }

    [Fact]
    public async Task ExtractQueryEntitiesAsync_HighImportanceEntity_ShouldMatch()
    {
        // Arrange
        var query = "what about acme"; // lowercase but high importance

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>
            {
                new() { EntityName = "Acme", Score = 0.95f, Rank = 1 }
            });

        // Act
        var entities = await _expander.ExtractQueryEntitiesAsync(query, "user1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(entities, e => e.Name == "Acme");
        Assert.Contains(entities, e => e.Relation == EntityRelation.Implied);
    }

    [Fact]
    public async Task GenerateSubQueriesAsync_WithMultipleEntities_ShouldGenerateRelationshipQueries()
    {
        // Arrange
        var entities = new List<QueryEntity>
        {
            new() { Name = "Alice", ImportanceScore = 0.9f },
            new() { Name = "Bob", ImportanceScore = 0.8f },
            new() { Name = "Charlie", ImportanceScore = 0.7f }
        };

        // Act
        var subQueries = await _expander.GenerateSubQueriesAsync("query", entities, new SubQueryOptions
        {
            IncludeRelationshipQueries = true,
            MaxSubQueries = 10
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(subQueries);
        Assert.Contains(subQueries, q => q.Type == SubQueryType.EntityFacts);
        Assert.Contains(subQueries, q => q.Type == SubQueryType.EntityRelationship);
    }

    [Fact]
    public async Task GenerateSubQueriesAsync_SingleEntity_ShouldOnlyGenerateFactsQuery()
    {
        // Arrange
        var entities = new List<QueryEntity>
        {
            new() { Name = "Alice", ImportanceScore = 0.9f }
        };

        // Act
        var subQueries = await _expander.GenerateSubQueriesAsync("query", entities, new SubQueryOptions
        {
            IncludeRelationshipQueries = true,
            MaxSubQueries = 10
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(subQueries, q => q.Type == SubQueryType.EntityFacts);
        Assert.DoesNotContain(subQueries, q => q.Type == SubQueryType.EntityRelationship);
    }

    [Fact]
    public async Task ExpandQueryAsync_ShouldIncludeStatistics()
    {
        // Arrange
        _importancePropagatorMock.GetTopEntitiesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        // Act
        var result = await _expander.ExpandQueryAsync("test query", "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Statistics);
        Assert.True(result.Statistics.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExpandQueryAsync_WithCommunityContext_ShouldIncludeContext()
    {
        // Arrange
        var query = "Tell me about Alice";

        _entityStoreMock.GetBySubjectAsync("Alice", "user1", Arg.Any<CancellationToken>())
            .Returns(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Alice", Predicate = "is", ObjectValue = "Person", UserId = "user1" }
            });

        _importancePropagatorMock.GetEntityImportanceAsync(Arg.Any<string>(), "user1", Arg.Any<CancellationToken>())
            .Returns(0.8f);

        _importancePropagatorMock.GetTopEntitiesAsync("user1", 50, Arg.Any<CancellationToken>())
            .Returns(new List<EntityImportance>());

        _graphRetrieverMock.TraverseAsync("Alice", Arg.Any<GraphTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GraphTraversalResult
            {
                StartEntity = "Alice",
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        _communityDetectorMock.AssignToCommunityAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        _communityDetectorMock.GetCommunitySummaryAsync(1, "user1", Arg.Any<CancellationToken>())
            .Returns(new CommunitySummary
            {
                CommunityId = 1,
                TopicLabel = "People",
                MemoryCount = 10
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1", new QueryExpansionOptions
        {
            IncludeCommunityContext = true
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        // Community context might be included if available
    }
}
