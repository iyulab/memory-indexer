using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of Virtual Context Management (VCM).
/// Acts as the "Operating System" for LLM memory paging.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 3 "Virtual Context Management"
/// Inspired by MemGPT's virtual context management approach.
/// </remarks>
public sealed partial class VirtualContextManager : IVirtualContextManager
{
    private readonly IShortTermMemory _workingMemory;
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly IScopeManager _scopeManager;
    private readonly ITierManager _tierManager;
    private readonly ITextCompletionService? _completionService;
    private readonly ILogger<VirtualContextManager> _logger;
    private readonly VCMOptions _options;
    private readonly VirtualContextState _state;

    public VirtualContextManager(
        IShortTermMemory workingMemory,
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        IScopeManager scopeManager,
        ITierManager tierManager,
        IOptions<VCMOptions> options,
        ILogger<VirtualContextManager> logger,
        ITextCompletionService? completionService = null)
    {
        _workingMemory = workingMemory;
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _scopeManager = scopeManager;
        _tierManager = tierManager;
        _completionService = completionService;
        _logger = logger;
        _options = options.Value;
        _state = new VirtualContextState
        {
            MaxTokenCapacity = _options.MaxTokenCapacity
        };
    }

    /// <inheritdoc />
    public VirtualContextState State => _state;

