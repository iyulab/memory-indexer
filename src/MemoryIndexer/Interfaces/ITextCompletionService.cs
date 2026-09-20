namespace MemoryIndexer.Interfaces;

/// <summary>
/// MemoryIndexer's own text completion contract, used by fact/knowledge extraction,
/// conflict detection, and virtual-context consolidation. Consumers register an
/// implementation wrapping their LLM stack; adapting an existing completion service
/// is a one-class wrapper around <see cref="CompleteAsync"/>.
/// </summary>
/// <remarks>
/// Deliberately minimal (single member + the options this package actually sends):
/// MemoryIndexer is a Tier-0 leaf module and must not depend on other package groups
/// for shared contracts (see umbrella docs/LAYERING.md).
/// </remarks>
public interface ITextCompletionService
{
    /// <summary>
    /// Generates a completion for the given prompt.
    /// </summary>
    /// <param name="prompt">The prompt to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated text completion.</returns>
    Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for a text completion request: generic LLM sampling knobs only
/// (the knobs MemoryIndexer core and its in-repo adapters actually send).
/// </summary>
public sealed class TextCompletionOptions
{
    /// <summary>Sampling temperature (0.0 = deterministic). Default: 0.7.</summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>Maximum tokens to generate. Default: 500.</summary>
    public int MaxTokens { get; init; } = 500;

    /// <summary>Sequences that stop generation.</summary>
    public IReadOnlyList<string>? StopSequences { get; init; }
}
