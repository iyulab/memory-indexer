using System.Collections.Concurrent;

namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Service for rate limiting memory operations per user/tenant.
/// Prevents abuse and ensures fair resource usage.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire a permit for the specified operation.
    /// </summary>
    /// <param name="userId">The user or tenant identifier.</param>
    /// <param name="operationType">Type of operation being performed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether the operation is permitted.</returns>
    Task<RateLimitResult> TryAcquireAsync(
        string userId,
        OperationType operationType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current rate limit status for a user.
    /// </summary>
    /// <param name="userId">The user or tenant identifier.</param>
    /// <param name="operationType">Type of operation to check.</param>
    /// <returns>Current rate limit status.</returns>
    RateLimitStatus GetStatus(string userId, OperationType operationType);

    /// <summary>
    /// Resets rate limits for a specific user (admin operation).
    /// </summary>
    /// <param name="userId">The user or tenant identifier.</param>
    void Reset(string userId);
}

/// <summary>
/// Types of operations that can be rate limited.
/// </summary>
public enum OperationType
{
    /// <summary>
    /// Memory store operations.
    /// </summary>
    Store,

    /// <summary>
    /// Memory recall/search operations.
    /// </summary>
    Recall,

    /// <summary>
    /// Memory update operations.
    /// </summary>
    Update,

    /// <summary>
    /// Memory delete operations.
    /// </summary>
    Delete,

    /// <summary>
    /// Batch operations.
    /// </summary>
    Batch,

    /// <summary>
    /// Any operation (global limit).
    /// </summary>
    Any
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public sealed class RateLimitResult
{
    /// <summary>
    /// Whether the operation is permitted.
    /// </summary>
    public bool IsPermitted { get; init; }

    /// <summary>
    /// Number of remaining permits in the current window.
    /// </summary>
    public int RemainingPermits { get; init; }

    /// <summary>
    /// Time until the rate limit resets.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Current window start time.
    /// </summary>
    public DateTimeOffset WindowStart { get; init; }

    /// <summary>
    /// Reason for denial (if not permitted).
    /// </summary>
    public string? DenialReason { get; init; }

    /// <summary>
    /// Creates a permitted result.
    /// </summary>
    public static RateLimitResult Permitted(int remaining, DateTimeOffset windowStart) =>
        new()
        {
            IsPermitted = true,
            RemainingPermits = remaining,
            WindowStart = windowStart
        };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static RateLimitResult Denied(TimeSpan retryAfter, string reason) =>
        new()
        {
            IsPermitted = false,
            RemainingPermits = 0,
            RetryAfter = retryAfter,
            DenialReason = reason
        };
}

/// <summary>
/// Current rate limit status for a user.
/// </summary>
public sealed class RateLimitStatus
{
    /// <summary>
    /// Maximum permits per window.
    /// </summary>
    public int MaxPermits { get; init; }

    /// <summary>
    /// Remaining permits in current window.
    /// </summary>
    public int RemainingPermits { get; init; }

    /// <summary>
    /// Used permits in current window.
    /// </summary>
    public int UsedPermits => MaxPermits - RemainingPermits;

    /// <summary>
    /// Time until window resets.
    /// </summary>
    public TimeSpan TimeUntilReset { get; init; }

    /// <summary>
    /// Window duration.
    /// </summary>
    public TimeSpan WindowDuration { get; init; }
}

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    /// Whether rate limiting is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default permits per minute for store operations.
    /// </summary>
    public int StorePermitsPerMinute { get; set; } = 60;

    /// <summary>
    /// Default permits per minute for recall operations.
    /// </summary>
    public int RecallPermitsPerMinute { get; set; } = 100;

    /// <summary>
    /// Default permits per minute for update operations.
    /// </summary>
    public int UpdatePermitsPerMinute { get; set; } = 30;

    /// <summary>
    /// Default permits per minute for delete operations.
    /// </summary>
    public int DeletePermitsPerMinute { get; set; } = 20;

    /// <summary>
    /// Default permits per minute for batch operations.
    /// </summary>
    public int BatchPermitsPerMinute { get; set; } = 10;

    /// <summary>
    /// Global permits per minute (across all operation types).
    /// </summary>
    public int GlobalPermitsPerMinute { get; set; } = 200;

    /// <summary>
    /// Window duration for rate limiting.
    /// </summary>
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets permits per minute for an operation type.
    /// </summary>
    public int GetPermits(OperationType type) => type switch
    {
        OperationType.Store => StorePermitsPerMinute,
        OperationType.Recall => RecallPermitsPerMinute,
        OperationType.Update => UpdatePermitsPerMinute,
        OperationType.Delete => DeletePermitsPerMinute,
        OperationType.Batch => BatchPermitsPerMinute,
        OperationType.Any => GlobalPermitsPerMinute,
        _ => GlobalPermitsPerMinute
    };
}