    /// <inheritdoc />
    public async Task InitializeAsync(
        string userId,
        string sessionId,
        string? initialContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        LogInitializingVcm(_logger, userId, sessionId);

        _state.UserId = userId;
        _state.SessionId = sessionId;
        _state.SessionStartedAt = DateTime.UtcNow;
        _state.IsInitialized = true;

        // Initialize scope tracking (3-axis model: Scope dimension)
        await _scopeManager.InitializeAsync(userId, sessionId, cancellationToken);

        // Clear any existing working memory
        await _workingMemory.ClearAsync(cancellationToken);

        // If initial context provided, page in relevant memories
        if (!string.IsNullOrWhiteSpace(initialContext))
        {
            await PageInAsync(initialContext, _options.InitialPageInCount, cancellationToken);
        }

        // Load locked memories (system prompts, core facts)
        await LoadLockedMemoriesAsync(userId, cancellationToken);

        // Update state
        await UpdateStateAsync(cancellationToken);

        LogVcmInitialized(_logger, _workingMemory.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> PageInAsync(
        string query,
        int maxItems = 3,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        LogPagingIn(_logger, query);

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search for relevant memories not in working memory
        var searchOptions = new MemorySearchOptions
        {
            UserId = _state.UserId!,
            Limit = maxItems * 2, // Get extra for filtering
            MinScore = _options.MinRelevanceScore
        };

        var results = await _memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);

        // Filter out memories already in working memory
        var candidates = results
            .Where(r => !_workingMemory.Contains(r.Memory.Id))
            .OrderByDescending(r => r.Score)
            .Take(maxItems)
            .ToList();

        var pagedIn = new List<MemoryUnit>();

        foreach (var result in candidates)
        {
            var memory = result.Memory;

            // Promote to working memory
            var evicted = await _workingMemory.PromoteAsync(memory, cancellationToken);

            if (evicted != null)
            {
                // Handle evicted memory - demote using tier manager
                var demoteResult = await _tierManager.DemoteAsync(
                    evicted,
                    Tier.Long,
                    PromotionReason.CapacityEviction,
                    cancellationToken);

                if (demoteResult.Success && demoteResult.UpdatedMemory != null)
                {
                    await _memoryStore.UpdateAsync(demoteResult.UpdatedMemory, cancellationToken);
                    LogEvictedMemory(_logger, evicted.Id);
                }
            }

            // Promote memory using tier manager
            var promoteResult = await _tierManager.PromoteAsync(
                memory,
                Tier.Short,
                PromotionReason.Manual,
                cancellationToken);

            if (promoteResult.Success && promoteResult.UpdatedMemory != null)
            {
                promoteResult.UpdatedMemory.RecordAccess();
                await _memoryStore.UpdateAsync(promoteResult.UpdatedMemory, cancellationToken);
                pagedIn.Add(promoteResult.UpdatedMemory);
            }
        }

        await UpdateStateAsync(cancellationToken);

        LogPagedIn(_logger, pagedIn.Count);

        return pagedIn;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> PageInByIdAsync(Guid memoryId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // Check if already in working memory
        if (_workingMemory.Contains(memoryId))
        {
            await _workingMemory.TouchAsync(memoryId, 0.1f, cancellationToken);
            return await _workingMemory.GetAsync(memoryId, cancellationToken);
        }

        var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        // Promote to working memory
        var evicted = await _workingMemory.PromoteAsync(memory, cancellationToken);

        if (evicted != null)
        {
            var demoteResult = await _tierManager.DemoteAsync(
                evicted,
                Tier.Long,
                PromotionReason.CapacityEviction,
                cancellationToken);

            if (demoteResult.Success && demoteResult.UpdatedMemory != null)
            {
                await _memoryStore.UpdateAsync(demoteResult.UpdatedMemory, cancellationToken);
            }
        }

        var promoteResult = await _tierManager.PromoteAsync(
            memory,
            Tier.Short,
            PromotionReason.Manual,
            cancellationToken);

        if (promoteResult.Success && promoteResult.UpdatedMemory != null)
        {
            promoteResult.UpdatedMemory.RecordAccess();
            await _memoryStore.UpdateAsync(promoteResult.UpdatedMemory, cancellationToken);
        }

        await UpdateStateAsync(cancellationToken);

        return promoteResult.UpdatedMemory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> PageOutAsync(int count = 1, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var pagedOut = new List<MemoryUnit>();

        for (int i = 0; i < count; i++)
        {
            var candidate = await _workingMemory.GetEvictionCandidateAsync(cancellationToken);
            if (candidate == null)
            {
                break;
            }

            var demoted = await _workingMemory.DemoteAsync(candidate.Id, cancellationToken);
            if (demoted != null)
            {
                var demoteResult = await _tierManager.DemoteAsync(
                    demoted,
                    Tier.Long,
                    PromotionReason.LowRetention,
                    cancellationToken);

                if (demoteResult.Success && demoteResult.UpdatedMemory != null)
                {
                    await _memoryStore.UpdateAsync(demoteResult.UpdatedMemory, cancellationToken);
                    pagedOut.Add(demoteResult.UpdatedMemory);
                }
            }
        }

        await UpdateStateAsync(cancellationToken);

        LogPagedOut(_logger, pagedOut.Count);

        return pagedOut;
    }

    /// <inheritdoc />
    public async Task<EvictionResult> DefensiveEvictAsync(
        ContextSaturationLevel targetSaturation = ContextSaturationLevel.Normal,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        LogStartingDefensiveEviction(_logger, targetSaturation);

        var targetPercentage = targetSaturation switch
        {
            ContextSaturationLevel.Normal => 75f,
            ContextSaturationLevel.Elevated => 85f,
            ContextSaturationLevel.High => 95f,
            _ => 100f
        };

        var evictedCount = 0;
        var demotedCount = 0;
        var tokensFreed = 0;
        var affectedIds = new List<Guid>();

        while (_state.SaturationPercentage > targetPercentage)
        {
            var candidate = await _workingMemory.GetEvictionCandidateAsync(cancellationToken);
            if (candidate == null)
            {
                break;
            }

            var demoted = await _workingMemory.DemoteAsync(candidate.Id, cancellationToken);
            if (demoted != null)
            {
                var demoteResult = await _tierManager.DemoteAsync(
                    demoted,
                    Tier.Long,
                    PromotionReason.CapacityEviction,
                    cancellationToken);

                if (demoteResult.Success && demoteResult.UpdatedMemory != null)
                {
                    await _memoryStore.UpdateAsync(demoteResult.UpdatedMemory, cancellationToken);
                    demotedCount++;
                    tokensFreed += EstimateTokens(demoteResult.UpdatedMemory.Content);
                    affectedIds.Add(demoteResult.UpdatedMemory.Id);
                }
            }

            await UpdateStateAsync(cancellationToken);

            // Prevent infinite loop
            if (demotedCount > _workingMemory.Capacity)
            {
                break;
            }
        }

        // Try to merge similar evicted memories to reduce memory count
        var summarizedCount = 0;
        if (_completionService != null && affectedIds.Count >= 2)
        {
            var evictedMemories = new List<MemoryUnit>();
            foreach (var id in affectedIds)
            {
                var m = await _memoryStore.GetByIdAsync(id, cancellationToken);
                if (m != null)
                {
                    evictedMemories.Add(m);
                }
            }

            var mergeGroups = FindMergeGroups(evictedMemories, 0.85f);
            foreach (var group in mergeGroups)
            {
                var merged = await MergeMemoryGroupAsync(group, cancellationToken);
                if (merged == null) continue;

                merged.Tier = Tier.Long;
                var stored = await _memoryStore.StoreAsync(merged, cancellationToken);

                foreach (var source in group)
                {
                    await _memoryStore.DeleteAsync(source.Id, hardDelete: false, cancellationToken);
                }

                summarizedCount += group.Count;
                LogMergedEvictedMemories(_logger, group.Count, stored.Id);
            }
        }

        LogDefensiveEvictionComplete(_logger, demotedCount, tokensFreed);

        return new EvictionResult
        {
            EvictedCount = evictedCount,
            DemotedCount = demotedCount,
            SummarizedCount = summarizedCount,
            TokensFreed = tokensFreed,
            AffectedIds = affectedIds,
            NewSaturationLevel = _state.SaturationLevel
        };
    }

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidateAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        LogStartingConsolidation(_logger, _state.UserId!);

        var stabilityUpgradedCount = 0;
        var promotedCount = 0;
        var newMergedIds = new List<Guid>();

        // Update retention scores for all session memories
        var sessionMemories = await _memoryStore.GetAllAsync(
            _state.UserId!,
            new MemoryFilterOptions { Limit = 1000 },
            cancellationToken);

        foreach (var memory in sessionMemories)
        {
            var originalStability = memory.Stability;
            var originalRetention = memory.RetentionScore;

            // Update retention based on Ebbinghaus curve
            memory.RetentionScore = memory.CalculateRetention();

            // Check for stability upgrade based on access patterns
            if (memory.AccessCount >= 10 && memory.Stability < MemoryStability.Consolidated)
            {
                memory.Stability = MemoryStability.Consolidated;
                stabilityUpgradedCount++;
            }
            else if (memory.AccessCount >= 5 && memory.Stability < MemoryStability.Stable)
            {
                memory.Stability = MemoryStability.Stable;
                stabilityUpgradedCount++;
            }
            else if (memory.AccessCount >= 2 && memory.Stability < MemoryStability.Stabilizing)
            {
                memory.Stability = MemoryStability.Stabilizing;
                stabilityUpgradedCount++;
            }

            // Evaluate promotion to Archive tier using tier manager
            if (memory.Tier == Tier.Long)
            {
                var context = new TierEvaluationContext
                {
                    UserId = _state.UserId!,
                    SessionId = _state.SessionId,
                    TurnCount = 0,
                    TokenCount = 0,
                    TimeElapsed = TimeSpan.Zero,
                    SessionEnding = false
                };

                var recommendation = await _tierManager.EvaluatePromotionAsync(memory, context, cancellationToken);

                if (recommendation.ShouldPromote && recommendation.TargetTier == Tier.Archive)
                {
                    var promoteResult = await _tierManager.PromoteAsync(
                        memory,
                        Tier.Archive,
                        recommendation.Reason,
                        cancellationToken);

                    if (promoteResult.Success)
                    {
                        promotedCount++;
                        // Memory already updated by tier manager
                        continue;
                    }
                }
            }

            // Only update if something changed (and not already updated by promotion)
            if (memory.Stability != originalStability ||
                Math.Abs(memory.RetentionScore - originalRetention) > 0.01f)
            {
                memory.MarkUpdated();
                await _memoryStore.UpdateAsync(memory, cancellationToken);
            }
        }

        // Merge similar memories using LLM summarization
        var mergedCount = 0;
        var summarizedCount = 0;
        if (_completionService != null)
        {
            var mergeGroups = FindMergeGroups(sessionMemories, 0.85f);
            foreach (var group in mergeGroups)
            {
                var merged = await MergeMemoryGroupAsync(group, cancellationToken);
                if (merged == null) continue;

                var stored = await _memoryStore.StoreAsync(merged, cancellationToken);
                newMergedIds.Add(stored.Id);

                foreach (var source in group)
                {
                    source.SupersedesId = stored.Id;
                    source.MarkUpdated();
                    await _memoryStore.UpdateAsync(source, cancellationToken);
                    await _memoryStore.DeleteAsync(source.Id, hardDelete: false, cancellationToken);
                }

                mergedCount += group.Count;
                LogMergedMemories(_logger, group.Count, stored.Id);
            }

            summarizedCount = mergedCount;
        }

        LogConsolidationComplete(_logger, stabilityUpgradedCount, promotedCount);

        return new ConsolidationResult
        {
            MergedCount = mergedCount,
            StabilityUpgradedCount = stabilityUpgradedCount,
            SummarizedCount = summarizedCount,
            PromotedCount = promotedCount,
            NewMergedIds = newMergedIds
        };
    }

    /// <inheritdoc />
    public async Task<int> UpdateRetentionScoresAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var memories = await _memoryStore.GetAllAsync(
            _state.UserId!,
            new MemoryFilterOptions { Limit = 10000 },
            cancellationToken);

        var updatedCount = 0;

        foreach (var memory in memories)
        {
            if (memory.IsLocked || memory.Stability == MemoryStability.Permanent)
            {
                continue;
            }

            var newRetention = memory.CalculateRetention();

            if (Math.Abs(memory.RetentionScore - newRetention) > 0.01f)
            {
                memory.RetentionScore = newRetention;
                memory.MarkUpdated();
                await _memoryStore.UpdateAsync(memory, cancellationToken);
                updatedCount++;
            }
        }

        LogUpdatedRetentionScores(_logger, updatedCount);

        return updatedCount;
    }

    /// <inheritdoc />
    public async Task<SessionEndResult> EndSessionAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var sessionStart = _state.SessionStartedAt ?? DateTime.UtcNow;
        var sessionId = _state.SessionId!;

        LogEndingSession(_logger, sessionId);

        // Clear working memory
        var clearedMemories = await _workingMemory.ClearAsync(cancellationToken);

        // Get session memories
        var sessionMemories = await _memoryStore.GetAllAsync(
            _state.UserId!,
            new MemoryFilterOptions
            {
                SessionId = sessionId,
                Limit = 10000
            },
            cancellationToken);

        var migratedIds = new List<Guid>();
        var discardedCount = 0;

        foreach (var memory in sessionMemories)
        {
            // Update retention before migration decision
            memory.RetentionScore = memory.CalculateRetention();

            // Migrate memories above retention threshold to Archive tier using tier manager
            if (memory.RetentionScore >= _options.SessionMigrationThreshold)
            {
                var promoteResult = await _tierManager.PromoteAsync(
                    memory,
                    Tier.Archive,
                    PromotionReason.SessionBoundary,
                    cancellationToken);

                if (promoteResult.Success && promoteResult.UpdatedMemory != null)
                {
                    await _memoryStore.UpdateAsync(promoteResult.UpdatedMemory, cancellationToken);
                    migratedIds.Add(promoteResult.UpdatedMemory.Id);
                }
            }
            else if (!memory.IsLocked)
            {
                // Soft delete low-retention memories
                await _memoryStore.DeleteAsync(memory.Id, hardDelete: false, cancellationToken);
                discardedCount++;
            }
        }

        // End scope tracking
        await _scopeManager.EndSessionAsync(cancellationToken);

        // Reset state
        _state.IsInitialized = false;
        _state.SessionId = null;

        LogSessionEnded(_logger, migratedIds.Count, discardedCount);

        return new SessionEndResult
        {
            SessionId = sessionId,
            MigrationResult = new TierMigrationResult
            {
                MigratedCount = migratedIds.Count,
                DiscardedCount = discardedCount,
                SkippedCount = sessionMemories.Count - migratedIds.Count - discardedCount,
                MigratedIds = migratedIds
            },
            DiscardedCount = discardedCount,
            WorkingMemoryClearedCount = clearedMemories.Count,
            SessionDuration = DateTime.UtcNow - sessionStart
        };
    }

