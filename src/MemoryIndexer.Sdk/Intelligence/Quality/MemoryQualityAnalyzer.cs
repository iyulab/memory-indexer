using System.Numerics.Tensors;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConflictContradictionType = MemoryIndexer.Sdk.Intelligence.Conflict.ContradictionType;

namespace MemoryIndexer.Sdk.Intelligence.Quality;

/// <summary>
/// Analyzes memory quality using multiple metrics.
/// Phase 20.1: Smart Deduplication & Quality Control
/// </summary>
public sealed partial class MemoryQualityAnalyzer : IMemoryQualityService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDeduplicationService? _deduplicationService;
    private readonly IContradictionDetector? _contradictionDetector;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<MemoryQualityAnalyzer> _logger;

    // Quality scoring thresholds
    private const int MinCompleteLength = 20;
    private const int TargetCompleteLength = 100;
    private const int OptimalCompleteLength = 200;

    public MemoryQualityAnalyzer(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IOptions<MemoryIndexerOptions> options,
        ILogger<MemoryQualityAnalyzer> logger,
        IDeduplicationService? deduplicationService = null,
        IContradictionDetector? contradictionDetector = null)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _deduplicationService = deduplicationService;
        _contradictionDetector = contradictionDetector;
        _searchOptions = options.Value.Search;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the quality of a memory unit.
    /// </summary>
    public async Task<QualityMetrics> AnalyzeQualityAsync(
        MemoryUnit memory,
        string userId,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();

        // 1. Uniqueness Score: Check for duplicates
        var uniquenessScore = await CalculateUniquenessScoreAsync(
            memory, userId, issues, cancellationToken);

        // 2. Relevance Score: Semantic similarity to query (if provided)
        var relevanceScore = 0f;
        if (!string.IsNullOrWhiteSpace(query) && memory.Embedding.HasValue)
        {
            relevanceScore = await CalculateRelevanceScoreAsync(
                memory, query, cancellationToken);
        }

        // 3. Completeness Score: Information completeness
        var completenessScore = CalculateCompletenessScore(memory, issues);

        // 4. Consistency Score: Logical consistency
        var consistencyScore = await CalculateConsistencyScoreAsync(
            memory, userId, issues, cancellationToken);

        // 5. Overall Score: Weighted average
        var overallScore = CalculateOverallScore(
            uniquenessScore,
            relevanceScore,
            completenessScore,
            consistencyScore,
            hasQuery: !string.IsNullOrWhiteSpace(query));

        return new QualityMetrics
        {
            MemoryId = memory.Id,
            UniquenessScore = uniquenessScore,
            RelevanceScore = relevanceScore,
            CompletenessScore = completenessScore,
            ConsistencyScore = consistencyScore,
            OverallScore = overallScore,
            Issues = issues.Count > 0 ? issues : null
        };
    }

    /// <summary>
    /// Batch analyzes quality for multiple memories.
    /// </summary>
    public async Task<IReadOnlyList<QualityMetrics>> AnalyzeBatchQualityAsync(
        IReadOnlyList<MemoryUnit> memories,
        string userId,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = memories.Select(m => AnalyzeQualityAsync(m, userId, query, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// Calculates uniqueness score (1 - similarity with most similar memory).
    /// </summary>
    private async Task<float> CalculateUniquenessScoreAsync(
        MemoryUnit memory,
        string userId,
        List<string> issues,
        CancellationToken cancellationToken)
    {
        if (_deduplicationService == null)
        {
            LogDeduplicationServiceAvailableDefaultingUniqueness(_logger);
            return 1.0f;
        }

        if (!memory.Embedding.HasValue)
        {
            issues.Add("No embedding available for uniqueness calculation");
            return 0.5f; // Unknown, assume moderate
        }

        try
        {
            var dupCheck = await _deduplicationService.CheckForDuplicateAsync(
                memory.Content,
                userId,
                similarityThreshold: 0.7f, // Lower threshold to find similar memories
                cancellationToken: cancellationToken);

            if (dupCheck.IsDuplicate)
            {
                var uniqueness = 1.0f - dupCheck.SimilarityScore;
                if (uniqueness < 0.3f)
                {
                    issues.Add($"High similarity ({dupCheck.SimilarityScore:F2}) with existing memory");
                }
                return uniqueness;
            }

            return 1.0f; // Unique
        }
        catch (Exception ex)
        {
            LogFailedCalculateUniquenessScore(_logger, ex);
            return 0.5f;
        }
    }

    /// <summary>
    /// Calculates relevance score based on query similarity.
    /// </summary>
    private async Task<float> CalculateRelevanceScoreAsync(
        MemoryUnit memory,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            var similarity = CalculateCosineSimilarity(memory.Embedding!.Value, queryEmbedding);
            return Math.Clamp(similarity, 0f, 1f);
        }
        catch (Exception ex)
        {
            LogFailedCalculateRelevanceScore(_logger, ex);
            return 0f;
        }
    }

    /// <summary>
    /// Calculates completeness score based on content length and structure.
    /// </summary>
    private static float CalculateCompletenessScore(MemoryUnit memory, List<string> issues)
    {
        var content = memory.Content;
        var length = content.Length;

        // Base score from length
        float lengthScore;
        if (length < MinCompleteLength)
        {
            lengthScore = 0.2f;
            issues.Add($"Content too short ({length} chars)");
        }
        else if (length < TargetCompleteLength)
        {
            lengthScore = 0.2f + (0.6f * (length - MinCompleteLength) / (TargetCompleteLength - MinCompleteLength));
        }
        else if (length < OptimalCompleteLength)
        {
            lengthScore = 0.8f + (0.2f * (length - TargetCompleteLength) / (OptimalCompleteLength - TargetCompleteLength));
        }
        else
        {
            lengthScore = 1.0f;
        }

        // Bonus for sentence structure (periods indicate detailed content)
        var sentenceCount = content.Count(c => c == '.' || c == '!' || c == '?');
        var sentenceBonus = sentenceCount >= 2 ? 0.1f : sentenceCount >= 1 ? 0.05f : 0f;

        // Penalty for excessive repetition
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            var uniqueWords = words.Distinct().Count();
            var repetitionRatio = (float)uniqueWords / words.Length;
            if (repetitionRatio < 0.5f)
            {
                issues.Add("High word repetition detected");
                return Math.Clamp(lengthScore + sentenceBonus - 0.2f, 0f, 1f);
            }
        }

        return Math.Clamp(lengthScore + sentenceBonus, 0f, 1f);
    }

    /// <summary>
    /// Calculates consistency score based on contradiction detection.
    /// Phase 20.3: Integrated full IContradictionDetector API.
    /// </summary>
    private async Task<float> CalculateConsistencyScoreAsync(
        MemoryUnit memory,
        string userId,
        List<string> issues,
        CancellationToken cancellationToken)
    {
        // If contradiction detector not available, use simple heuristic fallback
        if (_contradictionDetector == null)
        {
            LogContradictionDetectorAvailableUsingSimple(_logger);
            return await CalculateSimpleConsistencyAsync(memory, userId, issues, cancellationToken);
        }

        try
        {
            // Get recent memories for contradiction check (limit to avoid performance impact)
            var recentMemories = await _memoryStore.GetAllAsync(
                userId,
                new MemoryFilterOptions
                {
                    Limit = 50,  // Reasonable limit for quality check
                    OrderBy = MemoryOrderBy.CreatedAtDesc
                },
                cancellationToken);

            // Detect contradictions using full detector
            var analysis = await _contradictionDetector.DetectMemoryContradictionAsync(
                memory,
                recentMemories,
                new ContradictionDetectionOptions
                {
                    SimilarityThreshold = 0.7f,
                    MinContradictionConfidence = 0.6f,
                    MaxComparisonItems = 50
                },
                cancellationToken);

            if (analysis.HasContradiction && analysis.ContradictionConfidence >= 0.6f)
            {
                var typeDescription = analysis.Type switch
                {
                    ConflictContradictionType.Factual => "factual contradiction",
                    ConflictContradictionType.Temporal => "temporal contradiction",
                    ConflictContradictionType.Semantic => "semantic contradiction",
                    ConflictContradictionType.Logical => "logical contradiction",
                    ConflictContradictionType.Preference => "preference contradiction",
                    _ => "unknown contradiction"
                };

                issues.Add($"{typeDescription}: {analysis.ConflictDescription} (confidence: {analysis.ContradictionConfidence:F2})");

                // Score based on contradiction severity
                if (analysis.ContradictionConfidence >= 0.9f)
                    return 0.3f;  // High confidence contradiction -> very low consistency
                else if (analysis.ContradictionConfidence >= 0.75f)
                    return 0.5f;  // Medium-high confidence -> low consistency
                else if (analysis.ContradictionConfidence >= 0.6f)
                    return 0.7f;  // Medium confidence -> moderate consistency
                else
                    return 0.9f;  // Low confidence -> mostly consistent
            }

            return 1.0f; // No contradiction detected
        }
        catch (Exception ex)
        {
            LogFailedDetectContradictionsUsingSimple(_logger, ex);
            return await CalculateSimpleConsistencyAsync(memory, userId, issues, cancellationToken);
        }
    }

    /// <summary>
    /// Fallback simple consistency check when IContradictionDetector is unavailable.
    /// </summary>
    private async Task<float> CalculateSimpleConsistencyAsync(
        MemoryUnit memory,
        string userId,
        List<string> issues,
        CancellationToken cancellationToken)
    {
        // Check for ContentType contradictions in metadata
        if (memory.Metadata != null &&
            memory.Metadata.TryGetValue("ContentType", out var contentType) &&
            contentType?.ToString() == "RULED OUT")
        {
            // RULED OUT memories should check for CONFIRMED contradictions
            var recentMemories = await _memoryStore.GetAllAsync(
                userId,
                new MemoryFilterOptions
                {
                    Limit = 20,
                    OrderBy = MemoryOrderBy.CreatedAtDesc
                },
                cancellationToken);

            var confirmedCount = recentMemories.Count(m =>
                m.Id != memory.Id &&
                m.Metadata != null &&
                m.Metadata.TryGetValue("ContentType", out var ct) &&
                ct?.ToString() == "CONFIRMED" &&
                IsSimilarContent(m.Content, memory.Content));

            if (confirmedCount > 0)
            {
                issues.Add($"Potential contradiction: RULED OUT conflicts with {confirmedCount} CONFIRMED memories");
                return 0.7f; // Lower score for potential contradiction
            }
        }

        return 1.0f; // Default to consistent
    }

    /// <summary>
    /// Simple content similarity check (basic keyword overlap).
    /// </summary>
    private static bool IsSimilarContent(string content1, string content2)
    {
        var words1 = new HashSet<string>(
            content1.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '?', '!')),
            StringComparer.OrdinalIgnoreCase);

        var words2 = new HashSet<string>(
            content2.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '?', '!')),
            StringComparer.OrdinalIgnoreCase);

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 && ((float)intersection / union) > 0.3f; // 30% keyword overlap
    }

    /// <summary>
    /// Calculates overall quality score as weighted average.
    /// </summary>
    private static float CalculateOverallScore(
        float uniqueness,
        float relevance,
        float completeness,
        float consistency,
        bool hasQuery)
    {
        if (hasQuery)
        {
            // With query: Uniqueness 25%, Relevance 35%, Completeness 20%, Consistency 20%
            return (0.25f * uniqueness) +
                   (0.35f * relevance) +
                   (0.20f * completeness) +
                   (0.20f * consistency);
        }
        else
        {
            // Without query: Uniqueness 35%, Completeness 30%, Consistency 35%
            return (0.35f * uniqueness) +
                   (0.30f * completeness) +
                   (0.35f * consistency);
        }
    }

    /// <summary>
    /// Calculates cosine similarity between two embeddings.
    /// </summary>
    private static float CalculateCosineSimilarity(
        ReadOnlyMemory<float> embedding1,
        ReadOnlyMemory<float> embedding2)
    {
        var span1 = embedding1.Span;
        var span2 = embedding2.Span;

        if (span1.Length != span2.Length)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(span1, span2);
        var norm1 = TensorPrimitives.Norm(span1);
        var norm2 = TensorPrimitives.Norm(span2);

        if (norm1 == 0 || norm2 == 0)
            return 0f;

        return dotProduct / (norm1 * norm2);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deduplication service not available, defaulting uniqueness to 1.0")]
    private static partial void LogDeduplicationServiceAvailableDefaultingUniqueness(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to calculate uniqueness score")]
    private static partial void LogFailedCalculateUniquenessScore(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to calculate relevance score")]
    private static partial void LogFailedCalculateRelevanceScore(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Contradiction detector not available, using simple heuristic")]
    private static partial void LogContradictionDetectorAvailableUsingSimple(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to detect contradictions, using simple heuristic")]
    private static partial void LogFailedDetectContradictionsUsingSimple(ILogger logger, Exception ex);
}
