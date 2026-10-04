using MemoryIndexer.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// What <see cref="ITextCompletionService"/> resolves to when no completion is configured
/// (<c>MemoryIndexer:Completion:Provider</c> = <c>None</c>, the default, and no service registered by the application).
/// Services that work without completion are given <c>null</c> instead (<see cref="TextCompletion.OrNull"/>), so their
/// designed fallbacks run. A service that needs a model gets this one: asking it throws, so that service's own error
/// path runs (an empty result, the reason logged) — never placeholder text.
/// </summary>
internal sealed class UnconfiguredTextCompletionService : ITextCompletionService
{
    public Task<string> CompleteAsync(string prompt, TextCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "No text completion service is configured. Register an ITextCompletionService that wraps your LLM " +
            "(see README.md 'As SDK'), or set MemoryIndexer:Completion:Provider to 'Mock' in tests " +
            "(fixed placeholder text - never for memories you keep).");
}

/// <summary>Resolution helpers for the optional completion service.</summary>
internal static class TextCompletion
{
    /// <summary>The configured completion service, or <c>null</c> when none is configured.</summary>
    public static ITextCompletionService? OrNull(IServiceProvider services) =>
        services.GetService<ITextCompletionService>() is { } completion and not UnconfiguredTextCompletionService
            ? completion
            : null;
}
