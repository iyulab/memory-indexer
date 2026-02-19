using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Services;

/// <summary>
/// Implementation of Simple API (Level 0-1) for memory operations.
/// Delegates to IMemoryPrimitives and uses IMemoryClassifier for auto-classification.
/// </summary>
/// <remarks>
/// This is a facade over the complex VCM architecture, providing:
/// - **Level 0 (Zero-Config)**: RememberAsync(userId, content)
/// - **Level 1 (Session-Aware)**: RememberAsync(userId, sessionId, content)
///
/// For advanced use cases, use:
/// - Level 2-3: IVirtualContextManager or IMemoryPrimitives directly
/// </remarks>
public sealed partial class SimpleMemoryService : IMemoryService
{
    private readonly IMemoryPrimitives _primitives;
    private readonly IMemoryClassifier _classifier;
    private readonly IScopeManager _scopeManager;
    private readonly ILogger<SimpleMemoryService> _logger;

    // Implicit session tracking for zero-config (Level 0) calls
    private readonly Dictionary<string, string> _implicitSessions = new();

    public SimpleMemoryService(
        IMemoryPrimitives primitives,
        IMemoryClassifier classifier,
        IScopeManager scopeManager,
        ILogger<SimpleMemoryService> logger)
    {
        _primitives = primitives;
        _classifier = classifier;
        _scopeManager = scopeManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RememberAsync(
        string userId,
        string content,
        string? role = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // Level 0: Zero-Config - create implicit session
        var sessionId = GetOrCreateImplicitSession(userId);

        await RememberAsync(userId, sessionId, content, role, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RememberAsync(
        string userId,
        string sessionId,
        string content,
        string? role = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var effectiveRole = role ?? "user";

        var contentPreview = content.Substring(0, Math.Min(50, content.Length));
        LogRememberAsync(_logger, userId, sessionId, effectiveRole, contentPreview);

        // Auto-classify using IMemoryClassifier
        var classification = await _classifier.ClassifyAsync(
            content,
            new ClassificationContext
            {
                UserId = userId,
                SessionId = sessionId,
                MessageRole = effectiveRole
            },
            cancellationToken);

        LogClassified(_logger, classification.Type, classification.Tier, classification.Importance, classification.ShouldPersist);

        // Skip transient content (greetings, acknowledgments)
        if (!classification.ShouldPersist)
        {
            LogSkippingTransient(_logger);
            return;
        }

        // Initialize ScopeManager if needed
        if (!_scopeManager.CurrentState.IsInitialized)
        {
            await _scopeManager.InitializeAsync(userId, sessionId, cancellationToken);
        }

        // Record turn for topic tracking
        await _scopeManager.RecordTurnAsync(content, role: effectiveRole, cancellationToken);

        // Resolve scope using importance and type
        var scope = await _scopeManager.ResolveScopeAsync(
            content,
            classification.Type,
            classification.Importance,
            cancellationToken);

        LogResolvedScope(_logger, scope);

        // Encode memory using MemoryPrimitives
        var encodeRequest = new EncodeRequest
        {
            UserId = userId,
            SessionId = sessionId,
            Role = effectiveRole,  // Preserve role for episodic memories
            Content = content,
            Type = classification.Type,
            Scope = scope,
            Tier = classification.Tier,
            ImportanceScore = classification.Importance,
            Topics = classification.Topics.ToList()
        };

        var memory = await _primitives.EncodeAsync(encodeRequest, cancellationToken);

        LogRemembered(_logger, memory.Id, memory.Type, memory.Scope, memory.Tier);
    }

    /// <inheritdoc />
    public async Task<MemoryContext> RecallAsync(
        string userId,
        string? sessionId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0");
        }

        LogRecallAsync(_logger, userId, sessionId, query, limit);

        // Retrieve memories using MemoryPrimitives
        var retrieveRequest = new RetrieveRequest
        {
            UserId = userId,
            SessionId = sessionId,
            Query = query,
            Limit = limit,
            MinScore = 0.3f
        };

        var results = await _primitives.RetrieveAsync(retrieveRequest, cancellationToken);

        LogRetrievedMemories(_logger, results.Count);

        // Group memories by scope
        var memories = results.Select(r => r.Memory).ToList();

        var userMemories = memories.Where(m => m.Scope == Scope.User).ToList();
        var sessionMemories = memories.Where(m => m.Scope == Scope.Session).ToList();
        var topicMemories = memories.Where(m => m.Scope == Scope.Topic).ToList();

        var context = new MemoryContext
        {
            UserMemories = userMemories,
            SessionMemories = sessionMemories,
            TopicMemories = topicMemories
        };

        LogRecalled(_logger, userMemories.Count, sessionMemories.Count, topicMemories.Count, context.TotalCount);

        return context;
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        LogEndSessionAsync(_logger, userId, sessionId);

        // End session in ScopeManager
        await _scopeManager.EndSessionAsync(cancellationToken);

        // Remove implicit session if exists
        _implicitSessions.Remove(userId);

        LogSessionEndedSuccessfully(_logger);
    }

    /// <inheritdoc />
    public async Task ForgetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        LogForgetUserAsync(_logger, userId);

        // Retrieve all user memories
        var retrieveRequest = new RetrieveRequest
        {
            UserId = userId,
            Query = "*", // Wildcard to retrieve all
            Limit = 10000, // High limit to get all memories
            MinScore = 0.0f
        };

        var results = await _primitives.RetrieveAsync(retrieveRequest, cancellationToken);

        LogFoundMemoriesForUser(_logger, results.Count, userId);

        // Delete all memories
        foreach (var result in results)
        {
            var deleteRequest = new DeleteRequest
            {
                MemoryId = result.Memory.Id,
                HardDelete = true // GDPR requires permanent deletion
            };

            await _primitives.DeleteAsync(deleteRequest, cancellationToken);
        }

        // Remove implicit session
        _implicitSessions.Remove(userId);

        LogDeletedMemoriesForUser(_logger, results.Count, userId);
    }

    /// <inheritdoc />
    public async Task ForgetSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        LogForgetSessionAsync(_logger, userId, sessionId);

        // Retrieve session-scoped memories
        var retrieveRequest = new RetrieveRequest
        {
            UserId = userId,
            SessionId = sessionId,
            Query = "*", // Wildcard to retrieve all
            Limit = 10000,
            MinScore = 0.0f
        };

        var results = await _primitives.RetrieveAsync(retrieveRequest, cancellationToken);

        LogFoundSessionMemories(_logger, results.Count);

        // Delete session-scoped memories only
        var sessionMemories = results.Where(r => r.Memory.Scope == Scope.Session || r.Memory.Scope == Scope.Topic).ToList();

        foreach (var result in sessionMemories)
        {
            var deleteRequest = new DeleteRequest
            {
                MemoryId = result.Memory.Id,
                HardDelete = false // Soft delete for session cleanup
            };

            await _primitives.DeleteAsync(deleteRequest, cancellationToken);
        }

        LogDeletedSessionMemories(_logger, sessionMemories.Count);
    }

