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
public sealed class OpenAIEmbeddingService : CachedEmbeddingServiceBase
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly int _dimensions;

    private const string DefaultEndpoint = "https://api.openai.com/v1";

    protected override string CacheKeyPrefix => "openai";
    public override int Dimensions => _dimensions;

    public OpenAIEmbeddingService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OpenAIEmbeddingService> logger)
        : base(cache, logger, options.Value.Embedding)
    {
        _httpClient = httpClient;
        var embeddingOptions = options.Value.Embedding;
        _model = embeddingOptions.Model;
        _dimensions = embeddingOptions.Dimensions;

        var endpoint = string.IsNullOrEmpty(embeddingOptions.Endpoint) || embeddingOptions.Endpoint.Contains("localhost")
            ? DefaultEndpoint
            : embeddingOptions.Endpoint;

        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds);

        if (!string.IsNullOrEmpty(embeddingOptions.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", embeddingOptions.ApiKey);
        }
    }

    protected override async Task<ReadOnlyMemory<float>> GenerateSingleEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var embeddings = await GenerateBatchInternalAsync([text], cancellationToken);
        return embeddings[0];
    }

    /// <summary>
    /// Override to use OpenAI's native batch API for better performance.
    /// </summary>
    protected override async Task ProcessUncachedBatchAsync(
        List<string> allTexts,
        ReadOnlyMemory<float>[] results,
        List<(int Index, string Text)> uncached,
        CancellationToken cancellationToken)
    {
        var batches = uncached
            .Select((item, i) => (item, BatchIndex: i / BatchSize))
            .GroupBy(x => x.BatchIndex)
            .Select(g => g.ToList());

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var textsInBatch = batch.Select(x => x.item.Text).ToList();
            var embeddings = await GenerateBatchInternalAsync(textsInBatch, cancellationToken);

            for (var i = 0; i < batch.Count; i++)
            {
                var originalIndex = batch[i].item.Index;
                results[originalIndex] = embeddings[i];
                CacheEmbedding(allTexts[originalIndex], embeddings[i]);
            }
        }
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchInternalAsync(
        List<string> texts,
        CancellationToken cancellationToken)
    {
        var request = new OpenAIEmbeddingRequest
        {
            Model = _model,
            Input = texts,
            Dimensions = _dimensions
        };

        Logger.LogDebug("Requesting {Count} embeddings from OpenAI", texts.Count);

        var response = await _httpClient.PostAsJsonAsync("/embeddings", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Logger.LogError("OpenAI embedding request failed: {StatusCode} - {Error}",
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

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => (ReadOnlyMemory<float>)d.Embedding)
            .ToList();
    }
}

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
