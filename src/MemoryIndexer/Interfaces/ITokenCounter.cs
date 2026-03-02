namespace MemoryIndexer.Interfaces;

/// <summary>
/// Extended token counter with text truncation support.
/// Inherits core counting contract (Count, SupportsModel, IsApproximate)
/// from <see cref="TokenMeter.Abstractions.ITokenCounter"/>.
/// </summary>
public interface ITokenCounter : TokenMeter.Abstractions.ITokenCounter
{
    /// <summary>
    /// Truncate text to fit within a maximum token count.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxTokens">The maximum number of tokens allowed.</param>
    /// <returns>The truncated text that fits within the token limit.</returns>
    string Truncate(string text, int maxTokens);
}
