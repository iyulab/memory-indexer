using MemoryIndexer.Interfaces;
using Microsoft.Extensions.AI;

namespace MemoryIndexer.Services;

/// <summary>
/// An <see cref="IEmbeddingService"/> over <c>Microsoft.Extensions.AI</c> embedding generators, so any generator
/// (OpenAI, Azure OpenAI, Ollama, LMSupply's <c>LocalEmbeddingGenerator</c>, IronHive's adapter) serves Memory Indexer
/// without an adapter class.
/// </summary>
/// <remarks>
/// <c>IEmbeddingGenerator</c> has no query/document distinction, so the role is carried by the instance: the
/// <c>documents</c> generator embeds what is stored and, for an asymmetric model (E5, BGE, Qwen3-Embedding), the
/// <c>queries</c> generator applies the query convention. Without <c>queries</c>, queries are embedded by <c>documents</c>.
/// The service does not dispose the generators.
/// </remarks>
public sealed class EmbeddingGeneratorEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _documents;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _queries;

    /// <summary>Creates the service.</summary>
    /// <param name="documents">Embeds stored memories.</param>
    /// <param name="queries">Embeds search queries; null uses <paramref name="documents"/>.</param>
    /// <param name="dimensions">
    /// The vector size, when the generator's <see cref="EmbeddingGeneratorMetadata.DefaultModelDimensions"/> is null;
    /// when both are set they must agree.
    /// </param>
    /// <exception cref="ArgumentException">No dimension is known, or the two disagree.</exception>
    public EmbeddingGeneratorEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> documents,
        IEmbeddingGenerator<string, Embedding<float>>? queries = null,
        int? dimensions = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _queries = queries ?? documents;

        var metadata = documents.GetService<EmbeddingGeneratorMetadata>();
        var reported = metadata?.DefaultModelDimensions;
        var model = metadata?.DefaultModelId ?? "(unnamed model)";
        if (dimensions is int set && reported is int known && set != known)
        {
            throw new ArgumentException(
                $"The dimension given is {set} but the generator reports {known} for model '{model}'.", nameof(dimensions));
        }
        Dimensions = dimensions ?? reported ?? throw new ArgumentException(
            $"The embedding generator does not report a dimension for model '{model}'; pass the model's dimension.",
            nameof(dimensions));
        if (Dimensions <= 0)
            throw new ArgumentException($"The dimension must be positive (got {Dimensions}).", nameof(dimensions));
    }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        EmbedOneAsync(_documents, text, cancellationToken);

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default) =>
        EmbedOneAsync(_queries, query, cancellationToken);

    /// <summary>Embeds every text in one generator call.</summary>
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var inputs = texts.ToList();
        if (inputs.Count == 0)
            return [];

        var generated = await _documents.GenerateAsync(inputs, options: null, cancellationToken).ConfigureAwait(false);
        if (generated.Count != inputs.Count)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned {generated.Count} embedding(s) for {inputs.Count} input(s). " +
                "Results are positional, so a partial set cannot be matched to its inputs.");
        }
        return generated.Select(Checked).ToList();
    }

    private async Task<ReadOnlyMemory<float>> EmbedOneAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var generated = await generator.GenerateAsync([text], options: null, cancellationToken).ConfigureAwait(false);
        if (generated.Count != 1)
            throw new InvalidOperationException($"The embedding generator returned {generated.Count} embedding(s) for one input.");
        return Checked(generated[0]);
    }

    private ReadOnlyMemory<float> Checked(Embedding<float> embedding)
    {
        if (embedding.Vector.Length != Dimensions)
        {
            throw new InvalidOperationException(
                $"The embedding generator returned a {embedding.Vector.Length}-dimension vector; Memory Indexer expects {Dimensions}.");
        }
        return embedding.Vector;
    }
}
