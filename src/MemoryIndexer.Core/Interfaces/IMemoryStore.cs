using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// Storage interface for memory units.
/// Provides CRUD operations and semantic search capabilities.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Stores a new memory unit.
    /// </summary>
    /// <param name="memory">The memory to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memory with generated ID.</returns>
    Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores multiple memory units in batch.
    /// </summary>
    /// <param name="memories">The memories to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(
        IEnumerable<MemoryUnit> memories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a memory by its ID.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory if found, null otherwise.</returns>
    Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple memories by their IDs.
    /// </summary>
    /// <param name="ids">The memory IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The found memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing memory.
    /// </summary>
    /// <param name="memory">The memory to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if not found.</returns>
    Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a memory by its ID.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="hardDelete">If true, permanently removes; if false, soft delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches memories using semantic similarity.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="options">Search options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with similarity scores.</returns>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding,
        MemorySearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all memories for a user with optional filtering.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="options">Filter options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of memories for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory count.</returns>
    Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the storage collection/table exists.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the storage collection/table.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteCollectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for memory search operations.
/// </summary>
public sealed class MemorySearchOptions
{
    /// <summary>
    /// The user ID to filter by.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The session ID to filter by.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Limit { get; set; } = 5;

    /// <summary>
    /// Minimum similarity score threshold (0.0 to 1.0).
    /// </summary>
    public float MinScore { get; set; }

    /// <summary>
    /// Memory types to include (null = all types).
    /// </summary>
    public MemoryType[]? Types { get; set; }

    /// <summary>
    /// Filter by creation time (start).
    /// </summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>
    /// Filter by creation time (end).
    /// </summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Include soft-deleted memories.
    /// </summary>
    public bool IncludeDeleted { get; set; }
}

/// <summary>
/// Options for filtering memories.
/// </summary>
public sealed class MemoryFilterOptions
{
    /// <summary>
    /// The session ID to filter by.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Memory types to include.
    /// </summary>
    public MemoryType[]? Types { get; set; }

    /// <summary>
    /// Filter by creation time (start).
    /// </summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>
    /// Filter by creation time (end).
    /// </summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Number of results to skip.
    /// </summary>
    public int Skip { get; set; }

    /// <summary>
    /// Include soft-deleted memories.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Order by field.
    /// </summary>
    public MemoryOrderBy OrderBy { get; set; } = MemoryOrderBy.CreatedAtDesc;
}

/// <summary>
/// Sort order options for memory queries.
/// </summary>
public enum MemoryOrderBy
{
    CreatedAtDesc,
    CreatedAtAsc,
    UpdatedAtDesc,
    UpdatedAtAsc,
    ImportanceDesc,
    AccessCountDesc
}

/// <summary>
/// Result of a memory search operation.
/// </summary>
public sealed class MemorySearchResult
{
    /// <summary>
    /// The matched memory.
    /// </summary>
    public required MemoryUnit Memory { get; init; }

    /// <summary>
    /// Similarity score (0.0 to 1.0, higher is more similar).
    /// </summary>
    public required float Score { get; init; }
}
