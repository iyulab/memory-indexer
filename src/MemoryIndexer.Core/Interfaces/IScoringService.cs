using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Interfaces;

/// <summary>
/// Service for calculating memory relevance scores.
/// Based on the Generative Agents scoring formula.
/// </summary>
public interface IScoringService
{
    /// <summary>
    /// Calculates the combined score for a memory.
    /// Formula: α × recency + β × importance + γ × relevance
    /// </summary>
    /// <param name="memory">The memory to score.</param>
    /// <param name="queryEmbedding">Optional query embedding for relevance calculation.</param>
    /// <returns>The combined score (0.0 to 3.0 with default weights).</returns>
    float CalculateScore(MemoryUnit memory, ReadOnlyMemory<float>? queryEmbedding = null);

    /// <summary>
    /// Calculates the recency score based on time since last access.
    /// Uses exponential decay: decay_factor ^ hours_since_access
    /// </summary>
    /// <param name="memory">The memory to score.</param>
    /// <returns>The recency score (0.0 to 1.0).</returns>
    float CalculateRecencyScore(MemoryUnit memory);

    /// <summary>
    /// Calculates the access frequency bonus.
    /// Formula: log(1 + access_count) / log(1 + max_expected)
    /// </summary>
    /// <param name="memory">The memory to score.</param>
    /// <returns>The access frequency score (0.0 to 1.0).</returns>
    float CalculateAccessFrequencyScore(MemoryUnit memory);

    /// <summary>
    /// Calculates cosine similarity between two embeddings.
    /// </summary>
    /// <param name="embedding1">First embedding.</param>
    /// <param name="embedding2">Second embedding.</param>
    /// <returns>Cosine similarity (0.0 to 1.0).</returns>
    float CalculateCosineSimilarity(ReadOnlyMemory<float> embedding1, ReadOnlyMemory<float> embedding2);
}
