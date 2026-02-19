using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services.ContextStrategies;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for token-budget-aware context building.
/// Phase v0.9.0: Context Budget API.
/// </summary>
[McpServerToolType]
public sealed class ContextTools(IContextBuilder contextBuilder)
{
    private const string DefaultUserId = "default";

    /// <summary>
    /// Build context using a token-budget-aware strategy.
    /// Assembles context from recent conversation, semantic memories, episodic session context, and user facts.
    /// </summary>
    [McpServerTool]
    [Description("Build context using token budget allocation. Combines recent conversation, semantic memories, session context, and user facts based on the selected strategy.")]
    public async Task<ContextBuildResult> ContextBuild(
        [Description("Query for semantic search relevance")] string query,
        [Description("Total token budget for context (default: 2000)")] int totalTokens = 2000,
        [Description("Strategy: Balanced (default), RecentHeavy (games/conversations), SemanticHeavy (RAG/QA)")] string strategy = "Balanced",
        [Description("Session ID for session-scoped context")] string? sessionId = null,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;
        totalTokens = Math.Clamp(totalTokens, 100, 16000);

        var request = new ContextRequest(
            UserId: userId,
            SessionId: sessionId,
            Query: query,
            Budget: new ContextBudget(TotalTokens: totalTokens)
        );

        var bundle = await contextBuilder.BuildAsync(request, strategy, cancellationToken);

        return new ContextBuildResult
        {
            Success = true,
            Content = bundle.Content,
            TotalTokens = bundle.TotalTokens,
            ItemCount = bundle.ItemCount,
            Strategy = strategy,
            Breakdown = new TokenBreakdown
            {
                RecentTokens = bundle.Breakdown.RecentTokens,
                SemanticTokens = bundle.Breakdown.SemanticTokens,
                EpisodicTokens = bundle.Breakdown.EpisodicTokens,
                FactTokens = bundle.Breakdown.FactTokens
            },
            Items = bundle.Items.Select(i => new ContextItemInfo
            {
                Content = i.Content,
                Tokens = i.Tokens,
                Source = i.Source.ToString(),
                Score = i.Score,
                Timestamp = i.Timestamp,
                Role = i.Role
            }).ToList()
        };
    }

    /// <summary>
    /// Get recent conversation turns up to a token limit.
    /// Retrieves from Buffer (T0) and Short-Term (T1) memory in chronological order.
    /// </summary>
    [McpServerTool]
    [Description("Get recent conversation turns up to a token limit. Useful for maintaining conversation continuity in sequential tasks.")]
    public async Task<RecentConversationResult> GetRecentConversation(
        [Description("Maximum tokens to include (default: 1000)")] int maxTokens = 1000,
        [Description("Session ID for session-scoped retrieval")] string sessionId = "default",
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;
        maxTokens = Math.Clamp(maxTokens, 50, 8000);

        var items = await contextBuilder.GetRecentTurnsAsync(userId, sessionId, maxTokens, cancellationToken);

        return new RecentConversationResult
        {
            Success = true,
            TotalTokens = items.Sum(i => i.Tokens),
            TurnCount = items.Count,
            Turns = items.Select(i => new ConversationTurn
            {
                Content = i.Content,
                Role = i.Role ?? "unknown",
                Tokens = i.Tokens,
                Timestamp = i.Timestamp
            }).ToList()
        };
    }

    /// <summary>
    /// Get session-scoped episodic context up to a token limit.
    /// Retrieves episodic memories from a specific session (e.g., game progress, task state).
    /// </summary>
    [McpServerTool]
    [Description("Get session-scoped episodic memories up to a token limit. Useful for retrieving session-specific context like game progress or task state.")]
    public async Task<SessionContextResult> GetSessionContext(
        [Description("Session ID to retrieve context from")] string sessionId,
        [Description("Maximum tokens to include (default: 1000)")] int maxTokens = 1000,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;
        maxTokens = Math.Clamp(maxTokens, 50, 8000);

        var items = await contextBuilder.GetSessionContextAsync(userId, sessionId, maxTokens, cancellationToken);

        return new SessionContextResult
        {
            Success = true,
            SessionId = sessionId,
            TotalTokens = items.Sum(i => i.Tokens),
            ItemCount = items.Count,
            Items = items.Select(i => new SessionContextItem
            {
                Content = i.Content,
                Tokens = i.Tokens,
                Timestamp = i.Timestamp,
                MemoryType = i.MemoryType?.ToString()
            }).ToList()
        };
    }

