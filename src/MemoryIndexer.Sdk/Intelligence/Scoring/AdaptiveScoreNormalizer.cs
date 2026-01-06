using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Scoring;

/// <summary>
/// Adaptive score normalizer.
/// Chooses the best normalization strategy based on score distribution characteristics.
/// Phase 21.2: Score Distribution Normalization.
/// </summary>
public sealed class AdaptiveScoreNormalizer : IScoreNormalizer
{
    private readonly ILogger<AdaptiveScoreNormalizer> _logger;
    private readonly ILogger<MinMaxScoreNormalizer> _minMaxLogger;
    private readonly ILogger<PercentileScoreNormalizer> _percentileLogger;
    private readonly ILogger<ZScoreNormalizer> _zScoreLogger;
    private NormalizationStats _stats = new() { Strategy = NormalizationStrategy.Adaptive };

    public AdaptiveScoreNormalizer(
        ILogger<AdaptiveScoreNormalizer> logger,
        ILogger<MinMaxScoreNormalizer> minMaxLogger,
        ILogger<PercentileScoreNormalizer> percentileLogger,
        ILogger<ZScoreNormalizer> zScoreLogger)
    {
        _logger = logger;
        _minMaxLogger = minMaxLogger;
        _percentileLogger = percentileLogger;
        _zScoreLogger = zScoreLogger;
    }

    /// <inheritdoc />
    public IReadOnlyList<NormalizableMemory> Normalize(IReadOnlyList<NormalizableMemory> scoredMemories)
    {
        if (scoredMemories.Count < 3)
        {
            // Too few samples - use simple min-max
            var normalizer = new MinMaxScoreNormalizer(_minMaxLogger);
            var result = normalizer.Normalize(scoredMemories);
            _stats = normalizer.GetStats();
            _stats.Strategy = NormalizationStrategy.Adaptive; // Preserve adaptive label
            return result;
        }

        var scores = scoredMemories.Select(m => m.RawScore).ToList();
        var min = scores.Min();
        var max = scores.Max();
        var spread = max - min;
        var mean = scores.Average();
        var stdDev = CalculateStdDev(scores, mean);
        var coefficientOfVariation = stdDev / (mean != 0 ? mean : 1f);

        // Update stats
        _stats.OriginalSpread = spread;
        _stats.OriginalMean = mean;
        _stats.OriginalStdDev = stdDev;

        // Choose strategy based on distribution characteristics
        IScoreNormalizer selectedNormalizer;

        if (spread < 0.3f)
        {
            // Very narrow distribution: Use percentile to force separation
            // This handles the case where all scores are clustered (like 1.10-1.69)
            selectedNormalizer = new PercentileScoreNormalizer(_percentileLogger);
            _logger.LogDebug(
                "Adaptive: narrow spread ({Spread:F3} < 0.3) → Percentile",
                spread);
        }
        else if (coefficientOfVariation > 0.5f)
        {
            // High variance relative to mean: Use z-score to handle outliers
            selectedNormalizer = new ZScoreNormalizer(_zScoreLogger);
            _logger.LogDebug(
                "Adaptive: high variance (CV={CV:F3} > 0.5) → Z-score",
                coefficientOfVariation);
        }
        else
        {
            // Normal distribution: Use min-max scaling
            selectedNormalizer = new MinMaxScoreNormalizer(_minMaxLogger);
            _logger.LogDebug(
                "Adaptive: normal distribution (spread={Spread:F3}, CV={CV:F3}) → MinMax",
                spread,
                coefficientOfVariation);
        }

        var normalized = selectedNormalizer.Normalize(scoredMemories);

        // Get stats from selected normalizer but preserve adaptive strategy
        var selectedStats = selectedNormalizer.GetStats();
        _stats.NormalizedSpread = selectedStats.NormalizedSpread;

        _logger.LogInformation(
            "Adaptive normalization: {Count} scores, spread {Original:F3} → {Normalized:F3}, strategy={Strategy}",
            scoredMemories.Count,
            _stats.OriginalSpread,
            _stats.NormalizedSpread,
            selectedStats.Strategy);

        return normalized;
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