    #region Helper Methods

    /// <summary>
    /// Gets or creates an implicit session ID for zero-config (Level 0) calls.
    /// Uses userId as key to maintain session continuity within the service lifetime.
    /// </summary>
    private string GetOrCreateImplicitSession(string userId)
    {
        if (!_implicitSessions.TryGetValue(userId, out var sessionId))
        {
            sessionId = $"implicit-{userId}-{Guid.NewGuid():N}";
            _implicitSessions[userId] = sessionId;

            LogCreatedImplicitSession(_logger, sessionId, userId);
        }

        return sessionId;
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "RememberAsync: UserId={UserId}, SessionId={SessionId}, Role={Role}, Content={Content}")]
    private static partial void LogRememberAsync(ILogger logger, string userId, string sessionId, string role, string content);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Classified: Type={Type}, Tier={Tier}, Importance={Importance}, ShouldPersist={ShouldPersist}")]
    private static partial void LogClassified(ILogger logger, MemoryType type, Tier tier, float importance, bool shouldPersist);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Skipping transient content")]
    private static partial void LogSkippingTransient(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Resolved Scope={Scope}")]
    private static partial void LogResolvedScope(ILogger logger, Scope scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remembered: MemoryId={MemoryId}, Type={Type}, Scope={Scope}, Tier={Tier}")]
    private static partial void LogRemembered(ILogger logger, Guid memoryId, MemoryType type, Scope scope, Tier tier);

    [LoggerMessage(Level = LogLevel.Debug, Message = "RecallAsync: UserId={UserId}, SessionId={SessionId}, Query={Query}, Limit={Limit}")]
    private static partial void LogRecallAsync(ILogger logger, string userId, string? sessionId, string query, int limit);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Retrieved {Count} memories")]
    private static partial void LogRetrievedMemories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recalled: User={User}, Session={Session}, Topic={Topic}, Total={Total}")]
    private static partial void LogRecalled(ILogger logger, int user, int session, int topic, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "EndSessionAsync: UserId={UserId}, SessionId={SessionId}")]
    private static partial void LogEndSessionAsync(ILogger logger, string userId, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session ended successfully")]
    private static partial void LogSessionEndedSuccessfully(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ForgetUserAsync: UserId={UserId} (GDPR deletion)")]
    private static partial void LogForgetUserAsync(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} memories for user {UserId}")]
    private static partial void LogFoundMemoriesForUser(ILogger logger, int count, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleted {Count} memories for user {UserId}")]
    private static partial void LogDeletedMemoriesForUser(ILogger logger, int count, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ForgetSessionAsync: UserId={UserId}, SessionId={SessionId}")]
    private static partial void LogForgetSessionAsync(ILogger logger, string userId, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} session memories")]
    private static partial void LogFoundSessionMemories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} session memories")]
    private static partial void LogDeletedSessionMemories(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created implicit session: {SessionId} for user {UserId}")]
    private static partial void LogCreatedImplicitSession(ILogger logger, string sessionId, string userId);
}
