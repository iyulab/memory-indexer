using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Detects communities/clusters in the memory graph for topic-based organization.
/// </summary>
/// <remarks>
/// Research basis: Graph-based topic clustering for memory organization.
/// Enables efficient retrieval of topically related memories.
/// </remarks>
public interface ICommunityDetector
{
    /// <summary>
    /// Detects communities in the memory graph using Label Propagation algorithm.
    /// </summary>
    /// <param name="userId">User ID for multi-tenant isolation.</param>
    /// <param name="options">Detection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Community detection result with cluster assignments.</returns>
    Task<CommunityDetectionResult> DetectCommunitiesAsync(
        string userId,
        CommunityDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all memories in a specific community.
    /// </summary>
    /// <param name="communityId">The community ID.</param>
    /// <param name="userId">User ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memories belonging to the community.</returns>
    Task<IReadOnlyList<MemoryUnit>> GetCommunityMemoriesAsync(
        int communityId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a new memory to the most appropriate community.
    /// </summary>
    /// <param name="memoryId">The memory to assign.</param>
    /// <param name="connectedEntities">Entities the memory is connected to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assigned community ID.</returns>
    Task<int> AssignToCommunityAsync(
        Guid memoryId,
        IReadOnlyList<string> connectedEntities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary description of a community based on its members.
    /// </summary>
    /// <param name="communityId">The community ID.</param>
    /// <param name="userId">User ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Community summary with topic label and key entities.</returns>
    Task<CommunitySummary> GetCommunitySummaryAsync(
        int communityId,
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for community detection.
/// </summary>
public sealed class CommunityDetectionOptions
{
    /// <summary>
    /// Maximum iterations for label propagation.
    /// </summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>
    /// Minimum community size to keep.
    /// </summary>
    public int MinCommunitySize { get; init; } = 2;

    /// <summary>
    /// Convergence threshold (fraction of nodes that changed labels).
    /// </summary>
    public float ConvergenceThreshold { get; init; } = 0.01f;

    /// <summary>
    /// Whether to use weighted edges (by confidence).
    /// </summary>
    public bool UseWeightedEdges { get; init; } = true;

    /// <summary>
    /// Random seed for reproducibility.
    /// </summary>
    public int? RandomSeed { get; init; }
}

/// <summary>
/// Result of community detection.
/// </summary>
public sealed class CommunityDetectionResult
{
    /// <summary>
    /// Total number of communities detected.
    /// </summary>
    public int CommunityCount { get; init; }

    /// <summary>
    /// Memory to community assignment.
    /// </summary>
    public IReadOnlyDictionary<Guid, int> MemoryAssignments { get; init; }
        = new Dictionary<Guid, int>();

    /// <summary>
    /// Entity to community assignment.
    /// </summary>
    public IReadOnlyDictionary<string, int> EntityAssignments { get; init; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Community sizes.
    /// </summary>
    public IReadOnlyDictionary<int, int> CommunitySizes { get; init; }
        = new Dictionary<int, int>();

    /// <summary>
    /// Modularity score (quality of clustering).
    /// </summary>
    public float Modularity { get; init; }

    /// <summary>
    /// Number of iterations until convergence.
    /// </summary>
    public int IterationsToConverge { get; init; }

    /// <summary>
    /// Detection duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Summary of a community/cluster.
/// </summary>
public sealed class CommunitySummary
{
    /// <summary>
    /// The community ID.
    /// </summary>
    public int CommunityId { get; init; }

    /// <summary>
    /// Generated topic label for the community.
    /// </summary>
    public string TopicLabel { get; init; } = string.Empty;

    /// <summary>
    /// Key entities that define this community.
    /// </summary>
    public IReadOnlyList<string> KeyEntities { get; init; } = [];

    /// <summary>
    /// Number of memories in this community.
    /// </summary>
    public int MemoryCount { get; init; }

    /// <summary>
    /// Number of entities in this community.
    /// </summary>
    public int EntityCount { get; init; }

    /// <summary>
    /// Most common predicates in this community.
    /// </summary>
    public IReadOnlyList<string> CommonPredicates { get; init; } = [];

    /// <summary>
    /// Time range of memories in this community.
    /// </summary>
    public (DateTime? Earliest, DateTime? Latest) TimeRange { get; init; }
}
