using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Intelligence.Retrieval;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for intelligent, context-aware memory retrieval.
/// Uses query intent classification to optimize retrieval strategies.
/// </summary>
[McpServerToolType]
public sealed class AdaptiveRetrievalTools
{
    private readonly MemoryService _memoryService;
    private readonly IQueryIntentClassifier _queryIntentClassifier;
    private readonly TieredMemoryRetriever _tieredRetriever;

    private const string DefaultUserId = "default";

    public AdaptiveRetrievalTools(
        MemoryService memoryService,
        IQueryIntentClassifier queryIntentClassifier,
        TieredMemoryRetriever tieredRetriever)
    {
        _memoryService = memoryService;
        _queryIntentClassifier = queryIntentClassifier;
        _tieredRetriever = tieredRetriever;
    }

    /// <summary>
    /// Classify the intent of a query to determine optimal retrieval strategy.
    /// Use this to understand what type of information the user is looking for.
    /// </summary>
    /// <param name="query">The user query to classify.</param>
    /// <param name="context">Optional conversation context for better classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with intent type and suggested retrieval approach.</returns>
    [McpServerTool]
    [Description("Classify query intent to determine optimal retrieval strategy. Identifies factual, contextual, temporal, or relational queries.")]
    public async Task<ClassifyQueryToolResult> ClassifyQueryIntent(
        [Description("Query to classify")] string query,
        [Description("Optional conversation context")] string? context = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _queryIntentClassifier.ClassifyAsync(query, context, cancellationToken);

        return new ClassifyQueryToolResult
        {
            Success = true,
            Intent = result.Intent.ToString(),
            Confidence = result.Confidence,
            SecondaryIntent = result.SecondaryIntent?.ToString(),
            Specificity = result.Specificity,
            TemporalReference = result.TemporalReference,
            EntityReferences = result.EntityReferences.ToList(),
            Keywords = result.Keywords.ToList(),
            SuggestedTierPriority = result.TierPriority.Select(t => t.ToString()).ToList(),
            RetrievalAdvice = GetRetrievalAdvice(result.Intent),
            Message = $"Query classified as {result.Intent} with {result.Confidence:P0} confidence."
        };
    }

    /// <summary>
    /// Perform adaptive retrieval that automatically selects the best strategy based on query intent.
    /// Combines intent classification with tiered retrieval for optimal results.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="context">Optional conversation context.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <param name="sessionId">Optional session ID for scoped retrieval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Adaptive retrieval results with strategy explanation.</returns>
    [McpServerTool]
    [Description("Smart retrieval that auto-selects strategy based on query intent. Best for general queries where you want optimal results without manual tuning.")]
    public async Task<AdaptiveRetrievalToolResult> AdaptiveRecall(
        [Description("Search query")] string query,
        [Description("Optional conversation context")] string? context = null,
        [Description("Maximum results (1-50)")] int maxResults = 10,
        [Description("Optional session ID")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new TieredRetrievalRequest
        {
            Query = query,
            UserId = DefaultUserId,
            SessionId = sessionId,
            ConversationContext = context,
            MaxResults = Math.Clamp(maxResults, 1, 50),
            MinSimilarity = 0.5f,
            IncludeGraphContext = true
        };

        var result = await _tieredRetriever.RetrieveAsync(request, cancellationToken);

        // Group by tier for structured output
        var byTier = result.TierResults
            .ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.Count);

        return new AdaptiveRetrievalToolResult
        {
            Success = true,
            Count = result.MergedResults.Count,
            DetectedIntent = result.Intent.Intent.ToString(),
            IntentConfidence = result.Intent.Confidence,
            AppliedStrategy = GetStrategyDescription(result.Intent.Intent),
            TiersSearched = result.Intent.TierPriority.Select(t => t.ToString()).ToList(),
            ResultsPerTier = byTier,
            Memories = result.MergedResults.Select(m => new AdaptiveMemoryItem
            {
                Id = m.Memory.Id.ToString(),
                Content = m.Memory.Content,
                Type = m.Memory.Type.ToString().ToLowerInvariant(),
                Tier = m.SourceTier.ToString().ToLowerInvariant(),
                SimilarityScore = m.SimilarityScore,
                RelevanceScore = m.RelevanceScore,
                Importance = m.Memory.ImportanceScore,
                CreatedAt = m.Memory.CreatedAt
            }).ToList(),
            Message = $"Found {result.MergedResults.Count} memories using {result.Intent.Intent} strategy."
        };
    }

