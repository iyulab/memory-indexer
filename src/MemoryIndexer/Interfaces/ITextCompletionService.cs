namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for generating text completions using language models.
/// Extends the canonical <see cref="Flux.Abstractions.ITextCompletionService"/>.
/// </summary>
/// <remarks>
/// New code should prefer <see cref="Flux.Abstractions.ITextCompletionService"/> directly.
/// This interface exists for backward compatibility within the MemoryIndexer module.
/// </remarks>
public interface ITextCompletionService : Flux.Abstractions.ITextCompletionService
{
}
