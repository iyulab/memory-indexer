using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Core.Services;

/// <summary>
/// Implementation of the 12 Memory Primitives.
/// Core operations for memory management system.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 2.2 "Memory Primitives"
/// </remarks>
public sealed class MemoryPrimitivesService : IMemoryPrimitives
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly IWorkingMemory _workingMemory;
    private readonly IRerankerService? _rerankerService;
    private readonly SearchOptions _searchOptions;
    private readonly ILogger<MemoryPrimitivesService> _logger;

    public MemoryPrimitivesService(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IScoringService scoringService,
        IWorkingMemory workingMemory,
        IOptions<MemoryIndexerOptions> options,
        ILogger<MemoryPrimitivesService> logger,
        IRerankerService? rerankerService = null)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _scoringService = scoringService;
        _workingMemory = workingMemory;
        _rerankerService = rerankerService;
        _searchOptions = options.Value.Search;
        _logger = logger;
    }

    #region Content Operations

    /// <inheritdoc />
    public async Task<MemoryUnit> EncodeAsync(EncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        _logger.LogDebug("Encoding new memory for user {UserId}", request.UserId);

        // Generate embedding
        var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Content, cancellationToken);

        // Create memory unit
        var memory = new MemoryUnit
        {
            UserId = request.UserId,
            SessionId = request.SessionId,
            Content = request.Content,
            Embedding = embedding,
            Type = request.Type ?? MemoryType.Episodic,
            Tier = request.Tier,
            ImportanceScore = request.ImportanceScore ?? 0.5f,
            ContentHash = ComputeContentHash(request.Content),
            Topics = request.Topics ?? [],
            Metadata = request.Metadata ?? [],
            IsLocked = request.IsLocked,
            ExpiresAt = request.ExpiresAt,
            Stability = MemoryStability.Volatile,
            RetentionScore = 1.0f
        };

        var stored = await _memoryStore.StoreAsync(memory, cancellationToken);

        _logger.LogInformation("Encoded memory {MemoryId} at tier {Tier}", stored.Id, stored.Tier);

        return stored;
    }

    /// <inheritdoc />
    public async Task<MemoryUnit?> UpdateAsync(UpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            _logger.LogWarning("Memory {MemoryId} not found for update", request.MemoryId);
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
            foreach (var kvp in request.Metadata)
            {
                memory.Metadata[kvp.Key] = kvp.Value;
            }
        }

        memory.MarkUpdated();

        await _memoryStore.UpdateAsync(memory, cancellationToken);

        _logger.LogDebug("Updated memory {MemoryId}", request.MemoryId);

        return memory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> SplitAsync(SplitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memory = await _memoryStore.GetByIdAsync(request.MemoryId, cancellationToken);
        if (memory == null)
        {
            _logger.LogWarning("Memory {MemoryId} not found for split", request.MemoryId);
            return [];
        }

        // Split content based on strategy
        var chunks = SplitContent(memory.Content, request.Strategy, request.MaxChunkSize, request.Overlap);

        if (chunks.Count <= 1)
        {
            _logger.LogDebug("Memory {MemoryId} not split - content too small", request.MemoryId);
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
                Topics = new List<string>(memory.Topics),
                Entities = new List<string>(memory.Entities),
                Metadata = new Dictionary<string, string>(memory.Metadata)
                {
                    ["split_source"] = memory.Id.ToString(),
                    ["split_index"] = i.ToString(),
                    ["split_total"] = chunks.Count.ToString()
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

        _logger.LogInformation("Split memory {MemoryId} into {Count} chunks", request.MemoryId, results.Count);

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
            MemoryMergeStrategy.Summarize => string.Join("\n\n", memories.Select(m => m.Content)), // TODO: LLM summarization
            MemoryMergeStrategy.ExtractKeyPoints => string.Join("\n", memories.Select(m => $"• {m.Content}")),
            _ => string.Join("\n\n", memories.Select(m => m.Content))
        };

        var firstMemory = memories[0];

        // Determine merged type
        var mergedType = request.ResultType ?? (memories.All(m => m.Type == firstMemory.Type)
            ? firstMemory.Type
            : MemoryType.Semantic);

        // Merge topics and entities
        var mergedTopics = memories.SelectMany(m => m.Topics).Distinct().ToList();
        var mergedEntities = memories.SelectMany(m => m.Entities).Distinct().ToList();

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

        _logger.LogInformation("Merged {Count} memories into {MemoryId}", memories.Count, stored.Id);

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
            _logger.LogWarning("Cannot delete locked memory {MemoryId}", request.MemoryId);
            return false;
        }

        // Remove from working memory if present
        if (_workingMemory.Contains(request.MemoryId))
        {
            await _workingMemory.DemoteAsync(request.MemoryId, cancellationToken);
        }

        var deleted = await _memoryStore.DeleteAsync(request.MemoryId, request.HardDelete, cancellationToken);

        _logger.LogInformation("Deleted memory {MemoryId} (hard: {HardDelete})", request.MemoryId, request.HardDelete);

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

        _logger.LogDebug("Set expiration for memory {MemoryId} to {ExpiresAt}", request.MemoryId, memory.ExpiresAt);

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
                memory.Metadata["lock_reason"] = request.Reason;
            }
        }
        else
        {
            memory.Metadata.Remove("lock_reason");
        }

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        _logger.LogInformation("{Action} memory {MemoryId}", request.IsLocked ? "Locked" : "Unlocked", request.MemoryId);

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
                memory.Topics.AddRange(request.AddTopics.Except(memory.Topics));
            }

            if (request.RemoveTopics != null)
            {
                memory.Topics.RemoveAll(t => request.RemoveTopics.Contains(t));
            }
        }

        // Update entities if specified
        if (request.Entities != null)
        {
            memory.Entities = request.Entities;
        }

        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        _logger.LogDebug("Labeled memory {MemoryId} with type {Type}", request.MemoryId, memory.Type);

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

        _logger.LogDebug("Retrieving memories for user {UserId} with query: {Query}", request.UserId, request.Query);

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
            Limit = request.Limit * candidateMultiplier, // Get extra for re-ranking/scoring
            Types = request.Types,
            MinScore = request.MinScore
        };

        // Search
        var searchResults = await _memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);

        // Filter by tier if specified
        var filtered = request.Tiers != null
            ? searchResults.Where(r => request.Tiers.Contains(r.Memory.Tier)).ToList()
            : searchResults.ToList();

        // Apply cross-encoder re-ranking if enabled and available
        IReadOnlyList<(MemorySearchResult Result, float RerankScore)>? rerankedResults = null;

        if (_searchOptions.EnableReranking && _rerankerService != null && filtered.Count > 0)
        {
            _logger.LogDebug("Re-ranking {Count} candidates with cross-encoder", filtered.Count);

            var candidates = filtered.Select(r => new RerankCandidate
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
                .Select(rr => ((MemorySearchResult)rr.Metadata!, rr.Score))
                .ToList();

            _logger.LogDebug("Re-ranking complete. Top score: {TopScore:F4}",
                rerankedResults.FirstOrDefault().RerankScore);
        }

        // Calculate weights (DAT or manual)
        var weights = request.Weights ?? GetDefaultWeights();

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
                var recencyScore = CalculateRecencyScore(memory);
                var importanceScore = memory.ImportanceScore;
                var retentionScore = memory.CalculateRetention();

                // Combined score
                var combinedScore = (
                    semanticScore * weights.Semantic +
                    recencyScore * weights.Recency +
                    importanceScore * weights.Importance
                ) / (weights.Semantic + weights.Recency + weights.Importance);

                return new RetrieveResult
                {
                    Memory = memory,
                    Score = combinedScore,
                    Breakdown = new ScoreBreakdown
                    {
                        SemanticScore = semanticScore,
                        KeywordScore = 0, // TODO: Implement keyword scoring
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

        _logger.LogDebug("Retrieved {Count} memories (reranking: {RerankEnabled})",
            results.Count, rerankedResults != null);

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

        // For now, simple concatenation - TODO: LLM-based summarization
        var combinedContent = string.Join("\n\n", memories.Select(m => m.Content));
        var summaryContent = request.FocusTopic != null
            ? $"[Summary focusing on: {request.FocusTopic}]\n{combinedContent}"
            : combinedContent;

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
            Topics = memories.SelectMany(m => m.Topics).Distinct().ToList(),
            Entities = memories.SelectMany(m => m.Entities).Distinct().ToList(),
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

        _logger.LogInformation("Summarized {Count} memories into {MemoryId}", memories.Count, stored.Id);

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
            _logger.LogDebug("Memory {MemoryId} already at tier {Tier}", request.MemoryId, memory.Tier);
            return memory;
        }

        // If promoting to Working memory, use working memory service
        if (targetTier == MemoryTier.Working)
        {
            await _workingMemory.PromoteAsync(memory, cancellationToken);
        }

        memory.Tier = targetTier;
        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        _logger.LogInformation("Promoted memory {MemoryId} to tier {Tier}", request.MemoryId, targetTier);

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
            _logger.LogDebug("Memory {MemoryId} already at tier {Tier}", request.MemoryId, memory.Tier);
            return memory;
        }

        // If demoting from Working memory
        if (memory.Tier == MemoryTier.Working)
        {
            await _workingMemory.DemoteAsync(memory.Id, cancellationToken);
        }

        memory.Tier = targetTier;
        memory.Metadata["demote_reason"] = request.Reason.ToString();
        memory.MarkUpdated();
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        _logger.LogInformation("Demoted memory {MemoryId} to tier {Tier} (reason: {Reason})",
            request.MemoryId, targetTier, request.Reason);

        return memory;
    }

    #endregion

    #region Helper Methods

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

    private static MemoryTier GetHigherTier(MemoryTier current) => current switch
    {
        MemoryTier.User => MemoryTier.Session,
        MemoryTier.Session => MemoryTier.Working,
        MemoryTier.Working => MemoryTier.Working,
        _ => current
    };

    private static MemoryTier GetLowerTier(MemoryTier current) => current switch
    {
        MemoryTier.Working => MemoryTier.Session,
        MemoryTier.Session => MemoryTier.User,
        MemoryTier.User => MemoryTier.User,
        _ => current
    };

    #endregion
}
