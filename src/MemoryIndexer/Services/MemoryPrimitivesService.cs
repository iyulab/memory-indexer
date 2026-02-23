using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of the 13 Memory Primitives.
/// Core operations for memory management system.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 2.2 "Memory Primitives"
///
/// Primitives:
/// - Content: Encode, Update, Split, Merge
/// - Lifecycle: Delete, Expire, Lock
/// - Classification: Label
/// - Retrieval: Retrieve, Summarize
/// - Tier: Promote, Demote
/// - Validation: Confirm (Phase 53)
/// </remarks>
public sealed partial class MemoryPrimitivesService : IMemoryPrimitives
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly IShortTermMemory _workingMemory;
    private readonly IRerankerService? _rerankerService;
    private readonly IDeduplicationService? _deduplicationService;
    private readonly IMemoryClassifier? _memoryClassifier;
    private readonly ITextCompletionService? _completionService;
    private readonly IShortTermMemoryOrchestrator _orchestrator;
    private readonly SearchOptions _searchOptions;
    private readonly Configuration.WorkingMemoryOptions _workingMemoryOptions;
    private readonly ILogger<MemoryPrimitivesService> _logger;

    public MemoryPrimitivesService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        IShortTermMemory workingMemory,
        IOptions<MemoryIndexerOptions> options,
        ILogger<MemoryPrimitivesService> logger,
        IShortTermMemoryOrchestrator orchestrator,
        IRerankerService? rerankerService = null,
        IDeduplicationService? deduplicationService = null,
        IMemoryClassifier? memoryClassifier = null,
        ITextCompletionService? completionService = null)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _workingMemory = workingMemory;
        _rerankerService = rerankerService;
        _deduplicationService = deduplicationService;
        _memoryClassifier = memoryClassifier;
        _completionService = completionService;
        _orchestrator = orchestrator;
        _searchOptions = options.Value.Search;
        _workingMemoryOptions = options.Value.WorkingMemory;
        _logger = logger;
    }

    #region Content Operations

    /// <inheritdoc />
    public async Task<MemoryUnit> EncodeAsync(EncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        LogEncodingMemory(_logger, request.UserId);

        // Phase 20.1: Check for duplicates before expensive embedding generation
        if (_deduplicationService != null)
        {
            var contentType = request.Metadata?.TryGetValue("ContentType", out var ctValue) == true
                ? ctValue?.ToString()
                : null;

            var dupCheck = await _deduplicationService.CheckForDuplicateAsync(
                request.Content,
                request.UserId,
                contentType: contentType,
                cancellationToken: cancellationToken);

            if (dupCheck.IsDuplicate)
            {
                LogDuplicateDetected(_logger, dupCheck.DuplicateType, dupCheck.RecommendedAction);

                switch (dupCheck.RecommendedAction)
                {
                    case DuplicateAction.Skip:
                        LogSkippingDuplicate(_logger, dupCheck.SimilarityScore);
                        // Phase 55: Implicit confirmation - duplicate = repeated mention
                        await ConfirmDuplicateAsync(dupCheck.ExistingMemory!, dupCheck.SimilarityScore, cancellationToken);
                        return dupCheck.ExistingMemory!;

                    case DuplicateAction.Update:
                        LogUpdatingExistingMemory(_logger, dupCheck.ExistingMemory!.Id);
                        // Phase 55: Implicit confirmation before update
                        await ConfirmDuplicateAsync(dupCheck.ExistingMemory!, dupCheck.SimilarityScore, cancellationToken);
                        return await UpdateAsync(new UpdateRequest
                        {
                            MemoryId = dupCheck.ExistingMemory.Id,
                            Content = request.Content,
                            RegenerateEmbedding = true
                        }, cancellationToken) ?? dupCheck.ExistingMemory;

                    case DuplicateAction.Merge:
                        LogMergingWithExisting(_logger, dupCheck.ExistingMemory!.Id);
                        // Phase 55: Implicit confirmation before merge
                        await ConfirmDuplicateAsync(dupCheck.ExistingMemory!, dupCheck.SimilarityScore, cancellationToken);
                        // Boost importance and update access count
                        dupCheck.ExistingMemory.ImportanceScore = Math.Min(1.0f,
                            dupCheck.ExistingMemory.ImportanceScore + 0.1f);
                        dupCheck.ExistingMemory.RecordAccess();
                        await _memoryStore.UpdateAsync(dupCheck.ExistingMemory, cancellationToken);
                        return dupCheck.ExistingMemory;

                    case DuplicateAction.AddWithRelation:
                        // Continue to store but add relation metadata
                        // Note: Metadata is added during memory creation (lines below)
                        LogAddingWithRelation(_logger, dupCheck.ExistingMemory!.Id, dupCheck.SimilarityScore);
                        break;

                    case DuplicateAction.Add:
                    default:
                        // Continue with normal encoding
                        break;
                }
            }
        }

        // Generate embedding
        var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Content, cancellationToken);

        // Auto-classify Type and ImportanceScore if not explicitly specified
        MemoryType memoryType = request.Type ?? MemoryType.Episodic;
        float importanceScore = request.ImportanceScore ?? 0.5f;
        List<string> topics = request.Topics?.ToList() ?? [];

        if (_memoryClassifier != null && (request.Type == null || request.ImportanceScore == null))
        {
            try
            {
                var classification = await _memoryClassifier.ClassifyAsync(
                    request.Content,
                    new ClassificationContext
                    {
                        UserId = request.UserId,
                        SessionId = request.SessionId,
                        MessageRole = request.Role ?? "user"
                    },
                    cancellationToken);

                if (request.Type == null)
                {
                    memoryType = classification.Type;
                    LogAutoClassifiedType(_logger, memoryType, classification.Confidence);
                }

                if (request.ImportanceScore == null)
                {
                    importanceScore = classification.Importance;
                    LogAutoClassifiedImportance(_logger, importanceScore);
                }

                // Use classified topics if none provided
                if (topics.Count == 0 && classification.Topics.Count > 0)
                {
                    topics = classification.Topics.ToList();
                }
            }
            catch (Exception ex)
            {
                LogAutoClassificationFailed(_logger, ex);
            }
        }

        // Create memory unit (3-axis model: Type × Scope × Tier)
        var memory = new MemoryUnit
        {
            UserId = request.UserId,
            SessionId = request.SessionId,
            Namespace = request.Namespace,
            Content = request.Content,
            Embedding = embedding,
            Type = memoryType,
            Scope = request.Scope,  // 3-axis: Scope dimension
            Tier = request.Tier,    // 3-axis: Tier dimension
            Role = request.Role,    // Preserved for episodic (T0-T2), null for semantic (T3)
            ImportanceScore = importanceScore,
            ContentHash = ComputeContentHash(request.Content),
            Topics = topics,
            Metadata = request.Metadata ?? [],
            IsLocked = request.IsLocked,
            ExpiresAt = request.ExpiresAt,
            Stability = MemoryStability.Volatile,
            RetentionScore = 1.0f
        };

        var stored = await _memoryStore.StoreAsync(memory, cancellationToken);

        LogEncodedMemory(_logger, stored.Id, stored.Tier);

        // Phase 48: Auto-trigger consolidation for Working Memory (Tier.Short)
        // This enables automatic promotion without requiring buffer routing
        if (stored.Tier == Tier.Short)
        {
            await TriggerConsolidationIfNeededAsync(stored, cancellationToken);
        }

        return stored;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> UpdateAsync(UpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            LogMemoryNotFoundForUpdate(_logger, request.MemoryId);
            return null;
        }

        // Track supersession if specified
        if (request.SupersedesId.HasValue)
        {
            memory.SupersedesId = request.SupersedesId;
        }

        // Update content if provided
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            memory.Content = request.Content;
            memory.ContentHash = ComputeContentHash(request.Content);

            if (request.RegenerateEmbedding)
            {
                memory.Embedding = await _embeddingService.GenerateEmbeddingAsync(request.Content, cancellationToken);
            }
        }

        // Update confidence score if provided
        if (request.ConfidenceScore.HasValue)
        {
            memory.ConfidenceScore = request.ConfidenceScore;
        }

        // Update topics if provided
        if (request.Topics != null)
        {
            memory.Topics = request.Topics;
        }

        // Merge metadata if provided
        if (request.Metadata != null)
        {
            memory.Metadata ??= new Dictionary<string, string>();
            foreach (var kvp in request.Metadata)
            {
                memory.Metadata[kvp.Key] = kvp.Value;
            }
        }

        memory.MarkUpdated();

        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogUpdatedMemory(_logger, request.MemoryId);

        return memory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> SplitAsync(SplitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            LogMemoryNotFoundForSplit(_logger, request.MemoryId);
            return [];
        }

        // Split content based on strategy
        var chunks = SplitContent(memory.Content, request.Strategy, request.MaxChunkSize, request.Overlap);

        if (chunks.Count <= 1)
        {
            LogMemoryNotSplit(_logger, request.MemoryId);
            return [memory];
        }

        var results = new List<MemoryUnit>(chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk, cancellationToken);

            var chunkMemory = new MemoryUnit
            {
                UserId = memory.UserId,
                SessionId = memory.SessionId,
                Content = chunk,
                Embedding = embedding,
                Type = memory.Type,
                Tier = memory.Tier,
                ImportanceScore = memory.ImportanceScore,
                ContentHash = ComputeContentHash(chunk),
                Topics = new List<string>(memory.Topics ?? []),
                Entities = new List<string>(memory.Entities ?? []),
                Metadata = new Dictionary<string, string>(memory.Metadata ?? [])
                {
                    ["split_source"] = memory.Id.ToString(),
                    ["split_index"] = i.ToString(CultureInfo.InvariantCulture),
                    ["split_total"] = chunks.Count.ToString(CultureInfo.InvariantCulture)
                },
                Stability = memory.Stability,
                RetentionScore = memory.RetentionScore
            };

            var stored = await _memoryStore.StoreAsync(chunkMemory, cancellationToken);
            results.Add(stored);
        }

        // Delete original if requested
        if (request.DeleteOriginal)
        {
            await _memoryStore.DeleteAsync(memory.Id, hardDelete: false, cancellationToken);
        }

        LogSplitMemory(_logger, request.MemoryId, results.Count);

        return results;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MemoryIds.Count < 2)
        {
            throw new ArgumentException("At least 2 memories required for merge", nameof(request));
        }

        var memories = await _memoryStore.GetByIdsAsync(request.MemoryIds, cancellationToken);

        if (memories.Count < 2)
        {
            throw new InvalidOperationException($"Only {memories.Count} memories found for merge");
        }

        // Merge content based on strategy
        var mergedContent = request.Strategy switch
        {
            MemoryMergeStrategy.Concatenate => string.Join("\n\n---\n\n", memories.Select(m => m.Content)),
            MemoryMergeStrategy.Summarize => await SummarizeForMergeAsync(memories, cancellationToken),
            MemoryMergeStrategy.ExtractKeyPoints => string.Join("\n", memories.Select(m => $"• {m.Content}")),
            _ => string.Join("\n\n", memories.Select(m => m.Content))
        };

        var firstMemory = memories[0];

        // Determine merged type
        var mergedType = request.ResultType ?? (memories.All(m => m.Type == firstMemory.Type)
            ? firstMemory.Type
            : MemoryType.Semantic);

        // Merge topics and entities
        var mergedTopics = memories.SelectMany(m => m.Topics ?? []).Distinct().ToList();
        var mergedEntities = memories.SelectMany(m => m.Entities ?? []).Distinct().ToList();

        // Calculate merged importance (max of sources)
        var mergedImportance = memories.Max(m => m.ImportanceScore);

        // Generate embedding for merged content
        var embedding = await _embeddingService.GenerateEmbeddingAsync(mergedContent, cancellationToken);

        var mergedMemory = new MemoryUnit
        {
            UserId = firstMemory.UserId,
            SessionId = firstMemory.SessionId,
            Content = mergedContent,
            Embedding = embedding,
            Type = mergedType,
            Tier = firstMemory.Tier,
            ImportanceScore = mergedImportance,
            ContentHash = ComputeContentHash(mergedContent),
            Topics = mergedTopics,
            Entities = mergedEntities,
            Metadata = new Dictionary<string, string>
            {
                ["merged_from"] = string.Join(",", request.MemoryIds),
                ["merge_strategy"] = request.Strategy.ToString()
            },
            Stability = memories.Max(m => m.Stability),
            RetentionScore = memories.Max(m => m.RetentionScore)
        };

        var stored = await _memoryStore.StoreAsync(mergedMemory, cancellationToken);

        // Delete sources if requested
        if (request.DeleteSources)
        {
            foreach (var memory in memories)
            {
                await _memoryStore.DeleteAsync(memory.Id, hardDelete: false, cancellationToken);
            }
        }

        LogMergedMemories(_logger, memories.Count, stored.Id);

        return stored;
    }

    #endregion

    #region Lifecycle Operations

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return false;
        }

        // Check lock
        if (memory.IsLocked && !request.ForceLocked)
        {
            LogCannotDeleteLocked(_logger, request.MemoryId);
            return false;
        }

        // Remove from working memory if present
        if (_workingMemory.Contains(request.MemoryId))
        {
            await _workingMemory.DemoteAsync(request.MemoryId, cancellationToken);
        }

        var deleted = await _memoryStore.DeleteAsync(request.MemoryId, request.HardDelete, cancellationToken);

        LogDeletedMemory(_logger, request.MemoryId, request.HardDelete);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> ExpireAsync(ExpireRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        // Calculate expiration
        if (request.TimeToLive.HasValue)
        {
            memory.ExpiresAt = DateTime.UtcNow.Add(request.TimeToLive.Value);
        }
        else
        {
            memory.ExpiresAt = request.ExpiresAt;
        }

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogSetExpiration(_logger, request.MemoryId, memory.ExpiresAt);

        return memory;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> LockAsync(LockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        memory.IsLocked = request.IsLocked;

        if (request.IsLocked)
        {
            memory.Stability = MemoryStability.Permanent;
            if (!string.IsNullOrEmpty(request.Reason))
            {
                memory.Metadata ??= new Dictionary<string, string>();
                memory.Metadata["lock_reason"] = request.Reason;
            }
        }
        else
        {
            memory.Metadata?.Remove("lock_reason");
        }

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogLockedOrUnlocked(_logger, request.IsLocked ? "Locked" : "Unlocked", request.MemoryId);

        return memory;
    }

    #endregion

    #region Classification Operations

    /// <inheritdoc />
    public async Task<MemoryUnit?> LabelAsync(LabelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        // Update type if specified
        if (request.Type.HasValue)
        {
            memory.Type = request.Type.Value;
        }

        // Handle topics
        if (request.Topics != null)
        {
            memory.Topics = request.Topics;
        }
        else
        {
            if (request.AddTopics != null)
            {
                memory.Topics ??= [];
                memory.Topics.AddRange(request.AddTopics.Except(memory.Topics));
            }

            if (request.RemoveTopics != null)
            {
                memory.Topics?.RemoveAll(t => request.RemoveTopics.Contains(t));
            }
        }

        // Update entities if specified
        if (request.Entities != null)
        {
            memory.Entities = request.Entities;
        }

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogLabeledMemory(_logger, request.MemoryId, memory.Type);

        return memory;
    }

    #endregion

    #region Retrieval Operations

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrieveResult>> RetrieveAsync(RetrieveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        LogRetrievingMemories(_logger, request.UserId, request.Query);

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken);

        // Determine candidate limit based on re-ranking configuration
        var candidateMultiplier = _searchOptions.EnableReranking && _rerankerService != null
            ? _searchOptions.RerankCandidateMultiplier
            : 2;

        // Build search options
        var searchOptions = new MemorySearchOptions
        {
            UserId = request.UserId,
            SessionId = request.SessionId,
            Namespace = request.Namespace,
            Limit = request.Limit * candidateMultiplier, // Get extra for re-ranking/scoring
            Types = request.Types,
            MinScore = request.MinScore
        };

        // Search
        var searchResults = await _memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);

        // Filter by tier and scope if specified (3-axis model)
        var filtered = searchResults
            .Where(r => request.Tiers == null || request.Tiers.Contains(r.Memory.Tier))
            .Where(r => request.Scopes == null || request.Scopes.Contains(r.Memory.Scope))
            .ToList();

        // Apply cross-encoder re-ranking if enabled and available
        List<(MemorySearchResult Result, float RerankScore)>? rerankedResults = null;

        if (_searchOptions.EnableReranking && _rerankerService != null && filtered.Count > 0)
        {
            LogReranking(_logger, filtered.Count);

            var candidates = filtered.Select(r => new RerankCandidate<MemorySearchResult>
            {
                Content = r.Memory.Content,
                OriginalScore = r.Score,
                MemoryId = r.Memory.Id,
                Metadata = r
            }).ToList();

            var rerankResults = await _rerankerService.RerankAsync(
                request.Query,
                candidates,
                Math.Min(request.Limit * 2, filtered.Count), // Get more than needed for final scoring
                cancellationToken);

            rerankedResults = rerankResults
                .Select(rr => (rr.Metadata!, rr.Score))
                .ToList();

            LogRerankingComplete(_logger, rerankedResults.Count > 0 ? rerankedResults[0].RerankScore : 0);
        }

        // Calculate weights (DAT or manual)
        var weights = request.Weights ?? GetDefaultWeights();

        // Pre-tokenize query for keyword scoring
        var queryTokens = TokenizeForKeywordMatch(request.Query);

        // Build final results with combined scoring
        var resultsSource = rerankedResults != null
            ? rerankedResults.Select(rr => (rr.Result, RerankScore: (float?)rr.RerankScore))
            : filtered.Select(r => (r, RerankScore: (float?)null));

        var results = resultsSource
            .Select(item =>
            {
                var memory = item.Item1.Memory;
                var vectorScore = item.Item1.Score;
                var rerankScore = item.RerankScore;

                // Calculate individual scores
                var semanticScore = rerankScore ?? vectorScore; // Use rerank score if available
                var keywordScore = CalculateKeywordScore(memory.Content, queryTokens);
                var recencyScore = CalculateRecencyScore(memory);
                var importanceScore = memory.ImportanceScore;
                var retentionScore = memory.CalculateRetention();

                // Combined score (includes keyword weight)
                var totalWeight = weights.Semantic + weights.Keyword + weights.Recency + weights.Importance;
                var combinedScore = totalWeight > 0
                    ? (semanticScore * weights.Semantic +
                       keywordScore * weights.Keyword +
                       recencyScore * weights.Recency +
                       importanceScore * weights.Importance) / totalWeight
                    : 0f;

                return new RetrieveResult
                {
                    Memory = memory,
                    Score = combinedScore,
                    Breakdown = new ScoreBreakdown
                    {
                        SemanticScore = semanticScore,
                        KeywordScore = keywordScore,
                        RecencyScore = recencyScore,
                        ImportanceScore = importanceScore,
                        RetentionScore = retentionScore,
                        VectorScore = vectorScore,
                        RerankScore = rerankScore
                    }
                };
            })
            .Where(r => r.Score >= request.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(request.Limit)
            .ToList();

        // Record access if requested
        if (request.RecordAccess)
        {
            foreach (var result in results)
            {
                result.Memory.RecordAccess();
                _ = _memoryStore.UpdateAsync(result.Memory, CancellationToken.None);
            }
        }

        LogRetrievedMemories(_logger, results.Count, rerankedResults != null);

        return results;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit> SummarizeAsync(SummarizeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MemoryIds.Count == 0)
        {
            throw new ArgumentException("At least 1 memory required for summarization", nameof(request));
        }

        var memories = await _memoryStore.GetByIdsAsync(request.MemoryIds, cancellationToken);

        if (memories.Count == 0)
        {
            throw new InvalidOperationException("No memories found for summarization");
        }

        var summaryContent = await SummarizeMemoriesAsync(
            memories, request.FocusTopic, request.MaxTokens, cancellationToken);

        // Generate embedding for summary
        var embedding = await _embeddingService.GenerateEmbeddingAsync(summaryContent, cancellationToken);

        var firstMemory = memories[0];

        var summaryMemory = new MemoryUnit
        {
            UserId = firstMemory.UserId,
            SessionId = firstMemory.SessionId,
            Content = summaryContent,
            Embedding = embedding,
            Type = MemoryType.Semantic,
            Tier = firstMemory.Tier,
            ImportanceScore = memories.Max(m => m.ImportanceScore),
            ContentHash = ComputeContentHash(summaryContent),
            Topics = memories.SelectMany(m => m.Topics ?? []).Distinct().ToList(),
            Entities = memories.SelectMany(m => m.Entities ?? []).Distinct().ToList(),
            Metadata = new Dictionary<string, string>
            {
                ["summary_of"] = string.Join(",", request.MemoryIds),
                ["summary_focus"] = request.FocusTopic ?? ""
            },
            Stability = memories.Max(m => m.Stability),
            RetentionScore = 1.0f
        };

        var stored = await _memoryStore.StoreAsync(summaryMemory, cancellationToken);

        // Delete sources if not preserving
        if (!request.PreserveSources)
        {
            foreach (var memory in memories)
            {
                await _memoryStore.DeleteAsync(memory.Id, hardDelete: false, cancellationToken);
            }
        }

        LogSummarizedMemories(_logger, memories.Count, stored.Id);

        return stored;
    }

    #endregion

    #region Tier Operations

    /// <inheritdoc />
    public async Task<MemoryUnit?> PromoteAsync(PromoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        var targetTier = request.TargetTier ?? GetHigherTier(memory.Tier);

        if (targetTier == memory.Tier)
        {
            LogAlreadyAtTier(_logger, request.MemoryId, memory.Tier);
            return memory;
        }

        // If promoting to Working memory, use working memory service
        if (targetTier == Tier.Short)
        {
            await _workingMemory.PromoteAsync(memory, cancellationToken);
        }

        memory.Tier = targetTier;
        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogPromotedMemory(_logger, request.MemoryId, targetTier);

        return memory;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> DemoteAsync(DemoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            return null;
        }

        var targetTier = request.TargetTier ?? GetLowerTier(memory.Tier);

        if (targetTier == memory.Tier)
        {
            LogAlreadyAtTier(_logger, request.MemoryId, memory.Tier);
            return memory;
        }

        // If demoting from Working memory
        if (memory.Tier == Tier.Short)
        {
            await _workingMemory.DemoteAsync(memory.Id, cancellationToken);
        }

        memory.Tier = targetTier;
        memory.Metadata ??= new Dictionary<string, string>();
        memory.Metadata["demote_reason"] = request.Reason.ToString();
        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        LogDemotedMemory(_logger, request.MemoryId, targetTier, request.Reason);

        return memory;
    }

    #endregion

    #region Validation Operations

    /// <inheritdoc />
    public async Task<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            LogConfirmNotFound(_logger, request.MemoryId);
            return ConfirmResult.NotFound(request.MemoryId);
        }

        // Store previous values
        var previousConfirmCount = memory.ConfirmCount;
        var previousConfidence = memory.Confidence;

        // Increment confirmation count
        memory.ConfirmCount++;

        // Apply optional confidence boost (capped at 1.0)
        if (request.ConfidenceBoost.HasValue && request.ConfidenceBoost.Value > 0)
        {
            var boost = Math.Min(request.ConfidenceBoost.Value, 0.2f);
            memory.Confidence = Math.Min(1.0f, memory.Confidence + boost);
        }

        // Record confirmation source in metadata
        memory.Metadata ??= new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(request.Source))
        {
            memory.Metadata[$"confirm_source_{memory.ConfirmCount}"] = request.Source;
        }
        memory.Metadata["last_confirmed_at"] = DateTime.UtcNow.ToString("O");

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        // Check Archive eligibility (AND logic: Confidence >= 0.8 AND ConfirmCount >= 3)
        const float minConfidence = 0.8f;
        const int minConfirmCount = 3;
        var isArchiveEligible = memory.Confidence >= minConfidence && memory.ConfirmCount >= minConfirmCount;

        LogConfirmed(_logger, request.MemoryId,
            previousConfirmCount, memory.ConfirmCount,
            previousConfidence, memory.Confidence,
            isArchiveEligible);

        return new ConfirmResult
        {
            Success = true,
            Memory = memory,
            PreviousConfirmCount = previousConfirmCount,
            NewConfirmCount = memory.ConfirmCount,
            PreviousConfidence = previousConfidence,
            NewConfidence = memory.Confidence,
            IsArchiveEligible = isArchiveEligible
        };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Phase 55: Implicit confirmation when duplicate is detected.
    /// Duplicate detection = same information mentioned again = implicit confirmation.
    /// This enables Archive tier promotion via ConfirmCount accumulation.
    /// </summary>
    /// <remarks>
    /// Confidence boost is proportional to similarity:
    /// - Exact duplicate (>= 0.95): +0.1 boost
    /// - High similarity (>= 0.85): +0.05 boost
    /// - Medium similarity (>= 0.75): +0.02 boost
    /// </remarks>
    private async Task ConfirmDuplicateAsync(
        MemoryUnit existingMemory,
        float similarityScore,
        CancellationToken cancellationToken)
    {
        // Calculate confidence boost based on similarity score
        var boost = similarityScore switch
        {
            >= 0.95f => 0.1f,   // Exact duplicate
            >= 0.85f => 0.05f,  // High similarity
            >= 0.75f => 0.02f,  // Medium similarity
            _ => 0.01f          // Fallback
        };

        var result = await ConfirmAsync(new ConfirmRequest
        {
            MemoryId = existingMemory.Id,
            ConfidenceBoost = boost,
            Source = $"deduplication (similarity: {similarityScore:F3})"
        }, cancellationToken);

        if (result.Success)
        {
            LogDedupConfirmSuccess(_logger, existingMemory.Id,
                result.PreviousConfirmCount, result.NewConfirmCount,
                result.PreviousConfidence, result.NewConfidence,
                result.IsArchiveEligible);
        }
        else
        {
            LogDedupConfirmFailed(_logger, existingMemory.Id, result.Error);
        }
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<string> SplitContent(string content, SplitStrategy strategy, int? maxSize, int? overlap)
    {
        var chunkSize = maxSize ?? 500;

        return strategy switch
        {
            SplitStrategy.Semantic => SplitBySentences(content, chunkSize),
            SplitStrategy.FixedSize => SplitBySize(content, chunkSize),
            SplitStrategy.TokenBased => SplitBySize(content, chunkSize), // Simplified for now
            SplitStrategy.SlidingWindow => SplitWithOverlap(content, chunkSize, overlap ?? 50),
            _ => [content]
        };
    }

    private static List<string> SplitBySentences(string content, int maxSize)
    {
        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim() + ".")
            .Where(s => s.Length > 1)
            .ToList();

        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > maxSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }
            currentChunk.Append(sentence).Append(' ');
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private static List<string> SplitBySize(string content, int chunkSize)
    {
        var chunks = new List<string>();
        for (int i = 0; i < content.Length; i += chunkSize)
        {
            chunks.Add(content.Substring(i, Math.Min(chunkSize, content.Length - i)));
        }
        return chunks;
    }

    private static List<string> SplitWithOverlap(string content, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var step = chunkSize - overlap;
        for (int i = 0; i < content.Length; i += step)
        {
            chunks.Add(content.Substring(i, Math.Min(chunkSize, content.Length - i)));
            if (i + chunkSize >= content.Length) break;
        }
        return chunks;
    }

    /// <summary>
    /// Tokenizes text for keyword matching: lowercase, split by non-alphanumeric, filter short tokens.
    /// </summary>
    private static HashSet<string> TokenizeForKeywordMatch(string text)
    {
        return text
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
    }

    private static readonly char[] TokenSeparators = [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_', '+', '=', '<', '>', '&', '|', '@', '#', '$', '%', '^', '*', '~', '`'];

    /// <summary>
    /// Calculates keyword match score between memory content and pre-tokenized query.
    /// Returns ratio of matched query tokens found in memory content (0.0 to 1.0).
    /// </summary>
    private static float CalculateKeywordScore(string memoryContent, HashSet<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0f;
        }

        var contentTokens = TokenizeForKeywordMatch(memoryContent);
        var matchCount = queryTokens.Count(token => contentTokens.Contains(token));
        return (float)matchCount / queryTokens.Count;
    }

    private static float CalculateRecencyScore(MemoryUnit memory)
    {
        var lastAccess = memory.LastAccessedAt ?? memory.CreatedAt;
        var hoursSince = (DateTime.UtcNow - lastAccess).TotalHours;
        return (float)Math.Exp(-hoursSince / 24.0); // Decay over ~24 hours
    }

    private static RetrievalWeights GetDefaultWeights() => new()
    {
        Semantic = 0.4f,
        Keyword = 0.2f,
        Recency = 0.2f,
        Importance = 0.2f
    };

    private static Tier GetHigherTier(Tier current) => current switch
    {
        Tier.Archive => Tier.Long,
        Tier.Long => Tier.Short,
        Tier.Short => Tier.Short,
        _ => current
    };

    private static Tier GetLowerTier(Tier current) => current switch
    {
        Tier.Short => Tier.Long,
        Tier.Long => Tier.Archive,
        Tier.Archive => Tier.Archive,
        _ => current
    };

    /// <summary>
    /// Auto-trigger consolidation check after encoding to Working Memory.
    /// This enables automatic Working→Session promotion for direct tier writes.
    /// Phase 48: Clean solution for memory retention without buffer routing.
    /// </summary>
    private async Task TriggerConsolidationIfNeededAsync(
        MemoryUnit memory,
        CancellationToken cancellationToken)
    {
        // Skip if no session ID (consolidation requires session context)
        if (string.IsNullOrWhiteSpace(memory.SessionId))
        {
            LogAutoConsolidationSkipped(_logger, memory.Id);
            return;
        }

        try
        {
            // Step 1: Record activity (updates turn count, tokens, timestamp)
            LogAutoConsolidationRecording(_logger, memory.UserId, memory.SessionId);

            await _orchestrator.RecordActivityAsync(
                memory.UserId,
                memory.SessionId,
                memory,
                cancellationToken);

            // Step 2: Check if any consolidation trigger is satisfied
            var trigger = await _orchestrator.CheckArchivalTriggerAsync(
                memory.UserId,
                cancellationToken);

            if (trigger.HasValue)
            {
                LogAutoConsolidationTriggerDetected(_logger, trigger.Value, memory.UserId);

                // Step 3: Archive Working Memory → Session
                var result = await _orchestrator.ArchiveToSessionAsync(
                    memory.UserId,
                    trigger.Value,
                    summarize: true,
                    cancellationToken);

                if (result.Success)
                {
                    var summaryIdStr = result.SummaryId?.ToString() ?? "none";
                    LogAutoConsolidationSuccess(_logger, result.MemoriesArchived, summaryIdStr);
                }
                else
                {
                    LogAutoConsolidationArchivalFailed(_logger, result.Error);
                }
            }
            else
            {
                LogAutoConsolidationNoTriggers(_logger, memory.UserId);
            }

            // Phase 51: Capacity enforcement - Baddeley's 7±2 working memory limit
            if (_workingMemoryOptions.EnableCapacityEnforcement)
            {
                await EnforceWorkingMemoryCapacityAsync(
                    memory.UserId,
                    memory.SessionId,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never fail the main EncodeAsync operation due to consolidation issues
            LogAutoConsolidationError(_logger, ex, memory.UserId);
        }
    }

    /// <summary>
    /// Enforces Baddeley's 7±2 working memory capacity limit.
    /// Phase 51: Promotes oldest Short tier items to Long tier when capacity exceeded.
    /// </summary>
    private async Task EnforceWorkingMemoryCapacityAsync(
        string userId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var capacity = _workingMemoryOptions.Capacity;

        // Query current Short tier count
        var shortTierMemories = await _memoryStore.GetAllAsync(
            userId,
            new MemoryFilterOptions
            {
                SessionId = sessionId,
                Tiers = [Tier.Short],
                OrderBy = MemoryOrderBy.CreatedAtAsc // Oldest first for promotion
            },
            cancellationToken);

        var currentCount = shortTierMemories.Count;

        if (currentCount <= capacity)
        {
            LogCapacityWithinLimit(_logger, currentCount, capacity);
            return;
        }

        // Calculate excess items to promote
        var excessCount = currentCount - capacity;

        LogCapacityExceeded(_logger, currentCount, capacity, excessCount);

        // Promote oldest items (already sorted by CreatedAtAsc)
        var itemsToPromote = shortTierMemories.Take(excessCount).ToList();

        foreach (var mem in itemsToPromote)
        {
            mem.Tier = Tier.Long;
            mem.UpdatedAt = DateTime.UtcNow;

            var updated = await _memoryStore.UpdateAsync(mem, cancellationToken);

            if (updated)
            {
                LogCapacityPromotedMemory(_logger, mem.Id);
            }
            else
            {
                LogCapacityPromotionFailed(_logger, mem.Id);
            }
        }

        LogCapacityEnforcementComplete(_logger, itemsToPromote.Count, currentCount - itemsToPromote.Count, capacity);
    }

    #endregion

    // --- LoggerMessage declarations ---

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encoding new memory for user {UserId}")]
    private static partial void LogEncodingMemory(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Duplicate detected: {DuplicateType}, Action: {Action}")]
    private static partial void LogDuplicateDetected(ILogger logger, DuplicateType duplicateType, DuplicateAction action);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping duplicate memory (similarity: {Score:F3})")]
    private static partial void LogSkippingDuplicate(ILogger logger, float score);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updating existing memory {MemoryId} with new content")]
    private static partial void LogUpdatingExistingMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merging with existing memory {MemoryId}")]
    private static partial void LogMergingWithExisting(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding memory with relation to {MemoryId} (similarity: {Score:F3})")]
    private static partial void LogAddingWithRelation(ILogger logger, Guid memoryId, float score);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-classified Type as {Type} with confidence {Confidence:F2}")]
    private static partial void LogAutoClassifiedType(ILogger logger, MemoryType type, float confidence);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-classified ImportanceScore as {Score:F2}")]
    private static partial void LogAutoClassifiedImportance(ILogger logger, float score);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auto-classification failed, using defaults")]
    private static partial void LogAutoClassificationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Encoded memory {MemoryId} at tier {Tier}")]
    private static partial void LogEncodedMemory(ILogger logger, Guid memoryId, Tier tier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory {MemoryId} not found for update")]
    private static partial void LogMemoryNotFoundForUpdate(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated memory {MemoryId}")]
    private static partial void LogUpdatedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory {MemoryId} not found for split")]
    private static partial void LogMemoryNotFoundForSplit(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory {MemoryId} not split - content too small")]
    private static partial void LogMemoryNotSplit(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Split memory {MemoryId} into {Count} chunks")]
    private static partial void LogSplitMemory(ILogger logger, Guid memoryId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merged {Count} memories into {MemoryId}")]
    private static partial void LogMergedMemories(ILogger logger, int count, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot delete locked memory {MemoryId}")]
    private static partial void LogCannotDeleteLocked(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted memory {MemoryId} (hard: {HardDelete})")]
    private static partial void LogDeletedMemory(ILogger logger, Guid memoryId, bool hardDelete);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Set expiration for memory {MemoryId} to {ExpiresAt}")]
    private static partial void LogSetExpiration(ILogger logger, Guid memoryId, DateTime? expiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} memory {MemoryId}")]
    private static partial void LogLockedOrUnlocked(ILogger logger, string action, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Labeled memory {MemoryId} with type {Type}")]
    private static partial void LogLabeledMemory(ILogger logger, Guid memoryId, MemoryType type);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieving memories for user {UserId} with query: {Query}")]
    private static partial void LogRetrievingMemories(ILogger logger, string userId, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Re-ranking {Count} candidates with cross-encoder")]
    private static partial void LogReranking(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Re-ranking complete. Top score: {TopScore:F4}")]
    private static partial void LogRerankingComplete(ILogger logger, float topScore);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieved {Count} memories (reranking: {RerankEnabled})")]
    private static partial void LogRetrievedMemories(ILogger logger, int count, bool rerankEnabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Summarized {Count} memories into {MemoryId}")]
    private static partial void LogSummarizedMemories(ILogger logger, int count, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory {MemoryId} already at tier {Tier}")]
    private static partial void LogAlreadyAtTier(ILogger logger, Guid memoryId, Tier tier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Promoted memory {MemoryId} to tier {Tier}")]
    private static partial void LogPromotedMemory(ILogger logger, Guid memoryId, Tier tier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demoted memory {MemoryId} to tier {Tier} (reason: {Reason})")]
    private static partial void LogDemotedMemory(ILogger logger, Guid memoryId, Tier tier, DemoteReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[CONFIRM] Memory {MemoryId} not found")]
    private static partial void LogConfirmNotFound(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[CONFIRM] Memory {MemoryId} confirmed: {PrevCount}->{NewCount}, confidence {PrevConf:F2}->{NewConf:F2}, eligible={Eligible}")]
    private static partial void LogConfirmed(ILogger logger, Guid memoryId, int prevCount, int newCount, float prevConf, float newConf, bool eligible);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DEDUP_CONFIRM] Memory {MemoryId} confirmed via deduplication: ConfirmCount {PrevCount}->{NewCount}, Confidence {PrevConf:F2}->{NewConf:F2}, ArchiveEligible={Eligible}")]
    private static partial void LogDedupConfirmSuccess(ILogger logger, Guid memoryId, int prevCount, int newCount, float prevConf, float newConf, bool eligible);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[DEDUP_CONFIRM] Failed to confirm memory {MemoryId}: {Error}")]
    private static partial void LogDedupConfirmFailed(ILogger logger, Guid memoryId, string? error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTO_CONSOLIDATION] Skipping - no session ID for memory {MemoryId}")]
    private static partial void LogAutoConsolidationSkipped(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTO_CONSOLIDATION] Recording activity for user {UserId}, session {SessionId}")]
    private static partial void LogAutoConsolidationRecording(ILogger logger, string userId, string? sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[AUTO_CONSOLIDATION] Trigger detected: {Trigger} for user {UserId}. Initiating archival to Session tier.")]
    private static partial void LogAutoConsolidationTriggerDetected(ILogger logger, Interfaces.WorkingPromotionTrigger trigger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[AUTO_CONSOLIDATION] Successfully archived {Count} memories. Summary ID: {SummaryId}")]
    private static partial void LogAutoConsolidationSuccess(ILogger logger, int count, string summaryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[AUTO_CONSOLIDATION] Archival failed: {Error}")]
    private static partial void LogAutoConsolidationArchivalFailed(ILogger logger, string? error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTO_CONSOLIDATION] No triggers satisfied for user {UserId}")]
    private static partial void LogAutoConsolidationNoTriggers(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "[AUTO_CONSOLIDATION] Consolidation check failed for user {UserId}. Main operation succeeded, but automatic archival could not proceed.")]
    private static partial void LogAutoConsolidationError(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[CAPACITY_ENFORCEMENT] Short tier within capacity: {Count}/{Capacity}")]
    private static partial void LogCapacityWithinLimit(ILogger logger, int count, int capacity);

    [LoggerMessage(Level = LogLevel.Information, Message = "[CAPACITY_ENFORCEMENT] Short tier exceeds capacity: {Count}/{Capacity}. Promoting {Excess} oldest items to Long tier (Baddeley's 7+/-2 model).")]
    private static partial void LogCapacityExceeded(ILogger logger, int count, int capacity, int excess);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[CAPACITY_ENFORCEMENT] Promoted memory {MemoryId} from Short to Long tier")]
    private static partial void LogCapacityPromotedMemory(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[CAPACITY_ENFORCEMENT] Failed to promote memory {MemoryId}")]
    private static partial void LogCapacityPromotionFailed(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[CAPACITY_ENFORCEMENT] Capacity enforcement complete: Promoted {Promoted} items, new Short tier count: {NewCount}/{Capacity}")]
    private static partial void LogCapacityEnforcementComplete(ILogger logger, int promoted, int newCount, int capacity);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[LLM_SUMMARY] Summarizing {Count} memories via LLM (focus: {Focus})")]
    private static partial void LogLlmSummarizing(ILogger logger, int count, string focus);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[LLM_SUMMARY] LLM summarization unavailable, falling back to concatenation")]
    private static partial void LogLlmSummarizationFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[LLM_SUMMARY] LLM summarization failed, falling back to concatenation")]
    private static partial void LogLlmSummarizationFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Summarizes multiple memories using LLM. Falls back to concatenation if LLM is unavailable.
    /// </summary>
    private async Task<string> SummarizeForMergeAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        return await SummarizeMemoriesAsync(memories, focusTopic: null, maxTokens: null, cancellationToken);
    }

    /// <summary>
    /// Core LLM summarization logic shared by MergeAsync and SummarizeAsync.
    /// </summary>
    private async Task<string> SummarizeMemoriesAsync(
        IReadOnlyList<MemoryUnit> memories,
        string? focusTopic,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        var combinedContent = string.Join("\n\n", memories.Select(m => m.Content));

        if (_completionService == null)
        {
            LogLlmSummarizationFallback(_logger);
            return focusTopic != null
                ? $"[Summary focusing on: {focusTopic}]\n{combinedContent}"
                : combinedContent;
        }

        LogLlmSummarizing(_logger, memories.Count, focusTopic ?? "all");

        try
        {
            var prompt = BuildSummarizationPrompt(combinedContent, focusTopic, maxTokens);
            var result = await _completionService.CompleteAsync(prompt, new TextCompletionOptions
            {
                Temperature = 0.1f,
                MaxTokens = maxTokens ?? 500,
            }, cancellationToken);

            return string.IsNullOrWhiteSpace(result) ? combinedContent : result;
        }
        catch (Exception ex)
        {
            LogLlmSummarizationFailed(_logger, ex);
            return focusTopic != null
                ? $"[Summary focusing on: {focusTopic}]\n{combinedContent}"
                : combinedContent;
        }
    }

    private static string BuildSummarizationPrompt(string content, string? focusTopic, int? maxTokens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Summarize the following memories into a concise, factual synthesis.");
        sb.AppendLine("Preserve key entities, concepts, and relationships.");
        if (focusTopic != null)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Focus on: {focusTopic}");
        if (maxTokens.HasValue)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Keep the summary under {maxTokens.Value} tokens.");
        sb.AppendLine();
        sb.AppendLine("Memories:");
        sb.AppendLine(content);
        sb.AppendLine();
        sb.AppendLine("Summary:");
        return sb.ToString();
    }
}