    /// <inheritdoc />
    public async Task<ContextUsageStatistics> GetContextUsageAsync(CancellationToken cancellationToken = default)
    {
        await UpdateStateAsync(cancellationToken);

        var workingMemories = await _workingMemory.GetAllAsync(cancellationToken);
        var lockedTokens = workingMemories
            .Where(m => m.IsLocked)
            .Sum(m => EstimateTokens(m.Content));

        var userMemoryCount = 0L;

        if (_state.IsInitialized)
        {
            userMemoryCount = await _memoryStore.GetCountAsync(_state.UserId!, cancellationToken);
        }

        return new ContextUsageStatistics
        {
            TotalTokens = _state.WorkingMemoryTokens,
            WorkingMemoryTokens = _state.WorkingMemoryTokens,
            SystemPromptTokens = lockedTokens,
            AvailableTokens = _state.MaxTokenCapacity - _state.WorkingMemoryTokens,
            SaturationLevel = _state.SaturationLevel,
            SaturationPercentage = _state.SaturationPercentage,
            WorkingMemoryCount = _workingMemory.Count,
            SessionMemoryCount = _workingMemory.Count, // Working memory is session-scoped
            UserMemoryCount = (int)userMemoryCount,
            Recommendation = GetRecommendation(_state.SaturationPercentage)
        };
    }

