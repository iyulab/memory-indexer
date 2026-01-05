using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Graph;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Retrieval;

/// <summary>
/// Tiered memory retrieval implementation using intent-based routing.
/// </summary>
/// <remarks>
/// Based on H-MEM (Hierarchical Memory) and AFM (Adaptive Focus Memory) research.
/// Routes queries to appropriate tiers based on classified intent:
/// - Factual: User → Session → Working (prioritize stable facts)
/// - Contextual: Working → Session → User (prioritize recent context)
/// - Temporal: Session → User → Working (prioritize timestamped data)
/// - Relational: Graph traversal → Session → User (prioritize entity relationships)
/// </remarks>
public sealed class TieredMemoryRetriever : ITieredRetrievalStrategy
{
    private readonly ITieredMemoryStore _store;
    private readonly IQueryIntentClassifier _intentClassifier;
    private readonly IWorkingMemory _workingMemory;
    private readonly IGraphRetriever? _graphRetriever;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<TieredMemoryRetriever> _logger;

    // Token budget allocation weights by intent
    private static readonly Dictionary<QueryIntent, TierWeights> IntentWeights = new()
    {
        [QueryIntent.Factual] = new(Working: 0.15f, Session: 0.25f, User: 0.50f, Graph: 0.10f),
        [QueryIntent.Contextual] = new(Working: 0.50f, Session: 0.30f, User: 0.10f, Graph: 0.10f),
        [QueryIntent.Temporal] = new(Working: 0.15f, Session: 0.50f, User: 0.25f, Graph: 0.10f),
        [QueryIntent.Relational] = new(Working: 0.10f, Session: 0.20f, User: 0.30f, Graph: 0.40f),
        [QueryIntent.General] = new(Working: 0.30f, Session: 0.30f, User: 0.30f, Graph: 0.10f)
    };

    // Tier boost factors for ranking
    private static readonly Dictionary<QueryIntent, Dictionary<MemoryTier, float>> TierBoosts = new()
    {
        [QueryIntent.Factual] = new()
        {
            [MemoryTier.User] = 1.2f,
            [MemoryTier.Session] = 1.0f,
            [MemoryTier.Working] = 0.8f
        },
        [QueryIntent.Contextual] = new()
        {
            [MemoryTier.Working] = 1.3f,
            [MemoryTier.Session] = 1.0f,
            [MemoryTier.User] = 0.7f
        },
        [QueryIntent.Temporal] = new()
        {
            [MemoryTier.Session] = 1.2f,
            [MemoryTier.User] = 1.0f,
            [MemoryTier.Working] = 0.9f
        },
        [QueryIntent.Relational] = new()
        {
            [MemoryTier.Session] = 1.1f,
            [MemoryTier.User] = 1.1f,
            [MemoryTier.Working] = 0.8f
        },
        [QueryIntent.General] = new()
        {
            [MemoryTier.Working] = 1.0f,
            [MemoryTier.Session] = 1.0f,
            [MemoryTier.User] = 1.0f
        }
    };

    public TieredMemoryRetriever(
        ITieredMemoryStore store,
        IQueryIntentClassifier intentClassifier,
        IWorkingMemory workingMemory,
        IEmbeddingService embeddingService,
        ILogger<TieredMemoryRetriever> logger,
        IGraphRetriever? graphRetriever = null)
    {
        _store = store;
        _intentClassifier = intentClassifier;
        _workingMemory = workingMemory;
        _embeddingService = embeddingService;
        _logger = logger;
        _graphRetriever = graphRetriever;
    }

    /// <inheritdoc />
    public async Task<TieredRetrievalResult> RetrieveAsync(
        TieredRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var tierDurations = new Dictionary<MemoryTier, TimeSpan>();
        var tierCandidates = new Dictionary<MemoryTier, int>();
        var tierSelected = new Dictionary<MemoryTier, int>();

        // Step 1: Classify query intent
        var classifyStopwatch = Stopwatch.StartNew();
        var intent = request.PrecomputedIntent ??
            await _intentClassifier.ClassifyAsync(
                request.Query,
                request.ConversationContext,
                cancellationToken);
        classifyStopwatch.Stop();

        _logger.LogDebug(
            "Query '{Query}' classified as {Intent} (confidence: {Confidence:F2})",
            request.Query, intent.Intent, intent.Confidence);

        // Step 2: Estimate budget allocation
        var budget = await EstimateBudgetAsync(
            request.Query,
            request.TokenBudget,
            cancellationToken);

        // Step 3: Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            request.Query,
            cancellationToken);

