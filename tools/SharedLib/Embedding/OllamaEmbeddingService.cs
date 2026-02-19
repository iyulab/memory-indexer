using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace SharedLib.Embedding;

/// <summary>
/// Ollama embedding service implementation for samples and tests.
/// Not included in the main MemoryIndexer packages.
/// </summary>
public sealed partial class OllamaEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Common Ollama embedding models and their dimensions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> SupportedModels = new Dictionary<string, int>
    {
        ["nomic-embed-text"] = 768,
        ["mxbai-embed-large"] = 1024,
        ["all-minilm"] = 384,
        ["bge-m3"] = 1024,
        ["bge-large"] = 1024,
        ["snowflake-arctic-embed"] = 1024
    };

    /// <summary>
    /// Gets the dimension of embeddings produced by this service.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Creates an Ollama embedding service.
    /// </summary>
    /// <param name="baseUrl">Ollama server URL (default: http://localhost:11434).</param>
    /// <param name="model">Model name (e.g., "nomic-embed-text").</param>
    /// <param name="dimensions">Embedding dimensions (auto-detected from model if not specified).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">Optional HTTP client (if not provided, a new one is created).</param>
    public OllamaEmbeddingService(
        string baseUrl = "http://localhost:11434",
        string model = "nomic-embed-text",
        int? dimensions = null,
        ILogger<OllamaEmbeddingService>? logger = null,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl, nameof(baseUrl));
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));

        _model = model;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaEmbeddingService>.Instance;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _ownsHttpClient = true;
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
            Dimensions = 1024; // Default fallback
            LogUnknownModelUsingDefaultDimensions(_logger, model, Dimensions);
        }

        LogOllamaEmbeddingServiceInitialized(_logger, baseUrl, model, Dimensions);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        var request = new OllamaEmbedRequest { Model = _model, Input = text };
        var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken);
        if (result?.Embeddings == null || result.Embeddings.Count == 0)
            throw new InvalidOperationException("Ollama returned no embeddings");

        return result.Embeddings[0];
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

        // Ollama API supports batch embedding via input array
        var request = new OllamaEmbedBatchRequest { Model = _model, Input = textList };
        var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken);
        if (result?.Embeddings == null)
            throw new InvalidOperationException("Ollama returned no embeddings");

        return result.Embeddings.Select(e => (ReadOnlyMemory<float>)e).ToList();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown model '{Model}', using default dimensions {Dimensions}")]
    private static partial void LogUnknownModelUsingDefaultDimensions(ILogger logger, string model, int dimensions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ollama embedding service initialized: url={Url}, model={Model}, dimensions={Dimensions}")]
    private static partial void LogOllamaEmbeddingServiceInitialized(ILogger logger, string url, string model, int dimensions);

    private sealed class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required string Input { get; init; }
    }

    private sealed class OllamaEmbedBatchRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
