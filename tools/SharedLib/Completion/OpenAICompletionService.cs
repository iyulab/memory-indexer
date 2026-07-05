using System.ClientModel;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace SharedLib.Completion;

/// <summary>
/// OpenAI completion service implementation for samples and tests.
/// Not included in the main MemoryIndexer packages.
/// </summary>
public sealed partial class OpenAICompletionService : ITextCompletionService
{
    private readonly ChatClient _client;
    private readonly ILogger<OpenAICompletionService> _logger;

    /// <summary>
    /// Creates an OpenAI completion service with the specified API key and model.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (e.g., "gpt-4o", "gpt-4o-mini").</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="endpoint">Optional custom endpoint for Azure OpenAI or compatible APIs.</param>
    public OpenAICompletionService(
        string apiKey,
        string model = "gpt-4o-mini",
        ILogger<OpenAICompletionService>? logger = null,
        Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));

        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAICompletionService>.Instance;

        var credential = new ApiKeyCredential(apiKey);
        if (endpoint != null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            var client = new OpenAIClient(credential, options);
            _client = client.GetChatClient(model);
        }
        else
        {
            _client = new ChatClient(model, credential);
        }

        LogOpenAICompletionServiceInitialized(_logger, model);
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(prompt));

        var chatOptions = MapToChatOptions(options);
        var messages = new List<ChatMessage> { new UserChatMessage(prompt) };

        var response = await _client.CompleteChatAsync(messages, chatOptions, cancellationToken);
        return response.Value.Content[0].Text ?? string.Empty;
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

        // OpenAI doesn't have native batch completion, so we process sequentially
        // For true batch processing, consider using the Batch API
        var results = new List<string>(promptList.Count);
        foreach (var prompt in promptList)
        {
            var result = await CompleteAsync(prompt, options, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenAI completion service initialized: model={Model}")]
    private static partial void LogOpenAICompletionServiceInitialized(ILogger logger, string model);

    private static ChatCompletionOptions MapToChatOptions(TextCompletionOptions? options)
    {
        if (options == null)
            return new ChatCompletionOptions();

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokenCount = options.MaxTokens
        };

        if (options.TopP.HasValue)
            chatOptions.TopP = options.TopP.Value;

        if (options.PresencePenalty.HasValue)
            chatOptions.PresencePenalty = options.PresencePenalty.Value;

        if (options.FrequencyPenalty.HasValue)
            chatOptions.FrequencyPenalty = options.FrequencyPenalty.Value;

        if (options.StopSequences != null)
        {
            foreach (var stop in options.StopSequences)
                chatOptions.StopSequences.Add(stop);
        }

        return chatOptions;
    }
}
