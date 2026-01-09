using System.ClientModel;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;

namespace SharedLib.Embedding;

/// <summary>
/// OpenAI embedding service implementation for samples and tests.
/// Not included in the main MemoryIndexer packages.
/// </summary>
/// <remarks>
/// This is a pure API caller without built-in caching.
/// For caching, wrap with <c>CachingEmbeddingService</c>:
/// <code>
/// var openAi = new OpenAIEmbeddingService(apiKey, model);
/// var cached = new CachingEmbeddingService(openAi);
/// </code>
/// </remarks>
public sealed class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAIEmbeddingService> _logger;

    /// <summary>
    /// Supported OpenAI embedding models and their dimensions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> SupportedModels = new Dictionary<string, int>
    {
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536
    };

    /// <summary>
    /// Gets the dimension of embeddings produced by this service.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Creates an OpenAI embedding service with the specified API key and model.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (e.g., "text-embedding-3-small").</param>
    /// <param name="dimensions">Optional dimension override for models that support it.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="endpoint">Optional custom endpoint for Azure OpenAI or compatible APIs.</param>
    public OpenAIEmbeddingService(
        string apiKey,
        string model = "text-embedding-3-small",
        int? dimensions = null,
        ILogger<OpenAIEmbeddingService>? logger = null,
        Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));

        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIEmbeddingService>.Instance;

        var credential = new ApiKeyCredential(apiKey);
        if (endpoint != null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            var client = new OpenAIClient(credential, options);
            _client = client.GetEmbeddingClient(model);
        }
        else
        {
            _client = new EmbeddingClient(model, credential);
        }

        // Determine dimensions
        if (dimensions.HasValue)
        {
            Dimensions = dimensions.Value;
        }
        else if (SupportedModels.TryGetValue(model, out var defaultDimensions))
        {
            Dimensions = defaultDimensions;
        }
        else
        {
            Dimensions = 1536; // Default fallback
            _logger.LogWarning("Unknown model '{Model}', using default dimensions {Dimensions}", model, Dimensions);
        }

        _logger.LogInformation("OpenAI embedding service initialized: model={Model}, dimensions={Dimensions}",
            model, Dimensions);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return response.Value.ToFloats();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts, nameof(texts));

        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        var response = await _client.GenerateEmbeddingsAsync(textList, cancellationToken: cancellationToken);
        return response.Value.OrderBy(e => e.Index).Select(e => e.ToFloats()).ToList();
    }
}
