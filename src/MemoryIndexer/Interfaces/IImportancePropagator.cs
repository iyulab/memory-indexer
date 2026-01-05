namespace MemoryIndexer.Interfaces;

/// <summary>
/// Computes importance scores for entities using PageRank-style propagation.
/// </summary>
/// <remarks>
/// Research basis: PageRank for knowledge graph importance, adapted for memory graphs.
/// Entities referenced by many memories or connected to important entities score higher.
/// </remarks>
public interface IImportancePropagator
{
    /// <summary>
    /// Computes importance scores for all entities using PageRank algorithm.
    /// </summary>
    /// <param name="userId">User ID for multi-tenant isolation.</param>
    /// <param name="options">PageRank options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Importance scores for each entity.</returns>
    Task<ImportanceResult> ComputeImportanceAsync(
        string userId,
        ImportanceOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current importance score for an entity.
    /// </summary>
    /// <param name="entityName">The entity name.</param>
    /// <param name="userId">User ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The importance score (0-1) or null if not computed.</returns>
    Task<float?> GetEntityImportanceAsync(
        string entityName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top-K most important entities.
    /// </summary>
    /// <param name="userId">User ID for filtering.</param>
    /// <param name="topK">Number of entities to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of important entities.</returns>
    Task<IReadOnlyList<EntityImportance>> GetTopEntitiesAsync(
        string userId,
        int topK = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates importance scores incrementally when new edges are added.
    /// </summary>
    /// <param name="affectedEntities">Entities affected by the change.</param>
    /// <param name="userId">User ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateImportanceAsync(
        IReadOnlyList<string> affectedEntities,
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for importance computation.
/// </summary>
public sealed class ImportanceOptions
{
    /// <summary>
    /// Damping factor (probability of continuing walk). Default: 0.85.
    /// </summary>
    public float DampingFactor { get; init; } = 0.85f;

    /// <summary>
    /// Maximum iterations. Default: 100.
    /// </summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>
    /// Convergence threshold (L1 norm difference). Default: 1e-6.
    /// </summary>
    public float ConvergenceThreshold { get; init; } = 1e-6f;

    /// <summary>
    /// Whether to use edge weights (confidence). Default: true.
    /// </summary>
    public bool UseWeightedEdges { get; init; } = true;

    /// <summary>
    /// Apply memory connection boost (entities with more memory links rank higher).
    /// </summary>
    public bool ApplyMemoryBoost { get; init; } = true;

    /// <summary>
    /// Memory connection boost factor. Default: 0.1.
    /// </summary>
    public float MemoryBoostFactor { get; init; } = 0.1f;
}

/// <summary>
/// Result of importance computation.
/// </summary>
public sealed class ImportanceResult
{
    /// <summary>
    /// Entity importance scores (entity name -> score).
    /// </summary>
    public IReadOnlyDictionary<string, float> EntityScores { get; init; }
        = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of entities processed.
    /// </summary>
    public int EntityCount { get; init; }

    /// <summary>
    /// Number of edges processed.
    /// </summary>
    public int EdgeCount { get; init; }

    /// <summary>
    /// Iterations until convergence.
    /// </summary>
    public int Iterations { get; init; }

    /// <summary>
    /// Final L1 difference (convergence measure).
    /// </summary>
    public float FinalDifference { get; init; }

    /// <summary>
    /// Whether the algorithm converged.
    /// </summary>
    public bool Converged { get; init; }

    /// <summary>
    /// Computation duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Entity with importance score.
/// </summary>
public sealed class EntityImportance
{
    /// <summary>
    /// Entity name.
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Importance score (0-1, normalized).
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Rank (1-based).
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// Number of incoming edges.
    /// </summary>
    public int InDegree { get; init; }

    /// <summary>
    /// Number of outgoing edges.
    /// </summary>
    public int OutDegree { get; init; }

    /// <summary>
    /// Number of memories connected to this entity.
    /// </summary>
    public int MemoryConnectionCount { get; init; }
}
