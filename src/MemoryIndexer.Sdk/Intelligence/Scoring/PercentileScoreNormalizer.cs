using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Scoring;

/// <summary>
/// Percentile-based score normalizer.
/// Assigns scores based on ranking position (0.0 to 1.0).
/// Best for narrow distributions where separation is needed.
/// Phase 21.2: Score Distribution Normalization.
/// </summary>
public sealed class PercentileScoreNormalizer : IScoreNormalizer
{
    private readonly ILogger<PercentileScoreNormalizer> _logger;
    private NormalizationStats _stats = new() { Strategy = NormalizationStrategy.Percentile };

    public PercentileScoreNormalizer(ILogger<PercentileScoreNormalizer> logger)
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
            scoredMemories[0].NormalizedScore = 1.0f;
            return scoredMemories;
        }

        var scores = scoredMemories.Select(m => m.RawScore).ToList();
        var min = scores.Min();
        var max = scores.Max();

        // Update stats
        _stats.OriginalSpread = max - min;
        _stats.OriginalMean = scores.Average();
        _stats.OriginalStdDev = CalculateStdDev(scores, _stats.OriginalMean);

        // Sort by raw score (ascending)
        var sorted = scoredMemories.OrderBy(m => m.RawScore).ToList();

        // Assign percentile scores (0.0 to 1.0)
        // Lower rank (lower score) → lower percentile
        for (var i = 0; i < sorted.Count; i++)
        {
            var percentile = (float)i / (sorted.Count - 1);
            sorted[i].NormalizedScore = percentile;
        }

        _stats.NormalizedSpread = 1.0f; // Always 0-1 after percentile

        _logger.LogDebug(
            "Percentile normalized {Count} scores: original spread {Original:F3}, forced to {Normalized:F3}",
            scoredMemories.Count,
            _stats.OriginalSpread,
            _stats.NormalizedSpread);

        // Return sorted by normalized score (descending)
        return sorted.OrderByDescending(m => m.NormalizedScore).ToList();
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
