using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Promotion;

/// <summary>
/// Implementation of buffer promotion from Sensory to Working tier.
/// Applies topic segmentation and creates MemoryUnits for promotion.
/// Implements Atkinson-Shiffrin Multi-Store Model's sensory→short-term memory transition.
/// </summary>
/// <remarks>
/// Promotion Pipeline:
/// 1. Drain items from SensoryBuffer (triggered by time/tokens/turns)
/// 2. Convert to ConversationMessages for topic analysis
/// 3. Segment by topic using TopicSegmenter
/// 4. Create MemoryUnit per topic group
/// 5. Promote to WorkingMemory (with eviction if at capacity)
/// </remarks>
public sealed class SensoryPromoterService : ISensoryPromoter
{
    private readonly IBuffer _sensoryBuffer;
    private readonly IShortTermMemory _workingMemory;
    private readonly IEmbeddingService _embeddingService;
    private readonly TopicSegmenter _topicSegmenter;
    private readonly ILogger<SensoryPromoterService> _logger;

    public SensoryPromoterService(
        IBuffer sensoryBuffer,
        IShortTermMemory workingMemory,
        IEmbeddingService embeddingService,
        TopicSegmenter topicSegmenter,
        ILogger<SensoryPromoterService> logger)
    {
        _sensoryBuffer = sensoryBuffer;
        _workingMemory = workingMemory;
        _embeddingService = embeddingService;
        _topicSegmenter = topicSegmenter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BufferPromotionResult> PromoteAsync(
        string userId,
        PromotionTriggerType trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[PROMOTION] Starting promotion cycle for user {UserId}", userId);

            // Drain all items from the buffer
            var items = await _sensoryBuffer.DrainAsync(userId, cancellationToken);

            if (items.Count == 0)
            {
                _logger.LogInformation("[PROMOTION] No items to promote for user {UserId}", userId);
                return BufferPromotionResult.Empty;
            }

            _logger.LogInformation(
                "[PROMOTION] Found {Count} promotable items for user {UserId} with trigger {Trigger}",
                items.Count, userId, trigger);

            var result = await PromoteItemsInternalAsync(items, trigger, cancellationToken);

            stopwatch.Stop();

            if (result.Success)
            {
                _logger.LogInformation(
                    "[PROMOTION] ✅ Successfully promoted {ItemCount} items as {SegmentCount} memories. " +
                    "Evicted: {EvictedCount}. Duration: {Duration:F2}s",
                    result.ItemsProcessed, result.CreatedMemories.Count, result.EvictedMemories.Count,
                    stopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _logger.LogError("[PROMOTION] ❌ Promotion failed: {Error}", result.Error);
            }

            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PROMOTION] ❌ Failed to promote buffer for user {UserId}", userId);
            stopwatch.Stop();
            return BufferPromotionResult.Failure(ex.Message) with { Duration = stopwatch.Elapsed };
        }
    }

    /// <inheritdoc />
    public async Task<BufferPromotionResult> PromoteItemsAsync(
        IReadOnlyList<SensoryMemory> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return BufferPromotionResult.Empty;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await PromoteItemsInternalAsync(
                items, PromotionTriggerType.Manual, cancellationToken);

            stopwatch.Stop();
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to promote {Count} items", items.Count);
            stopwatch.Stop();
            return BufferPromotionResult.Failure(ex.Message) with { Duration = stopwatch.Elapsed };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserPromotionCheck>> CheckPendingPromotionsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<UserPromotionCheck>();
        var activeUsers = _sensoryBuffer.GetActiveUserIds();

        foreach (var userId in activeUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trigger = await _sensoryBuffer.CheckTriggerAsync(userId, cancellationToken);
            if (trigger.HasValue)
            {
                var stats = _sensoryBuffer.GetStats(userId);
                results.Add(new UserPromotionCheck
                {
                    UserId = userId,
                    Trigger = trigger.Value,
                    PendingItems = stats.ItemCount,
                    PendingTokens = stats.TotalTokens
                });
            }
        }

        return results;
    }

