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
    Task<ContradictionAnalysis<MemoryUnit>> DetectMemoryContradictionAsync(
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
    Task<ContradictionAnalysis<EntityTriple>> DetectTripleContradictionAsync(
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
    Task<IReadOnlyList<ContradictionAnalysis<MemoryUnit>>> DetectBatchContradictionsAsync(
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
    /// Resolves a detected memory contradiction using the specified strategy.
    /// </summary>
    Task<ResolutionResult<MemoryUnit>> ResolveMemoryAsync(
        ContradictionAnalysis<MemoryUnit> analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a detected triple contradiction using the specified strategy.
    /// </summary>
    Task<ResolutionResult<EntityTriple>> ResolveTripleAsync(
        ContradictionAnalysis<EntityTriple> analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Automatically determines and applies the best resolution strategy for memories.
    /// </summary>
    Task<ResolutionResult<MemoryUnit>> AutoResolveMemoryAsync(
        ContradictionAnalysis<MemoryUnit> analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Automatically determines and applies the best resolution strategy for triples.
    /// </summary>
    Task<ResolutionResult<EntityTriple>> AutoResolveTripleAsync(
        ContradictionAnalysis<EntityTriple> analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests the best resolution strategy for a given contradiction type and confidence.
    /// </summary>
    (ResolutionStrategy Strategy, string Explanation) SuggestStrategy(
        ContradictionType type,
        float confidence);
}
