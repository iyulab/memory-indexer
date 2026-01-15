using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Promotion;

/// <summary>
/// Service for fast-track promotion of high-confidence facts directly to Archive (T3).
/// Bypasses the standard tier promotion pipeline for verified user facts.
/// </summary>
/// <remarks>
/// Fast-Track Pipeline:
/// 1. Extract facts from conversation content using IFactExtractor
/// 2. Validate against existing facts for conflicts/duplicates
/// 3. Promote high-confidence facts (≥0.9) directly to IArchiveStore
/// 4. Queue lower-confidence facts for standard promotion path
///
/// Integration:
/// - Called during buffer drain in SensoryPromoterService
/// - Can also be invoked directly via MCP tools
/// </remarks>
public sealed class FastTrackPromoterService : IFastTrackPromoter
{
    private readonly IFactExtractor _factExtractor;
    private readonly IArchiveStore _archiveStore;
    private readonly IMemoryStore? _memoryStore;
    private readonly ILogger<FastTrackPromoterService> _logger;

    /// <summary>
    /// Confidence threshold for fast-track promotion.
    /// </summary>
    public const float FastTrackThreshold = 0.9f;

    /// <summary>
    /// Minimum confidence for standard promotion path.
    /// </summary>
    public const float StandardThreshold = 0.5f;

