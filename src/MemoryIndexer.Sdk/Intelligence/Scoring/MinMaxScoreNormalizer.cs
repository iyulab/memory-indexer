using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Scoring;

/// <summary>
/// Min-max score normalizer.
/// Scales scores to 0-1 range linearly.
/// Phase 21.2: Score Distribution Normalization.
/// </summary>
public sealed class MinMaxScoreNormalizer : IScoreNormalizer
{
    private readonly ILogger<MinMaxScoreNormalizer> _logger;
    private NormalizationStats _stats = new() { Strategy = NormalizationStrategy.MinMax };

    public MinMaxScoreNormalizer(ILogger<MinMaxScoreNormalizer> logger)
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
        var spread = max - min;

        // Update stats
        _stats.OriginalSpread = spread;
        _stats.OriginalMean = scores.Average();
        _stats.OriginalStdDev = CalculateStdDev(scores, _stats.OriginalMean);

        if (spread == 0)
        {
            // All scores identical - assign same normalized score
            foreach (var memory in scoredMemories)
            {
                memory.NormalizedScore = 0.5f;
            }
            _stats.NormalizedSpread = 0f;
            _logger.LogWarning("All scores identical (spread=0), normalized to 0.5");
            return scoredMemories;
        }

        // Min-max normalization: (x - min) / (max - min)
        foreach (var memory in scoredMemories)
        {
            memory.NormalizedScore = (memory.RawScore - min) / spread;
        }

        _stats.NormalizedSpread = 1.0f; // Always 0-1 after min-max

        _logger.LogDebug(
            "MinMax normalized {Count} scores: spread {Original:F3} → {Normalized:F3}",
            scoredMemories.Count,
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
