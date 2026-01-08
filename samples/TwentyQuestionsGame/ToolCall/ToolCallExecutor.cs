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

        // Phase 49: Cognitive-aware tier selection
        // - High importance (secrets, rules): Long (episodic)
        // - Regular content (Q&A): Short (working memory, Baddeley 7±2)
        var tier = SelectTierByContent(content, importance);

        try
        {
            await memoryPrimitives.EncodeAsync(new EncodeRequest
            {
                UserId = userId,
                SessionId = sessionId,
                Content = content,
                ImportanceScore = importance,
                Scope = Scope.Session,
                Tier = tier
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

    /// <summary>
    /// Phase 49: Cognitive-aware tier selection based on content and importance.
    /// Aligns with Baddeley's Working Memory model (7±2 items in Short tier).
    /// </summary>
    private static Tier SelectTierByContent(string content, float importance)
    {
        // High importance items → Long (episodic memory, persistent)
        if (importance >= 0.9f)
        {
            return Tier.Long;
        }

        // Important secrets/rules → Long (critical game state)
        if (content.Contains("MY_SECRET", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("GAME_RULES", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("[GAME_RULES]", StringComparison.OrdinalIgnoreCase))
        {
            return Tier.Long;
        }

        // Q&A round data → Short (working memory, 7±2 capacity)
        // This enables cognitive compliance: WorkingMemory(7±2) metric
        if (content.Contains("Round", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Q=", StringComparison.OrdinalIgnoreCase))
        {
            return Tier.Short;
        }

        // Default: Short (working memory) for regular game content
        return Tier.Short;
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
                return ToolCallResult.Success("No relevant memories found.", recallMs);
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

            return ToolCallResult.Success(formatted, recallMs);
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
    public long RecallMs { get; init; }

    public static ToolCallResult Success(string data, long recallMs = 0) => new() { IsSuccess = true, Data = data, RecallMs = recallMs };
    public static ToolCallResult Error(string message) => new() { IsSuccess = false, ErrorMessage = message };
}