    public FastTrackPromoterService(
        IFactExtractor factExtractor,
        IArchiveStore archiveStore,
        ILogger<FastTrackPromoterService> logger,
        IMemoryStore? memoryStore = null)
    {
        _factExtractor = factExtractor;
        _archiveStore = archiveStore;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FastTrackResult> ProcessAsync(
        FactExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.UserId);

        try
        {
            _logger.LogDebug("Processing content for fast-track promotion: {UserId}",
                context.UserId);

            // Extract facts from content
            var extractionResult = await _factExtractor.ExtractAsync(context, cancellationToken);

            if (!extractionResult.HasFacts)
            {
                _logger.LogDebug("No facts extracted from content");
                return new FastTrackResult
                {
                    Success = true,
                    ExtractedFacts = [],
                    ContextType = extractionResult.ContextType
                };
            }

            // Get existing facts for validation
            var existingFacts = await GetExistingFactsAsync(context.UserId, cancellationToken);

            var fastTracked = new List<UserFact>();
            var standardPath = new List<UserFact>();
            var skipped = new List<FactSkipReason>();

            foreach (var fact in extractionResult.Facts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Validate against existing facts
                var validation = await _factExtractor.ValidateAsync(fact, existingFacts, cancellationToken);

                // Determine promotion path
                var decision = DeterminePromotionDecision(fact, validation, extractionResult.ContextType);

                switch (decision)
                {
                    case PromotionDecision.FastTrack:
                        await PromoteToArchiveAsync(context.UserId, fact, context.SessionId, cancellationToken);
                        fastTracked.Add(fact);
                        break;

                    case PromotionDecision.Standard:
                        standardPath.Add(fact);
                        break;

                    case PromotionDecision.Skip:
                        skipped.Add(new FactSkipReason
                        {
                            Fact = fact,
                            Reason = MapConflictToSkipReason(validation),
                            Details = validation.Reasoning
                        });
                        break;

                    case PromotionDecision.Replace:
                        await ReplaceInArchiveAsync(context.UserId, fact, validation.ConflictingMemory, cancellationToken);
                        fastTracked.Add(fact);
                        break;
                }
            }

            _logger.LogInformation(
                "Fast-track processing complete: Extracted={Extracted}, FastTracked={FastTracked}, " +
                "StandardPath={Standard}, Skipped={Skipped}",
                extractionResult.Facts.Count, fastTracked.Count, standardPath.Count, skipped.Count);

            return new FastTrackResult
            {
                Success = true,
                ExtractedFacts = extractionResult.Facts,
                FastTrackedFacts = fastTracked,
                StandardPathFacts = standardPath,
                SkippedFacts = skipped,
                ContextType = extractionResult.ContextType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process content for fast-track promotion");
            return FastTrackResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<FastTrackBatchResult> ProcessBatchAsync(
        IReadOnlyList<SensoryMemory> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return FastTrackBatchResult.Empty;
        }

        var stopwatch = Stopwatch.StartNew();
        var results = new List<FastTrackResult>();
        var totalFastTracked = 0;
        var totalStandardPath = 0;
        var totalSkipped = 0;

        try
        {
            // Group by user for efficiency
            var byUser = items.GroupBy(i => i.UserId);

            foreach (var userGroup in byUser)
            {
                var userId = userGroup.Key;

                // Process each item for the user
                foreach (var item in userGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Only process user messages (not assistant responses)
                    if (item.Role?.Equals("assistant", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        continue;
                    }

                    var context = new FactExtractionContext
                    {
                        Content = item.Content,
                        UserId = userId,
                        SessionId = item.SessionId,
                        Role = item.Role
                    };

                    var result = await ProcessAsync(context, cancellationToken);
                    results.Add(result);

                    totalFastTracked += result.FastTrackedFacts.Count;
                    totalStandardPath += result.StandardPathFacts.Count;
                    totalSkipped += result.SkippedFacts.Count;
                }
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Batch fast-track complete: Items={Items}, FastTracked={FastTracked}, " +
                "StandardPath={Standard}, Skipped={Skipped}, Duration={Duration:F2}s",
                items.Count, totalFastTracked, totalStandardPath, totalSkipped,
                stopwatch.Elapsed.TotalSeconds);

            return new FastTrackBatchResult
            {
                Success = true,
                ItemsProcessed = items.Count,
                TotalFactsExtracted = results.Sum(r => r.ExtractedFacts.Count),
                TotalFastTracked = totalFastTracked,
                TotalStandardPath = totalStandardPath,
                TotalSkipped = totalSkipped,
                Results = results,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process batch for fast-track promotion");
            stopwatch.Stop();
            return new FastTrackBatchResult
            {
                Success = false,
                Error = ex.Message,
                ItemsProcessed = results.Count,
                Results = results,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<UserProfile> GetUserProfileAsync(
        string userId,
        FactCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            IReadOnlyList<SemanticStoreEntry> entries;

            if (category.HasValue)
            {
                var semanticCategory = MapFactCategoryToSemanticCategory(category.Value);
                entries = await _archiveStore.GetByCategoryAsync(userId, semanticCategory, cancellationToken);
            }
            else
            {
                entries = await _archiveStore.GetAllAsync(userId, cancellationToken);
            }

            var facts = entries.Select(e => new UserProfileFact
            {
                Key = e.Key,
                Content = e.Value,
                Category = MapSemanticCategoryToFactCategory(e.Category),
                Confidence = e.Confidence,
                ConfirmationCount = e.ConfirmationCount,
                IsConfirmed = e.IsConfirmed,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                Subject = e.Metadata.GetValueOrDefault("subject"),
                Predicate = e.Metadata.GetValueOrDefault("predicate"),
                Object = e.Metadata.GetValueOrDefault("object")
            }).ToList();

            var byCategory = facts
                .GroupBy(f => f.Category)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<UserProfileFact>)g.ToList());

            return new UserProfile
            {
                UserId = userId,
                Facts = facts,
                FactsByCategory = byCategory,
                LastUpdatedAt = facts.Count > 0 ? facts.Max(f => f.UpdatedAt) : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user profile for {UserId}", userId);
            return UserProfile.Empty(userId);
        }
    }

    private async Task<IReadOnlyList<MemoryUnit>> GetExistingFactsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (_memoryStore == null)
        {
            return [];
        }

        try
        {
            var options = new MemoryFilterOptions
            {
                Types = [MemoryType.Fact, MemoryType.Semantic]
            };

            return await _memoryStore.GetAllAsync(userId, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get existing facts for validation");
            return [];
        }
    }

    private enum PromotionDecision
    {
        FastTrack,
        Standard,
        Skip,
        Replace
    }

    private static PromotionDecision DeterminePromotionDecision(
        UserFact fact,
        FactValidationResult validation,
        StatementContextType contextType)
    {
        // Context-based filtering
        if (contextType is StatementContextType.Quoted or
            StatementContextType.Narrative or
            StatementContextType.RolePlay or
            StatementContextType.Question)
        {
            return PromotionDecision.Skip;
        }

        // Handle conflicts
        if (validation.ConflictType != FactConflictType.None)
        {
            return validation.RecommendedAction switch
            {
                FactConflictAction.Skip => PromotionDecision.Skip,
                FactConflictAction.Replace => fact.Confidence >= FastTrackThreshold
                    ? PromotionDecision.Replace
                    : PromotionDecision.Standard,
                FactConflictAction.RequireReview => PromotionDecision.Skip,
                _ => PromotionDecision.Skip
            };
        }

        // Confidence-based promotion path
        return fact.PromotionPath switch
        {
            PromotionPath.FastTrack when fact.Confidence >= FastTrackThreshold => PromotionDecision.FastTrack,
            PromotionPath.Standard when fact.Confidence >= StandardThreshold => PromotionDecision.Standard,
            PromotionPath.SessionOnly => PromotionDecision.Skip,
            PromotionPath.Discard => PromotionDecision.Skip,
            _ => fact.Confidence >= FastTrackThreshold ? PromotionDecision.FastTrack
                : fact.Confidence >= StandardThreshold ? PromotionDecision.Standard
                : PromotionDecision.Skip
        };
    }

    private static SkipReasonType MapConflictToSkipReason(FactValidationResult validation)
    {
        return validation.ConflictType switch
        {
            FactConflictType.Duplicate => SkipReasonType.Duplicate,
            FactConflictType.SemanticDuplicate => SkipReasonType.SemanticDuplicate,
            FactConflictType.Contradiction => SkipReasonType.Conflict,
            _ when validation.RecommendedAction == FactConflictAction.RequireReview => SkipReasonType.RequiresReview,
            _ => SkipReasonType.Other
        };
    }

    private async Task PromoteToArchiveAsync(
        string userId,
        UserFact fact,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var key = GenerateFactKey(fact);
        var entry = CreateSemanticEntry(key, fact, sessionId);

        await _archiveStore.SetAsync(userId, entry, cancellationToken);

        _logger.LogInformation(
            "Fast-tracked fact to Archive: Key={Key}, Category={Category}, Confidence={Confidence}",
            key, fact.Category, fact.Confidence);
    }

    private async Task ReplaceInArchiveAsync(
        string userId,
        UserFact fact,
        MemoryUnit? conflictingMemory,
        CancellationToken cancellationToken)
    {
        var key = GenerateFactKey(fact);

        // If we know the existing key, use it; otherwise generate new
        if (conflictingMemory?.Metadata?.TryGetValue("semantic_key", out var existingKey) == true)
        {
            key = existingKey;
        }

        var entry = CreateSemanticEntry(key, fact, null);

        await _archiveStore.SetAsync(userId, entry, cancellationToken);

        _logger.LogInformation(
            "Replaced fact in Archive: Key={Key}, Category={Category}, Confidence={Confidence}",
            key, fact.Category, fact.Confidence);
    }

    private static string GenerateFactKey(UserFact fact)
    {
        // Generate a stable key from fact components
        var subject = fact.Subject?.ToLowerInvariant() ?? "user";
        var predicate = fact.Predicate?.ToLowerInvariant() ?? "fact";
        var category = fact.Category.ToString().ToLowerInvariant();

        return $"{category}:{subject}:{predicate}";
    }

    private static SemanticStoreEntry CreateSemanticEntry(string key, UserFact fact, string? sessionId)
    {
        var entry = new SemanticStoreEntry
        {
            Key = key,
            Value = fact.Content,
            Category = MapFactCategoryToSemanticCategory(fact.Category),
            Confidence = fact.Confidence,
            ConfirmationCount = 1,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "fast_track_promotion",
                ["original_source"] = fact.Source ?? "extraction"
            }
        };

        // Add SPO triple to metadata for future reference
        if (!string.IsNullOrEmpty(fact.Subject))
            entry.Metadata["subject"] = fact.Subject;
        if (!string.IsNullOrEmpty(fact.Predicate))
            entry.Metadata["predicate"] = fact.Predicate;
        if (!string.IsNullOrEmpty(fact.Object))
            entry.Metadata["object"] = fact.Object;

        if (!string.IsNullOrEmpty(sessionId))
        {
            entry.SourceSessions.Add(sessionId);
        }

        if (fact.Entities?.Count > 0)
        {
            entry.Metadata["entities"] = string.Join(",", fact.Entities);
        }

        return entry;
    }

    /// <summary>
    /// Maps FactCategory to SemanticStoreCategory.
    /// </summary>
    public static SemanticStoreCategory MapFactCategoryToSemanticCategory(FactCategory category)
    {
        return category switch
        {
            FactCategory.Identity => SemanticStoreCategory.Fact,
            FactCategory.Preference => SemanticStoreCategory.Preference,
            FactCategory.Relationship => SemanticStoreCategory.Relationship,
            FactCategory.Location => SemanticStoreCategory.Fact,
            FactCategory.Temporal => SemanticStoreCategory.Fact,
            FactCategory.Skill => SemanticStoreCategory.Skill,
            FactCategory.Goal => SemanticStoreCategory.Goal,
            FactCategory.Health => SemanticStoreCategory.Fact,
            FactCategory.Professional => SemanticStoreCategory.Work,
            FactCategory.General => SemanticStoreCategory.Fact,
            _ => SemanticStoreCategory.Other
        };
    }

    /// <summary>
    /// Maps SemanticStoreCategory to FactCategory.
    /// </summary>
    public static FactCategory MapSemanticCategoryToFactCategory(SemanticStoreCategory category)
    {
        return category switch
        {
            SemanticStoreCategory.Fact => FactCategory.General,
            SemanticStoreCategory.Preference => FactCategory.Preference,
            SemanticStoreCategory.Skill => FactCategory.Skill,
            SemanticStoreCategory.Interest => FactCategory.Preference,
            SemanticStoreCategory.Relationship => FactCategory.Relationship,
            SemanticStoreCategory.Work => FactCategory.Professional,
            SemanticStoreCategory.Goal => FactCategory.Goal,
            SemanticStoreCategory.Behavior => FactCategory.General,
            SemanticStoreCategory.Communication => FactCategory.Preference,
            SemanticStoreCategory.Other => FactCategory.General,
            _ => FactCategory.General
        };
    }
}
