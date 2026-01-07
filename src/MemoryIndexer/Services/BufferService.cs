using System.Collections.Concurrent;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of the Sensory buffer (Tier 0).
/// Provides async staging for raw sensory input before promotion to Working memory.
/// Implements Atkinson-Shiffrin Multi-Store Model's sensory register.
/// </summary>
/// <remarks>
/// 4-Tier Cognitive Architecture:
/// - Buffer (T0): This implementation
/// - Short-Term Memory (T1): ShortTermMemoryService
/// - LongTermStore (T2): ITieredMemoryStore
/// - ArchiveStore (T3): ITieredMemoryStore
///
/// Features:
/// - Thread-safe per-user buffers
/// - Multi-signal promotion triggers (OR logic)
/// - Token counting for threshold detection
/// - Idle timeout tracking
/// </remarks>
public sealed class BufferService : IBuffer
{
    private readonly ConcurrentDictionary<string, UserBuffer> _userBuffers = new();
    private readonly SensoryBufferOptions _options;
    private readonly ILogger<BufferService> _logger;
    private readonly object _lock = new();

    public BufferService(
        IOptions<MemoryIndexerOptions> options,
        ILogger<BufferService> logger)
    {
        _options = options.Value.SensoryBuffer;
        _logger = logger;
    }

    /// <inheritdoc />
    public int GetCount(string userId)
    {
        return _userBuffers.TryGetValue(userId, out var buffer) ? buffer.Items.Count : 0;
    }

    /// <inheritdoc />
    public int GetTokenCount(string userId)
    {
        return _userBuffers.TryGetValue(userId, out var buffer) ? buffer.TotalTokens : 0;
    }

    /// <inheritdoc />
    public Task<SensoryMemory> EnqueueAsync(
        string content,
        string userId,
        string? sessionId = null,
        string? role = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = GetOrCreateBuffer(userId);
        var tokenCount = EstimateTokenCount(content);

        lock (buffer.Lock)
        {
            buffer.TurnCounter++;
            var item = new SensoryMemory
            {
                Content = content,
                UserId = userId,
                SessionId = sessionId,
                Timestamp = DateTime.UtcNow,
                TokenCount = tokenCount,
                TurnIndex = buffer.TurnCounter,
                Role = role,
                Metadata = metadata
            };

            buffer.Items.Enqueue(item);
            buffer.TotalTokens += tokenCount;
            buffer.LastActivityTime = DateTime.UtcNow;

            // Enforce max buffer size - remove oldest if exceeded
            while (buffer.Items.Count > _options.MaxBufferSize ||
                   buffer.TotalTokens > _options.MaxBufferTokens)
            {
                if (buffer.Items.TryDequeue(out var removed))
                {
                    buffer.TotalTokens -= removed.TokenCount;
                    _logger.LogDebug(
                        "Buffer overflow for user {UserId}, removed oldest item {ItemId}",
                        userId, removed.Id);
                }
                else break;
            }

            _logger.LogTrace(
                "Enqueued item for user {UserId}: {Tokens} tokens, turn {Turn}",
                userId, tokenCount, buffer.TurnCounter);

            return Task.FromResult(item);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SensoryMemory>> GetPendingAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_userBuffers.TryGetValue(userId, out var buffer))
        {
            return Task.FromResult<IReadOnlyList<SensoryMemory>>([]);
        }

        lock (buffer.Lock)
        {
            return Task.FromResult<IReadOnlyList<SensoryMemory>>(
                buffer.Items.ToList());
        }
    }

