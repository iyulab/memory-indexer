using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.ContextOptimization;

/// <summary>
/// Implements context window optimization strategies.
/// </summary>
public sealed class ContextWindowOptimizer : IContextOptimizer
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<ContextWindowOptimizer> _logger;

    private const float TokensPerWord = 1.3f;

    public ContextWindowOptimizer(
        IEmbeddingService embeddingService,
        IMemoryStore memoryStore,
        ILogger<ContextWindowOptimizer> logger)
    {
        _embeddingService = embeddingService;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<MemoryUnit> LongContextReorder(IEnumerable<MemoryUnit> memories)
    {
        var memoryList = memories.ToList();
        if (memoryList.Count <= 2)
            return memoryList;

        // Sort by importance/relevance
        var sorted = memoryList
            .OrderByDescending(m => m.ImportanceScore)
            .ToList();

        // LongContextReorder: Important at beginning and end
        // Pattern: [most_important, less_important..., second_most_important]
        var result = new List<MemoryUnit>(sorted.Count);

        // Take top items alternating between start and end positions
        var startItems = new List<MemoryUnit>();
        var endItems = new List<MemoryUnit>();

        for (int i = 0; i < sorted.Count; i++)
        {
            if (i % 2 == 0)
                startItems.Add(sorted[i]);
            else
                endItems.Add(sorted[i]);
        }

        // Build final order: start items, then remaining in middle, end items reversed
        result.AddRange(startItems);
        endItems.Reverse();
        result.AddRange(endItems);

        _logger.LogDebug("LongContextReorder applied to {Count} memories", memoryList.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> ApplyMMRAsync(
        IEnumerable<MemoryUnit> memories,
        ReadOnlyMemory<float> queryEmbedding,
        int k = 10,
        float lambda = 0.7f,
        CancellationToken cancellationToken = default)
    {
        var memoryList = memories.Where(m => m.Embedding.HasValue).ToList();
        if (memoryList.Count == 0)
            return [];

        if (memoryList.Count <= k)
            return memoryList;

        var selected = new List<MemoryUnit>(k);
        var remaining = new HashSet<MemoryUnit>(memoryList);

        // Calculate relevance scores to query
        var relevanceScores = memoryList.ToDictionary(
            m => m,
            m => CosineSimilarity(queryEmbedding, m.Embedding!.Value));

        while (selected.Count < k && remaining.Count > 0)
        {
            MemoryUnit? bestCandidate = null;
            float bestScore = float.MinValue;

            foreach (var candidate in remaining)
            {
                // Relevance to query
                var relevance = relevanceScores[candidate];

                // Maximum similarity to already selected
                var maxSimilarityToSelected = selected.Count > 0
                    ? selected.Max(s => CosineSimilarity(candidate.Embedding!.Value, s.Embedding!.Value))
                    : 0f;

                // MMR formula: λ * relevance - (1 - λ) * maxSimilarity
                var mmrScore = lambda * relevance - (1 - lambda) * maxSimilarityToSelected;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate != null)
            {
                selected.Add(bestCandidate);
                remaining.Remove(bestCandidate);
            }
            else
            {
                break;
            }
        }

        _logger.LogDebug("MMR applied: selected {Selected} from {Total} memories (λ={Lambda})",
            selected.Count, memoryList.Count, lambda);

        return selected;
    }

    /// <inheritdoc />
    public async Task<HyDEResult> GenerateHyDEAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        // Generate hypothetical document based on query
        // In production, this would use an LLM to generate a hypothetical answer
        // For now, we expand the query with relevant terms
        var hypotheticalDocument = GenerateHypotheticalDocument(query);

        // Generate embeddings
        var originalEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var hydeEmbedding = await _embeddingService.GenerateEmbeddingAsync(hypotheticalDocument, cancellationToken);

        // Average the embeddings for enhanced retrieval
        var enhancedEmbedding = AverageEmbeddings(originalEmbedding, hydeEmbedding);

        _logger.LogDebug("HyDE generated for query: {Query}", query);

        return new HyDEResult
        {
            OriginalQuery = query,
            HypotheticalDocument = hypotheticalDocument,
            OriginalEmbedding = originalEmbedding,
            EnhancedEmbedding = enhancedEmbedding
        };
    }

    /// <inheritdoc />
    public async Task<ExpandedChunkResult> ExpandChunkContextAsync(
        MemoryUnit childMemory,
        int expandBefore = 1,
        int expandAfter = 1,
        CancellationToken cancellationToken = default)
    {
        var result = new ExpandedChunkResult
        {
            OriginalChunk = childMemory
        };

        // Look for sibling chunks based on metadata
        if (childMemory.Metadata.TryGetValue("chunk_index", out var chunkIndexStr) &&
            int.TryParse(chunkIndexStr?.ToString(), out var chunkIndex) &&
            childMemory.Metadata.TryGetValue("parent_id", out var parentId))
        {
            // Search for sibling chunks
            var searchOptions = new MemorySearchOptions
            {
                UserId = childMemory.UserId,
                Limit = expandBefore + expandAfter + 10
            };

            if (childMemory.Embedding.HasValue)
            {
                var siblings = await _memoryStore.SearchAsync(
                    childMemory.Embedding.Value,
                    searchOptions,
                    cancellationToken);

                foreach (var sibling in siblings.Select(s => s.Memory))
                {
                    if (sibling.Metadata.TryGetValue("parent_id", out var siblingParent) &&
                        siblingParent?.ToString() == parentId?.ToString() &&
                        sibling.Metadata.TryGetValue("chunk_index", out var siblingIndexStr) &&
                        int.TryParse(siblingIndexStr?.ToString(), out var siblingIndex))
                    {
                        var indexDiff = siblingIndex - chunkIndex;
                        if (indexDiff < 0 && indexDiff >= -expandBefore)
                        {
                            result.PrecedingChunks.Add(sibling);
                        }
                        else if (indexDiff > 0 && indexDiff <= expandAfter)
                        {
                            result.FollowingChunks.Add(sibling);
                        }
                    }
                }

                // Sort by chunk index
                result.PrecedingChunks = result.PrecedingChunks
                    .OrderBy(c => int.TryParse(c.Metadata["chunk_index"]?.ToString(), out var i) ? i : 0)
                    .ToList();
                result.FollowingChunks = result.FollowingChunks
                    .OrderBy(c => int.TryParse(c.Metadata["chunk_index"]?.ToString(), out var i) ? i : 0)
                    .ToList();
            }
        }

        _logger.LogDebug("Expanded chunk context: {Before} before, {After} after",
            result.PrecedingChunks.Count, result.FollowingChunks.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<ContextOptimizationResult> OptimizeContextAsync(
        IEnumerable<MemoryUnit> memories,
        ContextOptimizationOptions options,
        CancellationToken cancellationToken = default)
    {
        var memoryList = memories.ToList();
        var result = new ContextOptimizationResult
        {
            OriginalTokenCount = EstimateTokens(memoryList)
        };

        var optimized = memoryList;

        // Apply HyDE if enabled and query provided
        ReadOnlyMemory<float>? queryEmbedding = options.QueryEmbedding;
        if (options.EnableHyDE && !string.IsNullOrEmpty(options.Query))
        {
            var hydeResult = await GenerateHyDEAsync(options.Query, cancellationToken);
            queryEmbedding = hydeResult.EnhancedEmbedding;
            result.OptimizationsApplied.Add("HyDE");
        }

        // Apply MMR if enabled and we have query embedding
        if (options.EnableMMR && queryEmbedding.HasValue)
        {
            var targetCount = Math.Min(optimized.Count, options.TargetTokens / 500); // Rough estimate
            optimized = (await ApplyMMRAsync(
                optimized,
                queryEmbedding.Value,
                k: Math.Max(targetCount, 5),
                lambda: options.MMRLambda,
                cancellationToken)).ToList();
            result.OptimizationsApplied.Add($"MMR(λ={options.MMRLambda})");
        }

        // Check if we're over token budget
        var currentTokens = EstimateTokens(optimized);
        if (currentTokens > options.TargetTokens)
        {
            // Remove lowest importance items until under budget
            optimized = optimized
                .OrderByDescending(m => m.ImportanceScore)
                .ToList();

            var kept = new List<MemoryUnit>();
            var tokens = 0;
            foreach (var memory in optimized)
            {
                var memoryTokens = EstimateTokens(memory.Content);
                if (tokens + memoryTokens <= options.TargetTokens)
                {
                    kept.Add(memory);
                    tokens += memoryTokens;
                }
            }
            result.MemoriesRemoved = optimized.Count - kept.Count;
            optimized = kept;
            result.OptimizationsApplied.Add("TokenBudgetTrimming");
        }

        // Apply LongContextReorder if enabled
        if (options.EnableReordering && optimized.Count > 2)
        {
            optimized = LongContextReorder(optimized).ToList();
            result.OptimizationsApplied.Add("LongContextReorder");
        }

        result.OptimizedMemories = optimized;
        result.FinalTokenCount = EstimateTokens(optimized);
        result.TargetAchieved = result.FinalTokenCount <= options.TargetTokens;

        _logger.LogInformation(
            "Context optimized: {OriginalTokens} → {FinalTokens} tokens, {Optimizations} applied",
            result.OriginalTokenCount, result.FinalTokenCount, result.OptimizationsApplied.Count);

        return result;
    }

    private static string GenerateHypotheticalDocument(string query)
    {
        // Simple heuristic-based hypothetical document generation
        // In production, this would use an LLM
        var expanded = query;

        // Add context-appropriate expansions
        if (query.Contains("how", StringComparison.OrdinalIgnoreCase))
        {
            expanded = $"The process involves the following steps: {query}. First, you need to understand the requirements. Then, implement the solution step by step.";
        }
        else if (query.Contains("what", StringComparison.OrdinalIgnoreCase))
        {
            expanded = $"The answer to '{query}' is: This refers to a concept or entity that can be described as follows.";
        }
        else if (query.Contains("why", StringComparison.OrdinalIgnoreCase))
        {
            expanded = $"The reason for '{query}' is: There are several factors that contribute to this, including important considerations.";
        }
        else if (query.Contains("when", StringComparison.OrdinalIgnoreCase))
        {
            expanded = $"Regarding '{query}': The timing and circumstances are as follows, with specific dates and conditions.";
        }
        else
        {
            expanded = $"Information about '{query}': This topic involves key concepts and details that are relevant to understanding the subject matter.";
        }

        return expanded;
    }

    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        if (spanA.Length != spanB.Length)
            return 0;

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < spanA.Length; i++)
        {
            dotProduct += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static ReadOnlyMemory<float> AverageEmbeddings(
        ReadOnlyMemory<float> a,
        ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;
        var result = new float[spanA.Length];

        for (int i = 0; i < spanA.Length; i++)
        {
            result[i] = (spanA[i] + spanB[i]) / 2;
        }

        // Normalize
        var norm = MathF.Sqrt(result.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < result.Length; i++)
            {
                result[i] /= norm;
            }
        }

        return result;
    }

    private static int EstimateTokens(IEnumerable<MemoryUnit> memories)
    {
        return memories.Sum(m => EstimateTokens(m.Content));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var wordCount = text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(wordCount * TokensPerWord);
    }
}
