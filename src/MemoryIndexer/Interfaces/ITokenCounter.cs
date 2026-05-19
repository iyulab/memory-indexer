namespace MemoryIndexer.Interfaces;

/// <summary>
/// Token counter with text truncation support for MemoryIndexer.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Count the number of tokens in the given text.
    /// </summary>
    /// <param name="text">The text to count tokens in.</param>
    /// <returns>The number of tokens.</returns>
    int Count(string text);

    /// <summary>
    /// Count the total number of tokens across multiple texts.
    /// </summary>
    /// <param name="texts">The texts to count tokens in.</param>
    /// <returns>The total number of tokens.</returns>
    int Count(IEnumerable<string> texts);

    /// <summary>
    /// Determines whether this counter supports the specified model.
    /// </summary>
    /// <param name="modelId">The model identifier to check.</param>
    /// <returns><see langword="true"/> if the model is supported; otherwise, <see langword="false"/>.</returns>
    bool SupportsModel(string modelId);

    /// <summary>
    /// Determines whether token counts for the specified model are approximate.
    /// </summary>
    /// <param name="modelId">The model identifier to check.</param>
    /// <returns><see langword="true"/> if counts are approximate; otherwise, <see langword="false"/>.</returns>
    bool IsApproximate(string modelId) => false;

    /// <summary>
    /// Truncate text to fit within a maximum token count.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxTokens">The maximum number of tokens allowed.</param>
    /// <returns>The truncated text that fits within the token limit.</returns>
    string Truncate(string text, int maxTokens);
}
