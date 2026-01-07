using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Completion.Providers;

/// <summary>
/// Text completion service using OpenAI Chat Completion API.
/// Supports GPT-4, GPT-3.5-turbo, and compatible models.
/// </summary>
public sealed class OpenAICompletionService : ITextCompletionService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OpenAICompletionService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public OpenAICompletionService(
        HttpClient httpClient,
        IOptions<MemoryIndexerOptions> options,
        ILogger<OpenAICompletionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var completionOptions = options.Value.Completion;
        _model = completionOptions.Model;

        _httpClient.BaseAddress = new Uri(completionOptions.Endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(completionOptions.TimeoutSeconds);

        // Set Authorization header
        if (!string.IsNullOrEmpty(completionOptions.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {completionOptions.ApiKey}");
        }
    }

    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var request = new OpenAICompletionRequest
            {
                Model = _model,
                Messages = [new OpenAIMessage { Role = "user", Content = prompt }],
                Temperature = options?.Temperature ?? 0.7f,
                MaxTokens = options?.MaxTokens ?? 500,
                TopP = options?.TopP,
                Stop = options?.StopSequences?.ToArray()
            };

            _logger.LogDebug("Generating completion with OpenAI model {Model}", _model);

            var response = await _httpClient.PostAsJsonAsync(
                "/v1/chat/completions",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAICompletionResponse>(
                cancellationToken: cancellationToken);

            if (result?.Choices == null || result.Choices.Count == 0)
            {
                throw new InvalidOperationException("OpenAI returned empty response");
            }

            var content = result.Choices[0].Message?.Content;
            if (string.IsNullOrEmpty(content))
            {
                throw new InvalidOperationException("OpenAI returned null content");
            }

            _logger.LogDebug("Completion generated successfully");

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate completion with OpenAI");
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

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

#region OpenAI API Models

internal sealed class OpenAICompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<OpenAIMessage> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; } = 0.7f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 500;

    [JsonPropertyName("top_p")]
    public float? TopP { get; init; }

    [JsonPropertyName("stop")]
    public string[]? Stop { get; init; }
}

internal sealed class OpenAIMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class OpenAICompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("choices")]
    public List<OpenAIChoice>? Choices { get; init; }

    [JsonPropertyName("usage")]
    public OpenAIUsage? Usage { get; init; }
}

internal sealed class OpenAIChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public OpenAIMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

#endregion
