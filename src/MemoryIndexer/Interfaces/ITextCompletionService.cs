namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for generating text completions using language models.
/// </summary>
public interface ITextCompletionService
{
    /// <summary>
    /// Generates a text completion for the given prompt.
    /// </summary>
    /// <param name="prompt">The prompt to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated completion text.</returns>
    Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates text completions for multiple prompts in batch.
    /// More efficient than calling CompleteAsync multiple times.
    /// </summary>
    /// <param name="prompts">The prompts to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated completions in the same order as inputs.</returns>
    Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for text completion generation.
/// </summary>
public sealed class TextCompletionOptions
{
    /// <summary>
    /// Temperature for sampling (0.0 = deterministic, 1.0 = creative).
    /// Default: 0.7
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// Maximum number of tokens to generate.
    /// Default: 500
    /// </summary>
    public int MaxTokens { get; init; } = 500;

    /// <summary>
    /// Stop sequences that will terminate generation.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Top-p nucleus sampling threshold.
    /// </summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0).
    /// </summary>
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0).
    /// </summary>
    public float? FrequencyPenalty { get; init; }
}