    /// <summary>
    /// Retrieve memories from specific tiers with custom priority order.
    /// Use when you know exactly which tiers to search and in what order.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="tierPriority">Comma-separated tier priority (e.g., "Archive,Long,Short,Buffer").</param>
    /// <param name="maxResults">Maximum total results.</param>
    /// <param name="sessionId">Optional session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tiered retrieval results.</returns>
    [McpServerTool]
    [Description("Retrieve from specific tiers with custom priority. Use for fine-grained control over retrieval sources.")]
    public async Task<TieredRetrievalToolResult> TieredRecall(
        [Description("Search query")] string query,
        [Description("Tier priority (comma-separated): Archive, Long, Short, Buffer")] string tierPriority = "Archive,Long,Short",
        [Description("Max total results (1-50)")] int maxResults = 10,
        [Description("Optional session ID")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var tiers = ParseTierPriority(tierPriority);
        if (tiers.Count == 0)
        {
            return new TieredRetrievalToolResult
            {
                Success = false,
                Message = "Invalid tier priority. Valid tiers: Archive, Long, Short, Buffer"
            };
        }

        // Create a pre-computed intent with custom tier priority
        var precomputedIntent = new QueryIntentResult
        {
            Intent = QueryIntent.General,
            Confidence = 1.0f,
            Specificity = 0.5f,
            TierPriority = tiers
        };

        var request = new TieredRetrievalRequest
        {
            Query = query,
            UserId = DefaultUserId,
            SessionId = sessionId,
            MaxResults = Math.Clamp(maxResults, 1, 50),
            PrecomputedIntent = precomputedIntent,
            MinSimilarity = 0.5f,
            IncludeGraphContext = false
        };

        var result = await _tieredRetriever.RetrieveAsync(request, cancellationToken);

        // Group by tier for structured output
        var byTier = result.TierResults
            .ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.Count);

        return new TieredRetrievalToolResult
        {
            Success = true,
            Count = result.MergedResults.Count,
            TiersSearched = tiers.Select(t => t.ToString()).ToList(),
            ResultsPerTier = byTier,
            Memories = result.MergedResults.Select(m => new TieredMemoryItem
            {
                Id = m.Memory.Id.ToString(),
                Content = m.Memory.Content,
                Type = m.Memory.Type.ToString().ToLowerInvariant(),
                Tier = m.SourceTier.ToString().ToLowerInvariant(),
                SimilarityScore = m.SimilarityScore,
                RelevanceScore = m.RelevanceScore,
                Importance = m.Memory.ImportanceScore,
                CreatedAt = m.Memory.CreatedAt
            }).ToList(),
            Message = $"Found {result.MergedResults.Count} memories across {tiers.Count} tiers."
        };
    }

    /// <summary>
    /// Get recommendations for how to retrieve specific types of information.
    /// Use this as a guide for choosing the right retrieval strategy.
    /// </summary>
    /// <param name="informationType">Type of information needed (facts, context, history, relationships).</param>
    /// <returns>Retrieval strategy recommendations.</returns>
    [McpServerTool]
    [Description("Get recommendations for retrieving specific information types. Helps choose the right strategy.")]
    public Task<RetrievalRecommendationToolResult> GetRetrievalRecommendation(
        [Description("Information type: facts, context, history, relationships, all")] string informationType = "all")
    {
        var recommendation = informationType.ToLowerInvariant() switch
        {
            "facts" => new RetrievalRecommendation
            {
                InformationType = "Facts",
                RecommendedIntent = "Factual",
                SuggestedTiers = ["Archive", "Long"],
                Description = "For factual information like preferences, attributes, and verified knowledge",
                ExampleQueries = ["What is my email?", "What's my favorite color?", "Tell me my account settings"],
                ToolRecommendation = "Use AdaptiveRecall or TieredRecall with Archive,Long priority"
            },
            "context" => new RetrievalRecommendation
            {
                InformationType = "Context",
                RecommendedIntent = "Contextual",
                SuggestedTiers = ["Short", "Buffer", "Long"],
                Description = "For recent conversation context and continuation",
                ExampleQueries = ["Tell me more about that", "Continue from before", "What were we discussing?"],
                ToolRecommendation = "Use AdaptiveRecall with context parameter"
            },
            "history" => new RetrievalRecommendation
            {
                InformationType = "History",
                RecommendedIntent = "Temporal",
                SuggestedTiers = ["Long", "Archive"],
                Description = "For time-based queries about past events and conversations",
                ExampleQueries = ["What did we discuss yesterday?", "What happened last week?", "Show me previous sessions"],
                ToolRecommendation = "Use AdaptiveRecall - temporal references will be auto-detected"
            },
            "relationships" => new RetrievalRecommendation
            {
                InformationType = "Relationships",
                RecommendedIntent = "Relational",
                SuggestedTiers = ["Archive", "Long"],
                Description = "For finding connections between entities and concepts",
                ExampleQueries = ["What's related to X?", "How is A connected to B?", "What else do I know about this?"],
                ToolRecommendation = "Use AdaptiveRecall or combine with knowledge graph tools"
            },
            _ => new RetrievalRecommendation
            {
                InformationType = "All",
                RecommendedIntent = "General",
                SuggestedTiers = ["Archive", "Long", "Short", "Buffer"],
                Description = "For general queries that may span multiple information types",
                ExampleQueries = ["Any query"],
                ToolRecommendation = "Use AdaptiveRecall which auto-detects the best strategy"
            }
        };

        return Task.FromResult(new RetrievalRecommendationToolResult
        {
            Success = true,
            Recommendation = recommendation,
            AllIntentTypes = Enum.GetNames<QueryIntent>().ToList(),
            AllTiers = Enum.GetNames<Tier>().ToList(),
            Message = $"Recommendation for {recommendation.InformationType} retrieval"
        });
    }

