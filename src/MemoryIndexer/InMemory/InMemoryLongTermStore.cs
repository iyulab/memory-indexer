using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.InMemory;

/// <summary>
/// In-memory implementation of ILongTermStore.
/// Stores episodic memories (conversation sessions) in memory.
/// Implements Tulving's Episodic Memory System.
/// </summary>
public sealed partial class InMemoryLongTermStore(ILogger<InMemoryLongTermStore> logger) : ILongTermStore
{
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    /// <inheritdoc />
    public Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        session.Id = session.Id == Guid.Empty ? Guid.NewGuid() : session.Id;
        session.CreatedAt = DateTime.UtcNow;

        _sessions[session.Id] = session;
        LogCreatedSession(logger, session.Id, session.UserId);

        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        if (!_sessions.ContainsKey(session.Id))
            return Task.FromResult(false);

        _sessions[session.Id] = session;
        LogUpdatedSession(logger, session.Id);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = _sessions.TryRemove(id, out _);
        if (removed)
            LogDeletedSession(logger, id);

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Session>> GetByUserAsync(
        string userId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _sessions.Values.Where(s => s.UserId == userId);

        if (activeOnly)
            query = query.Where(s => s.IsActive);

        var results = query.OrderByDescending(s => s.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<Session>>(results);
    }

    /// <inheritdoc />
    public async Task<Session> GetOrCreateActiveSessionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await GetByUserAsync(userId, activeOnly: true, cancellationToken);
        var activeSession = sessions.Count > 0 ? sessions[0] : null;

        if (activeSession is not null)
            return activeSession;

        var newSession = new Session
        {
            UserId = userId,
            Name = $"Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
        };

        return await CreateAsync(newSession, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created session {SessionId} for user {UserId}")]
    private static partial void LogCreatedSession(ILogger logger, Guid sessionId, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated session {SessionId}")]
    private static partial void LogUpdatedSession(ILogger logger, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted session {SessionId}")]
    private static partial void LogDeletedSession(ILogger logger, Guid sessionId);
}
