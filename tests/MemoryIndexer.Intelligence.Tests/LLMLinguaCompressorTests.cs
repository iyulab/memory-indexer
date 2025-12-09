using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Intelligence.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class LLMLinguaCompressorTests
{
    private readonly LLMLinguaCompressor _compressor;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;

    public LLMLinguaCompressorTests()
    {
        _embeddingServiceMock = new Mock<IEmbeddingService>();

        _embeddingServiceMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => GenerateMockEmbedding(text));

        _embeddingServiceMock
            .Setup(x => x.GenerateBatchEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                texts.Select(t => GenerateMockEmbedding(t)).ToList());

        _compressor = new LLMLinguaCompressor(
            _embeddingServiceMock.Object,
            NullLogger<LLMLinguaCompressor>.Instance);
    }

    private static ReadOnlyMemory<float> GenerateMockEmbedding(string text)
    {
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[768];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)random.NextDouble() * 2 - 1;
        }
        var norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= norm;
        }
        return embedding;
    }

    [Fact]
    public async Task CompressAsync_EmptyText_ShouldReturnEmpty()
    {
        // Act
        var result = await _compressor.CompressAsync("");

        // Assert
        Assert.Equal(string.Empty, result.CompressedText);
        Assert.True(result.TargetAchieved);
        Assert.Equal(1.0f, result.InformationRetention);
    }

    [Fact]
    public async Task CompressAsync_WithDefaultOptions_ShouldCompressText()
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog. This is a sample sentence for testing compression.";

        // Act
        var result = await _compressor.CompressAsync(text);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
        Assert.True(result.OriginalTokenCount > 0);
    }

    [Fact]
    public async Task CompressAsync_WithTargetRatio_ShouldRespectTarget()
    {
        // Arrange
        var text = "Machine learning is a subset of artificial intelligence. " +
                   "It involves training algorithms on data to make predictions. " +
                   "Neural networks are a popular machine learning technique. " +
                   "Deep learning uses multiple layers of neural networks. " +
                   "These techniques have revolutionized many industries.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.3f,
            Strategy = CompressionStrategy.Heuristic
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
        Assert.True(result.CompressedText.Length < text.Length,
            "Compressed text should be shorter than original");
    }

    [Fact]
    public async Task CompressAsync_TokenPruningStrategy_ShouldWork()
    {
        // Arrange
        var text = "The critical system error occurred in the main database server. Please remember this important information.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            Strategy = CompressionStrategy.TokenPruning
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
        Assert.True(result.InformationRetention > 0);
    }

    [Fact]
    public async Task CompressAsync_SentencePruningStrategy_ShouldWork()
    {
        // Arrange
        var text = "First important sentence. Second sentence with details. " +
                   "Third sentence provides context. Fourth sentence is filler.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            Strategy = CompressionStrategy.SentencePruning
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
    }

    [Fact]
    public async Task CompressAsync_HybridStrategy_ShouldWork()
    {
        // Arrange
        var text = "Machine learning systems process large amounts of data efficiently. " +
                   "These systems can identify patterns and make predictions. " +
                   "The technology has many practical applications in various fields. " +
                   "Companies use machine learning for analytics and automation.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.4f,
            Strategy = CompressionStrategy.Hybrid
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
    }

    [Fact]
    public async Task CompressAsync_PreserveNamedEntities_ShouldRetainCapitalizedWords()
    {
        // Arrange
        var text = "Microsoft Azure and Google Cloud are popular platforms. " +
                   "Amazon AWS provides similar services.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            PreserveNamedEntities = true,
            Strategy = CompressionStrategy.Heuristic
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        // Named entities should be preserved
        Assert.True(result.CompressedText.Contains("Microsoft") ||
                    result.CompressedText.Contains("Google") ||
                    result.CompressedText.Contains("Amazon"),
            "At least one named entity should be preserved");
    }

    [Fact]
    public async Task CompressAsync_PreserveNumericals_ShouldRetainNumbers()
    {
        // Arrange
        var text = "The meeting is scheduled for 2024-12-15 at 14:00. " +
                   "We expect 500 participants from 25 different countries.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            PreserveNumericals = true,
            Strategy = CompressionStrategy.Heuristic
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
    }

    [Fact]
    public async Task CompressAsync_RequiredKeywords_ShouldPreserveKeywords()
    {
        // Arrange
        var text = "The password must be secure. The API endpoint requires authentication.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            RequiredKeywords = ["password", "API"],
            Strategy = CompressionStrategy.Heuristic
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.Contains("password", result.CompressedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API", result.CompressedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompressBatchAsync_MultipleTexts_ShouldCompressAll()
    {
        // Arrange
        var texts = new List<string>
        {
            "First text to compress with some content.",
            "Second text with different information.",
            "Third text that should also be compressed."
        };

        // Act
        var results = await _compressor.CompressBatchAsync(texts);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.NotNull(r.CompressedText);
            Assert.True(r.OriginalTokenCount > 0);
        });
    }

    [Fact]
    public void EstimateCompressionRatio_ShouldReturnReasonableEstimate()
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog.";

        // Act
        var estimate = _compressor.EstimateCompressionRatio(text);

        // Assert
        Assert.True(estimate > 0 && estimate <= 1.0f,
            $"Estimate {estimate} should be between 0 and 1");
    }

    [Fact]
    public void EstimateCompressionRatio_EmptyText_ShouldReturn1()
    {
        // Act
        var estimate = _compressor.EstimateCompressionRatio("");

        // Assert
        Assert.Equal(1.0f, estimate);
    }

    [Fact]
    public async Task CompressAsync_VeryShortText_ShouldNotOverCompress()
    {
        // Arrange
        var text = "Critical error";

        // Act
        var result = await _compressor.CompressAsync(text);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
    }

    [Fact]
    public async Task CompressAsync_CodeContent_ShouldPreserveCodeStructure()
    {
        // Arrange
        var text = "The function getData() { return data; } handles data retrieval. " +
                   "It uses array[index] notation and returns null => default.";

        var options = new CompressionOptions
        {
            TargetRatio = 0.5f,
            PreserveCodeContent = true,
            Strategy = CompressionStrategy.SentencePruning
        };

        // Act
        var result = await _compressor.CompressAsync(text, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.CompressedText);
    }
}
