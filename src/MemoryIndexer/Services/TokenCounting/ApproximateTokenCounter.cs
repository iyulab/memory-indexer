using MemoryIndexer.Interfaces;
using TokenEstimation;

namespace MemoryIndexer.Services.TokenCounting;

/// <summary>
/// Fast approximate token counter using character-based estimation: each kind of character (Latin letters, Hangul, Kana,
/// Han, digits, punctuation, whitespace runs) has its own characters-per-token ratio, fitted to o200k_base. English,
/// Korean, mixed prose, code and JSON each come within a few percent of the real count.
/// This is faster than BPE-based counting but less accurate.
/// </summary>
public class ApproximateTokenCounter : ITokenCounter
{
    /// <inheritdoc />
    public int Count(string text) => CharTokenEstimator.Count(text, CharTokenRatios.O200k);

    /// <inheritdoc />
    public int Count(IEnumerable<string> texts)
    {
        if (texts == null)
            return 0;

        return texts.Sum(Count);
    }

    /// <inheritdoc />
    public string Truncate(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (maxTokens <= 0)
            return string.Empty;

        // Walk the text at each character's own cost, so a Korean or Chinese text is cut at the same token budget as
        // an English one rather than several times later.
        var maxChars = 0;
        var cost = 0.0;
        var inWhitespace = false;
        while (maxChars < text.Length)
        {
            cost += CharTokenEstimator.Cost(CharTokenEstimator.KindOf(text[maxChars], ref inWhitespace), CharTokenRatios.O200k);
            if (cost > maxTokens + 1e-9)
                break;
            maxChars++;
        }

        if (text.Length <= maxChars)
            return text;

        // Try to truncate at a word boundary
        var truncated = text[..maxChars];
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > maxChars * 0.8) // Only use word boundary if not too far back
            return truncated[..lastSpace] + "...";

        return truncated + "...";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <c>true</c> for all models since this counter uses a model-agnostic
    /// character-based heuristic rather than a model-specific tokenizer.
    /// </remarks>
    public bool SupportsModel(string modelId) => true;

    /// <inheritdoc />
    /// <remarks>
    /// Always returns <c>true</c> because this counter uses a character-based
    /// approximation rather than a real tokenizer.
    /// </remarks>
#pragma warning disable CA1822 // Interface default method override — must be instance member
    public bool IsApproximate(string modelId) => true;
#pragma warning restore CA1822
}
