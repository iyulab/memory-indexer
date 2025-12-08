using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// Storage interface for conversation sessions.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Creates a new session.
    /// </summary>
    /// <param name="session">The session to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created session.</returns>
    Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    /// <param name="id">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session if found, null otherwise.</returns>
    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing session.
    /// </summary>
    /// <param name="session">The session to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if not found.</returns>
    Task<bool> UpdateAsync(Session session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session.
    /// </summary>
    /// <param name="id">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="activeOnly">If true, only return active sessions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's sessions.</returns>
    Task<IReadOnlyList<Session>> GetByUserAsync(
        string userId,
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current active session for a user, or creates a new one.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active session.</returns>
    Task<Session> GetOrCreateActiveSessionAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