    private async Task<BufferPromotionResult> PromoteItemsInternalAsync(
        IReadOnlyList<SensoryMemory> items,
        PromotionTriggerType trigger,
        CancellationToken cancellationToken)
    {
        var userId = items[0].UserId;
        var sessionId = items[0].SessionId;

        // Convert to conversation messages for topic segmentation
        var messages = items.Select(item => new ConversationMessage
        {
            Role = item.Role ?? "user",
            Content = item.Content,
            Timestamp = item.Timestamp
        }).ToList();

        // Segment by topic
        IReadOnlyList<TopicSegment> segments;
        if (messages.Count >= 2)
        {
            segments = await _topicSegmenter.SegmentConversationAsync(messages, cancellationToken);
        }
        else
        {
            // Single message - create single segment
            segments =
            [
                new TopicSegment
                {
                    Content = messages[0].Content,
                    StartIndex = 0,
                    EndIndex = 0,
                    Messages = messages
                }
            ];
        }

        _logger.LogDebug(
            "Created {SegmentCount} topic segments from {ItemCount} items",
            segments.Count, items.Count);

        // Create MemoryUnits for each topic segment
        var createdMemories = new List<MemoryUnit>();
        var evictedMemories = new List<MemoryUnit>();

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Generate embedding for the segment
            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                segment.Content, cancellationToken);

            // Create MemoryUnit
            var memory = new MemoryUnit
            {
                Content = segment.Content,
                UserId = userId,
                SessionId = sessionId,
                Embedding = embedding,
                Type = MemoryType.Episodic, // Episodic: conversation with temporal context
                Tier = Tier.Short,
                Stability = MemoryStability.Volatile, // Initial stability
                ImportanceScore = CalculateImportance(segment),
                Topics = ExtractTopics(segment),
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "buffer_promotion",
                    ["promotion_trigger"] = trigger.ToString(),
                    ["topic_label"] = segment.TopicLabel ?? "auto",
                    ["message_count"] = segment.Messages.Count.ToString(),
                    ["start_index"] = segment.StartIndex.ToString(),
                    ["end_index"] = segment.EndIndex.ToString()
                }
            };

            // Promote to working memory
            var evicted = await _workingMemory.PromoteAsync(memory, cancellationToken);
            createdMemories.Add(memory);

            _logger.LogInformation(
                "[PROMOTION] ✅ Promoted: {Content}",
                memory.Content.Length > 50 ? memory.Content[..50] + "..." : memory.Content);

            if (evicted != null)
            {
                evictedMemories.Add(evicted);
                _logger.LogWarning(
                    "[PROMOTION] ⚠️ Evicted memory {EvictedId} to make room for {NewId}. " +
                    "Content: {Content}",
                    evicted.Id, memory.Id,
                    evicted.Content.Length > 50 ? evicted.Content[..50] + "..." : evicted.Content);
            }
        }

        _logger.LogInformation(
            "Promoted {ItemCount} items as {SegmentCount} topic segments for user {UserId}. " +
            "Created {CreatedCount} memories, evicted {EvictedCount}",
            items.Count, segments.Count, userId, createdMemories.Count, evictedMemories.Count);

        return new BufferPromotionResult
        {
            Success = true,
            Trigger = trigger,
            ItemsProcessed = items.Count,
            TopicGroupsCreated = segments.Count,
            CreatedMemories = createdMemories,
            EvictedMemories = evictedMemories
        };
    }

    /// <summary>
    /// Calculates importance score for a topic segment.
    /// </summary>
    private static float CalculateImportance(TopicSegment segment)
    {
        // Base importance on message count and content length
        var messageCount = segment.Messages.Count;
        var contentLength = segment.Content.Length;

        // More messages = more important conversation thread
        var messageScore = Math.Min(1.0f, messageCount / 10.0f);

        // Longer content = more substantial
        var lengthScore = Math.Min(1.0f, contentLength / 1000.0f);

        // Combine with weights
        return Math.Clamp(messageScore * 0.4f + lengthScore * 0.3f + 0.3f, 0.1f, 1.0f);
    }

    /// <summary>
    /// Extracts topics from a topic segment.
    /// </summary>
    private static List<string> ExtractTopics(TopicSegment segment)
    {
        var topics = new List<string> { "buffer_promoted" };

        if (!string.IsNullOrEmpty(segment.TopicLabel))
        {
            topics.Add(segment.TopicLabel);
        }

        if (segment.Messages.Count > 5)
        {
            topics.Add("extended_discussion");
        }

        return topics;
    }
}
