using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Completion.Providers;

/// <summary>
/// Text completion service using Ollama local inference.
/// Supports llama3, phi3, gemma2, and other Ollama-compatible models.
/// </summary>
public sealed class OllamaCompletionService : ITextCompletionService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaCompletionService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public OllamaCompletionService(
        HttpClient httpClient,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OllamaCompletionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var completionOptions = options.Value.Completion;
        _model = completionOptions.Model;

        _httpClient.BaseAddress = new Uri(completionOptions.Endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(completionOptions.TimeoutSeconds);
    }

    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var request = new OllamaCompletionRequest
            {
                Model = _model,
                Prompt = prompt,
                Stream = false,
                Options = CreateOllamaOptions(options)
            };

            _logger.LogDebug("Generating completion with Ollama model {Model}", _model);

            var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaCompletionResponse>(
                cancellationToken: cancellationToken);

            if (result?.Response == null)
            {
                throw new InvalidOperationException("Ollama returned null response");
            }

            _logger.LogDebug("Completion generated successfully");

            return result.Response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate completion with Ollama");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var prompt in prompts)
        {
            var result = await CompleteAsync(prompt, options, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    private static OllamaModelOptions? CreateOllamaOptions(TextCompletionOptions? options)
    {
        if (options == null)
        {
            return null;
        }

        return new OllamaModelOptions
        {
            Temperature = options.Temperature,
            NumPredict = options.MaxTokens,
            TopP = options.TopP,
            Stop = options.StopSequences?.ToArray()
        };
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

#region Ollama API Models

internal sealed class OllamaCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("options")]
    public OllamaModelOptions? Options { get; init; }
}

internal sealed class OllamaModelOptions
{
    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.7f;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; } = 500;

    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    [JsonPropertyName("stop")]
    public string[]? Stop { get; init; }
}

internal sealed class OllamaCompletionResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }
}

#endregion