    /// <summary>
    /// Get user-scoped facts and knowledge up to a token limit.
    /// Retrieves persistent user facts that span across sessions.
    /// </summary>
    [McpServerTool]
    [Description("Get user-scoped facts and knowledge up to a token limit. Retrieves persistent facts about the user that span across sessions.")]
    public async Task<UserFactsResult> GetUserFacts(
        [Description("Maximum tokens to include (default: 500)")] int maxTokens = 500,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;
        maxTokens = Math.Clamp(maxTokens, 50, 4000);

        var items = await contextBuilder.GetUserFactsAsync(userId, maxTokens, cancellationToken);

        return new UserFactsResult
        {
            Success = true,
            TotalTokens = items.Sum(i => i.Tokens),
            FactCount = items.Count,
            Facts = items.Select(i => new UserFactItem
            {
                Content = i.Content,
                Tokens = i.Tokens,
                Importance = i.Score,
                Timestamp = i.Timestamp,
                MemoryType = i.MemoryType?.ToString()
            }).ToList()
        };
    }

    /// <summary>
    /// Get available context strategies with their budget allocations.
    /// </summary>
    [McpServerTool]
    [Description("List available context strategies and their token budget allocations.")]
    public static Task<StrategyListResult> GetContextStrategies()
    {
        var strategies = new List<StrategyInfo>
        {
            new()
            {
                Name = "Balanced",
                Description = "General-purpose: 30% recent, 25% semantic, 25% episodic, 20% facts",
                RecentPercent = 0.30,
                SemanticPercent = 0.25,
                EpisodicPercent = 0.25,
                FactPercent = 0.20
            },
            new()
            {
                Name = "RecentHeavy",
                Description = "For games/conversations: 45% recent, 10% semantic, 35% episodic, 10% facts",
                RecentPercent = 0.45,
                SemanticPercent = 0.10,
                EpisodicPercent = 0.35,
                FactPercent = 0.10
            },
            new()
            {
                Name = "SemanticHeavy",
                Description = "For RAG/QA: 15% recent, 45% semantic, 15% episodic, 25% facts",
                RecentPercent = 0.15,
                SemanticPercent = 0.45,
                EpisodicPercent = 0.15,
                FactPercent = 0.25
            }
        };

        return Task.FromResult(new StrategyListResult
        {
            Success = true,
            Strategies = strategies
        });
    }
}

#region Result Models

/// <summary>
/// Result of context build operation.
/// </summary>
public sealed class ContextBuildResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public int TotalTokens { get; init; }
    public int ItemCount { get; init; }
    public string? Strategy { get; init; }
    public TokenBreakdown? Breakdown { get; init; }
    public List<ContextItemInfo>? Items { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Token breakdown by source.
/// </summary>
public sealed class TokenBreakdown
{
    public int RecentTokens { get; init; }
    public int SemanticTokens { get; init; }
    public int EpisodicTokens { get; init; }
    public int FactTokens { get; init; }
}

/// <summary>
/// Individual context item info.
/// </summary>
public sealed class ContextItemInfo
{
    public string? Content { get; init; }
    public int Tokens { get; init; }
    public string? Source { get; init; }
    public float? Score { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Role { get; init; }
}

/// <summary>
/// Result of recent conversation retrieval.
/// </summary>
public sealed class RecentConversationResult
{
    public bool Success { get; init; }
    public int TotalTokens { get; init; }
    public int TurnCount { get; init; }
    public List<ConversationTurn>? Turns { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// A single conversation turn.
/// </summary>
public sealed class ConversationTurn
{
    public string? Content { get; init; }
    public string? Role { get; init; }
    public int Tokens { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Result of session context retrieval.
/// </summary>
public sealed class SessionContextResult
{
    public bool Success { get; init; }
    public string? SessionId { get; init; }
    public int TotalTokens { get; init; }
    public int ItemCount { get; init; }
    public List<SessionContextItem>? Items { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// A session context item.
/// </summary>
public sealed class SessionContextItem
{
    public string? Content { get; init; }
    public int Tokens { get; init; }
    public DateTime Timestamp { get; init; }
    public string? MemoryType { get; init; }
}

/// <summary>
/// Result of user facts retrieval.
/// </summary>
public sealed class UserFactsResult
{
    public bool Success { get; init; }
    public int TotalTokens { get; init; }
    public int FactCount { get; init; }
    public List<UserFactItem>? Facts { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// A user fact item.
/// </summary>
public sealed class UserFactItem
{
    public string? Content { get; init; }
    public int Tokens { get; init; }
    public float? Importance { get; init; }
    public DateTime Timestamp { get; init; }
    public string? MemoryType { get; init; }
}

/// <summary>
/// Result of strategy list operation.
/// </summary>
public sealed class StrategyListResult
{
    public bool Success { get; init; }
    public List<StrategyInfo>? Strategies { get; init; }
}

/// <summary>
/// Strategy information.
/// </summary>
public sealed class StrategyInfo
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public double RecentPercent { get; init; }
    public double SemanticPercent { get; init; }
    public double EpisodicPercent { get; init; }
    public double FactPercent { get; init; }
}

#endregion