    /// <inheritdoc />
    public Task<PromotionTriggerType?> CheckTriggerAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_userBuffers.TryGetValue(userId, out var buffer))
        {
            return Task.FromResult<PromotionTriggerType?>(null);
        }

        lock (buffer.Lock)
        {
            if (buffer.Items.IsEmpty)
            {
                return Task.FromResult<PromotionTriggerType?>(null);
            }

            // Check triggers in priority order (OR logic)

            // 1. Token threshold
            if (buffer.TotalTokens >= _options.TokenThreshold)
            {
                _logger.LogDebug(
                    "Token trigger for user {UserId}: {Tokens} >= {Threshold}",
                    userId, buffer.TotalTokens, _options.TokenThreshold);
                return Task.FromResult<PromotionTriggerType?>(PromotionTriggerType.TokenThreshold);
            }

            // 2. Turn threshold
            if (buffer.TurnCounter >= _options.TurnThreshold)
            {
                _logger.LogDebug(
                    "Turn trigger for user {UserId}: {Turns} >= {Threshold}",
                    userId, buffer.TurnCounter, _options.TurnThreshold);
                return Task.FromResult<PromotionTriggerType?>(PromotionTriggerType.TurnThreshold);
            }

            // 3. Idle timeout
            var idleDuration = DateTime.UtcNow - buffer.LastActivityTime;
            if (idleDuration >= _options.IdleTimeout)
            {
                _logger.LogDebug(
                    "Idle trigger for user {UserId}: {Duration} >= {Timeout}",
                    userId, idleDuration, _options.IdleTimeout);
                return Task.FromResult<PromotionTriggerType?>(PromotionTriggerType.IdleTimeout);
            }

            return Task.FromResult<PromotionTriggerType?>(null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SensoryMemory>> DrainAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return DrainAsync(userId, int.MaxValue, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SensoryMemory>> DrainAsync(
        string userId,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_userBuffers.TryGetValue(userId, out var buffer))
        {
            return Task.FromResult<IReadOnlyList<SensoryMemory>>([]);
        }

        var drained = new List<SensoryMemory>();

        lock (buffer.Lock)
        {
            while (drained.Count < maxItems && buffer.Items.TryDequeue(out var item))
            {
                drained.Add(item);
                buffer.TotalTokens -= item.TokenCount;
            }

            // Reset turn counter if fully drained
            if (buffer.Items.IsEmpty)
            {
                buffer.TurnCounter = 0;
            }

            _logger.LogDebug(
                "Drained {Count} items for user {UserId}, {RemainingTokens} tokens remaining",
                drained.Count, userId, buffer.TotalTokens);
        }

        return Task.FromResult<IReadOnlyList<SensoryMemory>>(drained);
    }

    /// <inheritdoc />
    public Task<int> ClearAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_userBuffers.TryRemove(userId, out var buffer))
        {
            return Task.FromResult(0);
        }

        var count = buffer.Items.Count;
        _logger.LogDebug("Cleared buffer for user {UserId}: {Count} items discarded", userId, count);
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public SensoryBufferStats GetStats(string userId)
    {
        if (!_userBuffers.TryGetValue(userId, out var buffer))
        {
            return SensoryBufferStats.Empty;
        }

        lock (buffer.Lock)
        {
            var items = buffer.Items.ToArray();
            var idleDuration = DateTime.UtcNow - buffer.LastActivityTime;
            var trigger = CheckTriggerSync(buffer);

            return new SensoryBufferStats
            {
                ItemCount = items.Length,
                TotalTokens = buffer.TotalTokens,
                TurnCount = buffer.TurnCounter,
                IdleDuration = idleDuration,
                OldestItemTimestamp = items.FirstOrDefault()?.Timestamp,
                NewestItemTimestamp = items.LastOrDefault()?.Timestamp,
                TriggerSatisfied = trigger.HasValue,
                SatisfiedTrigger = trigger
            };
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetActiveUserIds()
    {
        return _userBuffers
            .Where(kvp => !kvp.Value.Items.IsEmpty)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private UserBuffer GetOrCreateBuffer(string userId)
    {
        return _userBuffers.GetOrAdd(userId, _ => new UserBuffer
        {
            LastActivityTime = DateTime.UtcNow
        });
    }

    private PromotionTriggerType? CheckTriggerSync(UserBuffer buffer)
    {
        if (buffer.Items.IsEmpty) return null;

        if (buffer.TotalTokens >= _options.TokenThreshold)
            return PromotionTriggerType.TokenThreshold;

        if (buffer.TurnCounter >= _options.TurnThreshold)
            return PromotionTriggerType.TurnThreshold;

        var idleDuration = DateTime.UtcNow - buffer.LastActivityTime;
        if (idleDuration >= _options.IdleTimeout)
            return PromotionTriggerType.IdleTimeout;

        return null;
    }

    /// <summary>
    /// Estimates token count using simple whitespace splitting.
    /// For more accurate counting, integrate tiktoken or similar.
    /// </summary>
    private static int EstimateTokenCount(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;

        // Simple estimation: ~4 characters per token (English average)
        // More accurate: use tiktoken library
        return (int)Math.Ceiling(content.Length / 4.0);
    }

    /// <summary>
    /// Per-user buffer state.
    /// </summary>
    private sealed class UserBuffer
    {
        public ConcurrentQueue<SensoryMemory> Items { get; } = new();
        public int TotalTokens { get; set; }
        public int TurnCounter { get; set; }
        public DateTime LastActivityTime { get; set; }
        public object Lock { get; } = new();
    }
}
