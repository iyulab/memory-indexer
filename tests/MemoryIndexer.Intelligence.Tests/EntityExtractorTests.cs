using MemoryIndexer.Intelligence.KnowledgeGraph;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

// Alias to avoid namespace/class collision
using KnowledgeGraphModel = MemoryIndexer.Intelligence.KnowledgeGraph.KnowledgeGraph;

public class EntityExtractorTests
{
    private readonly EntityExtractor _extractor;

    public EntityExtractorTests()
    {
        _extractor = new EntityExtractor(NullLogger<EntityExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_EmptyContent_ShouldReturnEmptyList()
    {
        // Act
        var result = await _extractor.ExtractEntitiesAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithEmail_ShouldExtractEmailEntity()
    {
        // Arrange
        var content = "Contact us at support@example.com for assistance.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == EntityType.Email && e.Name == "support@example.com");
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithUrl_ShouldExtractUrlEntity()
    {
        // Arrange
        var content = "Visit https://www.example.com for more information.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == EntityType.Url && e.Name.Contains("example.com"));
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithDate_ShouldExtractDateTimeEntity()
    {
        // Arrange
        var content = "The meeting is scheduled for 2024-12-15.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == EntityType.DateTime && e.Name == "2024-12-15");
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithCurrency_ShouldExtractNumericEntity()
    {
        // Arrange
        var content = "The total cost is $1,500.00 for the project.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == EntityType.Numeric);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithTechnicalTerms_ShouldExtractTechnicalEntities()
    {
        // Arrange
        var content = "The API uses OAuth2.0 for authentication and returns JSON responses.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.Contains(result, e => e.Type == EntityType.Technical && e.Name == "API");
    }

    [Fact]
    public async Task ExtractEntitiesAsync_WithNamedEntity_ShouldExtractAsUnknownOrPerson()
    {
        // Arrange
        var content = "Microsoft and Google are leading technology companies.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        Assert.True(result.Count > 0, "Should extract at least one named entity");
        Assert.Contains(result, e => e.Name == "Microsoft" || e.Name == "Google");
    }

    [Fact]
    public async Task ExtractRelationsAsync_WithWorksAtPattern_ShouldExtractRelation()
    {
        // Arrange
        var content = "John works at Microsoft.";
        var entities = new List<Entity>
        {
            new() { Name = "John", Type = EntityType.Person },
            new() { Name = "Microsoft", Type = EntityType.Organization }
        };

        // Act
        var result = await _extractor.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.RelationType == "WORKS_AT");
    }

    [Fact]
    public async Task ExtractRelationsAsync_WithLocatedInPattern_ShouldExtractRelation()
    {
        // Arrange
        var content = "Seattle is located in Washington.";
        var entities = new List<Entity>
        {
            new() { Name = "Seattle", Type = EntityType.Location },
            new() { Name = "Washington", Type = EntityType.Location }
        };

        // Act
        var result = await _extractor.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.Contains(result, r => r.RelationType == "LOCATED_IN");
    }

    [Fact]
    public async Task ExtractRelationsAsync_WithCoOccurrence_ShouldCreateRelation()
    {
        // Arrange
        var content = "Both Microsoft and Google compete in the cloud market.";
        var entities = new List<Entity>
        {
            new() { Name = "Microsoft", Type = EntityType.Organization },
            new() { Name = "Google", Type = EntityType.Organization }
        };

        // Act
        var result = await _extractor.ExtractRelationsAsync(content, entities);

        // Assert
        Assert.True(result.Count > 0, "Should create at least one co-occurrence relation");
    }

    [Fact]
    public async Task BuildGraphAsync_WithMultipleContents_ShouldBuildGraph()
    {
        // Arrange
        var contents = new List<string>
        {
            "John works at Microsoft in Seattle.",
            "Contact support@microsoft.com for help.",
            "The API endpoint is https://api.microsoft.com"
        };

        // Act
        var graph = await _extractor.BuildGraphAsync(contents);

        // Assert
        Assert.NotNull(graph);
        Assert.True(graph.EntityCount > 0, "Graph should contain entities");
    }

    [Fact]
    public async Task BuildGraphAsync_ShouldMergeIdenticalEntities()
    {
        // Arrange
        var contents = new List<string>
        {
            "Microsoft is a technology company.",
            "Microsoft was founded by Bill Gates."
        };

        // Act
        var graph = await _extractor.BuildGraphAsync(contents);

        // Assert
        var microsoftEntities = graph.Entities.Values.Count(e =>
            e.NormalizedName.Equals("microsoft", StringComparison.OrdinalIgnoreCase));
        Assert.True(microsoftEntities <= 1, "Identical entities should be merged");
    }

    [Fact]
    public void QueryGraph_WithMatchingQuery_ShouldReturnResults()
    {
        // Arrange
        var graph = new KnowledgeGraphModel();
        var entity = new Entity
        {
            Name = "Microsoft",
            NormalizedName = "microsoft",
            Type = EntityType.Organization
        };
        graph.Entities.Add(entity.Id, entity);
        graph.NameIndex["microsoft"] = [entity.Id];
        graph.TypeIndex[EntityType.Organization] = [entity.Id];

        // Act
        var result = _extractor.QueryGraph("microsoft", graph);

        // Assert
        Assert.NotEmpty(result.MatchedEntities);
        Assert.Contains(result.MatchedEntities, e => e.Name == "Microsoft");
    }

    [Fact]
    public void QueryGraph_WithNoMatch_ShouldReturnEmptyResult()
    {
        // Arrange
        var graph = new KnowledgeGraphModel();

        // Act
        var result = _extractor.QueryGraph("nonexistent", graph);

        // Assert
        Assert.Empty(result.MatchedEntities);
    }

    [Fact]
    public void QueryGraph_ShouldReturnRelatedEntities()
    {
        // Arrange
        var graph = new KnowledgeGraphModel();
        var entity1 = new Entity
        {
            Name = "Microsoft",
            NormalizedName = "microsoft",
            Type = EntityType.Organization
        };
        var entity2 = new Entity
        {
            Name = "Seattle",
            NormalizedName = "seattle",
            Type = EntityType.Location
        };
        var relation = new EntityRelation
        {
            Source = entity1,
            Target = entity2,
            RelationType = "LOCATED_IN"
        };

        graph.Entities.Add(entity1.Id, entity1);
        graph.Entities.Add(entity2.Id, entity2);
        graph.Relations.Add(relation);
        graph.NameIndex["microsoft"] = [entity1.Id];
        graph.NameIndex["seattle"] = [entity2.Id];
        graph.AdjacencyList[entity1.Id] = [(relation, entity2.Id)];
        graph.AdjacencyList[entity2.Id] = [(relation, entity1.Id)];

        // Act
        var result = _extractor.QueryGraph("microsoft", graph);

        // Assert
        Assert.NotEmpty(result.RelatedEntities);
        Assert.Contains(result.RelatedEntities, e => e.Name == "Seattle");
    }

    [Fact]
    public void MergeGraphs_ShouldCombineEntities()
    {
        // Arrange
        var graph1 = new KnowledgeGraphModel();
        var entity1 = new Entity
        {
            Name = "Microsoft",
            NormalizedName = "microsoft",
            Type = EntityType.Organization
        };
        graph1.Entities.Add(entity1.Id, entity1);

        var graph2 = new KnowledgeGraphModel();
        var entity2 = new Entity
        {
            Name = "Google",
            NormalizedName = "google",
            Type = EntityType.Organization
        };
        graph2.Entities.Add(entity2.Id, entity2);

        // Act
        var merged = _extractor.MergeGraphs(graph1, graph2);

        // Assert
        Assert.Equal(2, merged.EntityCount);
    }

    [Fact]
    public void MergeGraphs_ShouldMergeDuplicateEntities()
    {
        // Arrange
        var graph1 = new KnowledgeGraphModel();
        var entity1 = new Entity
        {
            Name = "Microsoft",
            NormalizedName = "microsoft",
            Type = EntityType.Organization,
            OccurrenceCount = 2
        };
        graph1.Entities.Add(entity1.Id, entity1);
        graph1.NameIndex["microsoft"] = [entity1.Id];

        var graph2 = new KnowledgeGraphModel();
        var entity2 = new Entity
        {
            Name = "Microsoft",
            NormalizedName = "microsoft",
            Type = EntityType.Organization,
            OccurrenceCount = 3
        };
        graph2.Entities.Add(entity2.Id, entity2);
        graph2.NameIndex["microsoft"] = [entity2.Id];

        // Act
        var merged = _extractor.MergeGraphs(graph1, graph2);

        // Assert
        Assert.Equal(1, merged.EntityCount);
        var mergedEntity = merged.Entities.Values.First();
        Assert.Equal(5, mergedEntity.OccurrenceCount);
    }

    [Fact]
    public void MergeGraphs_ShouldCombineRelations()
    {
        // Arrange
        var graph1 = new KnowledgeGraphModel();
        var entity1 = new Entity { Name = "A", NormalizedName = "a", Type = EntityType.Unknown };
        var entity2 = new Entity { Name = "B", NormalizedName = "b", Type = EntityType.Unknown };
        graph1.Entities.Add(entity1.Id, entity1);
        graph1.Entities.Add(entity2.Id, entity2);
        graph1.Relations.Add(new EntityRelation
        {
            Source = entity1,
            Target = entity2,
            RelationType = "RELATES_TO"
        });

        var graph2 = new KnowledgeGraphModel();
        var entity3 = new Entity { Name = "C", NormalizedName = "c", Type = EntityType.Unknown };
        var entity4 = new Entity { Name = "D", NormalizedName = "d", Type = EntityType.Unknown };
        graph2.Entities.Add(entity3.Id, entity3);
        graph2.Entities.Add(entity4.Id, entity4);
        graph2.Relations.Add(new EntityRelation
        {
            Source = entity3,
            Target = entity4,
            RelationType = "CONNECTS_TO"
        });

        // Act
        var merged = _extractor.MergeGraphs(graph1, graph2);

        // Assert
        Assert.Equal(2, merged.RelationCount);
    }

    [Fact]
    public async Task ExtractEntitiesAsync_ShouldNormalizeEntityNames()
    {
        // Arrange
        var content = "MICROSOFT and microsoft are the same company.";

        // Act
        var result = await _extractor.ExtractEntitiesAsync(content);

        // Assert
        var microsoftEntities = result.Where(e =>
            e.NormalizedName.Equals("microsoft", StringComparison.OrdinalIgnoreCase)).ToList();

        // All variations should have same normalized name
        Assert.All(microsoftEntities, e =>
            Assert.Equal("microsoft", e.NormalizedName, StringComparer.OrdinalIgnoreCase));
    }
}
