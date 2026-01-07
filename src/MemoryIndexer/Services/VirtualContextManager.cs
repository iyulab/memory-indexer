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
public sealed class VirtualContextManager : IVirtualContextManager
{
    private readonly IShortTermMemory _workingMemory;
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly ILogger<VirtualContextManager> _logger;
    private readonly VCMOptions _options;
    private readonly VirtualContextState _state;

    public VirtualContextManager(
        IShortTermMemory workingMemory,
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        IOptions<VCMOptions> options,
        ILogger<VirtualContextManager> logger)
    {
        _workingMemory = workingMemory;
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
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

        _logger.LogInformation("Initializing VCM for user {UserId}, session {SessionId}", userId, sessionId);

        _state.UserId = userId;
        _state.SessionId = sessionId;
        _state.IsInitialized = true;

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

        _logger.LogInformation("VCM initialized with {Count} working memories", _workingMemory.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> PageInAsync(
        string query,
        int maxItems = 3,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogDebug("Paging in memories for query: {Query}", query);

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
                // Handle evicted memory - update in store
                evicted.Tier = Tier.Long;
                await _memoryStore.UpdateAsync(evicted, cancellationToken);
                _logger.LogDebug("Evicted memory {MemoryId} to session tier", evicted.Id);
            }

            memory.Tier = Tier.Short;
            memory.RecordAccess();
            await _memoryStore.UpdateAsync(memory, cancellationToken);

            pagedIn.Add(memory);
        }

        await UpdateStateAsync(cancellationToken);

        _logger.LogInformation("Paged in {Count} memories", pagedIn.Count);

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
            evicted.Tier = Tier.Long;
            await _memoryStore.UpdateAsync(evicted, cancellationToken);
        }

        memory.Tier = Tier.Short;
        memory.RecordAccess();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        await UpdateStateAsync(cancellationToken);

        return memory;
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
                demoted.Tier = Tier.Long;
                await _memoryStore.UpdateAsync(demoted, cancellationToken);
                pagedOut.Add(demoted);
            }
        }

        await UpdateStateAsync(cancellationToken);

        _logger.LogDebug("Paged out {Count} memories", pagedOut.Count);

        return pagedOut;
    }

    /// <inheritdoc />
    public async Task<EvictionResult> DefensiveEvictAsync(
        ContextSaturationLevel targetSaturation = ContextSaturationLevel.Normal,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("Starting defensive eviction, target saturation: {Target}", targetSaturation);

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
                demoted.Tier = Tier.Long;
                await _memoryStore.UpdateAsync(demoted, cancellationToken);

                demotedCount++;
                tokensFreed += EstimateTokens(demoted.Content);
                affectedIds.Add(demoted.Id);
            }

            await UpdateStateAsync(cancellationToken);

            // Prevent infinite loop
            if (demotedCount > _workingMemory.Capacity)
            {
                break;
            }
        }

        _logger.LogInformation("Defensive eviction complete: {Demoted} demoted, {Tokens} tokens freed",
            demotedCount, tokensFreed);

        return new EvictionResult
        {
            EvictedCount = evictedCount,
            DemotedCount = demotedCount,
            SummarizedCount = 0, // TODO: Implement summarization during eviction
            TokensFreed = tokensFreed,
            AffectedIds = affectedIds,
            NewSaturationLevel = _state.SaturationLevel
        };
    }

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidateAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogInformation("Starting memory consolidation for user {UserId}", _state.UserId);

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

            // Promote frequently accessed session memories to user tier
            if (memory.Tier == Tier.Long &&
                memory.Stability >= MemoryStability.Stable &&
                memory.RetentionScore > 0.7f)
            {
                memory.Tier = Tier.Archive;
                promotedCount++;
            }

            // Only update if something changed
            if (memory.Stability != originalStability ||
                Math.Abs(memory.RetentionScore - originalRetention) > 0.01f ||
                memory.Tier == Tier.Archive)
            {
                memory.MarkUpdated();
                await _memoryStore.UpdateAsync(memory, cancellationToken);
            }
        }

        _logger.LogInformation("Consolidation complete: {Upgraded} stability upgrades, {Promoted} promotions",
            stabilityUpgradedCount, promotedCount);

        return new ConsolidationResult
        {
            MergedCount = 0, // TODO: Implement memory merging
            StabilityUpgradedCount = stabilityUpgradedCount,
            SummarizedCount = 0, // TODO: Implement summarization
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

        _logger.LogDebug("Updated retention scores for {Count} memories", updatedCount);

        return updatedCount;
    }

    /// <inheritdoc />
    public async Task<SessionEndResult> EndSessionAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var sessionStart = DateTime.UtcNow.AddHours(-1); // TODO: Track actual session start
        var sessionId = _state.SessionId!;

        _logger.LogInformation("Ending session {SessionId}", sessionId);

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

            // Migrate memories above retention threshold to user tier
            if (memory.RetentionScore >= _options.SessionMigrationThreshold)
            {
                memory.Tier = Tier.Archive;
                memory.MarkUpdated();
                await _memoryStore.UpdateAsync(memory, cancellationToken);
                migratedIds.Add(memory.Id);
            }
            else if (!memory.IsLocked)
            {
                // Soft delete low-retention memories
                await _memoryStore.DeleteAsync(memory.Id, hardDelete: false, cancellationToken);
                discardedCount++;
            }
        }

        // Reset state
        _state.IsInitialized = false;
        _state.SessionId = null;

        _logger.LogInformation("Session ended: {Migrated} migrated, {Discarded} discarded",
            migratedIds.Count, discardedCount);

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

        var sessionCount = 0L;

        if (_state.IsInitialized)
        {
            sessionCount = await _memoryStore.GetCountAsync(_state.UserId!, cancellationToken);
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
            SessionMemoryCount = (int)sessionCount,
            UserMemoryCount = 0, // TODO: Track user tier separately
            Recommendation = GetRecommendation(_state.SaturationPercentage)
        };
    }

    /// <inheritdoc />
    public async Task OptimizeWorkingMemoryAsync(string currentContext, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _logger.LogDebug("Optimizing working memory for context");

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
                await _workingMemory.DemoteAsync(item.Memory.Id, cancellationToken);
                item.Memory.Tier = Tier.Long;
                await _memoryStore.UpdateAsync(item.Memory, cancellationToken);
            }

            _logger.LogDebug("Optimized: paged out {Count} low-relevance memories", toPageOut.Count);
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

        _logger.LogDebug("Loaded {Count} locked memories", lockedMemories.Count);
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

    #endregion
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
