namespace MemoryIndexer.Intelligence.Compression;

/// <summary>
/// Interface for prompt compression services.
/// </summary>
public interface IPromptCompressor
{
    /// <summary>
    /// Compresses a prompt while preserving important information.
    /// </summary>
    /// <param name="text">The text to compress.</param>
    /// <param name="options">Compression options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compressed text with metadata.</returns>
    Task<CompressionResult> CompressAsync(
        string text,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compresses multiple texts in batch.
    /// </summary>
    /// <param name="texts">The texts to compress.</param>
    /// <param name="options">Compression options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compressed results for each text.</returns>
    Task<IReadOnlyList<CompressionResult>> CompressBatchAsync(
        IEnumerable<string> texts,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates compression ratio without performing full compression.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>Estimated compression ratio (0.0 to 1.0).</returns>
    float EstimateCompressionRatio(string text);
}

/// <summary>
/// Options for text compression.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>
    /// Target compression ratio (0.0 to 1.0).
    /// 0.3 means compress to 30% of original size.
    /// </summary>
    public float TargetRatio { get; set; } = 0.5f;

    /// <summary>
    /// Minimum token importance threshold for retention (0.0 to 1.0).
    /// Tokens below this threshold may be removed.
    /// </summary>
    public float MinTokenImportance { get; set; } = 0.3f;

    /// <summary>
    /// Whether to preserve sentence structure.
    /// </summary>
    public bool PreserveSentenceStructure { get; set; } = true;

    /// <summary>
    /// Whether to preserve named entities (people, places, organizations).
    /// </summary>
    public bool PreserveNamedEntities { get; set; } = true;

    /// <summary>
    /// Whether to preserve numerical values and dates.
    /// </summary>
    public bool PreserveNumericals { get; set; } = true;

    /// <summary>
    /// Whether to preserve code snippets and technical content.
    /// </summary>
    public bool PreserveCodeContent { get; set; } = true;

    /// <summary>
    /// Keywords that must be preserved if present.
    /// </summary>
    public List<string>? RequiredKeywords { get; set; }

    /// <summary>
    /// Maximum output tokens (0 for no limit).
    /// </summary>
    public int MaxOutputTokens { get; set; } = 0;

    /// <summary>
    /// Compression strategy to use.
    /// </summary>
    public CompressionStrategy Strategy { get; set; } = CompressionStrategy.Hybrid;
}

/// <summary>
/// Compression strategy options.
/// </summary>
public enum CompressionStrategy
{
    /// <summary>
    /// Remove low-importance tokens based on perplexity.
    /// </summary>
    TokenPruning,

    /// <summary>
    /// Remove redundant sentences based on similarity.
    /// </summary>
    SentencePruning,

    /// <summary>
    /// Combine token and sentence-level pruning.
    /// </summary>
    Hybrid,

    /// <summary>
    /// Fast heuristic-based compression.
    /// </summary>
    Heuristic
}

/// <summary>
/// Result of text compression.
/// </summary>
public sealed class CompressionResult
{
    /// <summary>
    /// The compressed text.
    /// </summary>
    public required string CompressedText { get; init; }

    /// <summary>
    /// Original token count.
    /// </summary>
    public int OriginalTokenCount { get; init; }

    /// <summary>
    /// Compressed token count.
    /// </summary>
    public int CompressedTokenCount { get; init; }

    /// <summary>
    /// Achieved compression ratio.
    /// </summary>
    public float CompressionRatio => OriginalTokenCount > 0
        ? (float)CompressedTokenCount / OriginalTokenCount
        : 0f;

    /// <summary>
    /// Estimated information preservation score (0.0 to 1.0).
    /// </summary>
    public float InformationRetention { get; init; }

    /// <summary>
    /// Tokens that were removed.
    /// </summary>
    public List<string> RemovedTokens { get; init; } = [];

    /// <summary>
    /// Whether the target ratio was achieved.
    /// </summary>
    public bool TargetAchieved { get; init; }
}
