using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.Summarization;

/// <summary>
/// Service for generating and managing memory summaries.
/// </summary>
public interface ISummarizationService
{
    /// <summary>
    /// Generates a summary from a collection of memories.
    /// </summary>
    /// <param name="memories">The memories to summarize.</param>
    /// <param name="options">Summarization options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated summary.</returns>
    Task<MemorySummary> SummarizeAsync(
        IEnumerable<MemoryUnit> memories,
        SummarizationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally updates an existing summary with new content.
    /// Uses CoK-style JSON update operations (Modify, Add, Keep).
    /// </summary>
    /// <param name="existing">The existing summary to update.</param>
    /// <param name="newMemories">New memories to incorporate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated summary.</returns>
    Task<MemorySummary> IncrementalUpdateAsync(
        MemorySummary existing,
        IEnumerable<MemoryUnit> newMemories,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a hierarchical summary structure from memories.
    /// </summary>
    /// <param name="memories">The memories to summarize hierarchically.</param>
    /// <param name="levels">Number of hierarchy levels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hierarchical summary.</returns>
    Task<HierarchicalSummary> CreateHierarchyAsync(
        IEnumerable<MemoryUnit> memories,
        int levels = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if summarization should be triggered based on current state.
    /// </summary>
    /// <param name="currentTokenCount">Current token count in context.</param>
    /// <param name="maxTokens">Maximum allowed tokens.</param>
    /// <param name="memoryCount">Number of memories.</param>
    /// <returns>True if summarization should be triggered.</returns>
    bool ShouldTriggerSummarization(int currentTokenCount, int maxTokens, int memoryCount);
}

/// <summary>
/// Options for summarization.
/// </summary>
public sealed class SummarizationOptions
{
    /// <summary>
    /// Target compression ratio (0.0 to 1.0).
    /// 0.2 means compress to 20% of original size.
    /// </summary>
    public float TargetCompressionRatio { get; set; } = 0.3f;

    /// <summary>
    /// Maximum output tokens for the summary.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 500;

    /// <summary>
    /// Whether to preserve key entities in the summary.
    /// </summary>
    public bool PreserveEntities { get; set; } = true;

    /// <summary>
    /// Whether to preserve timestamps/dates.
    /// </summary>
    public bool PreserveTimestamps { get; set; } = true;

    /// <summary>
    /// Focus topics for the summary (if any).
    /// </summary>
    public List<string>? FocusTopics { get; set; }

    /// <summary>
    /// Summary style: extractive (select sentences) or abstractive (generate new text).
    /// </summary>
    public SummaryStyle Style { get; set; } = SummaryStyle.Hybrid;
}

/// <summary>
/// Summary generation style.
/// </summary>
public enum SummaryStyle
{
    /// <summary>
    /// Select and combine existing sentences.
    /// </summary>
    Extractive,

    /// <summary>
    /// Generate new summarizing text.
    /// </summary>
    Abstractive,

    /// <summary>
    /// Combine both approaches.
    /// </summary>
    Hybrid
}

/// <summary>
/// Represents a memory summary.
/// </summary>
public sealed class MemorySummary
{
    /// <summary>
    /// Unique identifier for this summary.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The summary content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Key points extracted from memories.
    /// </summary>
    public List<string> KeyPoints { get; set; } = [];

    /// <summary>
    /// Entities mentioned in the summarized content.
    /// </summary>
    public List<string> Entities { get; set; } = [];

    /// <summary>
    /// Topics covered by this summary.
    /// </summary>
    public List<string> Topics { get; set; } = [];

    /// <summary>
    /// IDs of memories included in this summary.
    /// </summary>
    public List<Guid> SourceMemoryIds { get; set; } = [];

    /// <summary>
    /// Original token count before summarization.
    /// </summary>
    public int OriginalTokenCount { get; set; }

    /// <summary>
    /// Token count after summarization.
    /// </summary>
    public int SummarizedTokenCount { get; set; }

    /// <summary>
    /// Compression ratio achieved.
    /// </summary>
    public float CompressionRatio => OriginalTokenCount > 0
        ? (float)SummarizedTokenCount / OriginalTokenCount
        : 0;

    /// <summary>
    /// When this summary was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this summary was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Summary embedding for retrieval.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

/// <summary>
/// Represents a hierarchical summary structure.
/// </summary>
public sealed class HierarchicalSummary
{
    /// <summary>
    /// Root level summary (most abstract).
    /// </summary>
    public required MemorySummary RootSummary { get; init; }

    /// <summary>
    /// Child summaries at each level.
    /// Level 0 is most detailed, increasing levels are more abstract.
    /// </summary>
    public List<List<MemorySummary>> Levels { get; init; } = [];

    /// <summary>
    /// Total memories covered by this hierarchy.
    /// </summary>
    public int TotalMemoryCount { get; init; }

    /// <summary>
    /// Overall compression ratio from all memories to root summary.
    /// </summary>
    public float OverallCompressionRatio { get; init; }
}
