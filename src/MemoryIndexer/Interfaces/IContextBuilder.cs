using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Builder for assembling context with token-budget awareness.
/// Provides flexible APIs for retrieving different types of context
/// within specified token limits.
/// </summary>
public interface IContextBuilder
{
    /// <summary>
    /// Get recent conversation turns up to a token limit.
    /// Retrieves from Buffer (T0) and Short-Term (T1) memory in chronological order.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID for session-scoped retrieval.</param>
    /// <param name="maxTokens">Maximum tokens to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of recent conversation items within the token budget.</returns>
    Task<IReadOnlyList<ContextItem>> GetRecentTurnsAsync(
        string userId,
        string sessionId,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>
    /// Get semantically relevant memories up to a token limit.
    /// Retrieves from Long-Term (T2) memory based on query similarity.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="query">The query for semantic matching.</param>
    /// <param name="maxTokens">Maximum tokens to include.</param>
    /// <param name="sessionId">Optional session ID to scope the search.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of semantically relevant items within the token budget.</returns>
    Task<IReadOnlyList<ContextItem>> GetSemanticContextAsync(
        string userId,
        string query,
        int maxTokens,
        string? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get episodic memories from a specific session up to a token limit.
    /// Retrieves session-scoped memories ordered by time.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="maxTokens">Maximum tokens to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of session episodic items within the token budget.</returns>
    Task<IReadOnlyList<ContextItem>> GetSessionContextAsync(
        string userId,
        string sessionId,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>
    /// Get user-specific facts and knowledge up to a token limit.
    /// Retrieves user-scoped semantic and fact memories.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="maxTokens">Maximum tokens to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of user fact items within the token budget.</returns>
    Task<IReadOnlyList<ContextItem>> GetUserFactsAsync(
        string userId,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>
    /// Build a complete context bundle using the specified strategy.
    /// </summary>
    /// <param name="request">The context request with budget configuration.</param>
    /// <param name="strategy">The strategy to use for budget allocation. Null = BalancedStrategy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled context bundle.</returns>
    Task<ContextBundle> BuildAsync(
        ContextRequest request,
        IContextStrategy? strategy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Build a complete context bundle using a named strategy.
    /// </summary>
    /// <param name="request">The context request with budget configuration.</param>
    /// <param name="strategyName">Name of the strategy ("Balanced", "RecentHeavy", "SemanticHeavy").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled context bundle.</returns>
    Task<ContextBundle> BuildAsync(
        ContextRequest request,
        string strategyName,
        CancellationToken ct = default);
}
