using System.Numerics.Tensors;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Scoring;

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

    /// <inheritdoc />
    public float CalculateKeywordBoost(string query, string memoryContent)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(memoryContent))
            return 0f;

        // Extract keywords from query (words with 3+ characters, excluding common stop words)
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had",
            "her", "was", "one", "our", "out", "has", "have", "been", "this", "that",
            "what", "when", "where", "which", "who", "will", "with", "from", "they"
        };

        var queryWords = query
            .Split([' ', ',', '.', '?', '!', ':', ';', '"', '\'', '[', ']', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (queryWords.Count == 0)
            return 0f;

        var contentLower = memoryContent.ToLowerInvariant();
        var matchCount = queryWords.Count(w => contentLower.Contains(w));

        // Normalize: matched keywords / total keywords
        return (float)matchCount / queryWords.Count;
    }

    /// <inheritdoc />
    public float CalculateContentTypeBoost(string memoryContent)
    {
        if (string.IsNullOrWhiteSpace(memoryContent))
            return 0f;

        var contentLower = memoryContent.ToLowerInvariant();

        // High-value positive indicators (confirmed facts, positive answers)
        var positiveIndicators = new[]
        {
            "confirmed", "yes", "correct", "true", "has the property",
            "is a", "can be", "does have", "is edible", "is alive"
        };

        // Lower-value indicators (ruled out, negative answers)
        var negativeIndicators = new[]
        {
            "ruled out", "no", "not", "does not", "cannot", "isn't", "doesn't"
        };

        // Check for positive indicators
        if (positiveIndicators.Any(p => contentLower.Contains(p)))
        {
            return 0.3f; // Significant boost for confirmed/positive info
        }

        // Negative indicators get smaller boost (still useful info, just less actionable)
        if (negativeIndicators.Any(n => contentLower.Contains(n)))
        {
            return 0.1f;
        }

        return 0f;
    }

    /// <inheritdoc />
    public float CalculateHybridScore(MemoryUnit memory, string query, ReadOnlyMemory<float>? queryEmbedding = null)
    {
        // Base score from original formula
        var baseScore = CalculateScore(memory, queryEmbedding);

        // Add keyword matching boost (hybrid search component)
        var keywordBoost = CalculateKeywordBoost(query, memory.Content) * 0.5f; // Weight: 0.5

        // Add content-type boost
        var contentTypeBoost = CalculateContentTypeBoost(memory.Content);

        return baseScore + keywordBoost + contentTypeBoost;
    }
}
