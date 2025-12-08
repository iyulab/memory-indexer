namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// Service for generating text embeddings.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gets the dimension of embeddings produced by this service.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch.
    /// More efficient than calling GenerateEmbeddingAsync multiple times.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vectors in the same order as inputs.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default);
}
