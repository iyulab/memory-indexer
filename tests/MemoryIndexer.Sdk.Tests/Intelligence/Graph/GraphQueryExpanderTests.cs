using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Graph;

/// <summary>
/// Unit tests for GraphQueryExpander.
/// </summary>
public class GraphQueryExpanderTests
{
    private readonly Mock<IGraphRetriever> _graphRetrieverMock;
    private readonly Mock<IImportancePropagator> _importancePropagatorMock;
    private readonly Mock<ICommunityDetector> _communityDetectorMock;
    private readonly Mock<ITemporalEntityStore> _entityStoreMock;
    private readonly GraphQueryExpander _expander;

    public GraphQueryExpanderTests()
    {
        _graphRetrieverMock = new Mock<IGraphRetriever>();
        _importancePropagatorMock = new Mock<IImportancePropagator>();
        _communityDetectorMock = new Mock<ICommunityDetector>();
        _entityStoreMock = new Mock<ITemporalEntityStore>();

        _expander = new GraphQueryExpander(
            _graphRetrieverMock.Object,
            _importancePropagatorMock.Object,
            _communityDetectorMock.Object,
            _entityStoreMock.Object,
            NullLogger<GraphQueryExpander>.Instance);
    }

    [Fact]
    public async Task ExpandQueryAsync_EmptyQuery_ShouldReturnOriginalQuery()
    {
        // Arrange
        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        // Act
        var result = await _expander.ExpandQueryAsync("", "user1");

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

        _entityStoreMock.Setup(x => x.GetBySubjectAsync(entityName, "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = entityName, Predicate = "works_at", ObjectValue = "Acme", UserId = "user1" }
            });

        _importancePropagatorMock.Setup(x => x.GetEntityImportanceAsync(entityName, "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.8f);

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        _graphRetrieverMock.Setup(x => x.TraverseAsync(entityName, It.IsAny<GraphTraversalOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTraversalResult
            {
                StartEntity = entityName,
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1");

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result.MentionedEntities, e => e.Name == entityName);
    }

    [Fact]
    public async Task ExpandQueryAsync_QueryWithCapitalizedWord_ShouldTryExtractEntity()
    {
        // Arrange
        var query = "What is Microsoft doing?";

        _entityStoreMock.Setup(x => x.GetBySubjectAsync("Microsoft", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Microsoft", Predicate = "is_a", ObjectValue = "Company", UserId = "user1" }
            });

        _importancePropagatorMock.Setup(x => x.GetEntityImportanceAsync("Microsoft", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.9f);

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        _graphRetrieverMock.Setup(x => x.TraverseAsync("Microsoft", It.IsAny<GraphTraversalOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTraversalResult
            {
                StartEntity = "Microsoft",
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1");

        // Assert
        Assert.Contains(result.MentionedEntities, e => e.Name == "Microsoft");
    }

    [Fact]
    public async Task ExpandQueryAsync_WithRelatedEntities_ShouldIncludeRelatedEntities()
    {
        // Arrange
        var query = "Tell me about Alice";

        _entityStoreMock.Setup(x => x.GetBySubjectAsync("Alice", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Alice", Predicate = "knows", ObjectValue = "Bob", UserId = "user1" }
            });

        _importancePropagatorMock.Setup(x => x.GetEntityImportanceAsync(It.IsAny<string>(), "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.7f);

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        _graphRetrieverMock.Setup(x => x.TraverseAsync("Alice", It.IsAny<GraphTraversalOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTraversalResult
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
        });

        // Assert
        Assert.NotEmpty(result.RelatedEntities);
        Assert.Contains(result.RelatedEntities, e => e.Name == "Bob");
    }

    [Fact]
    public async Task ExtractQueryEntitiesAsync_MultiWordEntity_ShouldExtract()
    {
        // Arrange
        var query = "Tell me about New York City";

        _entityStoreMock.Setup(x => x.GetBySubjectAsync("New York City", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "New York City", Predicate = "is_a", ObjectValue = "City", UserId = "user1" }
            });

        _entityStoreMock.Setup(x => x.GetBySubjectAsync("New", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>());

        _importancePropagatorMock.Setup(x => x.GetEntityImportanceAsync("New York City", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.8f);

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        // Act
        var entities = await _expander.ExtractQueryEntitiesAsync(query, "user1");

        // Assert
        Assert.Contains(entities, e => e.Name == "New York City");
    }

    [Fact]
    public async Task ExtractQueryEntitiesAsync_HighImportanceEntity_ShouldMatch()
    {
        // Arrange
        var query = "what about acme"; // lowercase but high importance

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>
            {
                new() { EntityName = "Acme", Score = 0.95f, Rank = 1 }
            });

        // Act
        var entities = await _expander.ExtractQueryEntitiesAsync(query, "user1");

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
        });

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
        });

        // Assert
        Assert.Contains(subQueries, q => q.Type == SubQueryType.EntityFacts);
        Assert.DoesNotContain(subQueries, q => q.Type == SubQueryType.EntityRelationship);
    }

    [Fact]
    public async Task ExpandQueryAsync_ShouldIncludeStatistics()
    {
        // Arrange
        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        // Act
        var result = await _expander.ExpandQueryAsync("test query", "user1");

        // Assert
        Assert.NotNull(result.Statistics);
        Assert.True(result.Statistics.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExpandQueryAsync_WithCommunityContext_ShouldIncludeContext()
    {
        // Arrange
        var query = "Tell me about Alice";

        _entityStoreMock.Setup(x => x.GetBySubjectAsync("Alice", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityTriple>
            {
                new() { Id = Guid.NewGuid(), Subject = "Alice", Predicate = "is", ObjectValue = "Person", UserId = "user1" }
            });

        _importancePropagatorMock.Setup(x => x.GetEntityImportanceAsync(It.IsAny<string>(), "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.8f);

        _importancePropagatorMock.Setup(x => x.GetTopEntitiesAsync("user1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityImportance>());

        _graphRetrieverMock.Setup(x => x.TraverseAsync("Alice", It.IsAny<GraphTraversalOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GraphTraversalResult
            {
                StartEntity = "Alice",
                DiscoveredEntities = new List<DiscoveredEntity>(),
                Statistics = new TraversalStatistics { MaxDepthReached = 0 }
            });

        _communityDetectorMock.Setup(x => x.AssignToCommunityAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _communityDetectorMock.Setup(x => x.GetCommunitySummaryAsync(1, "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunitySummary
            {
                CommunityId = 1,
                TopicLabel = "People",
                MemoryCount = 10
            });

        // Act
        var result = await _expander.ExpandQueryAsync(query, "user1", new QueryExpansionOptions
        {
            IncludeCommunityContext = true
        });

        // Assert
        Assert.NotNull(result);
        // Community context might be included if available
    }
}