    /// <inheritdoc />
    public async Task OptimizeWorkingMemoryAsync(string currentContext, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        LogOptimizingWorkingMemory(_logger);

        // Generate context embedding
        var contextEmbedding = await _embeddingService.GenerateEmbeddingAsync(currentContext, cancellationToken);

        // Get current working memories
        var workingMemories = await _workingMemory.GetAllAsync(cancellationToken);

        // Score relevance to current context
        var scoredMemories = workingMemories
            .Where(m => !m.IsLocked)
            .Select(m => new
            {
                Memory = m,
                Score = _scoringService.CalculateScore(m, contextEmbedding)
            })
            .OrderBy(x => x.Score)
            .ToList();

        // Page out low-relevance memories if at capacity or saturation is high
        if (_state.SaturationLevel >= ContextSaturationLevel.Elevated)
        {
            var toPageOut = scoredMemories
                .Where(x => x.Score < _options.MinRelevanceScore)
                .Take(2)
                .ToList();

            foreach (var item in toPageOut)
            {
                var demoted = await _workingMemory.DemoteAsync(item.Memory.Id, cancellationToken);
                if (demoted != null)
                {
                    var demoteResult = await _tierManager.DemoteAsync(
                        demoted,
                        Tier.Long,
                        PromotionReason.LowRetention,
                        cancellationToken);

                    if (demoteResult.Success && demoteResult.UpdatedMemory != null)
                    {
                        await _memoryStore.UpdateAsync(demoteResult.UpdatedMemory, cancellationToken);
                    }
                }
            }

            LogOptimizedPagedOut(_logger, toPageOut.Count);
        }

        // Update relevance scores for remaining memories
        foreach (var item in scoredMemories)
        {
            await _workingMemory.TouchAsync(item.Memory.Id, item.Score - 0.5f, cancellationToken);
        }

        await UpdateStateAsync(cancellationToken);
    }

