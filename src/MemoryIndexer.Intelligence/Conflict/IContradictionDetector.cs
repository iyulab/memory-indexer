using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Intelligence.Conflict;

/// <summary>
/// Interface for detecting contradictions in memory content.
/// Implements hybrid detection: semantic similarity + rule-based analysis.
/// </summary>
public interface IContradictionDetector
{
    /// <summary>
    /// Detects contradictions between a new memory and existing memories.
    /// </summary>
    /// <param name="newMemory">The new memory to check.</param>
    /// <param name="existingMemories">Existing memories to compare against.</param>
    /// <param name="options">Detection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with any detected contradictions.</returns>
    Task<ContradictionAnalysis> DetectMemoryContradictionAsync(
        MemoryUnit newMemory,
        IReadOnlyList<MemoryUnit> existingMemories,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects contradictions between a new entity triple and existing triples.
    /// </summary>
    /// <param name="newTriple">The new triple to check.</param>
    /// <param name="existingTriples">Existing triples to compare against.</param>
    /// <param name="options">Detection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Analysis result with any detected contradictions.</returns>
    Task<ContradictionAnalysis> DetectTripleContradictionAsync(
        EntityTriple newTriple,
        IReadOnlyList<EntityTriple> existingTriples,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs batch contradiction detection for multiple new items.
    /// </summary>
    /// <param name="newMemories">New memories to check.</param>
    /// <param name="existingMemories">Existing memories to compare against.</param>
    /// <param name="options">Detection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of analysis results for each new memory.</returns>
    Task<IReadOnlyList<ContradictionAnalysis>> DetectBatchContradictionsAsync(
        IReadOnlyList<MemoryUnit> newMemories,
        IReadOnlyList<MemoryUnit> existingMemories,
        ContradictionDetectionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for resolving detected contradictions.
/// </summary>
public interface IContradictionResolver
{
    /// <summary>
    /// Resolves a detected contradiction using the specified strategy.
    /// </summary>
    /// <param name="analysis">The contradiction analysis to resolve.</param>
    /// <param name="strategy">The resolution strategy to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the resolution attempt.</returns>
    Task<ResolutionResult> ResolveAsync(
        ContradictionAnalysis analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Automatically determines and applies the best resolution strategy.
    /// </summary>
    /// <param name="analysis">The contradiction analysis to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the resolution attempt.</returns>
    Task<ResolutionResult> AutoResolveAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests the best resolution strategy for a given contradiction.
    /// </summary>
    /// <param name="analysis">The contradiction analysis.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommended strategy with explanation.</returns>
    Task<(ResolutionStrategy Strategy, string Explanation)> SuggestStrategyAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken = default);
}
