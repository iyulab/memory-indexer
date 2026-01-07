using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Classifies query intent to determine optimal retrieval strategy.
/// </summary>
/// <remarks>
/// Research reference: Intent detection for adaptive retrieval routing.
/// Query intent determines which memory tiers to prioritize.
/// </remarks>
public interface IQueryIntentClassifier
{
    /// <summary>
    /// Classifies the intent of a query.
    /// </summary>
    /// <param name="query">The user query.</param>
    /// <param name="context">Optional conversation context for better classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with intent type and confidence.</returns>
    Task<QueryIntentResult> ClassifyAsync(
        string query,
        string? context = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query intent types for retrieval routing.
/// </summary>
/// <remarks>
/// Based on research patterns:
/// - Factual: Direct fact lookup (prioritize User Profile)
/// - Contextual: Continuation/elaboration (prioritize Working + Session)
/// - Temporal: Time-based queries (prioritize Session with time filters)
/// - Relational: Entity relationship queries (prioritize Graph traversal)
/// </remarks>
public enum QueryIntent
{
    /// <summary>
    /// Direct fact lookup: "What is my favorite color?"
    /// Prioritizes: User Profile → Session facts
    /// </summary>
    Factual,

    /// <summary>
    /// Context continuation: "Tell me more about that"
    /// Prioritizes: Short-Term Memory → Recent buffer
    /// </summary>
    Contextual,

    /// <summary>
    /// Time-based recall: "What did we discuss last week?"
    /// Prioritizes: Session with temporal filters
    /// </summary>
    Temporal,

    /// <summary>
    /// Relationship queries: "What's related to X?"
    /// Prioritizes: Graph traversal → Entity relationships
    /// </summary>
    Relational,

    /// <summary>
    /// Unknown or general queries.
    /// Uses balanced multi-tier retrieval.
    /// </summary>
    General
}

/// <summary>
/// Result of query intent classification.
/// </summary>
public sealed class QueryIntentResult
{
    /// <summary>
    /// Primary classified intent.
    /// </summary>
    public required QueryIntent Intent { get; init; }

    /// <summary>
    /// Confidence score for the classification (0.0 to 1.0).
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// Secondary intent if query is ambiguous.
    /// </summary>
    public QueryIntent? SecondaryIntent { get; init; }

    /// <summary>
    /// Query specificity score (0.0 to 1.0).
    /// Higher values indicate more specific queries that should prioritize semantic relevance over importance.
    /// </summary>
    public required float Specificity { get; init; }

    /// <summary>
    /// Extracted temporal reference if present (e.g., "last week", "yesterday").
    /// </summary>
    public string? TemporalReference { get; init; }

    /// <summary>
    /// Extracted entity references if present.
    /// </summary>
    public IReadOnlyList<string> EntityReferences { get; init; } = [];

    /// <summary>
    /// Keywords extracted from the query for retrieval boosting.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// Suggested tier priority order for retrieval.
    /// </summary>
    public IReadOnlyList<Tier> TierPriority { get; init; } = [];
}
