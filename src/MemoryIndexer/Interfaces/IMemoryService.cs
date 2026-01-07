using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Simple API (Level 0-1) for memory operations.
/// Progressive API design: zero-config → session-aware.
/// </summary>
/// <remarks>
/// This interface provides a simplified API for 99% of use cases:
/// - **Level 0 (Zero-Config)**: RememberAsync(userId, content)
///   - Auto-detects memory type using ITypeClassifier
///   - Stores in default scope (Session)
///   - Suitable for: chat apps, personal assistants, simple games
///
/// - **Level 1 (Session-Aware)**: RememberAsync(userId, sessionId, content)
///   - Explicit session management
///   - Enables session-scoped recall
///   - Suitable for: multi-session apps, conversation history
///
/// For advanced use cases (Type-Aware, Full Control), use:
/// - Level 2: IMemoryServiceAdvanced (StoreFact/Event/Rule)
/// - Level 3: IVirtualContextManager (full VCM control)
/// </remarks>
public interface IMemoryService
{
    /// <summary>
    /// Level 0: Zero-Config Remember.
    /// Stores content for a user without session context.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="content">Content to remember</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <remarks>
    /// Behavior:
    /// - Auto-detects Type using ITypeClassifier
    /// - Scope: Session (default for non-session-aware calls)
    /// - Tier: Short (suitable for general use)
    /// - Creates implicit session ID if not provided
    /// </remarks>
    Task RememberAsync(
        string userId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Level 1: Session-Aware Remember.
    /// Stores content for a user within a specific session.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="content">Content to remember</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <remarks>
    /// Behavior:
    /// - Auto-detects Type using ITypeClassifier
    /// - Scope: Session (explicit session context)
    /// - Tier: Short (suitable for general use)
    /// - Enables session-scoped recall
    /// </remarks>
    Task RememberAsync(
        string userId,
        string sessionId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalls relevant memories for a user query.
    /// Returns memories grouped by scope (User/Session/Topic).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="sessionId">Optional session identifier (null for user-wide recall)</param>
    /// <param name="query">Query text for semantic search</param>
    /// <param name="limit">Maximum number of memories to return (default: 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>MemoryContext with scope-grouped memories</returns>
    /// <remarks>
    /// Scope grouping:
    /// - UserMemories: Cross-session facts (Scope=User)
    /// - SessionMemories: Current session context (Scope=Session)
    /// - TopicMemories: Current topic (Scope=Topic, internal use)
    ///
    /// If sessionId is null:
    /// - Returns only UserMemories (cross-session)
    /// - SessionMemories and TopicMemories will be empty
    ///
    /// If sessionId is provided:
    /// - Returns UserMemories + SessionMemories + TopicMemories
    /// - Filtered by relevance to query
    /// </remarks>
    Task<MemoryContext> RecallAsync(
        string userId,
        string? sessionId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a session and triggers session-level memory consolidation.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <remarks>
    /// Behavior:
    /// - Promotes worthy session memories to User scope
    /// - Archives session context for future reference
    /// - Cleans up short-term session data
    /// - Should be called when user conversation ends
    /// </remarks>
    Task EndSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets all memories for a user (GDPR compliance).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <remarks>
    /// Behavior:
    /// - Deletes all memories for the user across all sessions
    /// - Irreversible operation
    /// - Use for GDPR "right to be forgotten" compliance
    /// </remarks>
    Task ForgetUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets all memories for a specific session.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <remarks>
    /// Behavior:
    /// - Deletes all session-scoped memories
    /// - Preserves User-scoped memories
    /// - Useful for session reset or testing
    /// </remarks>
    Task ForgetSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
