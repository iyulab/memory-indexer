using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace SharedLib.Completion;

/// <summary>
/// Ollama completion service implementation for samples and tests.
/// Not included in the main MemoryIndexer packages.
/// </summary>
public sealed partial class OllamaCompletionService : ITextCompletionService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<OllamaCompletionService> _logger;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates an Ollama completion service.
    /// </summary>
    /// <param name="baseUrl">Ollama server URL (default: http://localhost:11434).</param>
    /// <param name="model">Model name (e.g., "llama3.2", "mistral").</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">Optional HTTP client (if not provided, a new one is created).</param>
    public OllamaCompletionService(
        string baseUrl = "http://localhost:11434",
        string model = "llama3.2",
        ILogger<OllamaCompletionService>? logger = null,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl, nameof(baseUrl));
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));

        _model = model;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OllamaCompletionService>.Instance;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMinutes(5) // LLM generation can be slow
            };
            _ownsHttpClient = true;
        }

        LogOllamaCompletionServiceInitialized(_logger, baseUrl, model);
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(prompt));

        var request = new OllamaGenerateRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false,
            Options = MapToOllamaOptions(options)
        };

        var response = await _httpClient.PostAsJsonAsync("/api/generate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
        return result?.Response ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompts, nameof(prompts));

        var promptList = prompts.ToList();
        if (promptList.Count == 0)
            return [];

        // Ollama doesn't have native batch completion, process sequentially
        var results = new List<string>(promptList.Count);
        foreach (var prompt in promptList)
        {
            var result = await CompleteAsync(prompt, options, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ollama completion service initialized: url={Url}, model={Model}")]
    private static partial void LogOllamaCompletionServiceInitialized(ILogger logger, string url, string model);

    private static OllamaOptions? MapToOllamaOptions(TextCompletionOptions? options)
    {
        if (options == null)
            return null;

        return new OllamaOptions
        {
            Temperature = options.Temperature,
            NumPredict = options.MaxTokens,
            TopP = options.TopP,
            Stop = options.StopSequences?.ToList()
        };
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("prompt")]
        public required string Prompt { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OllamaOptions? Options { get; init; }
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int NumPredict { get; init; }

        [JsonPropertyName("top_p")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TopP { get; init; }

        [JsonPropertyName("stop")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Stop { get; init; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }
}
