using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Intelligence.Chunking;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public sealed class ParentChildChunkManagerTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly ParentChildChunkManager _manager;

    public ParentChildChunkManagerTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();

        // Setup mock embedding service to return deterministic embeddings
        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => CreateMockEmbedding(text));

        _mockEmbeddingService
            .Setup(x => x.GenerateBatchEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                texts.Select(CreateMockEmbedding).ToList());

        _manager = new ParentChildChunkManager(
            _mockEmbeddingService.Object,
            NullLogger<ParentChildChunkManager>.Instance);
    }

    private static ReadOnlyMemory<float> CreateMockEmbedding(string text)
    {
        // Create deterministic embedding based on text hash
        var hash = text.GetHashCode();
        var embedding = new float[384];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = ((hash + i) % 100) / 100f;
        }
        // Normalize
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= norm;
            }
        }
        return new ReadOnlyMemory<float>(embedding);
    }

    [Fact]
    public async Task CreateHierarchyAsync_WithShortContent_CreatesSingleParent()
    {
        // Arrange
        var shortContent = "Short content that is less than MinContentLength.";

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(shortContent);

        // Assert
        Assert.Single(hierarchy.ParentChunks);
        Assert.Empty(hierarchy.ChildChunks);
        Assert.Equal(shortContent, hierarchy.ParentChunks[0].Content);
        Assert.Equal(ChunkType.Parent, hierarchy.ParentChunks[0].ChunkType);
    }

    [Fact]
    public async Task CreateHierarchyAsync_WithLongContent_CreatesParentAndChildChunks()
    {
        // Arrange
        var longContent = new string('A', 2000) + " " + new string('B', 2000);

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(longContent);

        // Assert
        Assert.NotEmpty(hierarchy.ChildChunks);
        Assert.NotEmpty(hierarchy.ParentChunks);
        Assert.True(hierarchy.ChildChunks.Count > hierarchy.ParentChunks.Count);
    }

    [Fact]
    public async Task CreateHierarchyAsync_SetsCorrectChunkTypes()
    {
        // Arrange
        var content = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"Word{i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        Assert.All(hierarchy.ParentChunks, c => Assert.Equal(ChunkType.Parent, c.ChunkType));
        Assert.All(hierarchy.ChildChunks, c => Assert.Equal(ChunkType.Child, c.ChunkType));
    }

    [Fact]
    public async Task CreateHierarchyAsync_LinksChildrenToParents()
    {
        // Arrange
        var content = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"Word{i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        foreach (var child in hierarchy.ChildChunks)
        {
            Assert.NotNull(child.ParentId);
            Assert.True(hierarchy.ChunkMap.ContainsKey(child.ParentId.Value));
        }

        foreach (var parent in hierarchy.ParentChunks)
        {
            Assert.NotEmpty(parent.ChildIds);
            Assert.All(parent.ChildIds, childId => Assert.True(hierarchy.ChunkMap.ContainsKey(childId)));
        }
    }

    [Fact]
    public async Task CreateHierarchyAsync_GeneratesEmbeddingsForAllChunks()
    {
        // Arrange
        var content = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"Word{i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        Assert.All(hierarchy.ChunkMap.Values, c => Assert.NotNull(c.Embedding));
    }

    [Fact]
    public async Task CreateHierarchyAsync_PreservesOriginalContent()
    {
        // Arrange
        var content = "This is some test content that should be preserved.";

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        Assert.Equal(content, hierarchy.OriginalContent);
    }

    [Fact]
    public async Task CreateHierarchyAsync_SetsSourceId()
    {
        // Arrange
        var content = "Some content for testing source ID.";
        var sourceId = "doc-123";

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content, sourceId);

        // Assert
        Assert.Equal(sourceId, hierarchy.SourceId);
        Assert.All(hierarchy.ChunkMap.Values, c => Assert.Equal(sourceId, c.SourceId));
    }

    [Fact]
    public async Task CreateHierarchyAsync_ThrowsOnEmptyContent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _manager.CreateHierarchyAsync(""));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _manager.CreateHierarchyAsync("   "));
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsOrderedByScore()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence number {i} with some content"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence number 50", hierarchy, topK: 5);

        // Assert
        Assert.NotEmpty(results);
        var scores = results.Select(r => r.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public async Task SearchAsync_WithReturnParentsTrue_ReturnsParentContext()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence number {i} with detailed content"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence number 50", hierarchy, topK: 5, returnParents: true);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results.Where(r => r.MatchType == ChunkMatchType.ChildToParent),
            r => Assert.NotNull(r.ParentChunk));
    }

    [Fact]
    public async Task SearchAsync_WithReturnParentsFalse_ReturnsChildOnly()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence number {i} with detailed content"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence number 50", hierarchy, topK: 5, returnParents: false);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.Null(r.ParentChunk);
            Assert.Equal(ChunkMatchType.ChildOnly, r.MatchType);
        });
    }

    [Fact]
    public async Task SearchAsync_WithNoChildren_SearchesParentsDirectly()
    {
        // Arrange - short content creates no children
        var shortContent = "Short content only.";
        var hierarchy = await _manager.CreateHierarchyAsync(shortContent);

        // Verify no children
        Assert.Empty(hierarchy.ChildChunks);

        // Act
        var results = await _manager.SearchAsync("Short content", hierarchy, topK: 5);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(ChunkMatchType.ParentDirect, r.MatchType));
    }

    [Fact]
    public async Task SearchAsync_LimitsResultsToTopK()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 200).Select(i => $"Sentence number {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence", hierarchy, topK: 3);

        // Assert
        Assert.True(results.Count <= 3);
    }

    [Fact]
    public async Task GetParent_ReturnsCorrectParent()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        var child = hierarchy.ChildChunks.First();

        // Act
        var parent = _manager.GetParent(child, hierarchy);

        // Assert
        Assert.NotNull(parent);
        Assert.Equal(child.ParentId, parent.Id);
        Assert.Contains(child.Id, parent.ChildIds);
    }

    [Fact]
    public async Task GetParent_ReturnsNullForParentChunk()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        var parent = hierarchy.ParentChunks.First();

        // Act
        var result = _manager.GetParent(parent, hierarchy);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetChildren_ReturnsAllChildren()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        var parent = hierarchy.ParentChunks.First();

        // Act
        var children = _manager.GetChildren(parent, hierarchy);

        // Assert
        Assert.Equal(parent.ChildIds.Count, children.Count);
        Assert.All(children, c => Assert.Equal(parent.Id, c.ParentId));
    }

    [Fact]
    public async Task GetSiblings_ReturnsOtherChildrenOfSameParent()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        var parent = hierarchy.ParentChunks.First();
        var child = hierarchy.ChildChunks.First(c => c.ParentId == parent.Id);

        // Act
        var siblings = _manager.GetSiblings(child, hierarchy);

        // Assert
        Assert.DoesNotContain(child.Id, siblings.Select(s => s.Id));
        Assert.All(siblings, s => Assert.Equal(parent.Id, s.ParentId));
    }

    [Fact]
    public async Task GetSiblings_ReturnsEmptyForChunkWithNoParent()
    {
        // Arrange - short content creates single parent with no children
        var shortContent = "Short content only.";
        var hierarchy = await _manager.CreateHierarchyAsync(shortContent);

        var parent = hierarchy.ParentChunks.First();

        // Act
        var siblings = _manager.GetSiblings(parent, hierarchy);

        // Assert
        Assert.Empty(siblings);
    }

    [Fact]
    public async Task ChunkHierarchy_TotalChunksIsCorrect()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence {i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        Assert.Equal(hierarchy.ParentChunks.Count + hierarchy.ChildChunks.Count, hierarchy.TotalChunks);
    }

    [Fact]
    public async Task ParentChildSearchResult_RetrievedContentReturnsParentWhenAvailable()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence number {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence", hierarchy, topK: 3, returnParents: true);

        // Assert
        var resultWithParent = results.FirstOrDefault(r => r.ParentChunk != null);
        if (resultWithParent != null)
        {
            Assert.Equal(resultWithParent.ParentChunk!.Content, resultWithParent.RetrievedContent);
        }
    }

    [Fact]
    public async Task ParentChildSearchResult_RetrievedContentReturnsMatchedWhenNoParent()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 100).Select(i => $"Sentence number {i}"));
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Act
        var results = await _manager.SearchAsync("Sentence", hierarchy, topK: 3, returnParents: false);

        // Assert
        Assert.All(results, r => Assert.Equal(r.MatchedChunk.Content, r.RetrievedContent));
    }

    [Fact]
    public async Task CreateHierarchyAsync_ChunksHaveCorrectPositions()
    {
        // Arrange
        var content = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"Word{i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        foreach (var chunk in hierarchy.ChunkMap.Values)
        {
            Assert.True(chunk.StartPosition >= 0);
            Assert.True(chunk.EndPosition <= content.Length);
            Assert.True(chunk.StartPosition < chunk.EndPosition);

            // Verify content is contained within position bounds (trim differences allowed)
            var positionSlice = content[chunk.StartPosition..chunk.EndPosition];
            Assert.Contains(chunk.Content.Trim(), positionSlice);
        }
    }

    [Fact]
    public async Task CreateHierarchyAsync_ParentsSpanTheirChildren()
    {
        // Arrange
        var content = string.Join(". ", Enumerable.Range(0, 200).Select(i => $"Sentence {i}"));

        // Act
        var hierarchy = await _manager.CreateHierarchyAsync(content);

        // Assert
        foreach (var parent in hierarchy.ParentChunks)
        {
            var children = _manager.GetChildren(parent, hierarchy);
            if (children.Count > 0)
            {
                var minChildStart = children.Min(c => c.StartPosition);
                var maxChildEnd = children.Max(c => c.EndPosition);

                Assert.True(parent.StartPosition <= minChildStart,
                    $"Parent start {parent.StartPosition} should be <= min child start {minChildStart}");
                Assert.True(parent.EndPosition >= maxChildEnd,
                    $"Parent end {parent.EndPosition} should be >= max child end {maxChildEnd}");
            }
        }
    }
}