/// <summary>
/// Sliding window rate limiter implementation.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, UserRateLimitState> _states = new();

    public SlidingWindowRateLimiter(RateLimitOptions? options = null)
    {
        _options = options ?? new RateLimitOptions();
    }

    public Task<RateLimitResult> TryAcquireAsync(
        string userId,
        OperationType operationType,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(RateLimitResult.Permitted(int.MaxValue, DateTimeOffset.UtcNow));
        }

        var key = $"{userId}:{operationType}";
        var globalKey = $"{userId}:{OperationType.Any}";
        var now = DateTimeOffset.UtcNow;

        // Check operation-specific limit
        var operationState = _states.GetOrAdd(key, _ => new UserRateLimitState(_options.GetPermits(operationType), _options.WindowDuration));
        var operationResult = operationState.TryAcquire(now);

        if (!operationResult.IsPermitted)
        {
            return Task.FromResult(operationResult);
        }

        // Check global limit
        if (operationType != OperationType.Any)
        {
            var globalState = _states.GetOrAdd(globalKey, _ => new UserRateLimitState(_options.GlobalPermitsPerMinute, _options.WindowDuration));
            var globalResult = globalState.TryAcquire(now);

            if (!globalResult.IsPermitted)
            {
                // Rollback operation-specific permit
                operationState.Release();
                return Task.FromResult(globalResult);
            }
        }

        return Task.FromResult(operationResult);
    }

    public RateLimitStatus GetStatus(string userId, OperationType operationType)
    {
        var key = $"{userId}:{operationType}";
        var now = DateTimeOffset.UtcNow;

        if (_states.TryGetValue(key, out var state))
        {
            return state.GetStatus(now);
        }

        var maxPermits = _options.GetPermits(operationType);
        return new RateLimitStatus
        {
            MaxPermits = maxPermits,
            RemainingPermits = maxPermits,
            TimeUntilReset = _options.WindowDuration,
            WindowDuration = _options.WindowDuration
        };
    }

    public void Reset(string userId)
    {
        var keysToRemove = _states.Keys.Where(k => k.StartsWith(userId + ":")).ToList();
        foreach (var key in keysToRemove)
        {
            _states.TryRemove(key, out _);
        }
    }

    private sealed class UserRateLimitState
    {
        private readonly int _maxPermits;
        private readonly TimeSpan _windowDuration;
        private readonly object _lock = new();
        private readonly Queue<DateTimeOffset> _timestamps = new();

        public UserRateLimitState(int maxPermits, TimeSpan windowDuration)
        {
            _maxPermits = maxPermits;
            _windowDuration = windowDuration;
        }

        public RateLimitResult TryAcquire(DateTimeOffset now)
        {
            lock (_lock)
            {
                // Remove expired timestamps
                var windowStart = now - _windowDuration;
                while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count >= _maxPermits)
                {
                    var oldestTimestamp = _timestamps.Peek();
                    var retryAfter = oldestTimestamp + _windowDuration - now;
                    return RateLimitResult.Denied(
                        retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1),
                        $"Rate limit exceeded. Max {_maxPermits} requests per {_windowDuration.TotalSeconds} seconds.");
                }

                _timestamps.Enqueue(now);
                return RateLimitResult.Permitted(_maxPermits - _timestamps.Count, windowStart);
            }
        }

        public void Release()
        {
            lock (_lock)
            {
                if (_timestamps.Count > 0)
                {
                    // Remove the most recent timestamp (approximation)
                    var list = _timestamps.ToList();
                    if (list.Count > 0)
                    {
                        list.RemoveAt(list.Count - 1);
                        _timestamps.Clear();
                        foreach (var ts in list)
                        {
                            _timestamps.Enqueue(ts);
                        }
                    }
                }
            }
        }

        public RateLimitStatus GetStatus(DateTimeOffset now)
        {
            lock (_lock)
            {
                var windowStart = now - _windowDuration;
                while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
                {
                    _timestamps.Dequeue();
                }

                var oldestTimestamp = _timestamps.Count > 0 ? _timestamps.Peek() : now;
                var timeUntilReset = oldestTimestamp + _windowDuration - now;

                return new RateLimitStatus
                {
                    MaxPermits = _maxPermits,
                    RemainingPermits = Math.Max(0, _maxPermits - _timestamps.Count),
                    TimeUntilReset = timeUntilReset > TimeSpan.Zero ? timeUntilReset : _windowDuration,
                    WindowDuration = _windowDuration
                };
            }
        }
    }
}
