using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Deduplication;

/// <summary>
/// Service for detecting and handling duplicate memories.
/// Implements semantic deduplication with tiered similarity thresholds.
/// Phase 21.1: Deduplication Target Fix.
/// </summary>
public sealed class DeduplicationService : IDeduplicationService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly ILogger<DeduplicationService> _logger;
    private readonly DeduplicationOptions _options;

    public DeduplicationService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        ILogger<DeduplicationService> logger,
        IOptions<MemoryIndexerOptions> options)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _logger = logger;
        _options = options.Value.Deduplication;
    }

    /// <inheritdoc />
    public async Task<DuplicateCheckResult> CheckForDuplicateAsync(
        string content,
        string userId,
        float? similarityThreshold = null,
        string? contentType = null,
        int? lookbackWindow = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new DuplicateCheckResult
            {
                IsDuplicate = false,
                DuplicateType = DuplicateType.None,
                SimilarityScore = 0f,
                RecommendedAction = DuplicateAction.Add
            };
        }

        var threshold = similarityThreshold ?? _options.DefaultSimilarityThreshold;
        var window = lookbackWindow ?? _options.LookbackWindow;

        _logger.LogDebug(
            "Checking for duplicates: userId={UserId}, threshold={Threshold}, window={Window}",
            userId, threshold, window);

        // Step 1: Get recent memories for comparison (lookback window)
        var recentMemories = await GetRecentMemoriesAsync(userId, window, cancellationToken);

        if (recentMemories.Count == 0)
        {
            _logger.LogDebug("No existing memories found, adding as new");
            return new DuplicateCheckResult
            {
                IsDuplicate = false,
                DuplicateType = DuplicateType.None,
                SimilarityScore = 0f,
                RecommendedAction = DuplicateAction.Add
            };
        }

        // Step 2: Generate embedding for new content
        var newEmbedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        // Step 3: Find most similar memory
        MemoryUnit? mostSimilar = null;
        float highestSimilarity = 0f;
        var similarMemories = new List<MemorySearchResult>();

        foreach (var memory in recentMemories)
        {
            if (!memory.Embedding.HasValue)
                continue;

            var similarity = _scoringService.CalculateCosineSimilarity(newEmbedding, memory.Embedding.Value);

            if (similarity > highestSimilarity)
            {
                highestSimilarity = similarity;
                mostSimilar = memory;
            }

            if (similarity >= threshold)
            {
                similarMemories.Add(new MemorySearchResult
                {
                    Memory = memory,
                    Score = similarity
                });
            }
        }

        // Step 4: Check ContentType-aware rules first
        if (contentType != null && mostSimilar != null)
        {
            var existingContentType = mostSimilar.Metadata?.GetValueOrDefault("ContentType")?.ToString();

            if (existingContentType != null && _options.ContentTypeRules != null)
            {
                var action = GetContentTypeAwareAction(contentType, existingContentType, highestSimilarity);

                if (action.HasValue)
                {
                    _logger.LogDebug(
                        "ContentType-aware rule applied: {NewType} + {ExistingType} = {Action}",
                        contentType, existingContentType, action.Value);

                    return new DuplicateCheckResult
                    {
                        IsDuplicate = action.Value != DuplicateAction.Add,
                        DuplicateType = DuplicateType.Semantic,
                        ExistingMemory = mostSimilar,
                        SimilarityScore = highestSimilarity,
                        RecommendedAction = action.Value,
                        SimilarMemories = similarMemories
                    };
                }
            }
        }

        // Step 5: Apply tiered similarity thresholds
        if (mostSimilar == null || highestSimilarity < _options.LowSimilarityThreshold)
        {
            // No similar memory found
            return new DuplicateCheckResult
            {
                IsDuplicate = false,
                DuplicateType = DuplicateType.None,
                SimilarityScore = highestSimilarity,
                RecommendedAction = DuplicateAction.Add
            };
        }

        var duplicateType = DuplicateType.Semantic;
        DuplicateAction recommendedAction;

        if (highestSimilarity >= _options.ExactDuplicateThreshold)
        {
            // Exact duplicate (>= 0.95): Skip
            duplicateType = DuplicateType.Exact;
            recommendedAction = DuplicateAction.Skip;
            _logger.LogInformation(
                "Exact duplicate found: similarity={Similarity:F3}, skipping",
                highestSimilarity);
        }
        else if (highestSimilarity >= _options.HighSimilarityThreshold)
        {
            // High similarity (0.85-0.94): Merge
            recommendedAction = DuplicateAction.Merge;
            _logger.LogInformation(
                "High similarity duplicate found: similarity={Similarity:F3}, merging",
                highestSimilarity);
        }
        else if (highestSimilarity >= _options.MediumSimilarityThreshold)
        {
            // Medium similarity (0.75-0.84): Update
            recommendedAction = DuplicateAction.Update;
            _logger.LogDebug(
                "Medium similarity found: similarity={Similarity:F3}, updating",
                highestSimilarity);
        }
        else
        {
            // Low similarity (0.65-0.74): AddWithRelation
            recommendedAction = DuplicateAction.AddWithRelation;
            _logger.LogDebug(
                "Low similarity found: similarity={Similarity:F3}, adding with relation",
                highestSimilarity);
        }

        return new DuplicateCheckResult
        {
            IsDuplicate = recommendedAction != DuplicateAction.Add,
            DuplicateType = duplicateType,
            ExistingMemory = mostSimilar,
            SimilarityScore = highestSimilarity,
            RecommendedAction = recommendedAction,
            SimilarMemories = similarMemories
        };
    }

    /// <summary>
    /// Gets recent memories for deduplication comparison.
    /// </summary>
    private async Task<IReadOnlyList<MemoryUnit>> GetRecentMemoriesAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var options = new MemoryFilterOptions
        {
            Limit = limit
        };

        var memories = await _memoryStore.GetAllAsync(
            userId,
            options: options,
            cancellationToken: cancellationToken);

        return memories
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets recommended action based on ContentType-aware rules.
    /// </summary>
    /// <returns>Recommended action, or null if no specific rule applies.</returns>
    private DuplicateAction? GetContentTypeAwareAction(
        string newContentType,
        string existingContentType,
        float similarity)
    {
        if (_options.ContentTypeRules == null)
            return null;

        // Check if there's a rule for this combination
        if (_options.ContentTypeRules.TryGetValue(newContentType, out var rules))
        {
            if (rules.TryGetValue(existingContentType, out var action))
            {
                // Special case: Only apply if similarity is high enough
                // For example, QUESTION + QUESTION should have >= 0.90 similarity
                if (newContentType == "QUESTION" && existingContentType == "QUESTION")
                {
                    return similarity >= 0.90f ? action : null;
                }

                return action;
            }
        }

        return null;
    }
}
