using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Utilities;
using MemoryIndexer.Intelligence.Conflict;
using MemoryIndexer.Intelligence.Scoring;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Operations;

/// <summary>
/// Semantic-based operation decider that analyzes content using embeddings and scoring.
/// Implements intelligent memory management without requiring external LLM calls.
/// </summary>
/// <remarks>
/// This implementation uses a hybrid approach:
/// - Embedding similarity for duplicate/merge detection
/// - Importance analysis for value assessment
/// - Pattern matching for contradiction detection
/// - Topic extraction for categorization
///
/// For full LLM-based decisions, this service can be extended or replaced
/// with an implementation that calls an LLM API for complex reasoning.
/// </remarks>
public sealed class SemanticOperationDecider : IMemoryOperationDecider
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IContradictionDetector _contradictionDetector;
    private readonly ImportanceAnalyzer _importanceAnalyzer;
    private readonly ILogger<SemanticOperationDecider> _logger;

    public SemanticOperationDecider(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IContradictionDetector contradictionDetector,
        ImportanceAnalyzer importanceAnalyzer,
        ILogger<SemanticOperationDecider> logger)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _contradictionDetector = contradictionDetector;
        _importanceAnalyzer = importanceAnalyzer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationDecision> DecideAsync(
        string content,
        string userId,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DecisionOptions();

        _logger.LogDebug("Evaluating content for operation decision: {ContentLength} chars", content.Length);

        // Step 1: Analyze content importance and extract topics
        var importanceScore = _importanceAnalyzer.AnalyzeImportance(content);
        var topics = ExtractTopicsSimple(content);
        var detectedType = options.PreferredType ?? DetectMemoryType(content);

        // Step 2: Check if content meets minimum importance threshold
        if (importanceScore < options.MinimumImportance)
        {
            _logger.LogDebug("Content below importance threshold: {Score} < {Threshold}",
                importanceScore, options.MinimumImportance);

            return CreateDecision(
                MemoryOperation.Noop,
                0.9f,
                $"Content importance ({importanceScore:F2}) below threshold ({options.MinimumImportance:F2})",
                content,
                importanceScore,
                detectedType,
                topics);
        }

        // Step 3: Generate embedding for similarity search
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        // Step 4: Find similar existing memories
        var searchOptions = new MemorySearchOptions
        {
            UserId = userId,
            SessionId = options.SessionId,
            Limit = options.MaxComparisons
        };
        var searchResults = await _memoryStore.SearchAsync(embedding, searchOptions, cancellationToken);
        var similarMemories = searchResults.Select(r => r.Memory).ToList();

        // Step 5: Analyze similarity and determine operation
        return await DetermineOperationAsync(
            content,
            embedding,
            similarMemories,
            importanceScore,
            detectedType,
            topics,
            options,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationDecision>> DecideBatchAsync(
        IReadOnlyList<string> contents,
        string userId,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var decisions = new List<OperationDecision>(contents.Count);

        foreach (var content in contents)
        {
            var decision = await DecideAsync(content, userId, options, cancellationToken);
            decisions.Add(decision);
        }

        return decisions;
    }

    private async Task<OperationDecision> DetermineOperationAsync(
        string content,
        ReadOnlyMemory<float> embedding,
        IReadOnlyList<MemoryUnit> similarMemories,
        float importanceScore,
        MemoryType detectedType,
        IReadOnlyList<string> topics,
        DecisionOptions options,
        CancellationToken cancellationToken)
    {
        // No similar memories found - ADD
        if (similarMemories.Count == 0)
        {
            return CreateDecision(
                MemoryOperation.Add,
                0.95f,
                "No similar memories found; content is novel",
                content,
                importanceScore,
                detectedType,
                topics);
        }

        // Calculate similarity scores
        var scoredMemories = CalculateSimilarities(embedding, similarMemories);

        // Check for exact or near-duplicate
        var duplicates = scoredMemories.Where(m => m.Score >= options.DuplicateThreshold).ToList();
        if (duplicates.Count > 0)
        {
            var topDuplicate = duplicates[0];

            // Check if new content has additional information
            if (HasAdditionalInformation(content, topDuplicate.Memory.Content))
            {
                return CreateDecision(
                    MemoryOperation.Update,
                    topDuplicate.Score,
                    $"Similar memory found (similarity: {topDuplicate.Score:F2}), new content has additional information",
                    content,
                    importanceScore,
                    detectedType,
                    topics,
                    topDuplicate.Memory,
                    suggestedContent: MergeContent(topDuplicate.Memory.Content, content));
            }

            return CreateDecision(
                MemoryOperation.Noop,
                topDuplicate.Score,
                $"Duplicate memory found (similarity: {topDuplicate.Score:F2})",
                content,
                importanceScore,
                detectedType,
                topics,
                topDuplicate.Memory);
        }

        // Check for related memories that could be merged
        var relatedMemories = scoredMemories
            .Where(m => m.Score >= options.RelatedThreshold && m.Score < options.DuplicateThreshold)
            .ToList();

        // Check for contradictions
        if (options.DetectContradictions && relatedMemories.Count > 0)
        {
            var tempMemory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                Embedding = embedding,
                Type = detectedType,
                Topics = topics.ToList()
            };

            // Check for contradictions against related memories
            var relatedMemoriesList = relatedMemories.Select(m => m.Memory).ToList();
            var analysis = await _contradictionDetector.DetectMemoryContradictionAsync(
                tempMemory, relatedMemoriesList, null, cancellationToken);

            if (analysis.HasContradiction)
            {
                var conflictingMemory = analysis.ConflictingItem;
                if (conflictingMemory != null)
                {
                    // If new content is more recent/important, recommend Replace
                    if (importanceScore > conflictingMemory.ImportanceScore)
                    {
                        return CreateDecision(
                            MemoryOperation.Replace,
                            0.8f,
                            "Contradiction detected: new content supersedes existing memory",
                            content,
                            importanceScore,
                            detectedType,
                            topics,
                            conflictingMemory,
                            contradictionDetected: true,
                            contradictionDetails: analysis.ConflictDescription);
                    }
                    else
                    {
                        // Existing memory is more reliable
                        return CreateDecision(
                            MemoryOperation.Noop,
                            0.7f,
                            "Contradiction detected: existing memory is more reliable",
                            content,
                            importanceScore,
                            detectedType,
                            topics,
                            conflictingMemory,
                            contradictionDetected: true,
                            contradictionDetails: analysis.ConflictDescription);
                    }
                }
            }
        }

        // Check if merge would be beneficial
        if (relatedMemories.Count >= 2)
        {
            var mergeTargets = relatedMemories.Take(3).Select(m => m.Memory).ToList();
            var mergedContent = GenerateMergedContent(content, mergeTargets);

            return CreateDecision(
                MemoryOperation.Merge,
                0.75f,
                $"Found {relatedMemories.Count} related memories that could be merged",
                content,
                importanceScore,
                detectedType,
                topics,
                relatedMemories[0].Memory,
                mergeTargets,
                mergedContent);
        }

        // Single related memory - decide between Update or Add
        if (relatedMemories.Count == 1)
        {
            var related = relatedMemories[0];

            if (importanceScore > related.Memory.ImportanceScore)
            {
                return CreateDecision(
                    MemoryOperation.Update,
                    related.Score,
                    "Related memory found; new content is more important",
                    content,
                    importanceScore,
                    detectedType,
                    topics,
                    related.Memory,
                    suggestedContent: MergeContent(related.Memory.Content, content));
            }

            // Different enough to warrant a new memory
            return CreateDecision(
                MemoryOperation.Add,
                0.85f,
                "Related memory exists but content is sufficiently distinct",
                content,
                importanceScore,
                detectedType,
                topics);
        }

        // Default: Add as new memory
        return CreateDecision(
            MemoryOperation.Add,
            0.9f,
            "Content is novel and valuable",
            content,
            importanceScore,
            detectedType,
            topics);
    }

    private static List<(MemoryUnit Memory, float Score)> CalculateSimilarities(
        ReadOnlyMemory<float> embedding,
        IReadOnlyList<MemoryUnit> memories)
    {
        return memories
            .Where(m => m.Embedding.HasValue && m.Embedding.Value.Length > 0)
            .Select(m =>
            {
                var score = VectorMath.CosineSimilarity(embedding.Span, m.Embedding!.Value.Span);
                return (Memory: m, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static bool HasAdditionalInformation(string newContent, string existingContent)
    {
        // Simple heuristic: new content is significantly longer
        // or contains words not in existing content
        if (newContent.Length > existingContent.Length * 1.2)
        {
            return true;
        }

        var existingWords = new HashSet<string>(
            existingContent.Split([' ', '.', ',', '!', '?'], StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        var newWords = newContent.Split([' ', '.', ',', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        var novelWords = newWords.Count(w => !existingWords.Contains(w));
        return novelWords > newWords.Length * 0.3; // >30% novel words
    }

    private static string MergeContent(string existing, string newContent)
    {
        // Simple merge: combine with separator
        return $"{existing}\n\n[Updated]: {newContent}";
    }

    private static string GenerateMergedContent(string newContent, IReadOnlyList<MemoryUnit> relatedMemories)
    {
        var parts = new List<string> { newContent };
        parts.AddRange(relatedMemories.Select(m => m.Content));

        return $"[Consolidated from {parts.Count} memories]: {string.Join(" | ", parts.Select(p => TruncateContent(p, 100)))}";
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
        return content[..maxLength] + "...";
    }

    private static List<string> ExtractTopicsSimple(string content)
    {
        // Simple keyword-based topic extraction
        var topics = new List<string>();
        var lowerContent = content.ToLowerInvariant();

        var topicKeywords = new Dictionary<string, string[]>
        {
            ["authentication"] = ["auth", "login", "password", "credential", "token", "jwt", "oauth"],
            ["database"] = ["database", "sql", "query", "table", "schema", "migration"],
            ["api"] = ["api", "endpoint", "rest", "graphql", "http", "request", "response"],
            ["security"] = ["security", "encrypt", "decrypt", "hash", "vulnerability", "attack"],
            ["performance"] = ["performance", "optimize", "cache", "latency", "throughput", "benchmark"],
            ["error"] = ["error", "exception", "bug", "fix", "issue", "problem", "crash"],
            ["configuration"] = ["config", "setting", "option", "parameter", "environment"],
            ["testing"] = ["test", "unit", "integration", "mock", "assert", "coverage"],
            ["deployment"] = ["deploy", "release", "pipeline", "ci/cd", "docker", "kubernetes"]
        };

        foreach (var (topic, keywords) in topicKeywords)
        {
            if (keywords.Any(k => lowerContent.Contains(k)))
            {
                topics.Add(topic);
            }
        }

        return topics;
    }

    private static MemoryType DetectMemoryType(string content)
    {
        var lowerContent = content.ToLowerInvariant();

        // Procedural: describes how to do something
        if (lowerContent.Contains("how to") ||
            lowerContent.Contains("step ") ||
            lowerContent.Contains("process") ||
            lowerContent.Contains("procedure") ||
            lowerContent.Contains("workflow"))
        {
            return MemoryType.Procedural;
        }

        // Fact: specific verifiable information
        if (lowerContent.Contains(" is ") ||
            lowerContent.Contains(" are ") ||
            lowerContent.Contains("defined as") ||
            lowerContent.Contains("equals"))
        {
            return MemoryType.Fact;
        }

        // Semantic: general knowledge or preferences
        if (lowerContent.Contains("prefer") ||
            lowerContent.Contains("like") ||
            lowerContent.Contains("dislike") ||
            lowerContent.Contains("usually") ||
            lowerContent.Contains("always"))
        {
            return MemoryType.Semantic;
        }

        // Default to Episodic
        return MemoryType.Episodic;
    }

    private static OperationDecision CreateDecision(
        MemoryOperation operation,
        float confidence,
        string reasoning,
        string content,
        float importanceScore,
        MemoryType suggestedType,
        IReadOnlyList<string> topics,
        MemoryUnit? targetMemory = null,
        IReadOnlyList<MemoryUnit>? relatedMemories = null,
        string? suggestedContent = null,
        bool contradictionDetected = false,
        string? contradictionDetails = null)
    {
        return new OperationDecision
        {
            Operation = operation,
            Confidence = confidence,
            Reasoning = reasoning,
            Content = content,
            TargetMemory = targetMemory,
            RelatedMemories = relatedMemories ?? [],
            SuggestedContent = suggestedContent,
            ImportanceScore = importanceScore,
            SuggestedType = suggestedType,
            Topics = topics,
            ContradictionDetected = contradictionDetected,
            ContradictionDetails = contradictionDetails
        };
    }
}
