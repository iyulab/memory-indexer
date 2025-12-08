using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Embedding.Providers;

/// <summary>
/// Mock embedding service for development and testing.
/// Generates deterministic pseudo-embeddings based on content hash.
/// </summary>
public sealed class MockEmbeddingService : IEmbeddingService
{
    private readonly ILogger<MockEmbeddingService> _logger;
    private readonly int _dimensions;

    public MockEmbeddingService(
        IOptions<MemoryIndexerOptions> options,
        ILogger<MockEmbeddingService> logger)
    {
        _logger = logger;
        _dimensions = options.Value.Embedding.Dimensions;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating mock embedding for text of length {Length}", text.Length);

        var embedding = GenerateDeterministicEmbedding(text);
        return Task.FromResult<ReadOnlyMemory<float>>(embedding);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        _logger.LogDebug("Generating mock embeddings for {Count} texts", textList.Count);

        var results = textList
            .Select(GenerateDeterministicEmbedding)
            .Select(e => (ReadOnlyMemory<float>)e)
            .ToList();

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
    }

    /// <summary>
    /// Generates a deterministic embedding based on text content.
    /// Same text will always produce the same embedding.
    /// </summary>
    private float[] GenerateDeterministicEmbedding(string text)
    {
        var embedding = new float[_dimensions];
        var hash = text.GetHashCode();
        var random = new Random(hash);

        for (var i = 0; i < _dimensions; i++)
        {
            // Generate values in range [-1, 1]
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize to unit vector
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < _dimensions; i++)
            {
                embedding[i] /= norm;
            }
        }

        return embedding;
    }
}
