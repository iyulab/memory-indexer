using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services.TokenCounting;

/// <summary>
/// Fast approximate token counter using character-based estimation.
/// Uses the common heuristic of ~4 characters per token for English text, and one token per character for Hangul,
/// Han and Kana, which BPE tokenizers split into about one token per character or more.
/// This is faster than BPE-based counting but less accurate.
/// </summary>
public class ApproximateTokenCounter : ITokenCounter
{
    private readonly double _charsPerToken;

    /// <summary>
    /// Creates a new approximate token counter.
    /// </summary>
    /// <param name="charsPerToken">
    /// Average characters per token for text outside Hangul, Han and Kana (those count one token per character).
    /// Default is 4.0 for English text.
    /// Use lower values (2.5-3.0) for code or technical content.
    /// Use higher values (4.5-5.0) for simple prose.
    /// </param>
    public ApproximateTokenCounter(double charsPerToken = 4.0)
    {
        if (charsPerToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(charsPerToken), "Must be positive");

        _charsPerToken = charsPerToken;
    }

    /// <inheritdoc />
    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var cjk = 0;
        foreach (var c in text)
        {
            if (IsCjk(c))
                cjk++;
        }

        return (int)Math.Ceiling(cjk + (text.Length - cjk) / _charsPerToken);
    }

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
        // an English one rather than four times later.
        var maxChars = 0;
        var cost = 0.0;
        while (maxChars < text.Length)
        {
            cost += IsCjk(text[maxChars]) ? 1.0 : 1.0 / _charsPerToken;
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

    /// <summary>Hangul (syllables and jamo), CJK ideographs, Hiragana and Katakana.</summary>
    private static bool IsCjk(char c) =>
        c is (>= '\uAC00' and <= '\uD7A3') or (>= '\u1100' and <= '\u11FF') or (>= '\u3130' and <= '\u318F')
            or (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF') or (>= '\uF900' and <= '\uFAFF')
            or (>= '\u3040' and <= '\u30FF');
}
