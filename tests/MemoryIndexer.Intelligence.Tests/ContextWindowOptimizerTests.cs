using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Tests;
using MemoryIndexer.Intelligence.ContextOptimization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class ContextWindowOptimizerTests
{
    private readonly ContextWindowOptimizer _optimizer;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IMemoryStore> _memoryStoreMock;

    public ContextWindowOptimizerTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => GenerateMockEmbedding(text));

        _memoryStoreMock = new Mock<IMemoryStore>();
        _memoryStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _optimizer = new ContextWindowOptimizer(
            _embeddingServiceMock.Object,
            _memoryStoreMock.Object,
            NullLogger<ContextWindowOptimizer>.Instance);
    }

    private static ReadOnlyMemory<float> GenerateMockEmbedding(string text)
        => TestHelpers.GenerateMockEmbedding(text, 1024);

    private static MemoryUnit CreateMemoryWithImportance(float importance, string content = "Test content")
        => TestHelpers.CreateTestMemoryWithImportance(importance, content);

    [Fact]
    public void LongContextReorder_EmptyList_ShouldReturnEmpty()
    {
        // Act
        var result = _optimizer.LongContextReorder([]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void LongContextReorder_SingleItem_ShouldReturnSameItem()
    {
        // Arrange
        var memory = CreateMemoryWithImportance(0.8f);

        // Act
        var result = _optimizer.LongContextReorder([memory]);

        // Assert
        Assert.Single(result);
        Assert.Equal(memory, result[0]);
    }

    [Fact]
    public void LongContextReorder_MultipleItems_ShouldPlaceImportantAtEnds()
    {
        // Arrange
        var memories = new[]
        {
            CreateMemoryWithImportance(0.5f, "Medium importance"),
            CreateMemoryWithImportance(0.9f, "High importance"),
            CreateMemoryWithImportance(0.3f, "Low importance"),
            CreateMemoryWithImportance(0.7f, "Medium-high importance")
        };

        // Act
        var result = _optimizer.LongContextReorder(memories);

        // Assert
        Assert.Equal(4, result.Count);
        // Most important should be at beginning or end
        var firstImportance = result[0].ImportanceScore;
        var lastImportance = result[^1].ImportanceScore;
        Assert.True(firstImportance >= 0.7f || lastImportance >= 0.7f,
            "Important items should be at beginning or end");
    }

    [Fact]
    public async Task ApplyMMRAsync_EmptyList_ShouldReturnEmpty()
    {
        // Arrange
        var queryEmbedding = GenerateMockEmbedding("query");

        // Act
        var result = await _optimizer.ApplyMMRAsync([], queryEmbedding);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ApplyMMRAsync_ShouldReturnKItems()
    {
        // Arrange
        var memories = Enumerable.Range(0, 20)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var queryEmbedding = GenerateMockEmbedding("query");

        // Act
        var result = await _optimizer.ApplyMMRAsync(memories, queryEmbedding, k: 5);

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task ApplyMMRAsync_FewerThanK_ShouldReturnAll()
    {
        // Arrange
        var memories = Enumerable.Range(0, 3)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var queryEmbedding = GenerateMockEmbedding("query");

        // Act
        var result = await _optimizer.ApplyMMRAsync(memories, queryEmbedding, k: 10);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ApplyMMRAsync_ShouldPromoteDiversity()
    {
        // Arrange
        // Create some similar memories and some diverse ones
        var memories = new List<MemoryUnit>
        {
            CreateMemoryWithImportance(0.8f, "Machine learning algorithms"),
            CreateMemoryWithImportance(0.7f, "Machine learning models"),
            CreateMemoryWithImportance(0.6f, "Machine learning training"),
            CreateMemoryWithImportance(0.5f, "Database optimization"),
            CreateMemoryWithImportance(0.4f, "Network security")
        };
        var queryEmbedding = GenerateMockEmbedding("machine learning");

        // Act
        var result = await _optimizer.ApplyMMRAsync(memories, queryEmbedding, k: 3, lambda: 0.5f);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GenerateHyDEAsync_ShouldReturnEnhancedEmbedding()
    {
        // Arrange
        var query = "How do I implement authentication?";

        // Act
        var result = await _optimizer.GenerateHyDEAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.NotEmpty(result.HypotheticalDocument);
        Assert.True(result.EnhancedEmbedding.Length > 0);
    }

    [Fact]
    public async Task GenerateHyDEAsync_DifferentQueryTypes_ShouldProduceRelevantHypothetical()
    {
        // Test different query types
        var queries = new[]
        {
            "How do I deploy the application?",
            "What is machine learning?",
            "Why does the system fail?",
            "When should I use caching?"
        };

        foreach (var query in queries)
        {
            // Act
            var result = await _optimizer.GenerateHyDEAsync(query);

            // Assert
            Assert.NotEmpty(result.HypotheticalDocument);
            Assert.Contains(query.Split()[0], result.HypotheticalDocument, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExpandChunkContextAsync_NoSiblings_ShouldReturnOriginalOnly()
    {
        // Arrange
        var memory = CreateMemoryWithImportance(0.8f, "Original chunk content");

        // Act
        var result = await _optimizer.ExpandChunkContextAsync(memory);

        // Assert
        Assert.Equal(memory, result.OriginalChunk);
        Assert.Empty(result.PrecedingChunks);
        Assert.Empty(result.FollowingChunks);
    }

    [Fact]
    public async Task OptimizeContextAsync_EmptyMemories_ShouldReturnEmptyResult()
    {
        // Arrange
        var options = new ContextOptimizationOptions();

        // Act
        var result = await _optimizer.OptimizeContextAsync([], options);

        // Assert
        Assert.Empty(result.OptimizedMemories);
        Assert.True(result.TargetAchieved);
    }

    [Fact]
    public async Task OptimizeContextAsync_WithReordering_ShouldApplyLongContextReorder()
    {
        // Arrange
        var memories = Enumerable.Range(0, 5)
            .Select(i => CreateMemoryWithImportance(0.1f * (i + 1), $"Memory {i}"))
            .ToList();
        var options = new ContextOptimizationOptions
        {
            EnableReordering = true,
            EnableMMR = false
        };

        // Act
        var result = await _optimizer.OptimizeContextAsync(memories, options);

        // Assert
        Assert.Contains("LongContextReorder", result.OptimizationsApplied);
    }

    [Fact]
    public async Task OptimizeContextAsync_WithMMR_ShouldApplyDiversity()
    {
        // Arrange
        var memories = Enumerable.Range(0, 10)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var queryEmbedding = GenerateMockEmbedding("query");
        var options = new ContextOptimizationOptions
        {
            EnableMMR = true,
            EnableReordering = false,
            QueryEmbedding = queryEmbedding
        };

        // Act
        var result = await _optimizer.OptimizeContextAsync(memories, options);

        // Assert
        Assert.Contains(result.OptimizationsApplied, o => o.Contains("MMR"));
    }

    [Fact]
    public async Task OptimizeContextAsync_OverTokenBudget_ShouldTrim()
    {
        // Arrange
        var memories = Enumerable.Range(0, 100)
            .Select(i => new MemoryUnit
            {
                Content = string.Join(" ", Enumerable.Repeat("word", 100)), // ~130 tokens each
                ImportanceScore = 0.1f * (i % 10),
                Embedding = GenerateMockEmbedding($"content{i}")
            })
            .ToList();
        var options = new ContextOptimizationOptions
        {
            EnableMMR = false,
            EnableReordering = false,
            TargetTokens = 1000
        };

        // Act
        var result = await _optimizer.OptimizeContextAsync(memories, options);

        // Assert
        Assert.True(result.MemoriesRemoved > 0);
        Assert.True(result.FinalTokenCount <= options.TargetTokens);
        Assert.Contains("TokenBudgetTrimming", result.OptimizationsApplied);
    }

    [Fact]
    public async Task OptimizeContextAsync_WithHyDE_ShouldGenerateEnhancedEmbedding()
    {
        // Arrange
        var memories = Enumerable.Range(0, 5)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory {i}"))
            .ToList();
        var options = new ContextOptimizationOptions
        {
            EnableHyDE = true,
            EnableMMR = false,
            EnableReordering = false,
            Query = "How to implement feature X?"
        };

        // Act
        var result = await _optimizer.OptimizeContextAsync(memories, options);

        // Assert
        Assert.Contains("HyDE", result.OptimizationsApplied);
    }

    [Fact]
    public async Task OptimizeContextAsync_CombinedOptimizations_ShouldApplyAll()
    {
        // Arrange
        var memories = Enumerable.Range(0, 10)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var options = new ContextOptimizationOptions
        {
            EnableHyDE = true,
            EnableMMR = true,
            EnableReordering = true,
            Query = "Test query"
        };

        // Act
        var result = await _optimizer.OptimizeContextAsync(memories, options);

        // Assert
        Assert.True(result.OptimizationsApplied.Count >= 2);
    }

    [Fact]
    public void LongContextReorder_ShouldPreserveAllItems()
    {
        // Arrange
        var memories = Enumerable.Range(0, 10)
            .Select(i => CreateMemoryWithImportance(i * 0.1f, $"Memory {i}"))
            .ToList();

        // Act
        var result = _optimizer.LongContextReorder(memories);

        // Assert
        Assert.Equal(memories.Count, result.Count);
        foreach (var memory in memories)
        {
            Assert.Contains(memory, result);
        }
    }

    [Fact]
    public async Task ApplyMMRAsync_HighLambda_ShouldFavorRelevance()
    {
        // Arrange
        var memories = Enumerable.Range(0, 10)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var queryEmbedding = GenerateMockEmbedding("Memory content 0");

        // Act with high lambda (favor relevance)
        var result = await _optimizer.ApplyMMRAsync(memories, queryEmbedding, k: 5, lambda: 0.95f);

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task ApplyMMRAsync_LowLambda_ShouldFavorDiversity()
    {
        // Arrange
        var memories = Enumerable.Range(0, 10)
            .Select(i => CreateMemoryWithImportance(0.5f, $"Memory content {i}"))
            .ToList();
        var queryEmbedding = GenerateMockEmbedding("Memory content 0");

        // Act with low lambda (favor diversity)
        var result = await _optimizer.ApplyMMRAsync(memories, queryEmbedding, k: 5, lambda: 0.2f);

        // Assert
        Assert.Equal(5, result.Count);
    }
}
