using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Graph;

/// <summary>
/// Unit tests for MemoryGraphService.
/// </summary>
public class MemoryGraphServiceTests
{
    private readonly Mock<ITemporalEntityStore> _entityStoreMock;
    private readonly Mock<IMemoryStore> _memoryStoreMock;
    private readonly MemoryGraphService _service;

    public MemoryGraphServiceTests()
    {
        _entityStoreMock = new Mock<ITemporalEntityStore>();
        _memoryStoreMock = new Mock<IMemoryStore>();
        _service = new MemoryGraphService(
            _entityStoreMock.Object,
            _memoryStoreMock.Object,
            NullLogger<MemoryGraphService>.Instance);
    }

    [Fact]
    public async Task LinkMemoryToGraphAsync_ShouldCreateNodeWithEntities()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "user1",
            Content = "Test memory",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f })
        };

        var entities = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Alice",
                Predicate = "works_at",
                ObjectValue = "Acme Corp",
                Confidence = 0.9f,
                SourceMemoryId = memory.Id,
                UserId = "user1"
            }
        };

        // Act
        var result = await _service.LinkMemoryToGraphAsync(memory, entities);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(memory.Id, result.MemoryId);
        Assert.Contains("Alice", result.ConnectedEntities);
        Assert.Contains("Acme Corp", result.ConnectedEntities);
    }

    [Fact]
    public async Task LinkMemoryToGraphAsync_EmptyEntities_ShouldReturnNodeWithEmptyConnections()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "user1",
            Content = "Test memory without entities",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f })
        };

        // Act
        var result = await _service.LinkMemoryToGraphAsync(memory, []);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.ConnectedEntities);
    }

    [Fact]
    public async Task FindRelatedMemoriesAsync_ShouldReturnRelatedMemoriesWithinHops()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var relatedMemoryId = Guid.NewGuid();
        var userId = "user1";

        var memory = new MemoryUnit
        {
            Id = memoryId,
            UserId = userId,
            Content = "Main memory",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f })
        };

        var relatedMemory = new MemoryUnit
        {
            Id = relatedMemoryId,
            UserId = userId,
            Content = "Related memory",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.3f, 0.4f })
        };

        var entities = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "SharedEntity",
                Predicate = "relates_to",
                ObjectValue = "Topic",
                SourceMemoryId = memoryId,
                UserId = userId,
                Confidence = 0.9f
            }
        };

        var relatedEntities = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "SharedEntity",
                Predicate = "discussed_in",
                ObjectValue = "Discussion",
                SourceMemoryId = relatedMemoryId,
                UserId = userId,
                Confidence = 0.8f
            }
        };

        // Link both memories
        await _service.LinkMemoryToGraphAsync(memory, entities);
        await _service.LinkMemoryToGraphAsync(relatedMemory, relatedEntities);

        _memoryStoreMock.Setup(x => x.GetByIdAsync(relatedMemoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(relatedMemory);

        // Act
        var result = await _service.FindRelatedMemoriesAsync(memoryId);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.Memory.Id == relatedMemoryId);
    }

    [Fact]
    public async Task ExtractSubgraphAsync_ShouldBuildSubgraphFromMemories()
    {
        // Arrange
        var memoryId1 = Guid.NewGuid();
        var memoryId2 = Guid.NewGuid();
        var userId = "user1";

        var memory1 = new MemoryUnit
        {
            Id = memoryId1,
            UserId = userId,
            Content = "Memory 1",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f })
        };

        var memory2 = new MemoryUnit
        {
            Id = memoryId2,
            UserId = userId,
            Content = "Memory 2",
            Embedding = new ReadOnlyMemory<float>(new float[] { 0.3f, 0.4f })
        };

        var entities1 = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Entity1",
                Predicate = "connects",
                ObjectValue = "Entity2",
                SourceMemoryId = memoryId1,
                UserId = userId
            }
        };

        var entities2 = new List<EntityTriple>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Subject = "Entity2",
                Predicate = "relates",
                ObjectValue = "Entity3",
                SourceMemoryId = memoryId2,
                UserId = userId
            }
        };

        await _service.LinkMemoryToGraphAsync(memory1, entities1);
        await _service.LinkMemoryToGraphAsync(memory2, entities2);

        // Act
        var result = await _service.ExtractSubgraphAsync([memoryId1, memoryId2]);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.MemoryNodes.Count);
        Assert.Contains("Entity1", result.Entities);
        Assert.Contains("Entity2", result.Entities);
        Assert.Contains("Entity3", result.Entities);
    }
}
