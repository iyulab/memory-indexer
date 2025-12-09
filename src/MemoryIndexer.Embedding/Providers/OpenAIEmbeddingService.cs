using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Embedding.Providers;

/// <summary>
/// Embedding service using OpenAI API.
/// Supports text-embedding-3-small, text-embedding-3-large, and text-embedding-ada-002.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly EmbeddingOptions _options;
    private readonly TimeSpan _cacheTtl;

    private const string DefaultEndpoint = "https://api.openai.com/v1";

    public OpenAIEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OpenAIEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _options = options.Value.Embedding;
        _cacheTtl = TimeSpan.FromMinutes(_options.CacheTtlMinutes);

        var endpoint = string.IsNullOrEmpty(_options.Endpoint) || _options.Endpoint.Contains("localhost")
            ? DefaultEndpoint
            : _options.Endpoint;

        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
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

        var embeddings = await GenerateEmbeddingsInternalAsync([text], cancellationToken);
        var embedding = embeddings[0];

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
        var uncachedIndices = new List<int>();
        var uncachedTexts = new List<string>();

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
                uncachedIndices.Add(i);
                uncachedTexts.Add(textList[i]);
            }
        }

        if (uncachedTexts.Count == 0)
        {
            _logger.LogDebug("All {Count} embeddings found in cache", textList.Count);
            return results;
        }

        // Process uncached texts in batches (OpenAI supports native batching)
        var batches = uncachedTexts
            .Select((text, index) => new { text, index })
            .GroupBy(x => x.index / _options.BatchSize)
            .Select(g => g.Select(x => x.text).ToList())
            .ToList();

        var processedCount = 0;
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchEmbeddings = await GenerateEmbeddingsInternalAsync(batch, cancellationToken);

            for (var i = 0; i < batchEmbeddings.Count; i++)
            {
                var originalIndex = uncachedIndices[processedCount + i];
                results[originalIndex] = batchEmbeddings[i];

                if (_options.CacheTtlMinutes > 0)
                {
                    var cacheKey = GetCacheKey(textList[originalIndex]);
                    _cache.Set(cacheKey, batchEmbeddings[i], _cacheTtl);
                }
            }

            processedCount += batch.Count;
        }

        return results;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsInternalAsync(
        List<string> texts,
        CancellationToken cancellationToken)
    {
        var request = new OpenAIEmbeddingRequest
        {
            Model = _options.Model,
            Input = texts,
            Dimensions = _options.Dimensions
        };

        _logger.LogDebug("Requesting {Count} embeddings from OpenAI", texts.Count);

        var response = await _httpClient.PostAsJsonAsync(
            "/embeddings",
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI embedding request failed: {StatusCode} - {Error}",
                response.StatusCode, errorContent);
            throw new HttpRequestException(
                $"OpenAI embedding request failed: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Data == null || result.Data.Count == 0)
        {
            throw new InvalidOperationException("OpenAI returned empty embeddings");
        }

        // Sort by index to ensure correct order
        var sortedData = result.Data.OrderBy(d => d.Index).ToList();

        return sortedData
            .Select(d => (ReadOnlyMemory<float>)d.Embedding)
            .ToList();
    }

    private static string GetCacheKey(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return $"emb:openai:{Convert.ToHexString(hash)}";
    }
}

/// <summary>
/// OpenAI embedding API request model.
/// </summary>
internal sealed class OpenAIEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required List<string> Input { get; init; }

    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; init; }

    [JsonPropertyName("encoding_format")]
    public string EncodingFormat { get; init; } = "float";
}

/// <summary>
/// OpenAI embedding API response model.
/// </summary>
internal sealed class OpenAIEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAIEmbeddingData>? Data { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("usage")]
    public OpenAIUsage? Usage { get; init; }
}

internal sealed class OpenAIEmbeddingData
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}

internal sealed class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
