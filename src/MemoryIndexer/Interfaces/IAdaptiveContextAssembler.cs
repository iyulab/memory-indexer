using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Assembles context from retrieval results with adaptive fidelity levels.
/// </summary>
/// <remarks>
/// Research basis: AFM (Adaptive Focus Memory) with FULL/COMPRESSED/PLACEHOLDER levels.
/// Optimizes context window usage while preserving essential information.
/// </remarks>
public interface IAdaptiveContextAssembler
{
    /// <summary>
    /// Assembles context from tiered retrieval results.
    /// </summary>
    /// <param name="retrievalResult">The retrieval result to assemble.</param>
    /// <param name="options">Assembly options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assembled context ready for LLM consumption.</returns>
    Task<AssembledContext> AssembleAsync(
        TieredRetrievalResult retrievalResult,
        ContextAssemblyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses a memory's content based on fidelity level.
    /// </summary>
    /// <param name="memory">The memory to compress.</param>
    /// <param name="fidelity">Target fidelity level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compressed content string.</returns>
    Task<string> CompressAsync(
        MemoryUnit memory,
        ContextFidelity fidelity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates token count for given content.
    /// </summary>
    /// <param name="content">Content to estimate.</param>
    /// <returns>Estimated token count.</returns>
    int EstimateTokens(string content);
}

/// <summary>
/// Options for context assembly.
/// </summary>
public sealed class ContextAssemblyOptions
{
    /// <summary>
    /// Maximum tokens for assembled context.
    /// </summary>
    public int MaxTokens { get; init; } = 4000;

    /// <summary>
    /// Format style for the assembled context.
    /// </summary>
    public ContextFormat Format { get; init; } = ContextFormat.Markdown;

    /// <summary>
    /// Whether to include tier headers in output.
    /// </summary>
    public bool IncludeTierHeaders { get; init; } = true;

    /// <summary>
    /// Whether to include graph context.
    /// </summary>
    public bool IncludeGraphContext { get; init; } = true;

    /// <summary>
    /// Whether to include metadata (timestamps, types).
    /// </summary>
    public bool IncludeMetadata { get; init; } = false;

    /// <summary>
    /// Percentage of budget for full-fidelity content (0-1).
    /// </summary>
    public float FullFidelityRatio { get; init; } = 0.6f;

    /// <summary>
    /// Percentage of budget for compressed content (0-1).
    /// </summary>
    public float CompressedRatio { get; init; } = 0.3f;

    /// <summary>
    /// Custom header text.
    /// </summary>
    public string? CustomHeader { get; init; }

    /// <summary>
    /// Custom footer text.
    /// </summary>
    public string? CustomFooter { get; init; }
}

/// <summary>
/// Format for assembled context.
/// </summary>
public enum ContextFormat
{
    /// <summary>
    /// Markdown formatting with headers.
    /// </summary>
    Markdown,

    /// <summary>
    /// Plain text with minimal formatting.
    /// </summary>
    PlainText,

    /// <summary>
    /// Structured XML format.
    /// </summary>
    Xml,

    /// <summary>
    /// JSON structured format.
    /// </summary>
    Json
}

/// <summary>
/// Result of context assembly.
/// </summary>
public sealed class AssembledContext
{
    /// <summary>
    /// The assembled context string.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Total tokens used.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Breakdown by fidelity level.
    /// </summary>
    public IReadOnlyDictionary<ContextFidelity, int> FidelityBreakdown { get; init; }
        = new Dictionary<ContextFidelity, int>();

    /// <summary>
    /// Breakdown by tier.
    /// </summary>
    public IReadOnlyDictionary<MemoryTier, int> TierBreakdown { get; init; }
        = new Dictionary<MemoryTier, int>();

    /// <summary>
    /// Number of memories included.
    /// </summary>
    public int MemoryCount { get; init; }

    /// <summary>
    /// Number of memories excluded due to budget.
    /// </summary>
    public int ExcludedCount { get; init; }

    /// <summary>
    /// Whether context was truncated due to budget.
    /// </summary>
    public bool WasTruncated { get; init; }

    /// <summary>
    /// Assembly statistics.
    /// </summary>
    public ContextAssemblyStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Statistics from context assembly.
/// </summary>
public sealed class ContextAssemblyStatistics
{
    /// <summary>
    /// Time spent on assembly.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Time spent on compression.
    /// </summary>
    public TimeSpan CompressionDuration { get; init; }

    /// <summary>
    /// Number of compressions performed.
    /// </summary>
    public int CompressionCount { get; init; }

    /// <summary>
    /// Tokens saved by compression.
    /// </summary>
    public int TokensSaved { get; init; }

    /// <summary>
    /// Average compression ratio achieved.
    /// </summary>
    public float AverageCompressionRatio { get; init; }
}
