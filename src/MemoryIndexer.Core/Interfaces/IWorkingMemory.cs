using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// L1: Working Memory (In-Context) interface.
/// Fast, limited-capacity memory following Baddeley's Working Memory Model.
/// </summary>
/// <remarks>
/// Research reference: research-03.md, research-04.md
/// - Capacity: 4-7 chunks (configurable)
/// - Latency: ~microseconds
/// - Storage: IMemoryCache
/// - Scope: Current task context
/// </remarks>
public interface IWorkingMemory
{
    /// <summary>
    /// Gets the current number of items in working memory.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the maximum capacity of working memory.
    /// Default: 7 (based on Baddeley's Working Memory Model).
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets whether working memory is at capacity.
    /// </summary>
    bool IsFull => Count >= Capacity;

    /// <summary>
    /// Promotes a memory from lower tier into working memory.
    /// If at capacity, evicts the least relevant memory back to session tier.
    /// </summary>
    /// <param name="memory">The memory to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evicted memory if capacity was reached, null otherwise.</returns>
    Task<MemoryUnit?> PromoteAsync(MemoryUnit memory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Demotes a memory from working memory to session tier.
    /// </summary>
    /// <param name="memoryId">The memory ID to demote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The demoted memory if found, null otherwise.</returns>
    Task<MemoryUnit?> DemoteAsync(Guid memoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a memory from working memory by ID.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory if in working memory, null otherwise.</returns>
    Task<MemoryUnit?> GetAsync(Guid memoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all memories currently in working memory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All working memory contents ordered by relevance.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a memory is currently in working memory.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <returns>True if in working memory.</returns>
    bool Contains(Guid memoryId);

    /// <summary>
    /// Clears all items from working memory.
    /// Typically called at session end.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cleared memories for potential demotion.</returns>
    Task<IReadOnlyList<MemoryUnit>> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the relevance score of a memory in working memory.
    /// Used for access-based prioritization.
    /// </summary>
    /// <param name="memoryId">The memory ID.</param>
    /// <param name="relevanceBoost">Relevance adjustment (positive or negative).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TouchAsync(Guid memoryId, float relevanceBoost = 0.1f, CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies the memory that should be evicted based on current policy.
    /// Does not actually evict; use DemoteAsync for that.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The eviction candidate, or null if working memory is empty.</returns>
    Task<MemoryUnit?> GetEvictionCandidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Context information for working memory state.
/// </summary>
public sealed class WorkingMemoryContext
{
    /// <summary>
    /// Current user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Current task or conversation context identifier.
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// Estimated token count of current working memory contents.
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Context saturation level based on token usage.
    /// </summary>
    public ContextSaturationLevel SaturationLevel { get; set; }
}
