using System.Collections.Concurrent;
using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.SelfCorrection;

/// <summary>
/// Memory Self-Correction service implementation.
/// Detects and corrects issues in stored memories autonomously.
/// </summary>
/// <remarks>
/// Research basis: A-MEM's self-evolution, MemR³'s evidence-gap tracking.
/// Implements contradiction detection, outdated identification, and confidence management.
/// </remarks>
public sealed partial class MemorySelfCorrector : IMemorySelfCorrector
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ITemporalEntityStore _entityStore;
    private readonly ILogger<MemorySelfCorrector> _logger;

    // Correction history storage
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CorrectionRecord>> _correctionHistory = new();
    private const int MaxHistoryPerUser = 1000;

    // Similarity thresholds
    private const float ContradictionSimilarityThreshold = 0.7f;
    private const float DuplicateSimilarityThreshold = 0.9f;

    public MemorySelfCorrector(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        ITemporalEntityStore entityStore,
        ILogger<MemorySelfCorrector> logger)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _entityStore = entityStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MemoryAnalysisResult> AnalyzeMemoriesAsync(
        string userId,
        MemoryAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MemoryAnalysisOptions();
        var stopwatch = Stopwatch.StartNew();

        LogStartingMemoryAnalysisUserUserId(_logger, userId);

        var memories = await GetMemoriesForAnalysisAsync(userId, options, cancellationToken);
        var result = new MemoryAnalysisResult
        {
            MemoriesAnalyzed = memories.Count
        };

        var tasks = new List<Task>();

        // Parallel analysis
        if (options.CheckContradictions)
        {
            var contradictionTask = DetectContradictionsAsync(memories, cancellationToken)
                .ContinueWith(t => result.Contradictions = t.Result, cancellationToken);
            tasks.Add(contradictionTask);
        }

        if (options.CheckOutdated)
        {
            var outdatedTask = IdentifyOutdatedMemoriesAsync(userId, null, cancellationToken)
                .ContinueWith(t => result.OutdatedMemories = t.Result, cancellationToken);
            tasks.Add(outdatedTask);
        }

        if (options.CheckDuplicates)
        {
            var duplicateTask = DetectDuplicatesAsync(memories, cancellationToken)
                .ContinueWith(t => result.DuplicateGroups = t.Result, cancellationToken);
            tasks.Add(duplicateTask);
        }

        if (options.TrackGaps && !string.IsNullOrEmpty(options.FocusQuery))
        {
            var gapTask = TrackEvidenceGapsAsync(userId, options.FocusQuery, cancellationToken)
                .ContinueWith(t => result.EvidenceGaps = t.Result, cancellationToken);
            tasks.Add(gapTask);
        }

        await Task.WhenAll(tasks);

        // Calculate health score
        result.HealthScore = CalculateHealthScore(result);

        // Generate suggested corrections
        result.SuggestedCorrections = GenerateSuggestedCorrections(result);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        LogMemoryAnalysisCompleteUserUserId(_logger, userId, result.MemoriesAnalyzed, result.HealthScore, stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryContradiction>> DetectContradictionsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        var contradictions = new List<MemoryContradiction>();

        if (memories.Count < 2)
            return contradictions;

        LogDetectingContradictionsCountMemories(_logger, memories.Count);

        // Group by related entities for efficient comparison
        var entityGroups = GroupMemoriesByEntities(memories);

        foreach (var (entity, groupMemories) in entityGroups)
        {
            if (groupMemories.Count < 2)
                continue;

            for (var i = 0; i < groupMemories.Count; i++)
            {
                for (var j = i + 1; j < groupMemories.Count; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var contradiction = await DetectContradictionBetweenAsync(
                        groupMemories[i],
                        groupMemories[j],
                        entity,
                        cancellationToken);

                    if (contradiction != null)
                    {
                        contradictions.Add(contradiction);
                    }
                }
            }
        }

        LogFoundCountContradictions(_logger, contradictions.Count);
        return contradictions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutdatedMemory>> IdentifyOutdatedMemoriesAsync(
        string userId,
        OutdatedDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new OutdatedDetectionOptions();
        var outdated = new List<OutdatedMemory>();

        var memories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = 1000 }, cancellationToken);
        var cutoffDate = DateTime.UtcNow.AddDays(-options.MaxAgeDays);

        foreach (var memory in memories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OutdatedMemory? outdatedInfo = null;

            // Check age
            if (memory.CreatedAt < cutoffDate)
            {
                outdatedInfo = new OutdatedMemory
                {
                    Memory = memory,
                    Reason = OutdatedReason.Age,
                    Explanation = $"Memory is {(DateTime.UtcNow - memory.CreatedAt).Days} days old",
                    Confidence = 0.7f,
                    SuggestedAction = OutdatedAction.Archive
                };
            }

            // Check for superseding memories
            if (options.CheckForSuperseding && outdatedInfo == null)
            {
                var superseding = await FindSupersedingMemoryAsync(memory, userId, cancellationToken);
                if (superseding != null)
                {
                    outdatedInfo = new OutdatedMemory
                    {
                        Memory = memory,
                        Reason = OutdatedReason.Superseded,
                        Explanation = "Newer memory with similar content exists",
                        Confidence = superseding.Value.confidence,
                        SupersededBy = superseding.Value.id,
                        SuggestedAction = OutdatedAction.Merge
                    };
                }
            }

            // Check confidence decay
            if (memory.Stability == MemoryStability.Volatile && memory.UpdatedAt < DateTime.UtcNow.AddDays(-7))
            {
                var daysSinceUpdate = (DateTime.UtcNow - memory.UpdatedAt).Days;
                var decayedConfidence = CalculateDecayedConfidence(1.0f, daysSinceUpdate, 30);

                if (decayedConfidence < options.MinConfidence)
                {
                    outdatedInfo = new OutdatedMemory
                    {
                        Memory = memory,
                        Reason = OutdatedReason.ConfidenceDecay,
                        Explanation = $"Confidence decayed to {decayedConfidence:F2} over {daysSinceUpdate} days",
                        Confidence = 0.8f,
                        SuggestedAction = OutdatedAction.FlagForReview
                    };
                }
            }

            if (outdatedInfo != null)
            {
                outdated.Add(outdatedInfo);
            }
        }

        LogIdentifiedCountOutdatedMemoriesUser(_logger, outdated.Count, userId);
        return outdated;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvidenceGap>> TrackEvidenceGapsAsync(
        string userId,
        string query,
        CancellationToken cancellationToken = default)
    {
        var gaps = new List<EvidenceGap>();

        // Extract entities from query
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && char.IsUpper(w[0]))
            .ToList();

        foreach (var entity in queryWords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check entity coverage
            var subjectTriples = await _entityStore.GetBySubjectAsync(entity, userId, cancellationToken);
            var objectTriples = await _entityStore.GetByObjectAsync(entity, userId, cancellationToken);

            var allTriples = (subjectTriples ?? []).Concat(objectTriples ?? []).ToList();

            if (allTriples.Count == 0)
            {
                gaps.Add(new EvidenceGap
                {
                    Description = $"No information found about '{entity}'",
                    RelatedEntities = [entity],
                    Importance = 0.8f,
                    SuggestedQueries = [$"What is {entity}?", $"Tell me about {entity}"]
                });
            }
            else if (allTriples.Count < 3)
            {
                gaps.Add(new EvidenceGap
                {
                    Description = $"Limited information about '{entity}' (only {allTriples.Count} facts)",
                    RelatedEntities = [entity],
                    PartialMemories = allTriples.Select(t => t.Id).ToList(),
                    Importance = 0.5f,
                    SuggestedQueries = [$"What else do you know about {entity}?"]
                });
            }
        }

        LogTrackedCountEvidenceGapsQuery(_logger, gaps.Count, query);
        return gaps;
    }

    /// <inheritdoc />
    public async Task<CorrectionResult> ApplyCorrectionsAsync(
        IReadOnlyList<MemoryCorrection> corrections,
        CorrectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CorrectionOptions();
        var stopwatch = Stopwatch.StartNew();

        var applied = new List<AppliedCorrection>();
        var failed = new List<FailedCorrection>();
        var skipped = new List<SkippedCorrection>();

        var filteredCorrections = corrections
            .Where(c => c.Priority >= options.MinPriority)
            .Take(options.MaxCorrectionsPerBatch)
            .ToList();

        foreach (var correction in filteredCorrections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Validate if required
                if (options.ValidateBeforeApply)
                {
                    var isValid = await ValidateCorrectionAsync(correction, cancellationToken);
                    if (!isValid)
                    {
                        skipped.Add(new SkippedCorrection
                        {
                            Correction = correction,
                            Reason = "Validation failed"
                        });
                        continue;
                    }
                }

                // Create backup if required
                Guid? backupId = null;
                if (options.CreateBackup)
                {
                    backupId = await CreateBackupAsync(correction.MemoryId, cancellationToken);
                }

                // Apply the correction
                await ApplySingleCorrectionAsync(correction, cancellationToken);

                applied.Add(new AppliedCorrection
                {
                    Correction = correction,
                    BackupId = backupId
                });

                // Record history
                if (options.RecordHistory)
                {
                    await RecordCorrectionAsync(correction, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogFailedApplyCorrectionId(_logger, ex, correction.Id);
                failed.Add(new FailedCorrection
                {
                    Correction = correction,
                    Error = ex.Message
                });
            }
        }

        stopwatch.Stop();

        LogCorrectionsAppliedAppliedAppliedFailed(_logger, applied.Count, failed.Count, skipped.Count, stopwatch.ElapsedMilliseconds);

        return new CorrectionResult
        {
            Success = failed.Count == 0,
            AppliedCorrections = applied,
            FailedCorrections = failed,
            SkippedCorrections = skipped,
            TotalProcessed = filteredCorrections.Count,
            Duration = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public async Task<ContradictionResolution> ResolveContradictionAsync(
        MemoryContradiction contradiction,
        ResolutionStrategy strategy = ResolutionStrategy.KeepNewest,
        CancellationToken cancellationToken = default)
    {
        LogResolvingContradictionIdStrategyStrategy(_logger, contradiction.Id, strategy);

        var resolution = new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = strategy
        };

        try
        {
            switch (strategy)
            {
                case ResolutionStrategy.KeepNewest:
                    resolution = await ResolveByKeepingNewestAsync(contradiction, cancellationToken);
                    break;

                case ResolutionStrategy.KeepOldest:
                    resolution = await ResolveByKeepingOldestAsync(contradiction, cancellationToken);
                    break;

                case ResolutionStrategy.KeepHigherConfidence:
                    resolution = await ResolveByHigherConfidenceAsync(contradiction, cancellationToken);
                    break;

                case ResolutionStrategy.Merge:
                    resolution = await ResolveByMergingAsync(contradiction, cancellationToken);
                    break;

                case ResolutionStrategy.MarkUncertain:
                    resolution = await ResolveByMarkingUncertainAsync(contradiction, cancellationToken);
                    break;

                case ResolutionStrategy.ManualReview:
                    resolution.Action = ResolutionAction.FlaggedForReview;
                    resolution.Success = true;
                    resolution.Explanation = "Flagged for manual review";
                    break;

                default:
                    resolution.Action = ResolutionAction.NoAction;
                    resolution.Success = false;
                    resolution.Explanation = "Unknown resolution strategy";
                    break;
            }
        }
        catch (Exception ex)
        {
            LogFailedResolveContradictionId(_logger, ex, contradiction.Id);
            resolution.Success = false;
            resolution.Explanation = $"Resolution failed: {ex.Message}";
        }

        return resolution;
    }

    /// <inheritdoc />
    public async Task<ConfidenceUpdateResult> UpdateConfidenceScoresAsync(
        string userId,
        ConfidenceUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ConfidenceUpdateOptions();
        var memories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = 10000 }, cancellationToken);

        var result = new ConfidenceUpdateResult
        {
            AverageConfidenceBefore = memories.Count > 0
                ? (float)memories.Average(m => GetConfidenceFromStability(m.Stability))
                : 0
        };

        var updatedCount = 0;
        var lowConfidenceIds = new List<Guid>();
        var confidenceSum = 0f;

        foreach (var memory in memories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentConfidence = GetConfidenceFromStability(memory.Stability);
            var newConfidence = currentConfidence;

            // Apply time decay
            if (options.ApplyTimeDecay)
            {
                var daysSinceUpdate = (DateTime.UtcNow - memory.UpdatedAt).Days;
                newConfidence = CalculateDecayedConfidence(
                    newConfidence,
                    daysSinceUpdate,
                    options.DecayHalfLifeDays);

                newConfidence = Math.Max(newConfidence, options.MinConfidenceAfterDecay);
            }

            // Update if changed significantly
            if (Math.Abs(newConfidence - currentConfidence) > 0.05f)
            {
                var newStability = GetStabilityFromConfidence(newConfidence);
                if (newStability != memory.Stability)
                {
                    memory.Stability = newStability;
                    memory.UpdatedAt = DateTime.UtcNow;
                    await _memoryStore.UpdateAsync(memory, cancellationToken);
                    updatedCount++;
                }
            }

            confidenceSum += newConfidence;

            if (newConfidence < 0.3f)
            {
                lowConfidenceIds.Add(memory.Id);
            }
        }

        result.MemoriesUpdated = updatedCount;
        result.AverageConfidenceAfter = memories.Count > 0 ? confidenceSum / memories.Count : 0;
        result.LowConfidenceMemories = lowConfidenceIds;

        LogConfidenceUpdateCompleteUpdatedMemories(_logger, result.MemoriesUpdated, result.AverageConfidenceBefore, result.AverageConfidenceAfter);

        return result;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CorrectionRecord>> GetCorrectionHistoryAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_correctionHistory.TryGetValue(userId, out var history))
        {
            return Task.FromResult<IReadOnlyList<CorrectionRecord>>([]);
        }

        var records = history
            .OrderByDescending(r => r.CorrectedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<CorrectionRecord>>(records);
    }

    #region Private Helper Methods

    private async Task<IReadOnlyList<MemoryUnit>> GetMemoriesForAnalysisAsync(
        string userId,
        MemoryAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.FocusQuery))
        {
            // Use embedding-based search
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(options.FocusQuery, cancellationToken);
            var searchResults = await _memoryStore.SearchAsync(
                queryEmbedding,
                new MemorySearchOptions { UserId = userId, Limit = options.MaxMemoriesToAnalyze },
                cancellationToken);
            return searchResults.Select(r => r.Memory).ToList();
        }

        return await _memoryStore.GetAllAsync(
            userId,
            new MemoryFilterOptions { Limit = options.MaxMemoriesToAnalyze },
            cancellationToken);
    }

    private static Dictionary<string, List<MemoryUnit>> GroupMemoriesByEntities(
        IReadOnlyList<MemoryUnit> memories)
    {
        var groups = new Dictionary<string, List<MemoryUnit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            // Extract entity-like words from content
            var entities = ExtractEntities(memory.Content);

            foreach (var entity in entities)
            {
                if (!groups.TryGetValue(entity, out var group))
                {
                    group = [];
                    groups[entity] = group;
                }
                group.Add(memory);
            }
        }

        return groups;
    }

    private static IEnumerable<string> ExtractEntities(string content)
    {
        if (string.IsNullOrEmpty(content))
            yield break;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var cleaned = word.Trim('.', ',', '!', '?', '"', '\'');
            if (cleaned.Length > 2 && char.IsUpper(cleaned[0]))
            {
                yield return cleaned;
            }
        }
    }

    private static async Task<MemoryContradiction?> DetectContradictionBetweenAsync(
        MemoryUnit memory1,
        MemoryUnit memory2,
        string sharedEntity,
        CancellationToken cancellationToken)
    {
        // Simple heuristic: check for negation patterns
        var content1Lower = memory1.Content.ToLowerInvariant();
        var content2Lower = memory2.Content.ToLowerInvariant();

        var negationPatterns = new[]
        {
            ("is not", "is"),
            ("isn't", "is"),
            ("was not", "was"),
            ("wasn't", "was"),
            ("does not", "does"),
            ("doesn't", "does"),
            ("never", "always"),
            ("no longer", "still")
        };

        foreach (var (negative, positive) in negationPatterns)
        {
            if ((content1Lower.Contains(negative) && content2Lower.Contains(positive)) ||
                (content1Lower.Contains(positive) && content2Lower.Contains(negative)))
            {
                return new MemoryContradiction
                {
                    Memory1 = memory1,
                    Memory2 = memory2,
                    Type = ContradictionType.Factual,
                    Description = $"Conflicting statements about '{sharedEntity}'",
                    Confidence = 0.7f,
                    ConflictingClaims =
                    [
                        new ConflictingClaim
                        {
                            Claim1 = TruncateContent(memory1.Content, 200),
                            Claim2 = TruncateContent(memory2.Content, 200),
                            ConflictReason = "Negation pattern detected"
                        }
                    ]
                };
            }
        }

        return null;
    }

    private async Task<(Guid id, float confidence)?> FindSupersedingMemoryAsync(
        MemoryUnit memory,
        string userId,
        CancellationToken cancellationToken)
    {
        // Use embedding-based search to find similar memories
        var embedding = await _embeddingService.GenerateEmbeddingAsync(memory.Content, cancellationToken);
        var newerMemories = await _memoryStore.SearchAsync(
            embedding,
            new MemorySearchOptions { UserId = userId, Limit = 5 },
            cancellationToken);

        foreach (var result in newerMemories)
        {
            if (result.Memory.Id != memory.Id &&
                result.Memory.CreatedAt > memory.CreatedAt &&
                result.Memory.Type == memory.Type)
            {
                // Calculate similarity
                var similarity = CalculateTextSimilarity(memory.Content, result.Memory.Content);
                if (similarity > 0.8f)
                {
                    return (result.Memory.Id, similarity);
                }
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<DuplicateGroup>> DetectDuplicatesAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var groups = new List<DuplicateGroup>();
        var processed = new HashSet<Guid>();

        for (var i = 0; i < memories.Count; i++)
        {
            if (processed.Contains(memories[i].Id))
                continue;

            var group = new List<Guid> { memories[i].Id };

            for (var j = i + 1; j < memories.Count; j++)
            {
                if (processed.Contains(memories[j].Id))
                    continue;

                var similarity = CalculateTextSimilarity(memories[i].Content, memories[j].Content);
                if (similarity >= DuplicateSimilarityThreshold)
                {
                    group.Add(memories[j].Id);
                    processed.Add(memories[j].Id);
                }
            }

            if (group.Count > 1)
            {
                processed.Add(memories[i].Id);

                // Find canonical (newest or highest tier)
                var canonical = memories
                    .Where(m => group.Contains(m.Id))
                    .OrderByDescending(m => m.Tier)
                    .ThenByDescending(m => m.CreatedAt)
                    .First();

                groups.Add(new DuplicateGroup
                {
                    MemoryIds = group,
                    Similarity = DuplicateSimilarityThreshold,
                    RecommendedCanonical = canonical.Id,
                    Reason = "Highest tier or newest"
                });
            }
        }

        return groups;
    }

    private static float CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        var words1 = text1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (float)intersection / union : 0;
    }

    private static float CalculateHealthScore(MemoryAnalysisResult result)
    {
        if (result.MemoriesAnalyzed == 0)
            return 1.0f;

        var contradictionPenalty = result.Contradictions.Count * 0.1f;
        var outdatedPenalty = result.OutdatedMemories.Count * 0.02f;
        var duplicatePenalty = result.DuplicateGroups.Count * 0.05f;
        var gapPenalty = result.EvidenceGaps.Count * 0.03f;

        var totalPenalty = contradictionPenalty + outdatedPenalty + duplicatePenalty + gapPenalty;
        return Math.Max(0, 1.0f - totalPenalty);
    }

    private static List<MemoryCorrection> GenerateSuggestedCorrections(MemoryAnalysisResult result)
    {
        var corrections = new List<MemoryCorrection>();

        // From contradictions
        foreach (var contradiction in result.Contradictions)
        {
            corrections.Add(new MemoryCorrection
            {
                MemoryId = contradiction.Memory2.Id, // Archive the older/lower confidence one
                Type = CorrectionType.Archive,
                Reason = $"Contradicts memory {contradiction.Memory1.Id}",
                Source = "contradiction_detection",
                Priority = CorrectionPriority.High
            });
        }

        // From outdated
        foreach (var outdated in result.OutdatedMemories)
        {
            corrections.Add(new MemoryCorrection
            {
                MemoryId = outdated.Memory.Id,
                Type = outdated.SuggestedAction switch
                {
                    OutdatedAction.Archive => CorrectionType.Archive,
                    OutdatedAction.Delete => CorrectionType.Delete,
                    _ => CorrectionType.TierChange
                },
                Reason = outdated.Explanation,
                Source = "outdated_detection",
                Priority = CorrectionPriority.Normal
            });
        }

        // From duplicates
        foreach (var group in result.DuplicateGroups)
        {
            foreach (var memoryId in group.MemoryIds.Where(id => id != group.RecommendedCanonical))
            {
                corrections.Add(new MemoryCorrection
                {
                    MemoryId = memoryId,
                    Type = CorrectionType.Delete,
                    Reason = $"Duplicate of {group.RecommendedCanonical}",
                    Source = "duplicate_detection",
                    Priority = CorrectionPriority.Low
                });
            }
        }

        return corrections;
    }

    private static float CalculateDecayedConfidence(float initial, int daysSinceUpdate, int halfLifeDays)
    {
        // Exponential decay: C(t) = C0 * 0.5^(t/halfLife)
        return initial * (float)Math.Pow(0.5, (double)daysSinceUpdate / halfLifeDays);
    }

    private static float GetConfidenceFromStability(MemoryStability stability)
    {
        return stability switch
        {
            MemoryStability.Permanent => 1.0f,
            MemoryStability.Consolidated => 0.9f,
            MemoryStability.Stable => 0.8f,
            MemoryStability.Stabilizing => 0.6f,
            MemoryStability.Volatile => 0.4f,
            _ => 0.5f
        };
    }

    private static MemoryStability GetStabilityFromConfidence(float confidence)
    {
        return confidence switch
        {
            >= 0.95f => MemoryStability.Permanent,
            >= 0.85f => MemoryStability.Consolidated,
            >= 0.7f => MemoryStability.Stable,
            >= 0.5f => MemoryStability.Stabilizing,
            _ => MemoryStability.Volatile
        };
    }

    private static Task<bool> ValidateCorrectionAsync(MemoryCorrection correction, CancellationToken cancellationToken)
    {
        // Basic validation
        if (correction.MemoryId == Guid.Empty)
            return Task.FromResult(false);

        if (correction.Type == CorrectionType.ContentUpdate && string.IsNullOrEmpty(correction.CorrectedContent))
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    private async Task<Guid?> CreateBackupAsync(Guid memoryId, CancellationToken cancellationToken)
    {
        var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
        if (memory == null)
            return null;

        // Create a backup copy at User tier (long-term storage)
        var backup = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = memory.UserId,
            SessionId = memory.SessionId,
            Content = $"[BACKUP] {memory.Content}",
            Type = memory.Type,
            Tier = Tier.Archive,
            Stability = memory.Stability,
            CreatedAt = memory.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        await _memoryStore.StoreAsync(backup, cancellationToken);
        return backup.Id;
    }

    private async Task ApplySingleCorrectionAsync(MemoryCorrection correction, CancellationToken cancellationToken)
    {
        var memory = await _memoryStore.GetByIdAsync(correction.MemoryId, cancellationToken);
        if (memory == null)
            return;

        switch (correction.Type)
        {
            case CorrectionType.ContentUpdate:
                if (!string.IsNullOrEmpty(correction.CorrectedContent))
                {
                    memory.Content = correction.CorrectedContent;
                    memory.UpdatedAt = DateTime.UtcNow;
                    await _memoryStore.UpdateAsync(memory, cancellationToken);
                }
                break;

            case CorrectionType.ConfidenceAdjustment:
                if (correction.NewConfidence.HasValue)
                {
                    memory.Stability = GetStabilityFromConfidence(correction.NewConfidence.Value);
                    memory.UpdatedAt = DateTime.UtcNow;
                    await _memoryStore.UpdateAsync(memory, cancellationToken);
                }
                break;

            case CorrectionType.Archive:
                // Move to User tier (long-term storage)
                memory.Tier = Tier.Archive;
                memory.UpdatedAt = DateTime.UtcNow;
                await _memoryStore.UpdateAsync(memory, cancellationToken);
                break;

            case CorrectionType.Delete:
                await _memoryStore.DeleteAsync(correction.MemoryId, false, cancellationToken);
                break;

            default:
                LogUnhandledCorrectionTypeType(_logger, correction.Type);
                break;
        }
    }

    private Task RecordCorrectionAsync(MemoryCorrection correction, CancellationToken cancellationToken)
    {
        var record = new CorrectionRecord
        {
            MemoryId = correction.MemoryId,
            Type = correction.Type,
            OriginalValue = correction.OriginalContent,
            NewValue = correction.CorrectedContent,
            Reason = correction.Reason,
            CorrectedAt = DateTime.UtcNow,
            Source = correction.Source
        };

        // Get or create user history
        var history = _correctionHistory.GetOrAdd(
            correction.Source,
            _ => new ConcurrentQueue<CorrectionRecord>());

        history.Enqueue(record);

        // Trim if too large
        while (history.Count > MaxHistoryPerUser)
        {
            history.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    private async Task<ContradictionResolution> ResolveByKeepingNewestAsync(
        MemoryContradiction contradiction,
        CancellationToken cancellationToken)
    {
        var newer = contradiction.Memory1.CreatedAt > contradiction.Memory2.CreatedAt
            ? contradiction.Memory1
            : contradiction.Memory2;
        var older = newer.Id == contradiction.Memory1.Id
            ? contradiction.Memory2
            : contradiction.Memory1;

        // Archive older memory to User tier
        older.Tier = Tier.Archive;
        older.UpdatedAt = DateTime.UtcNow;
        await _memoryStore.UpdateAsync(older, cancellationToken);

        return new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = ResolutionStrategy.KeepNewest,
            Action = ResolutionAction.KeptAndArchived,
            Success = true,
            KeptMemoryId = newer.Id,
            RemovedMemoryId = older.Id,
            Explanation = $"Kept newer memory ({newer.CreatedAt:yyyy-MM-dd}), archived older one"
        };
    }

    private async Task<ContradictionResolution> ResolveByKeepingOldestAsync(
        MemoryContradiction contradiction,
        CancellationToken cancellationToken)
    {
        var older = contradiction.Memory1.CreatedAt < contradiction.Memory2.CreatedAt
            ? contradiction.Memory1
            : contradiction.Memory2;
        var newer = older.Id == contradiction.Memory1.Id
            ? contradiction.Memory2
            : contradiction.Memory1;

        // Archive newer memory to User tier
        newer.Tier = Tier.Archive;
        newer.UpdatedAt = DateTime.UtcNow;
        await _memoryStore.UpdateAsync(newer, cancellationToken);

        return new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = ResolutionStrategy.KeepOldest,
            Action = ResolutionAction.KeptAndArchived,
            Success = true,
            KeptMemoryId = older.Id,
            RemovedMemoryId = newer.Id,
            Explanation = $"Kept older memory ({older.CreatedAt:yyyy-MM-dd}), archived newer one"
        };
    }

    private async Task<ContradictionResolution> ResolveByHigherConfidenceAsync(
        MemoryContradiction contradiction,
        CancellationToken cancellationToken)
    {
        var conf1 = GetConfidenceFromStability(contradiction.Memory1.Stability);
        var conf2 = GetConfidenceFromStability(contradiction.Memory2.Stability);

        var keeper = conf1 >= conf2 ? contradiction.Memory1 : contradiction.Memory2;
        var archiver = keeper.Id == contradiction.Memory1.Id
            ? contradiction.Memory2
            : contradiction.Memory1;

        archiver.Tier = Tier.Archive;
        archiver.UpdatedAt = DateTime.UtcNow;
        await _memoryStore.UpdateAsync(archiver, cancellationToken);

        return new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = ResolutionStrategy.KeepHigherConfidence,
            Action = ResolutionAction.KeptAndArchived,
            Success = true,
            KeptMemoryId = keeper.Id,
            RemovedMemoryId = archiver.Id,
            Explanation = $"Kept higher confidence memory (confidence: {Math.Max(conf1, conf2):F2})"
        };
    }

    private async Task<ContradictionResolution> ResolveByMergingAsync(
        MemoryContradiction contradiction,
        CancellationToken cancellationToken)
    {
        // Create a merged memory noting the contradiction
        var mergedContent = $"[Merged from contradicting memories]\n" +
                           $"Version 1 ({contradiction.Memory1.CreatedAt:yyyy-MM-dd}): {contradiction.Memory1.Content}\n" +
                           $"Version 2 ({contradiction.Memory2.CreatedAt:yyyy-MM-dd}): {contradiction.Memory2.Content}\n" +
                           $"[Note: These claims may conflict - requires verification]";

        var mergedMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = contradiction.Memory1.UserId,
            SessionId = contradiction.Memory1.SessionId,
            Content = mergedContent,
            Type = MemoryType.Fact,
            Tier = Tier.Long,
            Stability = MemoryStability.Volatile,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _memoryStore.StoreAsync(mergedMemory, cancellationToken);

        // Archive originals to User tier
        contradiction.Memory1.Tier = Tier.Archive;
        contradiction.Memory2.Tier = Tier.Archive;
        await _memoryStore.UpdateAsync(contradiction.Memory1, cancellationToken);
        await _memoryStore.UpdateAsync(contradiction.Memory2, cancellationToken);

        return new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = ResolutionStrategy.Merge,
            Action = ResolutionAction.Merged,
            Success = true,
            MergedMemoryId = mergedMemory.Id,
            Explanation = "Created merged memory noting the contradiction"
        };
    }

    private async Task<ContradictionResolution> ResolveByMarkingUncertainAsync(
        MemoryContradiction contradiction,
        CancellationToken cancellationToken)
    {
        contradiction.Memory1.Stability = MemoryStability.Volatile;
        contradiction.Memory2.Stability = MemoryStability.Volatile;
        contradiction.Memory1.UpdatedAt = DateTime.UtcNow;
        contradiction.Memory2.UpdatedAt = DateTime.UtcNow;

        await _memoryStore.UpdateAsync(contradiction.Memory1, cancellationToken);
        await _memoryStore.UpdateAsync(contradiction.Memory2, cancellationToken);

        return new ContradictionResolution
        {
            Contradiction = contradiction,
            Strategy = ResolutionStrategy.MarkUncertain,
            Action = ResolutionAction.UpdatedConfidence,
            Success = true,
            Explanation = "Marked both memories as volatile/uncertain"
        };
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return content.Length <= maxLength
            ? content
            : content[..maxLength] + "...";
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting memory analysis for user {UserId}")]
    private static partial void LogStartingMemoryAnalysisUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Memory analysis complete for user {UserId}: {Analyzed} memories, health={Health:F2}, {Duration}ms")]
    private static partial void LogMemoryAnalysisCompleteUserUserId(ILogger logger, string userId, int analyzed, float health, long duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detecting contradictions in {Count} memories")]
    private static partial void LogDetectingContradictionsCountMemories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} contradictions")]
    private static partial void LogFoundCountContradictions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Identified {Count} outdated memories for user {UserId}")]
    private static partial void LogIdentifiedCountOutdatedMemoriesUser(ILogger logger, int count, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tracked {Count} evidence gaps for query: {Query}")]
    private static partial void LogTrackedCountEvidenceGapsQuery(ILogger logger, int count, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to apply correction {Id}")]
    private static partial void LogFailedApplyCorrectionId(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Corrections applied: {Applied} applied, {Failed} failed, {Skipped} skipped in {Duration}ms")]
    private static partial void LogCorrectionsAppliedAppliedAppliedFailed(ILogger logger, int applied, int failed, int skipped, long duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolving contradiction {Id} with strategy {Strategy}")]
    private static partial void LogResolvingContradictionIdStrategyStrategy(ILogger logger, Guid id, ResolutionStrategy strategy);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resolve contradiction {Id}")]
    private static partial void LogFailedResolveContradictionId(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Confidence update complete: {Updated} memories updated, avg confidence {Before:F2} → {After:F2}")]
    private static partial void LogConfidenceUpdateCompleteUpdatedMemories(ILogger logger, int updated, float before, float after);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled correction type: {Type}")]
    private static partial void LogUnhandledCorrectionTypeType(ILogger logger, CorrectionType type);
}