    #region Helper Methods

    private void EnsureInitialized()
    {
        if (!_state.IsInitialized)
        {
            throw new InvalidOperationException("VCM must be initialized before use. Call InitializeAsync first.");
        }
    }

    private async Task LoadLockedMemoriesAsync(string userId, CancellationToken cancellationToken)
    {
        var memories = await _memoryStore.GetAllAsync(
            userId,
            new MemoryFilterOptions { Limit = 100 },
            cancellationToken);

        var lockedMemories = memories.Where(m => m.IsLocked).ToList();

        foreach (var memory in lockedMemories)
        {
            await _workingMemory.PromoteAsync(memory, cancellationToken);
        }

        LogLoadedLockedMemories(_logger, lockedMemories.Count);
    }

    private async Task UpdateStateAsync(CancellationToken cancellationToken)
    {
        var workingMemories = await _workingMemory.GetAllAsync(cancellationToken);

        _state.WorkingMemoryTokens = workingMemories.Sum(m => EstimateTokens(m.Content));
        _state.SaturationLevel = GetSaturationLevel(_state.SaturationPercentage);
    }

    private static int EstimateTokens(string content)
    {
        // Rough estimate: ~4 characters per token
        return (int)Math.Ceiling(content.Length / 4.0);
    }

    private static ContextSaturationLevel GetSaturationLevel(float percentage) => percentage switch
    {
        < 75f => ContextSaturationLevel.Normal,
        < 85f => ContextSaturationLevel.Elevated,
        < 95f => ContextSaturationLevel.High,
        _ => ContextSaturationLevel.Critical
    };