        // Step 4: Retrieve from each tier in priority order
        var tierResults = new Dictionary<MemoryTier, IReadOnlyList<ScoredMemory>>();
        var allResults = new List<ScoredMemory>();

        foreach (var tier in intent.TierPriority)
        {
            var tierStopwatch = Stopwatch.StartNew();

            var tierMemories = await RetrieveFromTierAsync(
                tier,
                request,
                queryEmbedding,
                intent,
                cancellationToken);

            tierStopwatch.Stop();
            tierDurations[tier] = tierStopwatch.Elapsed;
            tierCandidates[tier] = tierMemories.Count;

            // Apply tier-specific scoring boost
            var boosts = TierBoosts.GetValueOrDefault(intent.Intent, TierBoosts[QueryIntent.General]);
            var tierBoost = boosts.GetValueOrDefault(tier, 1.0f);

            var scoredMemories = tierMemories
                .Select(m => CreateScoredMemory(m, tier, tierBoost, budget))
                .Where(sm => sm.SimilarityScore >= request.MinSimilarity)
                .ToList();

            tierSelected[tier] = scoredMemories.Count;
            tierResults[tier] = scoredMemories;
            allResults.AddRange(scoredMemories);
        }

        // Step 5: Graph retrieval for relational queries
        GraphRetrievalContext? graphContext = null;
        var graphPerformed = false;

        if (intent.Intent == QueryIntent.Relational &&
            request.IncludeGraphContext &&
            _graphRetriever != null)
        {
            graphContext = await RetrieveGraphContextAsync(
                request,
                intent,
                cancellationToken);
            graphPerformed = true;
        }

        // Step 6: Merge and rank all results
        var mergedResults = allResults
            .OrderByDescending(m => m.RelevanceScore)
            .Take(request.MaxResults)
            .ToList();

        // Assign fidelity levels based on ranking
        AssignFidelityLevels(mergedResults, budget);

        totalStopwatch.Stop();

        var statistics = new TieredRetrievalStatistics
        {
            TotalDuration = totalStopwatch.Elapsed,
            ClassificationDuration = classifyStopwatch.Elapsed,
            TierDurations = tierDurations,
            TierCandidateCounts = tierCandidates,
            TierSelectedCounts = tierSelected,
            TotalTokensUsed = mergedResults.Sum(m => m.EstimatedTokens),
            GraphRetrievalPerformed = graphPerformed
        };

        _logger.LogInformation(
            "Tiered retrieval completed: {ResultCount} results from {TierCount} tiers in {Duration}ms",
            mergedResults.Count, tierResults.Count, totalStopwatch.ElapsedMilliseconds);

