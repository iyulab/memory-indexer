namespace MemoryIndexer.Interfaces;

/// <summary>
/// Interface for counting tokens in text content.
/// Used for token-budget-aware context building.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Count tokens in a single text string.
    /// </summary>
    /// <param name="text">The text to count tokens for.</param>
    /// <returns>The number of tokens.</returns>
    int Count(string text);

    /// <summary>
    /// Count tokens in multiple text strings.
    /// </summary>
    /// <param name="texts">The texts to count tokens for.</param>
    /// <returns>The total number of tokens across all texts.</returns>
    int Count(IEnumerable<string> texts);

    /// <summary>
    /// Truncate text to fit within a maximum token count.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxTokens">The maximum number of tokens allowed.</param>
    /// <returns>The truncated text that fits within the token limit.</returns>
    string Truncate(string text, int maxTokens);
}
