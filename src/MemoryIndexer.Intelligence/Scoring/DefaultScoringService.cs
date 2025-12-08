using System.Numerics.Tensors;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Intelligence.Scoring;

/// <summary>
/// Default implementation of IScoringService.
/// Based on the Generative Agents scoring formula.
/// </summary>
public sealed class DefaultScoringService : IScoringService
{
    private readonly ScoringOptions _options;

    public DefaultScoringService(IOptions<MemoryIndexerOptions> options)
    {
        _options = options.Value.Scoring;
    }

    /// <inheritdoc />
    public float CalculateScore(MemoryUnit memory, ReadOnlyMemory<float>? queryEmbedding = null)
    {
        var recency = CalculateRecencyScore(memory);
        var importance = memory.ImportanceScore;

        float relevance;
        if (queryEmbedding.HasValue && memory.Embedding.HasValue)
        {
            relevance = CalculateCosineSimilarity(queryEmbedding.Value, memory.Embedding.Value);
        }
        else
        {
            relevance = 0.5f; // Default relevance when no query
        }

        // Generative Agents formula: α × recency + β × importance + γ × relevance
        var score = _options.RecencyWeight * recency
                  + _options.ImportanceWeight * importance
                  + _options.RelevanceWeight * relevance;

        // Optionally add access frequency bonus
        var accessBonus = CalculateAccessFrequencyScore(memory) * 0.1f;

        return score + accessBonus;
    }

    /// <inheritdoc />
    public float CalculateRecencyScore(MemoryUnit memory)
    {
        var lastAccess = memory.LastAccessedAt ?? memory.CreatedAt;
        var hoursSinceAccess = (DateTime.UtcNow - lastAccess).TotalHours;

        // Exponential decay: decay_factor ^ hours_since_access
        // With decay_factor = 0.99, half-life ≈ 69 hours (about 3 days)
        var score = MathF.Pow(_options.DecayFactor, (float)hoursSinceAccess);

        return Math.Clamp(score, 0f, 1f);
    }

    /// <inheritdoc />
    public float CalculateAccessFrequencyScore(MemoryUnit memory)
    {
        // Formula: log(1 + access_count) / log(1 + max_expected)
        var numerator = MathF.Log(1 + memory.AccessCount);
        var denominator = MathF.Log(1 + _options.MaxExpectedAccessCount);

        if (denominator == 0)
            return 0f;

        var score = numerator / denominator;
        return Math.Clamp(score, 0f, 1f);
    }

    /// <inheritdoc />
    public float CalculateCosineSimilarity(ReadOnlyMemory<float> embedding1, ReadOnlyMemory<float> embedding2)
    {
        var span1 = embedding1.Span;
        var span2 = embedding2.Span;

        if (span1.Length != span2.Length)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(span1, span2);
        var norm1 = TensorPrimitives.Norm(span1);
        var norm2 = TensorPrimitives.Norm(span2);

        if (norm1 == 0 || norm2 == 0)
            return 0f;

        // Cosine similarity: dot(a, b) / (||a|| * ||b||)
        var similarity = dotProduct / (norm1 * norm2);

        // Convert from [-1, 1] to [0, 1] range
        return (similarity + 1) / 2;
    }
}
