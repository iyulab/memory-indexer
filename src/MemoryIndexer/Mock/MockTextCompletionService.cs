using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Mock;

/// <summary>
/// Mock text completion service for development and testing.
/// Returns placeholder responses without calling any external LLM.
/// </summary>
public sealed class MockTextCompletionService : ITextCompletionService
{
    private readonly ILogger<MockTextCompletionService> _logger;

    public MockTextCompletionService(ILogger<MockTextCompletionService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Mock completion for prompt of length {Length}", prompt.Length);

        // Return a placeholder response
        var response = $"[Mock completion for prompt: {TruncatePrompt(prompt)}]";
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var promptList = prompts.ToList();
        _logger.LogDebug("Mock batch completion for {Count} prompts", promptList.Count);

        var results = promptList
            .Select(p => $"[Mock completion for prompt: {TruncatePrompt(p)}]")
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    private static string TruncatePrompt(string prompt)
    {
        const int maxLength = 50;
        return prompt.Length <= maxLength
            ? prompt
            : string.Concat(prompt.AsSpan(0, maxLength), "...");
    }
}
