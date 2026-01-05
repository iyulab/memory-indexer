using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Promotion;

/// <summary>
/// Implementation of buffer promotion from Recently to Working tier.
/// Applies topic segmentation and creates MemoryUnits for promotion.
/// </summary>
/// <remarks>
/// Promotion Pipeline:
/// 1. Drain items from RecentlyBuffer (triggered by time/tokens/turns)
/// 2. Convert to ConversationMessages for topic analysis
/// 3. Segment by topic using TopicSegmenter
/// 4. Create MemoryUnit per topic group
/// 5. Promote to WorkingMemory (with eviction if at capacity)
/// </remarks>
public sealed class BufferPromoterService : IBufferPromoter
{
    private readonly IRecentlyBuffer _recentlyBuffer;
    private readonly IWorkingMemory _workingMemory;
    private readonly IEmbeddingService _embeddingService;
    private readonly TopicSegmenter _topicSegmenter;
    private readonly ILogger<BufferPromoterService> _logger;

    public BufferPromoterService(
        IRecentlyBuffer recentlyBuffer,
        IWorkingMemory workingMemory,
        IEmbeddingService embeddingService,
        TopicSegmenter topicSegmenter,
        ILogger<BufferPromoterService> logger)
    {
        _recentlyBuffer = recentlyBuffer;
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
            // Drain all items from the buffer
            var items = await _recentlyBuffer.DrainAsync(userId, cancellationToken);

            if (items.Count == 0)
            {
                return BufferPromotionResult.Empty;
            }

            _logger.LogDebug(
                "Promoting {Count} items for user {UserId} with trigger {Trigger}",
                items.Count, userId, trigger);

            var result = await PromoteItemsInternalAsync(items, trigger, cancellationToken);

            stopwatch.Stop();
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to promote buffer for user {UserId}", userId);
            stopwatch.Stop();
            return BufferPromotionResult.Failure(ex.Message) with { Duration = stopwatch.Elapsed };
        }
    }

    /// <inheritdoc />
    public async Task<BufferPromotionResult> PromoteItemsAsync(
        IReadOnlyList<RecentlyMemory> items,
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
        var activeUsers = _recentlyBuffer.GetActiveUserIds();

        foreach (var userId in activeUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trigger = await _recentlyBuffer.CheckTriggerAsync(userId, cancellationToken);
            if (trigger.HasValue)
            {
                var stats = _recentlyBuffer.GetStats(userId);
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
        IReadOnlyList<RecentlyMemory> items,
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
                Tier = MemoryTier.Working,
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

            if (evicted != null)
            {
                evictedMemories.Add(evicted);
                _logger.LogDebug(
                    "Evicted memory {EvictedId} to make room for promoted memory {NewId}",
                    evicted.Id, memory.Id);
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
