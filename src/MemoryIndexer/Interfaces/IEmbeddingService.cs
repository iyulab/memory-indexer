namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for generating text embeddings.
/// </summary>
/// <remarks>
/// <para>
/// Two roles reach an embedder: the text that is <b>stored</b> (a memory, a chunk, a summary —
/// <see cref="GenerateEmbeddingAsync"/> and <see cref="GenerateBatchEmbeddingsAsync"/>) and the <b>query</b> compared
/// against it (<see cref="GenerateQueryEmbeddingAsync"/>). The library calls the query method wherever it embeds a
/// query to search stored vectors.
/// </para>
/// <para>
/// A symmetric model embeds both the same way and needs nothing: the query method defaults to
/// <see cref="GenerateEmbeddingAsync"/>. An asymmetric model (E5 <c>query: </c>/<c>passage: </c>, Qwen3-Embedding's
/// or BGE's query instruction) overrides it to apply its query convention — together with its document convention in
/// <see cref="GenerateEmbeddingAsync"/>, since a query prefix against memories embedded without theirs mixes two
/// conventions. A service that wraps another one forwards both methods.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Generates an embedding for a search query, to be compared against vectors from
    /// <see cref="GenerateEmbeddingAsync"/>. Defaults to <see cref="GenerateEmbeddingAsync"/> (a symmetric model);
    /// an asymmetric model overrides it.
    /// </summary>
    /// <param name="query">The query to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    Task<ReadOnlyMemory<float>> GenerateQueryEmbeddingAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        GenerateEmbeddingAsync(query, cancellationToken);
}