        return new TieredRetrievalResult
        {
            Query = request.Query,
            Intent = intent,
            TierResults = tierResults,
            MergedResults = mergedResults,
            GraphContext = graphContext,
            BudgetUsed = budget,
            Statistics = statistics
        };
    }

    /// <inheritdoc />
    public Task<TierBudgetAllocation> EstimateBudgetAsync(
        string query,
        int totalBudget,
        CancellationToken cancellationToken = default)
    {
        // Quick intent estimation for budget (or reuse if available)
        var intent = _intentClassifier.ClassifyAsync(query, null, cancellationToken).GetAwaiter().GetResult();
        var weights = IntentWeights.GetValueOrDefault(intent.Intent, IntentWeights[QueryIntent.General]);

        var allocation = new TierBudgetAllocation
        {
            TotalBudget = totalBudget,
            WorkingBudget = (int)(totalBudget * weights.Working),
            SessionBudget = (int)(totalBudget * weights.Session),
            UserBudget = (int)(totalBudget * weights.User),
            GraphBudget = (int)(totalBudget * weights.Graph),
            TierPercentages = new Dictionary<MemoryTier, float>
            {
                [MemoryTier.Working] = weights.Working,
                [MemoryTier.Session] = weights.Session,
                [MemoryTier.User] = weights.User
            }
        };

        return Task.FromResult(allocation);
    }

    private async Task<IReadOnlyList<MemoryUnit>> RetrieveFromTierAsync(
        MemoryTier tier,
        TieredRetrievalRequest request,
        ReadOnlyMemory<float> queryEmbedding,
        QueryIntentResult intent,
        CancellationToken cancellationToken)
    {
        // Working memory uses different interface
        if (tier == MemoryTier.Working)
        {
            var workingMemories = await _workingMemory.GetAllAsync(cancellationToken);
            return workingMemories;
        }

        // Session and User tiers use ITieredMemoryStore
        var options = new MemoryFilterOptions
        {
            SessionId = tier == MemoryTier.Session ? request.SessionId : null,
            Limit = 50
        };

        // Apply temporal filter if intent suggests it
        if (intent.Intent == QueryIntent.Temporal && intent.TemporalReference != null)
        {
            options = ApplyTemporalFilter(options, intent.TemporalReference);
        }

        var memories = await _store.GetByTierAsync(
            request.UserId,
            tier,
            options,
            cancellationToken);

        // Sort by embedding similarity if we have embeddings
        if (queryEmbedding.Length > 0)
        {
            var scored = new List<(MemoryUnit Memory, float Score)>();
            foreach (var memory in memories)
            {
                if (memory.Embedding is { Length: > 0 })
                {
                    var similarity = ComputeCosineSimilarity(queryEmbedding, memory.Embedding.Value);
                    scored.Add((memory, similarity));
                }
                else
                {
                    scored.Add((memory, 0.5f)); // Default score for memories without embeddings
                }
            }

            return scored
                .OrderByDescending(s => s.Score)
                .Take(20) // Limit per tier
                .Select(s => s.Memory)
                .ToList();
        }

        return memories;
    }

    private async Task<GraphRetrievalContext?> RetrieveGraphContextAsync(
        TieredRetrievalRequest request,
        QueryIntentResult intent,
        CancellationToken cancellationToken)
    {
        if (_graphRetriever == null || intent.EntityReferences.Count == 0)
            return null;

        try
        {
            var allFacts = new List<EntityTriple>();
            var allPaths = new List<string[]>();

            foreach (var entity in intent.EntityReferences.Take(3)) // Limit entity exploration
            {
                var facts = await _graphRetriever.GetEntityFactsAsync(
                    entity,
                    new EntityQueryOptions
                    {
                        MaxFacts = 10,
                        UserId = request.UserId
                    },
                    cancellationToken);

                allFacts.AddRange(facts.SubjectFacts);
                allFacts.AddRange(facts.ObjectFacts);
            }

            // Find paths between entities if multiple
            if (intent.EntityReferences.Count >= 2)
            {
                for (int i = 0; i < intent.EntityReferences.Count - 1; i++)
                {
                    var path = await _graphRetriever.FindPathAsync(
                        intent.EntityReferences[i],
                        intent.EntityReferences[i + 1],
                        new GraphTraversalOptions { MaxHops = 3, UserId = request.UserId },
                        cancellationToken);

                    if (path?.PathFound == true)
                    {
                        allPaths.Add([.. path.PathEntities]);
                    }
                }
            }

            return new GraphRetrievalContext
            {
                QueryEntities = intent.EntityReferences.ToList(),
                RelatedFacts = allFacts.Distinct().Take(20).ToList(),
                EntityPaths = allPaths,
                FormattedContext = FormatGraphContext(allFacts, allPaths)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph retrieval failed, continuing without graph context");
            return null;
        }
    }

    private ScoredMemory CreateScoredMemory(
        MemoryUnit memory,
        MemoryTier tier,
        float tierBoost,
        TierBudgetAllocation budget)
    {
        // Calculate similarity from retention score or default
        var similarity = memory.RetentionScore > 0 ? memory.RetentionScore : 0.5f;
        var recencyBoost = CalculateRecencyBoost(memory.CreatedAt);
        var relevance = similarity * tierBoost * recencyBoost;
        var estimatedTokens = EstimateTokens(memory.Content);

        return new ScoredMemory
        {
            Memory = memory,
            SimilarityScore = similarity,
            RelevanceScore = relevance,
            SourceTier = tier,
            EstimatedTokens = estimatedTokens,
            Fidelity = ContextFidelity.Full // Will be adjusted later
        };
    }

    private static void AssignFidelityLevels(
        List<ScoredMemory> results,
        TierBudgetAllocation budget)
    {
        var runningTokens = 0;
        var fullBudget = budget.TotalBudget * 0.6f;  // 60% for full content
        var compressedBudget = budget.TotalBudget * 0.3f; // 30% for compressed

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (runningTokens < fullBudget)
            {
                // Keep as Full
            }
            else if (runningTokens < fullBudget + compressedBudget)
            {
                results[i] = result with { Fidelity = ContextFidelity.Compressed };
            }
            else
            {
                results[i] = result with { Fidelity = ContextFidelity.Placeholder };
            }
            runningTokens += result.EstimatedTokens;
        }
    }

    private static MemoryFilterOptions ApplyTemporalFilter(
        MemoryFilterOptions options,
        string temporalReference)
    {
        var now = DateTime.UtcNow;

        // Parse common temporal references
        var (start, end) = temporalReference.ToLowerInvariant() switch
        {
            "yesterday" => (now.Date.AddDays(-1), now.Date),
            "today" => (now.Date, now),
            "last week" => (now.AddDays(-7), now),
            "last month" => (now.AddMonths(-1), now),
            "recently" or "lately" => (now.AddDays(-3), now),
            var s when s.Contains("days ago") =>
                ParseDaysAgo(s, now),
            var s when s.Contains("first") && s.Contains("session") =>
                (DateTime.MinValue, now.AddMonths(-6)), // Approximate "first session"
            _ => (now.AddDays(-7), now) // Default to last week
        };

        options.CreatedAfter = start;
        options.CreatedBefore = end;
        return options;
    }

    private static (DateTime Start, DateTime End) ParseDaysAgo(string reference, DateTime now)
    {
        // Try to extract number from "X days ago"
        var parts = reference.Split(' ');
        if (int.TryParse(parts[0], out var days))
        {
            return (now.AddDays(-days - 1), now.AddDays(-days + 1));
        }
        return (now.AddDays(-7), now);
    }

    private static float CalculateRecencyBoost(DateTime createdAt)
    {
        var age = DateTime.UtcNow - createdAt;

        return age.TotalHours switch
        {
            < 1 => 1.5f,      // Very recent
            < 24 => 1.3f,     // Today
            < 168 => 1.1f,    // This week
            < 720 => 1.0f,    // This month
            _ => 0.9f         // Older
        };
    }

    private static float ComputeCosineSimilarity(
        ReadOnlyMemory<float> a,
        ReadOnlyMemory<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        var spanA = a.Span;
        var spanB = b.Span;

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < spanA.Length; i++)
        {
            dotProduct += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0f;
    }

    private static int EstimateTokens(string content)
    {
        // Approximate: 1 token ≈ 4 characters
        return (content?.Length ?? 0) / 4;
    }

    private static string FormatGraphContext(
        IEnumerable<EntityTriple> facts,
        IEnumerable<string[]> paths)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("## Knowledge Graph Context");
        sb.AppendLine();

        var factList = facts.Take(10).ToList();
        if (factList.Count > 0)
        {
            sb.AppendLine("### Related Facts:");
            foreach (var fact in factList)
            {
                sb.AppendLine($"- {fact.Subject} {fact.Predicate} {fact.ObjectValue}");
            }
            sb.AppendLine();
        }

        var pathList = paths.Take(3).ToList();
        if (pathList.Count > 0)
        {
            sb.AppendLine("### Entity Relationships:");
            foreach (var path in pathList)
            {
                sb.AppendLine($"- {string.Join(" → ", path)}");
            }
        }

        return sb.ToString();
    }

    private sealed record TierWeights(float Working, float Session, float User, float Graph);
}
