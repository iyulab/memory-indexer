using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of scope management for the 3-axis memory model.
/// Tracks temporal boundaries: Turn → Topic → Session → User.
/// </summary>
public sealed class ScopeManager : IScopeManager
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ScopeManager> _logger;
    private readonly ScopeManagerOptions _options;
    private readonly ScopeState _state;
    private readonly List<string> _topicHistory = [];
    private readonly Queue<(string Content, ReadOnlyMemory<float>? Embedding, DateTime Timestamp)> _recentTurns = new();

    public ScopeManager(
        IEmbeddingService embeddingService,
        IOptions<ScopeManagerOptions> options,
        ILogger<ScopeManager> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
        _options = options.Value;
        _state = new ScopeState();
    }

    /// <inheritdoc />
    public ScopeState CurrentState => _state;

    /// <inheritdoc />
    public Task InitializeAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _logger.LogInformation("Initializing ScopeManager for user {UserId}, session {SessionId}", userId, sessionId);

        _state.UserId = userId;
        _state.SessionId = sessionId;
        _state.TopicId = GenerateTopicId();
        _state.TurnCount = 0;
        _state.TopicTurnCount = 0;
        _state.TopicTransitionCount = 0;
        _state.SessionStartTime = DateTime.UtcNow;
        _state.TopicStartTime = DateTime.UtcNow;
        _state.LastTurnTimestamp = null;
        _state.IsInitialized = true;

        _topicHistory.Clear();
        _topicHistory.Add(_state.TopicId);
        _recentTurns.Clear();

        _logger.LogDebug("ScopeManager initialized with topic {TopicId}", _state.TopicId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ScopeResolution> RecordTurnAsync(
        string content,
        string? role = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _state.TurnCount++;
        _state.TopicTurnCount++;
        _state.LastTurnTimestamp = DateTime.UtcNow;

        // Generate embedding for topic change detection
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        // Detect topic change
        var topicChanged = await DetectTopicChangeAsync(content, cancellationToken);

        var boundaryType = ScopeBoundaryType.Turn;
        var boundaryCrossed = true; // Always crosses turn boundary

        if (topicChanged)
        {
            // Topic transition detected
            _state.TopicId = GenerateTopicId();
            _topicHistory.Add(_state.TopicId);
            _state.TopicTransitionCount++;
            _state.TopicTurnCount = 1; // Reset topic turn counter
            _state.TopicStartTime = DateTime.UtcNow;

            boundaryType = ScopeBoundaryType.Topic;

            _logger.LogInformation("Topic transition detected: {OldTopic} → {NewTopic}",
                _topicHistory[^2], _state.TopicId);
        }

        // Add to recent turns queue for topic detection
        _recentTurns.Enqueue((content, embedding, DateTime.UtcNow));

        // Keep only recent turns for comparison (circular buffer)
        while (_recentTurns.Count > _options.TopicDetectionWindowSize)
        {
            _recentTurns.Dequeue();
        }

        var resolution = new ScopeResolution
        {
            ResolvedScope = Scope.Turn, // Default for individual turns
            TopicId = _state.TopicId!,
            BoundaryCrossed = boundaryCrossed,
            BoundaryType = boundaryType,
            TurnIndex = _state.TurnCount,
            TopicTurnIndex = _state.TopicTurnCount,
            Confidence = 1.0f
        };

        _logger.LogTrace("Turn {TurnIndex} recorded in topic {TopicId} (topic turn {TopicTurnIndex})",
            _state.TurnCount, _state.TopicId, _state.TopicTurnCount);

        return resolution;
    }

    /// <inheritdoc />
    public Task<Scope> ResolveScopeAsync(
        string content,
        MemoryType type,
        float importance = 0.5f,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // Scope resolution heuristics based on Type × Importance
        var scope = (type, importance) switch
        {
            // Episodic: Contextual experiences, typically Turn/Topic scope
            (MemoryType.Episodic, < 0.3f) => Scope.Turn,
            (MemoryType.Episodic, < 0.7f) => Scope.Topic,
            (MemoryType.Episodic, _) => Scope.Session,

            // Semantic: Facts and knowledge, typically Session/User scope
            (MemoryType.Semantic, < 0.5f) => Scope.Topic,
            (MemoryType.Semantic, < 0.8f) => Scope.Session,
            (MemoryType.Semantic, _) => Scope.User,

            // Procedural: How-to knowledge, typically Session/User scope
            (MemoryType.Procedural, < 0.6f) => Scope.Session,
            (MemoryType.Procedural, _) => Scope.User,

            // Fact: Assertions and preferences, typically User scope
            (MemoryType.Fact, < 0.7f) => Scope.Session,
            (MemoryType.Fact, _) => Scope.User,

            // Default fallback
            _ => Scope.Session
        };

        _logger.LogTrace("Resolved scope {Scope} for type {Type} with importance {Importance:F2}",
            scope, type, importance);

        return Task.FromResult(scope);
    }

    /// <inheritdoc />
    public Task<bool> DetectTopicChangeAsync(
        string currentContent,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // No topic change if this is the first turn or not enough history
        if (_state.TurnCount == 0 || _recentTurns.Count < 2)
        {
            return Task.FromResult(false);
        }

        // Topic change detection based on:
        // 1. Semantic similarity with recent turns
        // 2. Time gap between turns
        // 3. Turn count threshold

        // Time-based detection: Long pause suggests topic change
        var timeSinceLastTurn = DateTime.UtcNow - _state.LastTurnTimestamp!.Value;
        if (timeSinceLastTurn > _options.TopicIdleThreshold)
        {
            _logger.LogDebug("Topic change detected: idle time {IdleTime} > threshold {Threshold}",
                timeSinceLastTurn, _options.TopicIdleThreshold);
            return Task.FromResult(true);
        }

        // Turn count-based detection: Long topic suggests natural transition
        if (_state.TopicTurnCount >= _options.MaxTurnsPerTopic)
        {
            _logger.LogDebug("Topic change detected: turn count {TurnCount} >= max {Max}",
                _state.TopicTurnCount, _options.MaxTurnsPerTopic);
            return Task.FromResult(true);
        }

        // Semantic similarity-based detection
        // Get the last embedding from queue
        var lastTurn = _recentTurns.LastOrDefault();
        if (lastTurn.Embedding == null || lastTurn.Embedding.Value.IsEmpty)
        {
            return Task.FromResult(false);
        }

        // TODO: Implement semantic similarity comparison with current content
        // For now, use conservative approach - no topic change
        // Future: Compare embedding similarity with recent turn average

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public string GetCurrentTopicId()
    {
        EnsureInitialized();
        return _state.TopicId!;
    }

    /// <inheritdoc />
    public IReadOnlyList<MemoryUnit> FilterByScope(
        IEnumerable<MemoryUnit> memories,
        Scope targetScope,
        bool includeNarrower = true)
    {
        var filtered = memories.Where(m =>
        {
            if (m.Scope == targetScope)
            {
                return true;
            }

            if (!includeNarrower)
            {
                return false;
            }

            // Include memories with narrower (lower numeric value) scopes
            // Scope enum: Turn=0, Topic=1, Session=2, User=3
            // Lower value = narrower scope
            return m.Scope < targetScope;
        }).ToList();

        _logger.LogTrace("Filtered {Count} memories for scope {Scope} (includeNarrower: {IncludeNarrower})",
            filtered.Count, targetScope, includeNarrower);

        return filtered;
    }

    /// <inheritdoc />
    public Task EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.IsInitialized)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Ending session {SessionId}: {Turns} turns, {Topics} topics",
            _state.SessionId, _state.TurnCount, _state.TopicTransitionCount + 1);

        _state.IsInitialized = false;
        _state.SessionId = null;
        _state.TopicId = null;
        _recentTurns.Clear();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ScopeStatistics GetStatistics()
    {
        if (!_state.IsInitialized)
        {
            return new ScopeStatistics
            {
                TotalTurns = 0,
                TopicTransitions = 0,
                AverageTurnsPerTopic = 0,
                CurrentTopicDuration = TimeSpan.Zero,
                SessionDuration = TimeSpan.Zero,
                TopicIds = []
            };
        }

        var now = DateTime.UtcNow;
        var sessionDuration = _state.SessionStartTime.HasValue
            ? now - _state.SessionStartTime.Value
            : TimeSpan.Zero;

        var topicDuration = _state.TopicStartTime.HasValue
            ? now - _state.TopicStartTime.Value
            : TimeSpan.Zero;

        var topicCount = _state.TopicTransitionCount + 1; // +1 for current topic
        var avgTurnsPerTopic = topicCount > 0
            ? (float)_state.TurnCount / topicCount
            : 0;

        return new ScopeStatistics
        {
            TotalTurns = _state.TurnCount,
            TopicTransitions = _state.TopicTransitionCount,
            AverageTurnsPerTopic = avgTurnsPerTopic,
            CurrentTopicDuration = topicDuration,
            SessionDuration = sessionDuration,
            TopicIds = _topicHistory.ToList()
        };
    }

    #region Helper Methods

    private void EnsureInitialized()
    {
        if (!_state.IsInitialized)
        {
            throw new InvalidOperationException(
                "ScopeManager must be initialized before use. Call InitializeAsync first.");
        }
    }

    private static string GenerateTopicId()
    {
        // Generate short, readable topic ID
        return $"topic-{Guid.NewGuid():N}"[..16];
    }

    #endregion
}

/// <summary>
/// Configuration options for ScopeManager.
/// </summary>
public sealed class ScopeManagerOptions
{
    /// <summary>
    /// Idle time threshold for detecting topic change.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan TopicIdleThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum turns per topic before suggesting topic transition.
    /// Default: 20 turns.
    /// </summary>
    public int MaxTurnsPerTopic { get; set; } = 20;

    /// <summary>
    /// Number of recent turns to keep for topic detection.
    /// Default: 10 turns.
    /// </summary>
    public int TopicDetectionWindowSize { get; set; } = 10;

    /// <summary>
    /// Semantic similarity threshold for topic change detection.
    /// Below this threshold suggests topic change.
    /// Default: 0.5.
    /// </summary>
    public float TopicSimilarityThreshold { get; set; } = 0.5f;
}
