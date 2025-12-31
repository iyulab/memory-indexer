using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Conflict;

/// <summary>
/// Hybrid contradiction detector using semantic similarity and rule-based analysis.
/// Based on research: NLI + Semantic approach achieves 70.9% F1 for contradiction detection.
/// </summary>
public sealed class SemanticContradictionDetector : IContradictionDetector
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SemanticContradictionDetector> _logger;

    // Negation indicators for rule-based detection
    private static readonly string[] NegationIndicators =
    [
        "not", "no", "never", "none", "nothing", "neither", "nobody", "nowhere",
        "isn't", "aren't", "wasn't", "weren't", "don't", "doesn't", "didn't",
        "won't", "wouldn't", "shouldn't", "couldn't", "can't", "cannot",
        "아니", "없", "안", "못", "불가"
    ];

    // Opposite relationship patterns
    private static readonly Dictionary<string, string[]> OppositePatterns = new()
    {
        ["likes"] = ["dislikes", "hates", "doesn't like"],
        ["prefers"] = ["avoids", "doesn't prefer", "dislikes"],
        ["is"] = ["is not", "isn't", "was not"],
        ["has"] = ["doesn't have", "has no", "lacks"],
        ["can"] = ["cannot", "can't", "is unable to"],
        ["will"] = ["won't", "will not", "refuses to"],
        ["true"] = ["false"],
        ["yes"] = ["no"],
        ["enable"] = ["disable"],
        ["active"] = ["inactive"],
        ["좋아"] = ["싫어", "안 좋아"],
        ["있"] = ["없"],
        ["할 수 있"] = ["할 수 없"]
    };

    public SemanticContradictionDetector(
        IEmbeddingService embeddingService,
        ILogger<SemanticContradictionDetector> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ContradictionAnalysis> DetectMemoryContradictionAsync(
        MemoryUnit newMemory,
        IReadOnlyList<MemoryUnit> existingMemories,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ContradictionDetectionOptions();

        if (existingMemories.Count == 0)
        {
            return new ContradictionAnalysis
            {
                HasContradiction = false,
                NewItem = newMemory,
                Type = ContradictionType.None
            };
        }

        // Generate embedding for new memory if not present
        var newEmbedding = newMemory.Embedding;
        if (!newEmbedding.HasValue || newEmbedding.Value.Length == 0)
        {
            newEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                newMemory.Content, cancellationToken);
        }

        ContradictionAnalysis? bestMatch = null;
        float highestContradictionScore = 0;

        // Limit comparison to avoid performance issues
        var memoriesToCheck = existingMemories
            .OrderByDescending(m => m.UpdatedAt)
            .Take(options.MaxComparisonItems)
            .ToList();

        foreach (var existing in memoriesToCheck)
        {
            // Skip if same memory
            if (existing.Id == newMemory.Id) continue;

            // Get embedding for existing memory
            var existingEmbedding = existing.Embedding;
            if (!existingEmbedding.HasValue || existingEmbedding.Value.Length == 0)
            {
                continue; // Skip if no embedding available
            }

            // Calculate semantic similarity
            var similarity = VectorMath.CosineSimilarity(
                newEmbedding.Value.Span,
                existingEmbedding.Value.Span);

            // Only check for contradiction if topics are related
            if (similarity < options.SimilarityThreshold)
            {
                continue;
            }

            // Check for contradiction using rule-based analysis
            var (hasContradiction, contradictionType, confidence, description) =
                AnalyzeContradiction(newMemory.Content, existing.Content);

            if (hasContradiction && confidence > highestContradictionScore)
            {
                highestContradictionScore = confidence;
                bestMatch = new ContradictionAnalysis
                {
                    HasContradiction = true,
                    NewItem = newMemory,
                    ConflictingItem = existing,
                    ContradictionConfidence = confidence,
                    Type = contradictionType,
                    ConflictDescription = description,
                    Context = new Dictionary<string, string>
                    {
                        ["SemanticSimilarity"] = similarity.ToString("F3"),
                        ["ExistingMemoryId"] = existing.Id.ToString(),
                        ["NewContent"] = TruncateForContext(newMemory.Content),
                        ["ExistingContent"] = TruncateForContext(existing.Content)
                    }
                };
            }
        }

        if (bestMatch != null && bestMatch.ContradictionConfidence >= options.MinContradictionConfidence)
        {
            _logger.LogInformation(
                "Detected {Type} contradiction (confidence: {Confidence:P1}) between new memory and existing {ExistingId}",
                bestMatch.Type, bestMatch.ContradictionConfidence,
                ((MemoryUnit)bestMatch.ConflictingItem!).Id);

            return bestMatch;
        }

        return new ContradictionAnalysis
        {
            HasContradiction = false,
            NewItem = newMemory,
            Type = ContradictionType.None
        };
    }

    /// <inheritdoc />
    public Task<ContradictionAnalysis> DetectTripleContradictionAsync(
        EntityTriple newTriple,
        IReadOnlyList<EntityTriple> existingTriples,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ContradictionDetectionOptions();

        if (existingTriples.Count == 0)
        {
            return Task.FromResult(new ContradictionAnalysis
            {
                HasContradiction = false,
                NewItem = newTriple,
                Type = ContradictionType.None
            });
        }

        // For triples, contradiction detection is more straightforward:
        // Same subject + same predicate + different object = potential contradiction
        foreach (var existing in existingTriples)
        {
            if (existing.Id == newTriple.Id) continue;

            // Check for same subject and predicate
            if (!existing.Subject.Equals(newTriple.Subject, StringComparison.OrdinalIgnoreCase) ||
                !existing.Predicate.Equals(newTriple.Predicate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Check if object values differ
            if (existing.ObjectValue.Equals(newTriple.ObjectValue, StringComparison.OrdinalIgnoreCase))
            {
                continue; // Same value, no contradiction
            }

            // Check for temporal overlap if temporal detection is enabled
            if (options.CheckTemporalContradictions)
            {
                var newFrom = newTriple.ValidFrom ?? DateTime.MinValue;
                var newTo = newTriple.ValidTo ?? DateTime.MaxValue;
                var existingFrom = existing.ValidFrom ?? DateTime.MinValue;
                var existingTo = existing.ValidTo ?? DateTime.MaxValue;

                // Check for time period overlap
                if (newFrom < existingTo && existingFrom < newTo)
                {
                    // Temporal overlap with different values = contradiction
                    var confidence = CalculateTripleContradictionConfidence(newTriple, existing);

                    if (confidence >= options.MinContradictionConfidence)
                    {
                        _logger.LogInformation(
                            "Detected temporal contradiction for {Subject}.{Predicate}: '{OldValue}' vs '{NewValue}'",
                            newTriple.Subject, newTriple.Predicate,
                            existing.ObjectValue, newTriple.ObjectValue);

                        return Task.FromResult(new ContradictionAnalysis
                        {
                            HasContradiction = true,
                            NewItem = newTriple,
                            ConflictingItem = existing,
                            ContradictionConfidence = confidence,
                            Type = ContradictionType.Temporal,
                            ConflictDescription = $"Conflicting values for {newTriple.Subject}.{newTriple.Predicate}: " +
                                                  $"'{existing.ObjectValue}' (v{existing.Version}) vs '{newTriple.ObjectValue}'",
                            Context = new Dictionary<string, string>
                            {
                                ["ExistingTripleId"] = existing.Id.ToString(),
                                ["ExistingVersion"] = existing.Version.ToString(),
                                ["ExistingValue"] = existing.ObjectValue,
                                ["NewValue"] = newTriple.ObjectValue,
                                ["OverlapPeriod"] = $"{(newFrom > existingFrom ? newFrom : existingFrom):yyyy-MM-dd} to {(newTo < existingTo ? newTo : existingTo):yyyy-MM-dd}"
                            }
                        });
                    }
                }
            }
            else
            {
                // Non-temporal: any different value is a potential contradiction
                var confidence = CalculateTripleContradictionConfidence(newTriple, existing);

                if (confidence >= options.MinContradictionConfidence)
                {
                    return Task.FromResult(new ContradictionAnalysis
                    {
                        HasContradiction = true,
                        NewItem = newTriple,
                        ConflictingItem = existing,
                        ContradictionConfidence = confidence,
                        Type = ContradictionType.Factual,
                        ConflictDescription = $"Different values for {newTriple.Subject}.{newTriple.Predicate}: " +
                                              $"'{existing.ObjectValue}' vs '{newTriple.ObjectValue}'",
                        Context = new Dictionary<string, string>
                        {
                            ["ExistingTripleId"] = existing.Id.ToString(),
                            ["ExistingValue"] = existing.ObjectValue,
                            ["NewValue"] = newTriple.ObjectValue
                        }
                    });
                }
            }
        }

        return Task.FromResult(new ContradictionAnalysis
        {
            HasContradiction = false,
            NewItem = newTriple,
            Type = ContradictionType.None
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContradictionAnalysis>> DetectBatchContradictionsAsync(
        IReadOnlyList<MemoryUnit> newMemories,
        IReadOnlyList<MemoryUnit> existingMemories,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ContradictionAnalysis>();

        foreach (var newMemory in newMemories)
        {
            var analysis = await DetectMemoryContradictionAsync(
                newMemory, existingMemories, options, cancellationToken);
            results.Add(analysis);
        }

        return results;
    }

    /// <summary>
    /// Analyzes two text contents for contradiction patterns.
    /// </summary>
    private (bool HasContradiction, ContradictionType Type, float Confidence, string Description)
        AnalyzeContradiction(string newContent, string existingContent)
    {
        var newLower = newContent.ToLowerInvariant();
        var existingLower = existingContent.ToLowerInvariant();

        // Check for negation patterns
        if (ContainsNegationOf(newLower, existingLower) || ContainsNegationOf(existingLower, newLower))
        {
            return (true, ContradictionType.Semantic, 0.8f,
                "Negation pattern detected between statements");
        }

        // Check for opposite relationship patterns
        foreach (var (positive, negatives) in OppositePatterns)
        {
            if (newLower.Contains(positive) && negatives.Any(n => existingLower.Contains(n)))
            {
                return (true, ContradictionType.Semantic, 0.75f,
                    $"Opposite relationship pattern: '{positive}' vs one of {string.Join(", ", negatives)}");
            }
            if (existingLower.Contains(positive) && negatives.Any(n => newLower.Contains(n)))
            {
                return (true, ContradictionType.Semantic, 0.75f,
                    $"Opposite relationship pattern: '{positive}' vs one of {string.Join(", ", negatives)}");
            }
        }

        // Check for preference contradictions (likes X vs dislikes X)
        var preferenceContradiction = DetectPreferenceContradiction(newLower, existingLower);
        if (preferenceContradiction.HasValue)
        {
            return (true, ContradictionType.Preference, preferenceContradiction.Value.Confidence,
                preferenceContradiction.Value.Description);
        }

        return (false, ContradictionType.None, 0f, string.Empty);
    }

    /// <summary>
    /// Checks if one content contains a negation of the other.
    /// </summary>
    private static bool ContainsNegationOf(string content1, string content2)
    {
        foreach (var negation in NegationIndicators)
        {
            // Check if content1 has a negated version of key phrases from content2
            var words2 = content2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words2)
            {
                if (word.Length < 3) continue; // Skip short words

                // Check if content1 contains "not {word}" or "{negation} {word}"
                if (content1.Contains($"{negation} {word}") ||
                    content1.Contains($"{word} {negation}"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Detects preference contradictions (e.g., likes X vs dislikes X).
    /// </summary>
    private static (float Confidence, string Description)? DetectPreferenceContradiction(
        string content1, string content2)
    {
        var preferenceVerbs = new[] { "like", "prefer", "enjoy", "love", "want", "need",
            "좋아하", "선호", "원하" };
        var antiPreferenceVerbs = new[] { "dislike", "hate", "avoid", "don't like", "doesn't like",
            "싫어하", "피하", "안 좋아하" };

        foreach (var prefVerb in preferenceVerbs)
        {
            if (!content1.Contains(prefVerb)) continue;

            foreach (var antiVerb in antiPreferenceVerbs)
            {
                if (content2.Contains(antiVerb))
                {
                    // Check if they're talking about the same subject
                    // (simplified check - in production would use NER)
                    return (0.7f, $"Preference contradiction: '{prefVerb}' vs '{antiVerb}'");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Calculates contradiction confidence for entity triples.
    /// </summary>
    private static float CalculateTripleContradictionConfidence(EntityTriple newTriple, EntityTriple existing)
    {
        // Base confidence for same subject+predicate with different object
        var confidence = 0.8f;

        // Adjust based on confidence of both triples
        confidence *= (newTriple.Confidence + existing.Confidence) / 2;

        // Adjust based on recency (newer info might be more reliable)
        if (newTriple.CreatedAt > existing.CreatedAt.AddDays(30))
        {
            confidence *= 0.9f; // Slightly lower confidence for older existing data
        }

        return Math.Min(confidence, 1.0f);
    }

    /// <summary>
    /// Truncates content for context storage.
    /// </summary>
    private static string TruncateForContext(string content, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        return content.Length <= maxLength ? content : content[..maxLength] + "...";
    }
}
