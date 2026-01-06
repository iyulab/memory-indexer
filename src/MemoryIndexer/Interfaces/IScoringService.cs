using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

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

    /// <summary>
    /// Calculates keyword matching boost between query and memory content.
    /// Implements hybrid search by combining semantic and lexical matching.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="memoryContent">The memory content to match against.</param>
    /// <returns>Keyword boost score (0.0 to 1.0).</returns>
    float CalculateKeywordBoost(string query, string memoryContent);

    /// <summary>
    /// Calculates content-type boost for positive/confirmed information.
    /// Positive indicators (CONFIRMED, Yes, etc.) get higher scores.
    /// </summary>
    /// <param name="memoryContent">The memory content to analyze.</param>
    /// <returns>Content type boost score (0.0 to 0.5).</returns>
    float CalculateContentTypeBoost(string memoryContent);

    /// <summary>
    /// Calculates combined score with hybrid search support.
    /// Includes keyword matching and content-type boosting.
    /// </summary>
    /// <param name="memory">The memory to score.</param>
    /// <param name="query">The search query text for keyword matching.</param>
    /// <param name="queryEmbedding">Optional query embedding for semantic similarity.</param>
    /// <returns>The combined score including all boost factors.</returns>
    float CalculateHybridScore(MemoryUnit memory, string query, ReadOnlyMemory<float>? queryEmbedding = null);

    /// <summary>
    /// Scores and normalizes a collection of memories.
    /// Phase 21.2: Score Distribution Normalization.
    /// </summary>
    /// <param name="memories">The memories to score and normalize.</param>
    /// <param name="query">The search query text for hybrid scoring.</param>
    /// <param name="queryEmbedding">Optional query embedding for semantic similarity.</param>
    /// <returns>Normalized scored memories with improved distribution.</returns>
    IReadOnlyList<NormalizableMemory> ScoreAndNormalize(
        IReadOnlyList<MemoryUnit> memories,
        string query,
        ReadOnlyMemory<float>? queryEmbedding = null);
}