    private static ContextActionRecommendation GetRecommendation(float saturationPercentage) => saturationPercentage switch
    {
        < 60f => ContextActionRecommendation.None,
        < 75f => ContextActionRecommendation.ConsiderSummarization,
        < 85f => ContextActionRecommendation.ShouldPageOut,
        < 95f => ContextActionRecommendation.ImmediateEvictionRequired,
        _ => ContextActionRecommendation.Critical
    };

    private List<List<MemoryUnit>> FindMergeGroups(
        IReadOnlyList<MemoryUnit> memories, float similarityThreshold)
    {
        var groups = new List<List<MemoryUnit>>();
        var assigned = new HashSet<Guid>();

        var withEmbeddings = memories
            .Where(m => m.Embedding.HasValue && !m.IsLocked && !m.IsDeleted && m.Tier != Tier.Archive)
            .ToList();

        for (var i = 0; i < withEmbeddings.Count; i++)
        {
            if (assigned.Contains(withEmbeddings[i].Id)) continue;

            var group = new List<MemoryUnit> { withEmbeddings[i] };
            assigned.Add(withEmbeddings[i].Id);

            for (var j = i + 1; j < withEmbeddings.Count; j++)
            {
                if (assigned.Contains(withEmbeddings[j].Id)) continue;

                var similarity = _scoringService.CalculateCosineSimilarity(
                    withEmbeddings[i].Embedding!.Value, withEmbeddings[j].Embedding!.Value);

                if (similarity >= similarityThreshold)
                {
                    group.Add(withEmbeddings[j]);
                    assigned.Add(withEmbeddings[j].Id);
                }
            }

            if (group.Count >= 2)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private async Task<MemoryUnit?> MergeMemoryGroupAsync(
        List<MemoryUnit> group, CancellationToken cancellationToken)
    {
        var combinedContent = string.Join("\n\n", group.Select(m => m.Content));

        try
        {
            LogMergingMemoryGroup(_logger, group.Count);

            var prompt = "Merge the following related memories into a single, concise synthesis.\n"
                + "Preserve all key facts, entities, and relationships.\n"
                + "Remove redundancy while keeping important details.\n\n"
                + "Memories:\n"
                + combinedContent + "\n\n"
                + "Merged memory:";

            var mergedContent = await _completionService!.CompleteAsync(
                prompt,
                new TextCompletionOptions { Temperature = 0.1f, MaxTokens = 500 },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(mergedContent))
            {
                return null;
            }

            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                mergedContent, cancellationToken);

            var primary = group.OrderByDescending(m => m.ImportanceScore).First();

            return new MemoryUnit
            {
                UserId = primary.UserId,
                SessionId = primary.SessionId,
                Content = mergedContent,
                Embedding = embedding,
                ImportanceScore = group.Max(m => m.ImportanceScore),
                Tier = primary.Tier,
                Type = MemoryType.Semantic,
                Stability = MemoryStability.Stable,
                Confidence = group.Average(m => m.Confidence),
                AccessCount = group.Sum(m => m.AccessCount),
                Topics = group.SelectMany(m => m.Topics ?? []).Distinct().ToList(),
                Entities = group.SelectMany(m => m.Entities ?? []).Distinct().ToList(),
                Metadata = new Dictionary<string, string>
                {
                    ["MergedFrom"] = string.Join(",", group.Select(m => m.Id))
                }
            };
        }
        catch (Exception ex)
        {
            LogMergeGroupFailed(_logger, group.Count, ex);
            return null;
        }
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing VCM for user {UserId}, session {SessionId}")]
    private static partial void LogInitializingVcm(ILogger logger, string userId, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "VCM initialized with {Count} working memories")]
    private static partial void LogVcmInitialized(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Paging in memories for query: {Query}")]
    private static partial void LogPagingIn(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evicted memory {MemoryId} to Long tier")]
    private static partial void LogEvictedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Paged in {Count} memories")]
    private static partial void LogPagedIn(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Paged out {Count} memories")]
    private static partial void LogPagedOut(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting defensive eviction, target saturation: {Target}")]
    private static partial void LogStartingDefensiveEviction(ILogger logger, ContextSaturationLevel target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Defensive eviction complete: {Demoted} demoted, {Tokens} tokens freed")]
    private static partial void LogDefensiveEvictionComplete(ILogger logger, int demoted, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting memory consolidation for user {UserId}")]
    private static partial void LogStartingConsolidation(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation complete: {Upgraded} stability upgrades, {Promoted} promotions")]
    private static partial void LogConsolidationComplete(ILogger logger, int upgraded, int promoted);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated retention scores for {Count} memories")]
    private static partial void LogUpdatedRetentionScores(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ending session {SessionId}")]
    private static partial void LogEndingSession(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session ended: {Migrated} migrated, {Discarded} discarded")]
    private static partial void LogSessionEnded(ILogger logger, int migrated, int discarded);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Optimizing working memory for context")]
    private static partial void LogOptimizingWorkingMemory(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Optimized: paged out {Count} low-relevance memories")]
    private static partial void LogOptimizedPagedOut(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {Count} locked memories")]
    private static partial void LogLoadedLockedMemories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Merging group of {Count} similar memories")]
    private static partial void LogMergingMemoryGroup(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to merge group of {Count} memories")]
    private static partial void LogMergeGroupFailed(ILogger logger, int count, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merged {Count} evicted memories into {MergedId}")]
    private static partial void LogMergedEvictedMemories(ILogger logger, int count, Guid mergedId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merged {Count} similar memories into {MergedId}")]
    private static partial void LogMergedMemories(ILogger logger, int count, Guid mergedId);
}

/// <summary>
/// Configuration options for Virtual Context Manager.
/// </summary>
public sealed class VCMOptions
{
    /// <summary>
    /// Maximum token capacity for working memory.
    /// Default: 8000 (leaving room for system prompts and responses).
    /// </summary>
    public int MaxTokenCapacity { get; set; } = 8000;

    /// <summary>
    /// Number of memories to page in during initialization.
    /// </summary>
    public int InitialPageInCount { get; set; } = 3;

    /// <summary>
    /// Minimum relevance score for paging in memories.
    /// </summary>
    public float MinRelevanceScore { get; set; } = 0.3f;

    /// <summary>
    /// Retention threshold for migrating session memories to user tier.
    /// </summary>
    public float SessionMigrationThreshold { get; set; } = 0.3f;

    /// <summary>
    /// Interval for periodic consolidation.
    /// </summary>
    public TimeSpan ConsolidationInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Enable automatic eviction when saturation is high.
    /// </summary>
    public bool EnableAutoEviction { get; set; } = true;

    /// <summary>
    /// Saturation level that triggers automatic eviction.
    /// </summary>
    public ContextSaturationLevel AutoEvictionTrigger { get; set; } = ContextSaturationLevel.High;
}
