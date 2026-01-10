using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using OpenAI.Chat;
using LmsChatMessage = LMSupply.Generator.Models.ChatMessage;
using OaiChatMessage = OpenAI.Chat.ChatMessage;

namespace TwentyQuestionsGame.LLM;

/// <summary>
/// LLM client supporting both OpenAI SDK and LMSupply.Generator.
/// </summary>
public sealed class LlmClient
{
    private readonly ChatClient? _chatClient;
    private readonly IGeneratorModel? _generatorModel;

    /// <summary>
    /// Creates an LLM client using OpenAI ChatClient (or GPUStack).
    /// </summary>
    public LlmClient(ChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    /// <summary>
    /// Creates an LLM client using LMSupply.Generator (local model).
    /// </summary>
    public LlmClient(IGeneratorModel generatorModel)
    {
        _generatorModel = generatorModel;
    }

    public async Task<LlmResponse> CallAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        if (_chatClient != null)
        {
            return await CallOpenAiAsync(systemPrompt, userMessage, ct);
        }
        else if (_generatorModel != null)
        {
            return await CallLmsAsync(systemPrompt, userMessage, ct);
        }
        else
        {
            throw new InvalidOperationException("No LLM provider configured");
        }
    }

    private async Task<LlmResponse> CallOpenAiAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        var messages = new List<OaiChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 300
        };

        var sw = Stopwatch.StartNew();
        var completion = await _chatClient!.CompleteChatAsync(messages, options, ct);
        sw.Stop();

        var content = completion.Value.Content.Count > 0
            ? completion.Value.Content[0].Text ?? ""
            : "";

        return new LlmResponse
        {
            Content = content,
            PromptTokens = completion.Value.Usage?.InputTokenCount ?? 0,
            CompletionTokens = completion.Value.Usage?.OutputTokenCount ?? 0,
            LatencyMs = sw.ElapsedMilliseconds
        };
    }

    private async Task<LlmResponse> CallLmsAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        var messages = new[]
        {
            LmsChatMessage.System(systemPrompt),
            LmsChatMessage.User(userMessage)
        };

        var sw = Stopwatch.StartNew();
        var content = await _generatorModel!.GenerateChatCompleteAsync(messages, cancellationToken: ct);
        sw.Stop();

        // LMSupply doesn't provide token counts - estimate based on content length
        var estimatedPromptTokens = (systemPrompt.Length + userMessage.Length) / 4;
        var estimatedCompletionTokens = content.Length / 4;

        return new LlmResponse
        {
            Content = content,
            PromptTokens = estimatedPromptTokens,
            CompletionTokens = estimatedCompletionTokens,
            LatencyMs = sw.ElapsedMilliseconds
        };
    }

    public async Task<LlmResponse> CallWithToolResultsAsync(
        string systemPrompt,
        string userMessage,
        string toolResults,
        CancellationToken ct = default)
    {
        var enhancedUser = $"""
            {userMessage}

            <tool_results>
            {toolResults}
            </tool_results>

            Now provide your response based on these tool results.
            """;

        return await CallAsync(systemPrompt, enhancedUser, ct);
    }
}

#region Response

public sealed class LlmResponse
{
    public string Content { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public long LatencyMs { get; init; }
}

#endregion

#region Alpha Response Parsing

/// <summary>
/// Alpha's JSON response format and parsing.
/// </summary>
public sealed partial class AlphaResponse
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "No";

    [JsonPropertyName("isGuess")]
    public bool IsGuess { get; set; }

    [JsonPropertyName("guessCorrect")]
    public bool GuessCorrect { get; set; }

    [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.Singleline)]
    private static partial Regex JsonRegex();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static AlphaResponse? TryParse(string llmOutput)
    {
        var match = JsonRegex().Match(llmOutput);
        if (!match.Success) return null;

        try { return JsonSerializer.Deserialize<AlphaResponse>(match.Value, JsonOptions); }
        catch { return null; }
    }

    public static string NormalizeAnswer(string response)
    {
        var lower = response.ToLowerInvariant().Trim();
        if (lower.StartsWith("yes") || lower == "y") return "Yes";
        if (lower.StartsWith("no") || lower == "n") return "No";
        if (lower.Contains("maybe") || lower.Contains("perhaps")) return "Maybe";
        return response.Trim();
    }
}

#endregion
