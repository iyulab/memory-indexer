namespace MemoryIndexer.Core.Interfaces;

using MemoryIndexer.Core.Models;

/// <summary>
/// Interface for temporal entity triple storage and querying.
/// Supports bitemporal modeling (valid time + transaction time) for knowledge graphs.
/// </summary>
public interface ITemporalEntityStore
{
    /// <summary>
    /// Stores a new entity triple with temporal information.
    /// </summary>
    /// <param name="triple">The entity triple to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored triple with assigned ID.</returns>
    Task<EntityTriple> StoreAsync(EntityTriple triple, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an entity triple by ID.
    /// </summary>
    /// <param name="id">The triple ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The triple if found, null otherwise.</returns>
    Task<EntityTriple?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries triples by subject, predicate, and/or object.
    /// </summary>
    /// <param name="query">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching triples.</returns>
    Task<IReadOnlyList<EntityTriple>> QueryAsync(
        TemporalEntityQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current value for a subject-predicate pair (latest valid version).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="subject">The subject entity.</param>
    /// <param name="predicate">The predicate/relationship.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current triple if found, null otherwise.</returns>
    Task<EntityTriple?> GetCurrentValueAsync(
        string userId,
        string subject,
        string predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the value for a subject-predicate pair as of a specific date (temporal query).
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="subject">The subject entity.</param>
    /// <param name="predicate">The predicate/relationship.</param>
    /// <param name="asOfDate">The date to query for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The triple that was valid at the specified date, null if not found.</returns>
    Task<EntityTriple?> GetValueAsOfAsync(
        string userId,
        string subject,
        string predicate,
        DateTime asOfDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the version history for a subject-predicate pair.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="subject">The subject entity.</param>
    /// <param name="predicate">The predicate/relationship.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All versions of the triple, ordered by version number descending.</returns>
    Task<IReadOnlyList<EntityTriple>> GetVersionHistoryAsync(
        string userId,
        string subject,
        string predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supersedes an existing triple with a new value.
    /// Automatically creates the version chain and updates validity.
    /// </summary>
    /// <param name="existingTripleId">The ID of the triple to supersede.</param>
    /// <param name="newObjectValue">The new object value.</param>
    /// <param name="validFrom">When the new fact became true (defaults to now).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new superseding triple.</returns>
    Task<EntityTriple> SupersedeAsync(
        Guid existingTripleId,
        string newObjectValue,
        DateTime? validFrom = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds triples that might contradict a given triple.
    /// </summary>
    /// <param name="triple">The triple to check for contradictions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Potentially contradicting triples.</returns>
    Task<IReadOnlyList<EntityTriple>> FindPotentialContradictionsAsync(
        EntityTriple triple,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all triples for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="includeInactive">Whether to include inactive triples.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All triples for the user.</returns>
    Task<IReadOnlyList<EntityTriple>> GetAllForUserAsync(
        string userId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all triples where the entity is the subject.
    /// </summary>
    /// <param name="subject">The subject entity name.</param>
    /// <param name="userId">Optional user ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Triples with matching subject.</returns>
    Task<IReadOnlyList<EntityTriple>> GetBySubjectAsync(
        string subject,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all triples where the entity is the object.
    /// </summary>
    /// <param name="objectValue">The object value to match.</param>
    /// <param name="userId">Optional user ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Triples with matching object.</returns>
    Task<IReadOnlyList<EntityTriple>> GetByObjectAsync(
        string objectValue,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active triples (not superseded, not deleted).
    /// </summary>
    /// <param name="userId">Optional user ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All active triples.</returns>
    Task<IReadOnlyList<EntityTriple>> GetAllActiveAsync(
        string? userId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query parameters for temporal entity triple queries.
/// </summary>
public sealed class TemporalEntityQuery
{
    /// <summary>
    /// Filter by user ID (required).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Filter by subject (optional, supports partial match).
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Filter by predicate (optional, supports partial match).
    /// </summary>
    public string? Predicate { get; init; }

    /// <summary>
    /// Filter by object value (optional, supports partial match).
    /// </summary>
    public string? ObjectValue { get; init; }

    /// <summary>
    /// Filter to triples valid at this date (temporal query).
    /// </summary>
    public DateTime? AsOfDate { get; init; }

    /// <summary>
    /// Filter to triples created after this date.
    /// </summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>
    /// Filter to triples created before this date.
    /// </summary>
    public DateTime? CreatedBefore { get; init; }

    /// <summary>
    /// Minimum confidence threshold.
    /// </summary>
    public float? MinConfidence { get; init; }

    /// <summary>
    /// Whether to include inactive triples.
    /// </summary>
    public bool IncludeInactive { get; init; } = false;

    /// <summary>
    /// Whether to only return current versions (exclude superseded).
    /// </summary>
    public bool CurrentVersionsOnly { get; init; } = true;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int? Limit { get; init; }
}
