using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Conflict;

/// <summary>
/// Service for validating facts with category-specific conflict resolution rules.
/// Phase v0.9.2: Fact Conflict Resolution.
/// </summary>
public sealed class FactValidatorService : IFactValidator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<FactValidatorService> _logger;

    /// <summary>
    /// Default confidence differential threshold for auto-resolution.
    /// </summary>
    public const float DefaultConfidenceDifferential = 0.2f;

    /// <summary>
    /// Default semantic similarity threshold for conflict detection.
    /// </summary>
    public const float DefaultSimilarityThreshold = 0.8f;

    public FactValidatorService(
        IEmbeddingService embeddingService,
        ILogger<FactValidatorService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FactConflictAnalysis> ValidateAsync(
        UserFact newFact,
        IReadOnlyList<SemanticStoreEntry> existingFacts,
        FactValidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FactValidationOptions();

        if (existingFacts.Count == 0)
        {
            _logger.LogDebug("No existing facts to compare against");
            return FactConflictAnalysis.Valid();
        }

        // Detect conflicts
        var conflicts = await DetectConflictsAsync(newFact, existingFacts, cancellationToken);

        if (conflicts.Count == 0)
        {
            return FactConflictAnalysis.Valid();
        }

        // Get category rule
        var categoryRule = GetCategoryRule(newFact.Category);

        // Analyze conflicts and determine action
        var primaryConflict = conflicts.OrderByDescending(c => c.Confidence).First();
        var strategy = DetermineStrategy(primaryConflict, categoryRule, options);

        // Check if auto-resolution is possible
        var canAutoResolve = CanAutoResolve(primaryConflict, newFact, options, categoryRule);

        if (primaryConflict.ConflictType == FactConflictType.Duplicate)
        {
            return new FactConflictAnalysis
            {
                IsValid = false,
                Conflicts = conflicts,
                RecommendedAction = FactConflictAction.Skip,
                AppliedStrategy = FactResolutionStrategy.Auto,
                Confidence = primaryConflict.Confidence,
                Reasoning = "Exact duplicate detected"
            };
        }

        if (primaryConflict.ConflictType == FactConflictType.SemanticDuplicate)
        {
            // Check confidence differential for auto-resolution
            var existingConfidence = primaryConflict.ExistingFact?.Confidence ?? 0;
            var confidenceDiff = newFact.Confidence - existingConfidence;

            if (confidenceDiff >= options.ConfidenceDifferentialThreshold)
            {
                return new FactConflictAnalysis
                {
                    IsValid = true,
                    Conflicts = conflicts,
                    RecommendedAction = FactConflictAction.Replace,
                    AppliedStrategy = FactResolutionStrategy.ConfidenceFirst,
                    Confidence = 0.9f,
                    Reasoning = $"New fact has significantly higher confidence (+{confidenceDiff:F2})"
                };
            }

            return new FactConflictAnalysis
            {
                IsValid = false,
                Conflicts = conflicts,
                RecommendedAction = FactConflictAction.Skip,
                AppliedStrategy = FactResolutionStrategy.Auto,
                Confidence = primaryConflict.Confidence,
                Reasoning = "Semantic duplicate exists with similar confidence"
            };
        }

        if (categoryRule.RequiresExplicitConfirmation &&
            primaryConflict.ConflictType is FactConflictType.Contradiction or FactConflictType.ValueUpdate)
        {
            return new FactConflictAnalysis
            {
                IsValid = false,
                Conflicts = conflicts,
                RecommendedAction = FactConflictAction.RequireReview,
                AppliedStrategy = FactResolutionStrategy.RequireConfirmation,
                RequiresConfirmation = true,
                ConfirmationQuestion = BuildConfirmationQuestion(newFact, primaryConflict),
                Confidence = primaryConflict.Confidence,
                Reasoning = $"{newFact.Category} facts require explicit confirmation for changes"
            };
        }

        // Apply category-specific resolution
        var action = DetermineAction(primaryConflict, newFact, categoryRule, options);

        return new FactConflictAnalysis
        {
            IsValid = action != FactConflictAction.Skip && action != FactConflictAction.RequireReview,
            Conflicts = conflicts,
            RecommendedAction = action,
            AppliedStrategy = strategy,
            RequiresConfirmation = action == FactConflictAction.RequireReview,
            Confidence = primaryConflict.Confidence,
            Reasoning = BuildReasoning(primaryConflict, action, categoryRule)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FactConflict>> DetectConflictsAsync(
        UserFact newFact,
        IReadOnlyList<SemanticStoreEntry> existingFacts,
        CancellationToken cancellationToken = default)
    {
        var conflicts = new List<FactConflict>();

        // Get embedding for new fact
        ReadOnlyMemory<float>? newEmbedding = null;
        try
        {
            newEmbedding = await _embeddingService.GenerateEmbeddingAsync(newFact.Content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding for new fact");
        }

        foreach (var existing in existingFacts)
        {
            // Skip inactive entries
            if (!existing.IsActive) continue;

            // Check for exact duplicate
            if (string.Equals(newFact.Content.Trim(), existing.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new FactConflict
                {
                    ConflictType = FactConflictType.Duplicate,
                    ExistingFact = existing,
                    Confidence = 1.0f,
                    Description = "Exact content match",
                    Category = newFact.Category
                });
                continue;
            }

            // Check SPO triple matching
            var spoMatch = CheckSpoMatch(newFact, existing);
            if (spoMatch.HasValue && spoMatch.Value)
            {
                // Same subject-predicate, different object = ValueUpdate
                conflicts.Add(new FactConflict
                {
                    ConflictType = FactConflictType.ValueUpdate,
                    ExistingFact = existing,
                    Confidence = 0.95f,
                    SpoMatch = true,
                    Description = "Same subject-predicate with different value",
                    Category = newFact.Category
                });
                continue;
            }

            // Check semantic similarity
            if (newEmbedding != null && existing.Embedding != null)
            {
                var similarity = CosineSimilarity(newEmbedding.Value.Span, existing.Embedding.Value.Span);

                if (similarity >= 0.95f)
                {
                    conflicts.Add(new FactConflict
                    {
                        ConflictType = FactConflictType.SemanticDuplicate,
                        ExistingFact = existing,
                        Confidence = similarity,
                        SimilarityScore = similarity,
                        Description = $"Semantic similarity: {similarity:P0}",
                        Category = newFact.Category
                    });
                }
                else if (similarity >= 0.8f)
                {
                    // Check for contradiction patterns
                    var isContradiction = DetectContradictionPattern(newFact.Content, existing.Value);
                    if (isContradiction)
                    {
                        conflicts.Add(new FactConflict
                        {
                            ConflictType = FactConflictType.Contradiction,
                            ExistingFact = existing,
                            Confidence = similarity,
                            SimilarityScore = similarity,
                            Description = "Contradictory information detected",
                            Category = newFact.Category
                        });
                    }
                }
            }
        }

        return conflicts.OrderByDescending(c => c.Confidence).ToList();
    }

    /// <inheritdoc />
    public Task<FactResolutionResult> ResolveConflictAsync(
        FactConflict conflict,
        FactResolutionStrategy? strategy = null,
        CancellationToken cancellationToken = default)
    {
        var categoryRule = GetCategoryRule(conflict.Category);
        var effectiveStrategy = strategy ?? categoryRule.DefaultStrategy;

        FactResolutionResult result;

        switch (effectiveStrategy)
        {
            case FactResolutionStrategy.RecencyFirst:
                result = new FactResolutionResult
                {
                    Success = true,
                    Action = FactConflictAction.Replace,
                    AppliedStrategy = effectiveStrategy,
                    ArchivedEntry = categoryRule.PreserveHistory ? conflict.ExistingFact : null,
                    Reasoning = "Newer information takes precedence"
                };
                break;

            case FactResolutionStrategy.ConfidenceFirst:
                result = new FactResolutionResult
                {
                    Success = true,
                    Action = FactConflictAction.Replace,
                    AppliedStrategy = effectiveStrategy,
                    ArchivedEntry = categoryRule.PreserveHistory ? conflict.ExistingFact : null,
                    Reasoning = "Higher confidence fact preserved"
                };
                break;

            case FactResolutionStrategy.TemporalPartition:
                result = new FactResolutionResult
                {
                    Success = true,
                    Action = FactConflictAction.ArchiveAndAdd,
                    AppliedStrategy = effectiveStrategy,
                    ArchivedEntry = conflict.ExistingFact,
                    Reasoning = "Old fact archived with timestamp, new fact added"
                };
                break;

            case FactResolutionStrategy.KeepBoth:
                result = new FactResolutionResult
                {
                    Success = true,
                    Action = FactConflictAction.Add,
                    AppliedStrategy = effectiveStrategy,
                    KeptEntry = conflict.ExistingFact,
                    Reasoning = "Both facts preserved"
                };
                break;

            case FactResolutionStrategy.RequireConfirmation:
                result = new FactResolutionResult
                {
                    Success = false,
                    Action = FactConflictAction.RequireReview,
                    AppliedStrategy = effectiveStrategy,
                    KeptEntry = conflict.ExistingFact,
                    RequiresUserAction = true,
                    Reasoning = "User confirmation required for this change"
                };
                break;

            default:
                result = new FactResolutionResult
                {
                    Success = true,
                    Action = FactConflictAction.Add,
                    AppliedStrategy = FactResolutionStrategy.Auto,
                    Reasoning = "Default: add as new fact"
                };
                break;
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public CategoryResolutionRule GetCategoryRule(FactCategory category)
    {
        return CategoryResolutionRule.Defaults.TryGetValue(category, out var rule)
            ? rule
            : CategoryResolutionRule.Defaults[FactCategory.General];
    }

    #region Private Methods

    private bool? CheckSpoMatch(UserFact newFact, SemanticStoreEntry existing)
    {
        // Check if both have SPO metadata
        if (string.IsNullOrEmpty(newFact.Subject) || string.IsNullOrEmpty(newFact.Predicate))
            return null;

        if (!existing.Metadata.TryGetValue("subject", out var existingSubject) ||
            !existing.Metadata.TryGetValue("predicate", out var existingPredicate))
            return null;

        // Subject and predicate match = same attribute
        var subjectMatch = string.Equals(newFact.Subject, existingSubject, StringComparison.OrdinalIgnoreCase);
        var predicateMatch = string.Equals(newFact.Predicate, existingPredicate, StringComparison.OrdinalIgnoreCase);

        return subjectMatch && predicateMatch;
    }

    private static bool DetectContradictionPattern(string newContent, string existingContent)
    {
        var newLower = newContent.ToLowerInvariant();
        var existingLower = existingContent.ToLowerInvariant();

        // Check for negation patterns
        var negationPairs = new[]
        {
            ("likes", "dislikes"),
            ("likes", "hates"),
            ("loves", "hates"),
            ("is", "is not"),
            ("can", "cannot"),
            ("will", "will not"),
            ("좋아", "싫어"),
            ("있다", "없다"),
            ("이다", "아니다")
        };

        foreach (var (positive, negative) in negationPairs)
        {
            if ((newLower.Contains(positive) && existingLower.Contains(negative)) ||
                (newLower.Contains(negative) && existingLower.Contains(positive)))
            {
                return true;
            }
        }

        return false;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static FactResolutionStrategy DetermineStrategy(
        FactConflict conflict,
        CategoryResolutionRule rule,
        FactValidationOptions options)
    {
        // Use category default unless specific conditions apply
        return conflict.ConflictType switch
        {
            FactConflictType.Duplicate => FactResolutionStrategy.Auto,
            FactConflictType.SemanticDuplicate => FactResolutionStrategy.ConfidenceFirst,
            FactConflictType.TemporalConflict => FactResolutionStrategy.TemporalPartition,
            _ => rule.DefaultStrategy
        };
    }

    private static bool CanAutoResolve(
        FactConflict conflict,
        UserFact newFact,
        FactValidationOptions options,
        CategoryResolutionRule rule)
    {
        if (!options.AllowAutoResolution) return false;
        if (rule.RequiresExplicitConfirmation) return false;

        var existingConfidence = conflict.ExistingFact?.Confidence ?? 0;
        var confidenceDiff = newFact.Confidence - existingConfidence;

        return confidenceDiff >= rule.MinConfidenceDifferential;
    }

    private static FactConflictAction DetermineAction(
        FactConflict conflict,
        UserFact newFact,
        CategoryResolutionRule rule,
        FactValidationOptions options)
    {
        var existingConfidence = conflict.ExistingFact?.Confidence ?? 0;
        var confidenceDiff = newFact.Confidence - existingConfidence;

        return conflict.ConflictType switch
        {
            FactConflictType.Duplicate => FactConflictAction.Skip,
            FactConflictType.SemanticDuplicate when confidenceDiff >= options.ConfidenceDifferentialThreshold
                => FactConflictAction.Replace,
            FactConflictType.SemanticDuplicate => FactConflictAction.Skip,
            FactConflictType.ValueUpdate when rule.PreserveHistory => FactConflictAction.ArchiveAndAdd,
            FactConflictType.ValueUpdate => FactConflictAction.Replace,
            FactConflictType.Contradiction when rule.RequiresExplicitConfirmation => FactConflictAction.RequireReview,
            FactConflictType.Contradiction when confidenceDiff >= options.ConfidenceDifferentialThreshold
                => FactConflictAction.ArchiveAndAdd,
            FactConflictType.Contradiction => FactConflictAction.RequireReview,
            FactConflictType.TemporalConflict => FactConflictAction.ArchiveAndAdd,
            FactConflictType.Refinement => FactConflictAction.Merge,
            _ => FactConflictAction.Add
        };
    }

    private static string BuildConfirmationQuestion(UserFact newFact, FactConflict conflict)
    {
        var existing = conflict.ExistingFact?.Value ?? "unknown";
        return conflict.ConflictType switch
        {
            FactConflictType.ValueUpdate =>
                $"Update {newFact.Category} from '{existing}' to '{newFact.Content}'?",
            FactConflictType.Contradiction =>
                $"Replace conflicting fact '{existing}' with '{newFact.Content}'?",
            _ => $"The new fact conflicts with '{existing}'. Proceed?"
        };
    }

    private static string BuildReasoning(
        FactConflict conflict,
        FactConflictAction action,
        CategoryResolutionRule rule)
    {
        return action switch
        {
            FactConflictAction.Skip => "Duplicate or semantically equivalent fact exists",
            FactConflictAction.Replace => "New fact has higher confidence",
            FactConflictAction.ArchiveAndAdd => $"{rule.Category} facts preserve history; old fact archived",
            FactConflictAction.Merge => "Refinement detected; facts merged",
            FactConflictAction.RequireReview => $"{rule.Category} changes require explicit confirmation",
            _ => "Added as independent fact"
        };
    }

    #endregion
}
