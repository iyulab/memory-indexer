using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using Microsoft.Extensions.Logging;
using System.Globalization;

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
public sealed partial class SensoryPromoterService : ISensoryPromoter
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
            LogPROMOTIONStartingPromotionCycleUser(_logger, userId);

            // Drain all items from the buffer
            var items = await _sensoryBuffer.DrainAsync(userId, cancellationToken);

            if (items.Count == 0)
            {
                LogPROMOTIONItemsPromoteUserUserId(_logger, userId);
                return BufferPromotionResult.Empty;
            }

            LogPROMOTIONFoundCountPromotableItems(_logger, items.Count, userId, trigger);

            var result = await PromoteItemsInternalAsync(items, trigger, cancellationToken);

            stopwatch.Stop();

            if (result.Success)
            {
                LogPROMOTIONSuccessfullyPromotedItemCountItems(_logger, result.ItemsProcessed, result.CreatedMemories.Count, result.EvictedMemories.Count, stopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                LogPROMOTIONPromotionFailedError(_logger, result.Error ?? "Unknown error");
            }

            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            LogPROMOTIONFailedPromoteBufferUser(_logger, ex, userId);
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
            LogFailedPromoteCountItems(_logger, ex, items.Count);
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

        LogCreatedSegmentCountTopicSegmentsItemCount(_logger, segments.Count, items.Count);

        // Create MemoryUnits for each topic segment
        var createdMemories = new List<MemoryUnit>();
        var evictedMemories = new List<MemoryUnit>();

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Generate embedding for the segment
            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                segment.Content, cancellationToken);

            // Determine dominant role from segment messages (for source attribution)
            var dominantRole = segment.Messages
                .Where(m => !string.IsNullOrEmpty(m.Role))
                .GroupBy(m => m.Role)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            // Collect all unique roles for metadata (multi-party conversations)
            var uniqueRoles = segment.Messages
                .Where(m => !string.IsNullOrEmpty(m.Role))
                .Select(m => m.Role)
                .Distinct()
                .ToList();

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
                Role = dominantRole, // Preserve role for episodic memory (T0-T2)
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "buffer_promotion",
                    ["promotion_trigger"] = trigger.ToString(),
                    ["topic_label"] = segment.TopicLabel ?? "auto",
                    ["message_count"] = segment.Messages.Count.ToString(CultureInfo.InvariantCulture),
                    ["start_index"] = segment.StartIndex.ToString(CultureInfo.InvariantCulture),
                    ["end_index"] = segment.EndIndex.ToString(CultureInfo.InvariantCulture),
                    ["roles"] = string.Join(",", uniqueRoles) // All roles in segment
                }
            };

            // Promote to working memory
            var evicted = await _workingMemory.PromoteAsync(memory, cancellationToken);
            createdMemories.Add(memory);

            var contentValue = memory.Content.Length > 50 ? memory.Content[..50] + "..." : memory.Content;
            LogPROMOTIONPromotedContent(_logger, contentValue);

            if (evicted != null)
            {
                evictedMemories.Add(evicted);
                var evictedContent = evicted.Content.Length > 50 ? evicted.Content[..50] + "..." : evicted.Content;
                LogPROMOTIONEvictedMemoryEvictedIdMake(_logger, evicted.Id, memory.Id, evictedContent);
            }
        }

        LogPromotedItemCountItemsSegmentCountTopic(_logger, items.Count, segments.Count, userId, createdMemories.Count, evictedMemories.Count);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "[PROMOTION] Starting promotion cycle for user {UserId}")]
    private static partial void LogPROMOTIONStartingPromotionCycleUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[PROMOTION] No items to promote for user {UserId}")]
    private static partial void LogPROMOTIONItemsPromoteUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[PROMOTION] Found {Count} promotable items for user {UserId} with trigger {Trigger}")]
    private static partial void LogPROMOTIONFoundCountPromotableItems(ILogger logger, int count, string userId, PromotionTriggerType trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "[PROMOTION] Successfully promoted {ItemCount} items as {SegmentCount} memories. Evicted: {EvictedCount}. Duration: {Duration:F2}s")]
    private static partial void LogPROMOTIONSuccessfullyPromotedItemCountItems(ILogger logger, int itemCount, int segmentCount, int evictedCount, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "[PROMOTION] Promotion failed: {Error}")]
    private static partial void LogPROMOTIONPromotionFailedError(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[PROMOTION] Failed to promote buffer for user {UserId}")]
    private static partial void LogPROMOTIONFailedPromoteBufferUser(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to promote {Count} items")]
    private static partial void LogFailedPromoteCountItems(ILogger logger, Exception ex, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created {SegmentCount} topic segments from {ItemCount} items")]
    private static partial void LogCreatedSegmentCountTopicSegmentsItemCount(ILogger logger, int segmentCount, int itemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[PROMOTION] Promoted: {Content}")]
    private static partial void LogPROMOTIONPromotedContent(ILogger logger, string content);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[PROMOTION] Evicted memory {EvictedId} to make room for {NewId}. Content: {Content}")]
    private static partial void LogPROMOTIONEvictedMemoryEvictedIdMake(ILogger logger, Guid evictedId, Guid newId, string content);

    [LoggerMessage(Level = LogLevel.Information, Message = "Promoted {ItemCount} items as {SegmentCount} topic segments for user {UserId}. Created {CreatedCount} memories, evicted {EvictedCount}")]
    private static partial void LogPromotedItemCountItemsSegmentCountTopic(ILogger logger, int itemCount, int segmentCount, string userId, int createdCount, int evictedCount);
}
