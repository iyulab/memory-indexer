using Microsoft.Extensions.Logging;
using TwentyQuestionsGame.LLM;
using TwentyQuestionsGame.ToolCall;

namespace TwentyQuestionsGame.Agents;

/// <summary>
/// Base class for game agents with tool call processing loop.
/// </summary>
public abstract class AgentBase(
    LlmClient llmClient,
    ToolCallParser parser,
    ToolCallExecutor executor,
    ILogger logger)
{
    protected const int MaxToolCallIterations = 3;

    protected abstract string UserId { get; }
    protected abstract string SessionId { get; }

    /// <summary>
    /// Processes LLM response with tool call loop until no more tool calls remain.
    /// </summary>
    protected async Task<AgentResponse> ProcessWithToolsAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var totalLatencyMs = 0L;

        // Initial LLM call
        var response = await llmClient.CallAsync(systemPrompt, userMessage, ct);
        totalPromptTokens += response.PromptTokens;
        totalCompletionTokens += response.CompletionTokens;
        totalLatencyMs += response.LatencyMs;

        var content = response.Content;
        var iteration = 0;

        // Tool call loop
        while (parser.HasToolCalls(content) && iteration < MaxToolCallIterations)
        {
            iteration++;
            logger.LogDebug("Tool call iteration {Iteration}", iteration);

            var toolCalls = parser.Parse(content);
            var results = await executor.ExecuteAllAsync(toolCalls, UserId, SessionId, ct);

            // Format tool results
            var toolResultsText = FormatToolResults(toolCalls, results);

            // Call LLM again with tool results
            response = await llmClient.CallWithToolResultsAsync(
                systemPrompt,
                userMessage,
                toolResultsText,
                ct);

            totalPromptTokens += response.PromptTokens;
            totalCompletionTokens += response.CompletionTokens;
            totalLatencyMs += response.LatencyMs;
            content = response.Content;
        }

        // Extract final output (remove any remaining tool call tags)
        var finalOutput = parser.RemoveToolCalls(content).Trim();

        return new AgentResponse
        {
            RawContent = content,
            FinalOutput = finalOutput,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            LatencyMs = totalLatencyMs,
            ToolCallIterations = iteration
        };
    }

    private static string FormatToolResults(
        IReadOnlyList<ParsedToolCall> calls,
        IReadOnlyList<ToolCallResult> results)
    {
        var lines = new List<string>();

        for (int i = 0; i < calls.Count && i < results.Count; i++)
        {
            var call = calls[i];
            var result = results[i];

            if (result.IsSuccess)
            {
                lines.Add($"[{call.ToolName}] {result.Data}");
            }
            else
            {
                lines.Add($"[{call.ToolName}] ERROR: {result.ErrorMessage}");
            }
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Response from an agent after processing.
/// </summary>
public sealed record AgentResponse
{
    public string RawContent { get; init; } = "";
    public string FinalOutput { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public long LatencyMs { get; init; }
    public int ToolCallIterations { get; init; }
}