    #region Private Helpers

    private static string GetRetrievalAdvice(QueryIntent intent) => intent switch
    {
        QueryIntent.Factual => "Prioritize Archive and Long tiers for verified facts. Use higher semantic similarity threshold.",
        QueryIntent.Contextual => "Prioritize Short and Buffer for recent context. Include conversation history if available.",
        QueryIntent.Temporal => "Apply time-based filtering. Check Session memories with temporal markers.",
        QueryIntent.Relational => "Consider using knowledge graph tools for entity relationships. Expand search to related concepts.",
        QueryIntent.General => "Use balanced multi-tier retrieval. Consider hybrid search for best coverage.",
        _ => "Use default retrieval strategy."
    };

    private static string GetStrategyDescription(QueryIntent intent) => intent switch
    {
        QueryIntent.Factual => "Fact-optimized: Archive -> Long -> High similarity threshold",
        QueryIntent.Contextual => "Context-optimized: Short -> Buffer -> Recent first",
        QueryIntent.Temporal => "Time-optimized: Session -> Long -> With temporal filter",
        QueryIntent.Relational => "Relationship-optimized: Archive -> Long -> Entity expansion",
        QueryIntent.General => "Balanced: All tiers with recency weighting",
        _ => "Default retrieval"
    };

    private static List<Tier> ParseTierPriority(string tierPriority)
    {
        var tiers = new List<Tier>();
        foreach (var tierName in tierPriority.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<Tier>(tierName, true, out var tier))
            {
                tiers.Add(tier);
            }
        }
        return tiers;
    }

    #endregion
}

#region Result Types

/// <summary>
/// Result of query intent classification.
/// </summary>
public sealed class ClassifyQueryToolResult
{
    public bool Success { get; init; }
    public string? Intent { get; init; }
    public float Confidence { get; init; }
    public string? SecondaryIntent { get; init; }
    public float Specificity { get; init; }
    public string? TemporalReference { get; init; }
    public List<string>? EntityReferences { get; init; }
    public List<string>? Keywords { get; init; }
    public List<string>? SuggestedTierPriority { get; init; }
    public string? RetrievalAdvice { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of adaptive retrieval.
/// </summary>
public sealed class AdaptiveRetrievalToolResult
{
    public bool Success { get; init; }
    public int Count { get; init; }
    public string? DetectedIntent { get; init; }
    public float IntentConfidence { get; init; }
    public string? AppliedStrategy { get; init; }
    public List<string>? TiersSearched { get; init; }
    public Dictionary<string, int>? ResultsPerTier { get; init; }
    public List<AdaptiveMemoryItem>? Memories { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Memory item in adaptive retrieval results.
/// </summary>
public sealed class AdaptiveMemoryItem
{
    public string? Id { get; init; }
    public string? Content { get; init; }
    public string? Type { get; init; }
    public string? Tier { get; init; }
    public float SimilarityScore { get; init; }
    public float RelevanceScore { get; init; }
    public float Importance { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Result of tiered retrieval.
/// </summary>
public sealed class TieredRetrievalToolResult
{
    public bool Success { get; init; }
    public int Count { get; init; }
    public List<string>? TiersSearched { get; init; }
    public Dictionary<string, int>? ResultsPerTier { get; init; }
    public List<TieredMemoryItem>? Memories { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Memory item in tiered retrieval results.
/// </summary>
public sealed class TieredMemoryItem
{
    public string? Id { get; init; }
    public string? Content { get; init; }
    public string? Type { get; init; }
    public string? Tier { get; init; }
    public float SimilarityScore { get; init; }
    public float RelevanceScore { get; init; }
    public float Importance { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Result of retrieval recommendation.
/// </summary>
public sealed class RetrievalRecommendationToolResult
{
    public bool Success { get; init; }
    public RetrievalRecommendation? Recommendation { get; init; }
    public List<string>? AllIntentTypes { get; init; }
    public List<string>? AllTiers { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Retrieval strategy recommendation.
/// </summary>
public sealed class RetrievalRecommendation
{
    public string? InformationType { get; init; }
    public string? RecommendedIntent { get; init; }
    public List<string>? SuggestedTiers { get; init; }
    public string? Description { get; init; }
    public List<string>? ExampleQueries { get; init; }
    public string? ToolRecommendation { get; init; }
}

#endregion
