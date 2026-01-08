using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenAI.Chat;

namespace TwentyQuestionsGame.LLM;

/// <summary>
/// LLM client using OpenAI official SDK.
/// </summary>
public sealed class LlmClient(ChatClient chatClient)
{
    public async Task<LlmResponse> CallAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        var sw = Stopwatch.StartNew();
        var completion = await chatClient.CompleteChatAsync(messages, cancellationToken: ct);
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
