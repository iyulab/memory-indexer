using MemoryIndexer.Models;

namespace MemoryIndexer.Sdk.Intelligence.Consolidation;

/// <summary>
/// Service for consolidating memories during "sleep" cycles.
/// Based on the SLEEP paradigm: consolidate fragile short-term memories
/// into stable long-term knowledge through reflection and compression.
/// </summary>
/// <remarks>
/// Research basis:
/// - "Language Models Need Sleep" (SLEEP paradigm)
/// - Stanford Generative Agents (reflection and memory stream)
/// - Atkinson-Shiffrin memory model (sensory → short-term → long-term)
/// </remarks>
public interface IMemoryConsolidator
{
    /// <summary>
    /// Runs a full consolidation cycle ("sleep") on the memory store.
    /// </summary>
    /// <param name="options">Consolidation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the consolidation process.</returns>
    Task<ConsolidationResult> ConsolidateAsync(
        ConsolidationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates reflections (higher-level inferences) from recent memories.
    /// Based on Stanford Generative Agents reflection mechanism.
    /// </summary>
    /// <param name="recentMemories">Recent memories to reflect upon.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated reflection memories.</returns>
    Task<IReadOnlyList<MemoryUnit>> GenerateReflectionsAsync(
        IReadOnlyList<MemoryUnit> recentMemories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies forgetting curve decay to memories based on access patterns.
    /// Uses Ebbinghaus forgetting curve: R = e^(-t/S) where S is memory strength.
    /// </summary>
    /// <param name="memories">Memories to apply decay to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated memories with decayed importance scores.</returns>
    Task<IReadOnlyList<MemoryDecayResult>> ApplyForgettingCurveAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges similar memories into consolidated units.
    /// </summary>
    /// <param name="candidates">Candidate memories for merging.</param>
    /// <param name="similarityThreshold">Minimum similarity for merging (default: 0.85).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Merge operations to perform.</returns>
    Task<IReadOnlyList<MemoryMergeOperation>> IdentifyMergeCandidatesAsync(
        IReadOnlyList<MemoryUnit> candidates,
        float similarityThreshold = 0.85f,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for memory consolidation.
/// </summary>
public sealed class ConsolidationOptions
{
    /// <summary>
    /// Maximum age of memories to consider for consolidation (default: 7 days).
    /// </summary>
    public TimeSpan MaxMemoryAge { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Minimum number of memories needed to trigger reflection (default: 5).
    /// </summary>
    public int MinMemoriesForReflection { get; init; } = 5;

    /// <summary>
    /// Similarity threshold for merging memories (default: 0.85).
    /// </summary>
    public float MergeSimilarityThreshold { get; init; } = 0.85f;

    /// <summary>
    /// Whether to apply forgetting curve decay (default: true).
    /// </summary>
    public bool ApplyForgettingCurve { get; init; } = true;

    /// <summary>
    /// Decay rate for forgetting curve - higher = faster forgetting (default: 0.1).
    /// </summary>
    public float ForgettingDecayRate { get; init; } = 0.1f;

    /// <summary>
    /// Minimum importance score below which memories may be archived (default: 0.2).
    /// </summary>
    public float ArchiveThreshold { get; init; } = 0.2f;

    /// <summary>
    /// Maximum number of reflections to generate per cycle (default: 10).
    /// </summary>
    public int MaxReflectionsPerCycle { get; init; } = 10;

    /// <summary>
    /// User/tenant ID to scope consolidation (null = all users).
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID to scope consolidation (null = all sessions).
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Result of a consolidation cycle.
/// </summary>
public sealed class ConsolidationResult
{
    /// <summary>
    /// Whether the consolidation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Total memories processed.
    /// </summary>
    public int MemoriesProcessed { get; init; }

    /// <summary>
    /// Number of reflections generated.
    /// </summary>
    public int ReflectionsGenerated { get; init; }

    /// <summary>
    /// Number of memories merged.
    /// </summary>
    public int MemoriesMerged { get; init; }

    /// <summary>
    /// Number of memories archived (below threshold).
    /// </summary>
    public int MemoriesArchived { get; init; }

    /// <summary>
    /// Number of memories with updated decay scores.
    /// </summary>
    public int MemoriesDecayed { get; init; }

    /// <summary>
    /// Duration of the consolidation process.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Generated reflections.
    /// </summary>
    public IReadOnlyList<MemoryUnit> Reflections { get; init; } = [];

    /// <summary>
    /// Error message if consolidation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of forgetting curve decay application.
/// </summary>
public sealed class MemoryDecayResult
{
    /// <summary>
    /// The memory that was processed.
    /// </summary>
    public required Guid MemoryId { get; init; }

    /// <summary>
    /// Previous importance score.
    /// </summary>
    public float PreviousScore { get; init; }

    /// <summary>
    /// New importance score after decay.
    /// </summary>
    public float NewScore { get; init; }

    /// <summary>
    /// Whether the memory should be archived (below threshold).
    /// </summary>
    public bool ShouldArchive { get; init; }

    /// <summary>
    /// Memory strength factor (based on access frequency and recency).
    /// </summary>
    public float StrengthFactor { get; init; }
}

/// <summary>
/// Represents a memory merge operation.
/// </summary>
public sealed class MemoryMergeOperation
{
    /// <summary>
    /// Primary memory (will be kept/updated).
    /// </summary>
    public required MemoryUnit PrimaryMemory { get; init; }

    /// <summary>
    /// Memories to merge into primary.
    /// </summary>
    public required IReadOnlyList<MemoryUnit> MemoriesToMerge { get; init; }

    /// <summary>
    /// Similarity scores between primary and each memory to merge.
    /// </summary>
    public required IReadOnlyList<float> SimilarityScores { get; init; }

    /// <summary>
    /// Suggested merged content.
    /// </summary>
    public string? SuggestedMergedContent { get; init; }
}
