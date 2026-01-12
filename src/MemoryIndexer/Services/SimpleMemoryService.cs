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
public sealed class SimpleMemoryService : IMemoryService
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

        _logger.LogDebug("RememberAsync: UserId={UserId}, SessionId={SessionId}, Role={Role}, Content={Content}",
            userId, sessionId, effectiveRole, content.Substring(0, Math.Min(50, content.Length)));

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

        _logger.LogTrace("Classified: Type={Type}, Tier={Tier}, Importance={Importance}, ShouldPersist={ShouldPersist}",
            classification.Type, classification.Tier, classification.Importance, classification.ShouldPersist);

        // Skip transient content (greetings, acknowledgments)
        if (!classification.ShouldPersist)
        {
            _logger.LogTrace("Skipping transient content");
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

        _logger.LogTrace("Resolved Scope={Scope}", scope);

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

        _logger.LogInformation("Remembered: MemoryId={MemoryId}, Type={Type}, Scope={Scope}, Tier={Tier}",
            memory.Id, memory.Type, memory.Scope, memory.Tier);
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

        _logger.LogDebug("RecallAsync: UserId={UserId}, SessionId={SessionId}, Query={Query}, Limit={Limit}",
            userId, sessionId, query, limit);

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

        _logger.LogTrace("Retrieved {Count} memories", results.Count);

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

        _logger.LogInformation("Recalled: User={User}, Session={Session}, Topic={Topic}, Total={Total}",
            userMemories.Count, sessionMemories.Count, topicMemories.Count, context.TotalCount);

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

        _logger.LogInformation("EndSessionAsync: UserId={UserId}, SessionId={SessionId}", userId, sessionId);

        // End session in ScopeManager
        await _scopeManager.EndSessionAsync(cancellationToken);

        // Remove implicit session if exists
        _implicitSessions.Remove(userId);

        _logger.LogDebug("Session ended successfully");
    }

    /// <inheritdoc />
    public async Task ForgetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        _logger.LogWarning("ForgetUserAsync: UserId={UserId} (GDPR deletion)", userId);

        // Retrieve all user memories
        var retrieveRequest = new RetrieveRequest
        {
            UserId = userId,
            Query = "*", // Wildcard to retrieve all
            Limit = 10000, // High limit to get all memories
            MinScore = 0.0f
        };

        var results = await _primitives.RetrieveAsync(retrieveRequest, cancellationToken);

        _logger.LogInformation("Found {Count} memories for user {UserId}", results.Count, userId);

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

        _logger.LogWarning("Deleted {Count} memories for user {UserId}", results.Count, userId);
    }

    /// <inheritdoc />
    public async Task ForgetSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _logger.LogInformation("ForgetSessionAsync: UserId={UserId}, SessionId={SessionId}", userId, sessionId);

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

        _logger.LogDebug("Found {Count} session memories", results.Count);

        // Delete session-scoped memories only
        var sessionMemories = results.Where(r => r.Memory.Scope == Scope.Session || r.Memory.Scope == Scope.Topic);

        foreach (var result in sessionMemories)
        {
            var deleteRequest = new DeleteRequest
            {
                MemoryId = result.Memory.Id,
                HardDelete = false // Soft delete for session cleanup
            };

            await _primitives.DeleteAsync(deleteRequest, cancellationToken);
        }

        _logger.LogInformation("Deleted {Count} session memories", sessionMemories.Count());
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

            _logger.LogDebug("Created implicit session: {SessionId} for user {UserId}", sessionId, userId);
        }

        return sessionId;
    }

    #endregion
}
