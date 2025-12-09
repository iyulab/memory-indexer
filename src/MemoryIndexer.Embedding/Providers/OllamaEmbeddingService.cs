using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class OllamaEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OllamaEmbeddingService> _logger;
    private readonly EmbeddingOptions _options;
    private readonly TimeSpan _cacheTtl;
    private readonly SemaphoreSlim _semaphore;

    public OllamaEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value.Embedding;
        _cacheTtl = TimeSpan.FromMinutes(_options.CacheTtlMinutes);
        _semaphore = new SemaphoreSlim(1, 1); // Serialize Ollama requests

        _httpClient.BaseAddress = new Uri(_options.Endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    /// <inheritdoc />
    public int Dimensions => _options.Dimensions;

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var cacheKey = GetCacheKey(text);

        if (_options.CacheTtlMinutes > 0 && _cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
        {
            _logger.LogDebug("Cache hit for embedding");
            return cached;
        }

        var embedding = await GenerateEmbeddingInternalAsync(text, cancellationToken);

        if (_options.CacheTtlMinutes > 0)
        {
            _cache.Set(cacheKey, embedding, _cacheTtl);
        }

        return embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return Array.Empty<ReadOnlyMemory<float>>();
        }

        _logger.LogDebug("Generating batch embeddings for {Count} texts", textList.Count);

        var results = new List<ReadOnlyMemory<float>>(textList.Count);
        var uncachedTexts = new List<(int Index, string Text)>();

        // Check cache first
        for (var i = 0; i < textList.Count; i++)
        {
            var cacheKey = GetCacheKey(textList[i]);
            if (_options.CacheTtlMinutes > 0 && _cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
            {
                results.Add(cached);
            }
            else
            {
                results.Add(ReadOnlyMemory<float>.Empty);
                uncachedTexts.Add((i, textList[i]));
            }
        }

        if (uncachedTexts.Count == 0)
        {
            _logger.LogDebug("All {Count} embeddings found in cache", textList.Count);
            return results;
        }

        // Process uncached texts in batches
        var batches = uncachedTexts
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / _options.BatchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Process batch items - Ollama doesn't support native batching,
            // so we process items in parallel within reasonable limits
            var batchTasks = batch.Select(async item =>
            {
                var embedding = await GenerateEmbeddingInternalAsync(item.Text, cancellationToken);
                return (item.Index, Embedding: embedding);
            });

            var batchResults = await Task.WhenAll(batchTasks);

            foreach (var (index, embedding) in batchResults)
            {
                results[index] = embedding;

                if (_options.CacheTtlMinutes > 0)
                {
                    var cacheKey = GetCacheKey(textList[index]);
                    _cache.Set(cacheKey, embedding, _cacheTtl);
                }
            }
        }

        return results;
    }

    private async Task<ReadOnlyMemory<float>> GenerateEmbeddingInternalAsync(
        string text,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var request = new OllamaEmbeddingRequest
            {
                Model = _options.Model,
                Input = text
            };

            _logger.LogDebug("Requesting embedding from Ollama for text of length {Length}", text.Length);

            var response = await _httpClient.PostAsJsonAsync(
                "/api/embed",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Ollama embedding request failed: {StatusCode} - {Error}",
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

            // Validate dimensions
            if (embedding.Length != _options.Dimensions)
            {
                _logger.LogWarning(
                    "Embedding dimension mismatch: expected {Expected}, got {Actual}. " +
                    "Consider updating the Dimensions configuration.",
                    _options.Dimensions, embedding.Length);
            }

            return embedding;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string GetCacheKey(string text)
    {
        // Use hash for cache key to handle long texts
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return $"emb:{Convert.ToHexString(hash)}";
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

/// <summary>
/// Ollama embedding API request model.
/// </summary>
internal sealed class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required string Input { get; init; }
}

/// <summary>
/// Ollama embedding API response model.
/// </summary>
internal sealed class OllamaEmbeddingResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("embeddings")]
    public List<float[]>? Embeddings { get; init; }
}
