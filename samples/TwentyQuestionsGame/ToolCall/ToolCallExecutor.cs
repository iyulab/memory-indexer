using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using TwentyQuestionsGame.Game;

namespace TwentyQuestionsGame.ToolCall;

/// <summary>
/// Executes parsed tool calls against the Memory Indexer SDK.
/// </summary>
public sealed class ToolCallExecutor(IMemoryPrimitives memoryPrimitives)
{
    public async Task<ToolCallResult> ExecuteAsync(
        ParsedToolCall call,
        string userId,
        string sessionId,
        CancellationToken ct = default)
    {
        return call.ToolName.ToLowerInvariant() switch
        {
            "memory_store" => await ExecuteStoreAsync(call, userId, sessionId, ct),
            "memory_recall" => await ExecuteRecallAsync(call, userId, sessionId, ct),
            _ => ToolCallResult.Error($"Unknown tool: {call.ToolName}")
        };
    }

    public async Task<IReadOnlyList<ToolCallResult>> ExecuteAllAsync(
        IEnumerable<ParsedToolCall> calls,
        string userId,
        string sessionId,
        CancellationToken ct = default)
    {
        var results = new List<ToolCallResult>();

        foreach (var call in calls)
        {
            var result = await ExecuteAsync(call, userId, sessionId, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<ToolCallResult> ExecuteStoreAsync(
        ParsedToolCall call,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var content = call.GetArgument("content");
        var importance = call.GetFloatArgument("importance", 0.7f);

        if (string.IsNullOrWhiteSpace(content))
        {
            return ToolCallResult.Error("memory_store requires 'content' argument");
        }

        try
        {
            await memoryPrimitives.EncodeAsync(new EncodeRequest
            {
                UserId = userId,
                SessionId = sessionId,
                Content = content,
                ImportanceScore = importance,
                Scope = Scope.Session,
                Tier = Tier.Long
            }, ct);

            // Output with color based on agent
            if (userId == GameConfiguration.AlphaUserId)
            {
                GameConsole.WriteAlphaMemory("STORED", content);
            }
            else
            {
                GameConsole.WriteBetaMemory("STORED", content);
            }

            return ToolCallResult.Success($"Stored: {content}");
        }
        catch (Exception ex)
        {
            GameConsole.WriteError($"Store failed: {ex.Message}");
            return ToolCallResult.Error($"Store failed: {ex.Message}");
        }
    }

    private async Task<ToolCallResult> ExecuteRecallAsync(
        ParsedToolCall call,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var query = call.GetArgument("query", "*");
        var limit = call.GetIntArgument("limit", 10);

        try
        {
            var sw = Stopwatch.StartNew();

            var results = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
            {
                UserId = userId,
                SessionId = sessionId,
                Query = query,
                Limit = limit,
                MinScore = 0.3f
            }, ct);

            sw.Stop();
            var recallMs = sw.ElapsedMilliseconds;

            if (results.Count == 0)
            {
                if (userId == GameConfiguration.AlphaUserId)
                {
                    GameConsole.WriteAlphaMemory("RECALL", $"No memories found for '{query}' ({recallMs}ms)");
                }
                else
                {
                    GameConsole.WriteBetaMemory("RECALL", $"No memories found for '{query}' ({recallMs}ms)");
                }
                return ToolCallResult.Success("No relevant memories found.");
            }

            // Output all recalled memories with color and timing
            var memories = results.Select(r => (r.Score, r.Memory.Content)).ToList();

            if (userId == GameConfiguration.AlphaUserId)
            {
                GameConsole.WriteAlphaRecall(memories, recallMs);
            }
            else
            {
                GameConsole.WriteBetaRecall(memories, recallMs);
            }

            var formatted = string.Join("\n", results.Select(r =>
                $"[{r.Score:F2}] {r.Memory.Content}"));

            return ToolCallResult.Success(formatted);
        }
        catch (Exception ex)
        {
            GameConsole.WriteError($"Recall failed: {ex.Message}");
            return ToolCallResult.Error($"Recall failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of executing a tool call.
/// </summary>
public sealed record ToolCallResult
{
    public bool IsSuccess { get; init; }
    public string Data { get; init; } = "";
    public string? ErrorMessage { get; init; }

    public static ToolCallResult Success(string data) => new() { IsSuccess = true, Data = data };
    public static ToolCallResult Error(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
