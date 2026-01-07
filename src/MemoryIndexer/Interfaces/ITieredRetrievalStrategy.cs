using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Strategy for retrieving memories across multiple tiers based on query intent.
/// </summary>
/// <remarks>
/// Research basis: H-MEM hierarchical index-based routing, AFM adaptive focus memory.
/// Routes queries to appropriate tiers based on intent classification.
/// </remarks>
public interface ITieredRetrievalStrategy
{
    /// <summary>
    /// Retrieves memories using tiered strategy based on query intent.
    /// </summary>
    /// <param name="request">The retrieval request containing query and options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with memories from prioritized tiers.</returns>
    Task<TieredRetrievalResult> RetrieveAsync(
        TieredRetrievalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates token budget allocation across tiers for a given query.
    /// </summary>
    /// <param name="query">The user query.</param>
    /// <param name="totalBudget">Total token budget available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommended budget allocation per tier.</returns>
    Task<TierBudgetAllocation> EstimateBudgetAsync(
        string query,
        int totalBudget,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for tiered memory retrieval.
/// </summary>
public sealed class TieredRetrievalRequest
{
    /// <summary>
    /// The user query for retrieval.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// The user ID for filtering.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Optional session ID for session-scoped queries.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Optional conversation context for better classification.
    /// </summary>
    public string? ConversationContext { get; init; }

    /// <summary>
    /// Maximum total results to return.
    /// </summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// Total token budget for context assembly.
    /// </summary>
    public int TokenBudget { get; init; } = 4000;

    /// <summary>
    /// Optional pre-computed query intent (skip classification if provided).
    /// </summary>
    public QueryIntentResult? PrecomputedIntent { get; init; }

    /// <summary>
    /// Minimum similarity score threshold.
    /// </summary>
    public float MinSimilarity { get; init; } = 0.5f;

    /// <summary>
    /// Whether to include graph context for relational queries.
    /// </summary>
    public bool IncludeGraphContext { get; init; } = true;
}

/// <summary>
/// Result of tiered memory retrieval.
/// </summary>
public sealed class TieredRetrievalResult
{
    /// <summary>
    /// The original query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// The classified query intent.
    /// </summary>
    public required QueryIntentResult Intent { get; init; }

    /// <summary>
    /// Retrieved memories grouped by tier, in priority order.
    /// </summary>
    public IReadOnlyDictionary<Tier, IReadOnlyList<ScoredMemory>> TierResults { get; init; }
        = new Dictionary<Tier, IReadOnlyList<ScoredMemory>>();

    /// <summary>
    /// All results merged and ranked.
    /// </summary>
    public IReadOnlyList<ScoredMemory> MergedResults { get; init; } = [];

    /// <summary>
    /// Graph context if relational query (entity triples, paths).
    /// </summary>
    public GraphRetrievalContext? GraphContext { get; init; }

    /// <summary>
    /// Budget allocation that was used.
    /// </summary>
    public TierBudgetAllocation BudgetUsed { get; init; } = new();

    /// <summary>
    /// Retrieval statistics.
    /// </summary>
    public TieredRetrievalStatistics Statistics { get; init; } = new();
}

/// <summary>
/// A memory with its retrieval score.
/// </summary>
public sealed record ScoredMemory
{
    /// <summary>
    /// The retrieved memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Similarity score from semantic search.
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Combined relevance score (similarity + tier boost + recency).
    /// </summary>
    public float RelevanceScore { get; init; }

    /// <summary>
    /// The tier this memory was retrieved from.
    /// </summary>
    public Tier SourceTier { get; init; }

    /// <summary>
    /// Estimated token count for this memory.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Fidelity level for context assembly.
    /// </summary>
    public ContextFidelity Fidelity { get; init; } = ContextFidelity.Full;
}

/// <summary>
/// Context fidelity levels for adaptive context assembly.
/// </summary>
/// <remarks>
/// Based on AFM (Adaptive Focus Memory) research:
/// - Full: Complete content for highest priority items
/// - Compressed: Summarized content for secondary items
/// - Placeholder: Minimal reference for low-priority items
/// </remarks>
public enum ContextFidelity
{
    /// <summary>
    /// Full content included (highest priority).
    /// </summary>
    Full,

    /// <summary>
    /// Compressed/summarized content.
    /// </summary>
    Compressed,

    /// <summary>
    /// Minimal placeholder reference.
    /// </summary>
    Placeholder
}

/// <summary>
/// Graph context from relational retrieval.
/// </summary>
public sealed class GraphRetrievalContext
{
    /// <summary>
    /// Extracted entity references from query.
    /// </summary>
    public IReadOnlyList<string> QueryEntities { get; init; } = [];

    /// <summary>
    /// Related entity triples.
    /// </summary>
    public IReadOnlyList<EntityTriple> RelatedFacts { get; init; } = [];

    /// <summary>
    /// Entity paths discovered.
    /// </summary>
    public IReadOnlyList<string[]> EntityPaths { get; init; } = [];

    /// <summary>
    /// Formatted graph context for LLM.
    /// </summary>
    public string FormattedContext { get; init; } = string.Empty;
}

/// <summary>
/// Token budget allocation across memory tiers.
/// </summary>
public sealed class TierBudgetAllocation
{
    /// <summary>
    /// Total budget available.
    /// </summary>
    public int TotalBudget { get; init; }

    /// <summary>
    /// Budget for Short-Term Memory (L1).
    /// </summary>
    public int WorkingBudget { get; init; }

    /// <summary>
    /// Budget for Session Memory (L2).
    /// </summary>
    public int SessionBudget { get; init; }

    /// <summary>
    /// Budget for User Memory (L3).
    /// </summary>
    public int UserBudget { get; init; }

    /// <summary>
    /// Budget for graph context.
    /// </summary>
    public int GraphBudget { get; init; }

    /// <summary>
    /// Percentage breakdown by tier.
    /// </summary>
    public IReadOnlyDictionary<Tier, float> TierPercentages { get; init; }
        = new Dictionary<Tier, float>();
}

/// <summary>
/// Statistics from tiered retrieval.
/// </summary>
public sealed class TieredRetrievalStatistics
{
    /// <summary>
    /// Total retrieval time.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Time spent on classification.
    /// </summary>
    public TimeSpan ClassificationDuration { get; init; }

    /// <summary>
    /// Time spent on each tier's retrieval.
    /// </summary>
    public IReadOnlyDictionary<Tier, TimeSpan> TierDurations { get; init; }
        = new Dictionary<Tier, TimeSpan>();

    /// <summary>
    /// Count of results per tier before filtering.
    /// </summary>
    public IReadOnlyDictionary<Tier, int> TierCandidateCounts { get; init; }
        = new Dictionary<Tier, int>();

    /// <summary>
    /// Count of results per tier after filtering.
    /// </summary>
    public IReadOnlyDictionary<Tier, int> TierSelectedCounts { get; init; }
        = new Dictionary<Tier, int>();

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int TotalTokensUsed { get; init; }

    /// <summary>
    /// Whether graph retrieval was performed.
    /// </summary>
    public bool GraphRetrievalPerformed { get; init; }
}
