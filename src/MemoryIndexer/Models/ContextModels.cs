namespace MemoryIndexer.Models;

/// <summary>
/// Source type for a context item.
/// </summary>
public enum ContextItemSource
{
    /// <summary>Recent conversation turns from buffer/short-term memory.</summary>
    Recent,

    /// <summary>Semantically relevant memories from long-term storage.</summary>
    Semantic,

    /// <summary>Episodic memories from a specific session.</summary>
    Episodic,

    /// <summary>User-specific facts and knowledge.</summary>
    Fact
}

/// <summary>
/// Configuration for token budget allocation across context sources.
/// </summary>
/// <param name="TotalTokens">Total token budget for the entire context. Default: 2000.</param>
/// <param name="RecentTokens">Token budget for recent conversation. Null = auto-allocate based on strategy.</param>
/// <param name="SemanticTokens">Token budget for semantic memories. Null = auto-allocate based on strategy.</param>
/// <param name="EpisodicTokens">Token budget for episodic memories. Null = auto-allocate based on strategy.</param>
/// <param name="FactTokens">Token budget for user facts. Null = auto-allocate based on strategy.</param>
public record ContextBudget(
    int TotalTokens = 2000,
    int? RecentTokens = null,
    int? SemanticTokens = null,
    int? EpisodicTokens = null,
    int? FactTokens = null
)
{
    /// <summary>Default balanced budget (2000 tokens total).</summary>
    public static ContextBudget Default => new();

    /// <summary>Small budget for constrained contexts (1000 tokens).</summary>
    public static ContextBudget Small => new(TotalTokens: 1000);

    /// <summary>Large budget for extended contexts (4000 tokens).</summary>
    public static ContextBudget Large => new(TotalTokens: 4000);

    /// <summary>
    /// Create a budget with explicit allocation percentages.
    /// </summary>
    public static ContextBudget WithAllocation(
        int totalTokens,
        double recentPercent = 0.4,
        double semanticPercent = 0.4,
        double episodicPercent = 0.0,
        double factPercent = 0.2)
    {
        return new ContextBudget(
            TotalTokens: totalTokens,
            RecentTokens: (int)(totalTokens * recentPercent),
            SemanticTokens: (int)(totalTokens * semanticPercent),
            EpisodicTokens: (int)(totalTokens * episodicPercent),
            FactTokens: (int)(totalTokens * factPercent)
        );
    }
}

/// <summary>
/// Request for building a context bundle.
/// </summary>
/// <param name="UserId">The user ID to build context for.</param>
/// <param name="SessionId">Optional session ID for session-scoped context.</param>
/// <param name="Query">The current query for semantic retrieval.</param>
/// <param name="Budget">Token budget configuration.</param>
public record ContextRequest(
    string UserId,
    string? SessionId,
    string Query,
    ContextBudget Budget
)
{
    /// <summary>
    /// Optional namespace for sub-user memory isolation.
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Create a request with default budget.
    /// </summary>
    public static ContextRequest Create(string userId, string query, string? sessionId = null)
        => new(userId, sessionId, query, ContextBudget.Default);
}

/// <summary>
/// Breakdown of token usage across context sources.
/// </summary>
/// <param name="RecentTokens">Tokens used for recent conversation.</param>
/// <param name="SemanticTokens">Tokens used for semantic memories.</param>
/// <param name="EpisodicTokens">Tokens used for episodic memories.</param>
/// <param name="FactTokens">Tokens used for user facts.</param>
public record ContextBreakdown(
    int RecentTokens,
    int SemanticTokens,
    int EpisodicTokens,
    int FactTokens
)
{
    /// <summary>Total tokens used across all sources.</summary>
    public int Total => RecentTokens + SemanticTokens + EpisodicTokens + FactTokens;

    /// <summary>Empty breakdown with zero tokens.</summary>
    public static ContextBreakdown Empty => new(0, 0, 0, 0);
}

/// <summary>
/// A single item in the context bundle.
/// </summary>
/// <param name="Content">The text content of this item.</param>
/// <param name="Tokens">Number of tokens in this item.</param>
/// <param name="Source">The source type of this item.</param>
/// <param name="Score">Optional relevance score (for semantic items).</param>
/// <param name="Timestamp">When this content was created.</param>
/// <param name="Role">Optional role (user, assistant, system) for conversation items.</param>
/// <param name="MemoryType">Optional memory type for long-term items.</param>
public record ContextItem(
    string Content,
    int Tokens,
    ContextItemSource Source,
    float? Score,
    DateTime Timestamp,
    string? Role = null,
    MemoryType? MemoryType = null
);

/// <summary>
/// The assembled context bundle ready for use in prompts.
/// </summary>
/// <param name="Content">The formatted context string.</param>
/// <param name="TotalTokens">Total tokens in the context.</param>
/// <param name="Breakdown">Token usage breakdown by source.</param>
/// <param name="Items">The individual context items included.</param>
public record ContextBundle(
    string Content,
    int TotalTokens,
    ContextBreakdown Breakdown,
    IReadOnlyList<ContextItem> Items
)
{
    /// <summary>Empty context bundle.</summary>
    public static ContextBundle Empty => new(
        string.Empty,
        0,
        ContextBreakdown.Empty,
        Array.Empty<ContextItem>()
    );

    /// <summary>Number of items in the bundle.</summary>
    public int ItemCount => Items.Count;

    /// <summary>Check if the bundle is empty.</summary>
    public bool IsEmpty => TotalTokens == 0 || Items.Count == 0;
}
