using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Scoring;

/// <summary>
/// Z-score (standard score) normalizer.
/// Normalizes based on mean and standard deviation.
/// Best for distributions with outliers.
/// Phase 21.2: Score Distribution Normalization.
/// </summary>
public sealed class ZScoreNormalizer : IScoreNormalizer
{
    private readonly ILogger<ZScoreNormalizer> _logger;
    private NormalizationStats _stats = new() { Strategy = NormalizationStrategy.ZScore };

    public ZScoreNormalizer(ILogger<ZScoreNormalizer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<NormalizableMemory> Normalize(IReadOnlyList<NormalizableMemory> scoredMemories)
    {
        if (scoredMemories.Count == 0)
        {
            return scoredMemories;
        }

        if (scoredMemories.Count == 1)
        {
            scoredMemories[0].NormalizedScore = 0.5f;
            return scoredMemories;
        }

        var scores = scoredMemories.Select(m => m.RawScore).ToList();
        var mean = scores.Average();
        var stdDev = CalculateStdDev(scores, mean);

        // Update stats
        _stats.OriginalSpread = scores.Max() - scores.Min();
        _stats.OriginalMean = mean;
        _stats.OriginalStdDev = stdDev;

        if (stdDev == 0)
        {
            // All scores identical - assign same normalized score
            foreach (var memory in scoredMemories)
            {
                memory.NormalizedScore = 0.5f;
            }
            _stats.NormalizedSpread = 0f;
            _logger.LogWarning("All scores identical (stdDev=0), normalized to 0.5");
            return scoredMemories;
        }

        // Apply z-score normalization: (x - mean) / stdDev
        // Then map to 0-1 range assuming ±3σ covers most data (99.7%)
        foreach (var memory in scoredMemories)
        {
            var zScore = (memory.RawScore - mean) / stdDev;

            // Map z-score to 0-1 range
            // z ∈ [-3, 3] → [0, 1]
            memory.NormalizedScore = Math.Clamp((zScore + 3) / 6, 0f, 1f);
        }

        var normalizedScores = scoredMemories.Select(m => m.NormalizedScore).ToList();
        _stats.NormalizedSpread = normalizedScores.Max() - normalizedScores.Min();

        _logger.LogDebug(
            "Z-score normalized {Count} scores: mean={Mean:F3}, stdDev={StdDev:F3}, spread {Original:F3} → {Normalized:F3}",
            scoredMemories.Count,
            mean,
            stdDev,
            _stats.OriginalSpread,
            _stats.NormalizedSpread);

        return scoredMemories.OrderByDescending(m => m.NormalizedScore).ToList();
    }

    /// <inheritdoc />
    public NormalizationStats GetStats() => _stats;

    private static float CalculateStdDev(List<float> values, float mean)
    {
        if (values.Count < 2) return 0f;
        var variance = values.Average(v => MathF.Pow(v - mean, 2));
        return MathF.Sqrt(variance);
    }
}
