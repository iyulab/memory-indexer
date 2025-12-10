using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Embedding.Providers;

/// <summary>
/// Embedding service using Ollama local inference.
/// Supports BGE-M3, nomic-embed-text, and other Ollama-compatible models.
/// </summary>
public sealed class OllamaEmbeddingService : CachedEmbeddingServiceBase, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimensions;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected override string CacheKeyPrefix => "ollama";
    public override int Dimensions => _dimensions;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OllamaEmbeddingService> logger)
        : base(cache, logger, options.Value.Embedding)
    {
        _httpClient = httpClient;
        var embeddingOptions = options.Value.Embedding;
        _model = embeddingOptions.Model;
        _dimensions = embeddingOptions.Dimensions;

        _httpClient.BaseAddress = new Uri(embeddingOptions.Endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds);
    }

    protected override async Task<ReadOnlyMemory<float>> GenerateSingleEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var request = new OllamaEmbeddingRequest
            {
                Model = _model,
                Input = text
            };

            Logger.LogDebug("Requesting embedding from Ollama for text of length {Length}", text.Length);

            var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogError("Ollama embedding request failed: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new HttpRequestException(
                    $"Ollama embedding request failed: {response.StatusCode} - {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                cancellationToken: cancellationToken);

            if (result?.Embeddings == null || result.Embeddings.Count == 0)
            {
                throw new InvalidOperationException("Ollama returned empty embeddings");
            }

            var embedding = result.Embeddings[0];

            if (embedding.Length != _dimensions)
            {
                Logger.LogWarning(
                    "Embedding dimension mismatch: expected {Expected}, got {Actual}",
                    _dimensions, embedding.Length);
            }

            return embedding;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}

internal sealed class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }
}

internal sealed class OllamaEmbeddingResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("embeddings")]
    public List<float[]>? Embeddings { get; init; }
}
