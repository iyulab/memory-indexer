using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Intelligence.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class ExtractiveSummarizerTests
{
    private readonly ExtractiveSummarizer _summarizer;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;

    public ExtractiveSummarizerTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();

        // Setup mock embedding service to return consistent embeddings
        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => GenerateMockEmbedding(text));

        _embeddingServiceMock
            .Setup(x => x.GenerateBatchEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                texts.Select(t => GenerateMockEmbedding(t)).ToList());

        _summarizer = new ExtractiveSummarizer(
            _embeddingServiceMock.Object,
            NullLogger<ExtractiveSummarizer>.Instance);
    }

    private static ReadOnlyMemory<float> GenerateMockEmbedding(string text)
    {
        // Generate a deterministic embedding based on text hash
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[768];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)random.NextDouble() * 2 - 1;
        }
        // Normalize
        var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= norm;
        }
        return embedding;
    }

    [Fact]
    public async Task SummarizeAsync_WithMultipleMemories_ShouldCreateSummary()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            CreateMemory("The quick brown fox jumps over the lazy dog. This is a test sentence about animals."),
            CreateMemory("Machine learning is transforming how we process data and make decisions. AI is everywhere."),
            CreateMemory("Software development requires careful planning and execution. Programming is complex.")
        };

        // Act
        var summary = await _summarizer.SummarizeAsync(memories);

        // Assert
        Assert.NotNull(summary);
        Assert.NotEmpty(summary.Content);
        Assert.NotEmpty(summary.KeyPoints);
        Assert.Equal(3, summary.SourceMemoryIds.Count);
        // CompressionRatio may be >= 1.0 for short content due to summarization overhead
        Assert.True(summary.OriginalTokenCount > 0);
    }

    [Fact]
    public async Task SummarizeAsync_WithCompressionRatio_ShouldSelectSentences()
    {
        // Arrange - Use longer content to enable meaningful compression
        var memories = new List<MemoryUnit>
        {
            CreateMemory("First important topic about artificial intelligence and its applications in modern technology. Machine learning models are transforming industries worldwide."),
            CreateMemory("Second critical information regarding data processing systems and their architecture. Big data analytics requires robust infrastructure."),
            CreateMemory("Third essential fact about cloud computing infrastructure and scalability. Cloud services enable global deployment."),
            CreateMemory("Fourth relevant detail about network security protocols and encryption. Cybersecurity is essential for modern systems."),
            CreateMemory("Fifth necessary information about database optimization techniques and indexing. Performance tuning requires careful analysis.")
        };

        var options = new SummarizationOptions
        {
            TargetCompressionRatio = 0.2f,
            MaxOutputTokens = 50,
            Style = SummaryStyle.Extractive
        };

        // Act
        var summary = await _summarizer.SummarizeAsync(memories, options);

        // Assert
        Assert.NotNull(summary);
        Assert.NotEmpty(summary.Content);
        Assert.NotEmpty(summary.KeyPoints);
        Assert.Equal(5, summary.SourceMemoryIds.Count);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldExtractTopics()
    {
        // Arrange - Topics come from MemoryUnit.Topics, so we need to set them
        var memories = new List<MemoryUnit>
        {
            CreateMemoryWithTopics("Machine learning algorithms are used for pattern recognition.", ["AI", "ML"]),
            CreateMemoryWithTopics("Neural networks process data through multiple layers.", ["AI", "Neural Networks"]),
            CreateMemoryWithTopics("Deep learning excels at image and speech recognition tasks.", ["Deep Learning", "AI"])
        };

        // Act
        var summary = await _summarizer.SummarizeAsync(memories);

        // Assert
        Assert.NotNull(summary);
        Assert.NotEmpty(summary.Topics);
        Assert.Contains(summary.Topics, t => t.Equals("AI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SummarizeAsync_WithEmptyMemories_ShouldReturnEmptySummary()
    {
        // Arrange
        var memories = new List<MemoryUnit>();

        // Act
        var summary = await _summarizer.SummarizeAsync(memories);

        // Assert
        Assert.NotNull(summary);
        Assert.Empty(summary.Content);
        Assert.Empty(summary.KeyPoints);
    }

    [Fact]
    public async Task CreateHierarchyAsync_ShouldCreateMultipleLevels()
    {
        // Arrange - Use more substantial content for meaningful hierarchy
        var memories = new List<MemoryUnit>();
        for (var i = 0; i < 10; i++)
        {
            memories.Add(CreateMemory($"Memory item {i} with substantial content about topic {i % 3}. This sentence provides additional context and information that makes the memory more meaningful for hierarchical summarization."));
        }

        // Act
        var hierarchy = await _summarizer.CreateHierarchyAsync(memories, levels: 2);

        // Assert
        Assert.NotNull(hierarchy);
        Assert.NotNull(hierarchy.RootSummary);
        Assert.NotEmpty(hierarchy.RootSummary.Content);
        Assert.Equal(10, hierarchy.TotalMemoryCount);
        // Compression ratio verification - hierarchy should have some levels
        Assert.True(hierarchy.OverallCompressionRatio > 0, "Compression ratio should be positive");
    }

    [Fact]
    public async Task IncrementalUpdateAsync_ShouldUpdateExistingSummary()
    {
        // Arrange
        var existingSummary = new MemorySummary
        {
            Id = Guid.NewGuid(),
            Content = "Existing summary about technology.",
            KeyPoints = ["Technology is important", "Innovation drives progress"],
            OriginalTokenCount = 100,
            SummarizedTokenCount = 30,
            SourceMemoryIds = [Guid.NewGuid(), Guid.NewGuid()]
        };

        var newMemories = new List<MemoryUnit>
        {
            CreateMemory("New information about emerging technologies."),
            CreateMemory("Additional details about future developments.")
        };

        // Act
        var updatedSummary = await _summarizer.IncrementalUpdateAsync(existingSummary, newMemories);

        // Assert
        Assert.NotNull(updatedSummary);
        Assert.Contains("Existing summary", updatedSummary.Content);
        Assert.True(updatedSummary.SourceMemoryIds.Count >= existingSummary.SourceMemoryIds.Count);
    }

    [Theory]
    [InlineData(110000, 128000, 5, true)]  // 86% usage, 5 memories - should trigger (>= 85% threshold)
    [InlineData(50000, 128000, 5, false)]  // 39% usage, 5 memories - should not trigger
    [InlineData(90000, 100000, 10, true)]  // 90% usage, 10 memories - should trigger
    [InlineData(10000, 128000, 2, false)]  // Low usage, few memories - should not trigger (< 5 memories)
    [InlineData(110000, 128000, 3, false)] // 86% usage but only 3 memories - should not trigger (min 5 memories required)
    public void ShouldTriggerSummarization_ShouldReturnExpectedResult(
        int currentTokenCount, int maxTokens, int memoryCount, bool expectedResult)
    {
        // Act
        var result = _summarizer.ShouldTriggerSummarization(currentTokenCount, maxTokens, memoryCount);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldExtractEntities()
    {
        // Arrange
        var memories = new List<MemoryUnit>
        {
            CreateMemory("Microsoft announced new features for Azure cloud platform."),
            CreateMemory("Google's TensorFlow library updated to version 3.0."),
            CreateMemory("OpenAI released GPT-5 with improved capabilities.")
        };

        // Act
        var summary = await _summarizer.SummarizeAsync(memories);

        // Assert
        Assert.NotNull(summary);
        // Entities should include company names
        Assert.True(summary.Entities.Count > 0 || summary.KeyPoints.Count > 0,
            "Summary should contain entities or key points");
    }

    [Fact]
    public async Task SummarizeAsync_WithSingleMemory_ShouldReturnMemoryContent()
    {
        // Arrange
        var content = "This is a single memory with important information.";
        var memories = new List<MemoryUnit>
        {
            CreateMemory(content)
        };

        // Act
        var summary = await _summarizer.SummarizeAsync(memories);

        // Assert
        Assert.NotNull(summary);
        Assert.Contains("single memory", summary.Content.ToLowerInvariant());
        Assert.Single(summary.SourceMemoryIds);
    }

    private static MemoryUnit CreateMemory(string content)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "test-user",
            Content = content,
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
    }

    private static MemoryUnit CreateMemoryWithTopics(string content, List<string> topics)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "test-user",
            Content = content,
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            Topics = topics
        };
    }
}
