using System.Collections.Concurrent;
using System.Text;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Promotion;

/// <summary>
/// Implementation of working memory orchestration with session archival triggers.
/// Tracks per-user state and evaluates multi-signal promotion triggers.
/// </summary>
/// <remarks>
/// Multi-signal promotion triggers (OR logic):
/// - IdleTimeout: 10 minutes of inactivity
/// - TokenThreshold: 2K tokens accumulated
/// - TurnThreshold: 10 conversation turns
/// - TopicChange: Significant topic shift detected
/// </remarks>
public sealed class WorkingMemoryOrchestratorService : IWorkingMemoryOrchestrator
{
    private readonly IWorkingMemory _workingMemory;
    private readonly IEmbeddingService _embeddingService;
    private readonly WorkingMemoryOrchestratorOptions _options;
    private readonly ILogger<WorkingMemoryOrchestratorService> _logger;

    // Per-user state tracking
    private readonly ConcurrentDictionary<string, UserWorkingState> _userStates = new();

    public WorkingMemoryOrchestratorService(
        IWorkingMemory workingMemory,
        IEmbeddingService embeddingService,
        IOptions<WorkingMemoryOrchestratorOptions> options,
        ILogger<WorkingMemoryOrchestratorService> logger)
    {
        _workingMemory = workingMemory;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordActivityAsync(
        string userId,
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(memory);

        var state = _userStates.GetOrAdd(userId, _ => new UserWorkingState(userId));

        lock (state.Lock)
        {
            state.SessionId = sessionId;
            state.TurnCount++;
            state.TotalTokens += EstimateTokens(memory.Content);
            state.LastActivityTime = DateTime.UtcNow;
            state.Memories.Add(memory);

            // Update topic embedding for topic change detection
            if (_options.EnableTopicChangeDetection &&
                memory.Embedding.HasValue && memory.Embedding.Value.Length > 0)
            {
                state.LastTopicEmbedding = state.CurrentTopicEmbedding;
                state.CurrentTopicEmbedding = memory.Embedding.Value;
            }
        }

        _logger.LogDebug(
            "Recorded activity for user {UserId}: Turn {Turn}, Tokens {Tokens}",
            userId, state.TurnCount, state.TotalTokens);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<WorkingPromotionTrigger?> CheckArchivalTriggerAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return null;
        }

        lock (state.Lock)
        {
            // Check idle timeout
            if (state.LastActivityTime.HasValue)
            {
                var idleDuration = DateTime.UtcNow - state.LastActivityTime.Value;
                if (idleDuration >= _options.IdleTimeout)
                {
                    _logger.LogDebug(
                        "Idle timeout trigger for user {UserId}: {Duration}",
                        userId, idleDuration);
                    return WorkingPromotionTrigger.IdleTimeout;
                }
            }

            // Check token threshold
            if (state.TotalTokens >= _options.TokenThreshold)
            {
                _logger.LogDebug(
                    "Token threshold trigger for user {UserId}: {Tokens} >= {Threshold}",
                    userId, state.TotalTokens, _options.TokenThreshold);
                return WorkingPromotionTrigger.TokenThreshold;
            }

            // Check turn threshold
            if (state.TurnCount >= _options.TurnThreshold)
            {
                _logger.LogDebug(
                    "Turn threshold trigger for user {UserId}: {Turns} >= {Threshold}",
                    userId, state.TurnCount, _options.TurnThreshold);
                return WorkingPromotionTrigger.TurnThreshold;
            }

            // Check topic change
            if (_options.EnableTopicChangeDetection &&
                state.CurrentTopicEmbedding.Length > 0 &&
                state.LastTopicEmbedding.Length > 0)
            {
                var similarity = CosineSimilarity(
                    state.CurrentTopicEmbedding.Span,
                    state.LastTopicEmbedding.Span);

                if (similarity < _options.TopicChangeSimilarityThreshold)
                {
                    _logger.LogDebug(
                        "Topic change trigger for user {UserId}: Similarity {Sim} < {Threshold}",
                        userId, similarity, _options.TopicChangeSimilarityThreshold);
                    return WorkingPromotionTrigger.TopicChange;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<WorkingArchivalResult> ArchiveToSessionAsync(
        string userId,
        WorkingPromotionTrigger trigger,
        bool summarize = true,
        CancellationToken cancellationToken = default)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return WorkingArchivalResult.Empty;
        }

        List<MemoryUnit> memoriesToArchive;
        lock (state.Lock)
        {
            if (state.Memories.Count == 0)
            {
                return WorkingArchivalResult.Empty;
            }

            memoriesToArchive = [.. state.Memories];
        }

        _logger.LogInformation(
            "Archiving {Count} memories for user {UserId} with trigger {Trigger}",
            memoriesToArchive.Count, userId, trigger);

        try
        {
            Guid? summaryId = null;

            if (summarize && _options.SummarizeBeforeArchival && memoriesToArchive.Count > 1)
            {
                // Create extractive summary of all memories
                var summaryContent = CreateExtractiveSummary(memoriesToArchive);

                // Generate embedding for summary
                var summaryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                    summaryContent, cancellationToken);

                // Create session summary memory
                var sessionSummary = new MemoryUnit
                {
                    Content = summaryContent,
                    UserId = userId,
                    SessionId = state.SessionId,
                    Embedding = summaryEmbedding,
                    Type = MemoryType.Semantic, // Summarized content becomes semantic
                    Tier = MemoryTier.Session,
                    Stability = MemoryStability.Stable,
                    ImportanceScore = CalculateSessionImportance(memoriesToArchive),
                    Topics = ExtractTopicsFromMemories(memoriesToArchive),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "working_archival",
                        ["archival_trigger"] = trigger.ToString(),
                        ["memory_count"] = memoriesToArchive.Count.ToString(),
                        ["original_tokens"] = state.TotalTokens.ToString(),
                        ["summary_tokens"] = EstimateTokens(summaryContent).ToString()
                    }
                };

                summaryId = sessionSummary.Id;

                _logger.LogDebug(
                    "Created session summary {SummaryId} for user {UserId}",
                    summaryId, userId);
            }

            // Clear working memory state for this user
            lock (state.Lock)
            {
                state.Reset();
            }

            // Demote memories from working memory to session tier
            foreach (var memory in memoriesToArchive)
            {
                await _workingMemory.DemoteAsync(memory.Id, cancellationToken);
            }

            _logger.LogInformation(
                "Successfully archived {Count} memories for user {UserId}",
                memoriesToArchive.Count, userId);

            return new WorkingArchivalResult
            {
                Success = true,
                Trigger = trigger,
                MemoriesArchived = memoriesToArchive.Count,
                SummaryId = summaryId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive working memory for user {UserId}", userId);
            return WorkingArchivalResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public WorkingMemoryState GetState(string userId)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return WorkingMemoryState.Empty(userId);
        }

        lock (state.Lock)
        {
            var idleDuration = state.LastActivityTime.HasValue
                ? DateTime.UtcNow - state.LastActivityTime.Value
                : (TimeSpan?)null;

            var trigger = CheckTriggerSync(state);

            return new WorkingMemoryState
            {
                UserId = userId,
                SessionId = state.SessionId,
                MemoryCount = state.Memories.Count,
                TotalTokens = state.TotalTokens,
                TurnCount = state.TurnCount,
                IdleDuration = idleDuration,
                LastActivityTime = state.LastActivityTime,
                TriggerSatisfied = trigger.HasValue,
                SatisfiedTrigger = trigger,
                CurrentTopic = state.CurrentTopic
            };
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetActiveUserIds()
    {
        return _userStates.Keys.ToList();
    }

    /// <inheritdoc />
    public void ClearState(string userId)
    {
        if (_userStates.TryRemove(userId, out var state))
        {
            lock (state.Lock)
            {
                state.Reset();
            }

            _logger.LogDebug("Cleared state for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Synchronous trigger check (called within lock).
    /// </summary>
    private WorkingPromotionTrigger? CheckTriggerSync(UserWorkingState state)
    {
        if (state.LastActivityTime.HasValue)
        {
            var idleDuration = DateTime.UtcNow - state.LastActivityTime.Value;
            if (idleDuration >= _options.IdleTimeout)
            {
                return WorkingPromotionTrigger.IdleTimeout;
            }
        }

        if (state.TotalTokens >= _options.TokenThreshold)
        {
            return WorkingPromotionTrigger.TokenThreshold;
        }

        if (state.TurnCount >= _options.TurnThreshold)
        {
            return WorkingPromotionTrigger.TurnThreshold;
        }

        if (_options.EnableTopicChangeDetection &&
            state.CurrentTopicEmbedding.Length > 0 &&
            state.LastTopicEmbedding.Length > 0)
        {
            var similarity = CosineSimilarity(
                state.CurrentTopicEmbedding.Span,
                state.LastTopicEmbedding.Span);

            if (similarity < _options.TopicChangeSimilarityThreshold)
            {
                return WorkingPromotionTrigger.TopicChange;
            }
        }

        return null;
    }

    /// <summary>
    /// Estimates token count from text (~4 characters per token).
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Creates an extractive summary from memories.
    /// Selects most important sentences based on position and content length.
    /// </summary>
    private static string CreateExtractiveSummary(List<MemoryUnit> memories)
    {
        if (memories.Count == 0) return string.Empty;
        if (memories.Count == 1) return memories[0].Content;

        var sb = new StringBuilder();
        sb.AppendLine("Session Summary:");
        sb.AppendLine();

        // Sort by importance and take top N
        var topMemories = memories
            .OrderByDescending(m => m.ImportanceScore)
            .Take(Math.Min(5, memories.Count))
            .ToList();

        foreach (var memory in topMemories)
        {
            // Take first 200 characters of each memory as a key point
            var excerpt = memory.Content.Length > 200
                ? memory.Content[..200] + "..."
                : memory.Content;

            sb.AppendLine($"• {excerpt}");
        }

        sb.AppendLine();
        sb.AppendLine($"[{memories.Count} memories archived]");

        return sb.ToString();
    }

    /// <summary>
    /// Calculates cosine similarity between two embeddings.
    /// </summary>
    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    /// <summary>
    /// Calculates session importance from archived memories.
    /// </summary>
    private static float CalculateSessionImportance(List<MemoryUnit> memories)
    {
        if (memories.Count == 0) return 0.5f;

        // Average importance weighted by recency
        var weightedSum = 0f;
        var weightSum = 0f;
        for (int i = 0; i < memories.Count; i++)
        {
            var weight = (i + 1f) / memories.Count; // More recent = higher weight
            weightedSum += memories[i].ImportanceScore * weight;
            weightSum += weight;
        }

        return Math.Clamp(weightedSum / weightSum, 0.1f, 1.0f);
    }

    /// <summary>
    /// Extracts unique topics from a collection of memories.
    /// </summary>
    private static List<string> ExtractTopicsFromMemories(List<MemoryUnit> memories)
    {
        var topics = new HashSet<string> { "session_summary" };
        foreach (var memory in memories)
        {
            if (memory.Topics != null)
            {
                foreach (var topic in memory.Topics)
                {
                    topics.Add(topic);
                }
            }
        }
        return [.. topics];
    }

    /// <summary>
    /// Internal state tracking for a user.
    /// </summary>
    private sealed class UserWorkingState
    {
        public string UserId { get; }
        public string? SessionId { get; set; }
        public int TurnCount { get; set; }
        public int TotalTokens { get; set; }
        public DateTime? LastActivityTime { get; set; }
        public string? CurrentTopic { get; set; }
        public ReadOnlyMemory<float> CurrentTopicEmbedding { get; set; }
        public ReadOnlyMemory<float> LastTopicEmbedding { get; set; }
        public List<MemoryUnit> Memories { get; } = [];
        public object Lock { get; } = new();

        public UserWorkingState(string userId)
        {
            UserId = userId;
        }

        public void Reset()
        {
            SessionId = null;
            TurnCount = 0;
            TotalTokens = 0;
            LastActivityTime = null;
            CurrentTopic = null;
            CurrentTopicEmbedding = ReadOnlyMemory<float>.Empty;
            LastTopicEmbedding = ReadOnlyMemory<float>.Empty;
            Memories.Clear();
        }
    }
}
