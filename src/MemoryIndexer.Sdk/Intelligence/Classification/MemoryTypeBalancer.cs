using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Classification;

/// <summary>
/// Memory type distribution balancer using adaptive weighting.
/// </summary>
/// <remarks>
/// Phase 23.1: Memory Type Distribution Balancing.
///
/// Provides boost factors for underrepresented memory types to achieve
/// target distribution percentages (default: Episodic 40%, Semantic 30%,
/// Procedural 20%, Fact 10%).
///
/// Algorithm:
/// 1. Calculate current distribution from memory store
/// 2. Compare to target distribution
/// 3. Apply boost for underrepresented types: boost = (target - current) * sensitivity
/// 4. Clamp boost to [0, maxBoost] to prevent over-correction
/// </remarks>
public sealed partial class MemoryTypeBalancer : IMemoryTypeBalancer
{
    private readonly IMemoryStore _store;
    private readonly TypeBalancerOptions _options;
    private readonly ILogger<MemoryTypeBalancer> _logger;

    public MemoryTypeBalancer(
        IMemoryStore store,
        IOptions<MemoryIndexerOptions> options,
        ILogger<MemoryTypeBalancer> logger)
    {
        _store = store;
        _options = options.Value.TypeBalancing;
        _logger = logger;

        var targetsStr = string.Join(", ", _options.TargetDistribution.Select(x => $"{x.Key}={x.Value:F2}"));
        LogBalancerInitialized(_logger, _options.Enabled, targetsStr);
    }

    /// <inheritdoc />
    public async Task<float> GetTypeBoostAsync(
        MemoryType type,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return 0f;
        }

        // Get counts first to check if we have enough memories
        var counts = await GetTypeCountsAsync(userId, cancellationToken);
        var totalCount = counts.Values.Sum();
        if (totalCount < _options.MinMemoriesForBalancing)
        {
            LogNotEnoughMemories(_logger, totalCount, _options.MinMemoriesForBalancing);
            return 0f;
        }

        // Calculate current distribution from counts
        var currentPercentage = totalCount > 0
            ? (float)counts.GetValueOrDefault(type, 0) / totalCount
            : 0f;
        var targetPercentage = _options.TargetDistribution.GetValueOrDefault(type, 0.25f);

        // Calculate boost: (target - current) * sensitivity
        // Only boost underrepresented types (negative boost = 0)
        var rawBoost = (targetPercentage - currentPercentage) * _options.BoostSensitivity;
        var boost = Math.Clamp(rawBoost, 0f, _options.MaxBoost);

        if (boost > 0.01f)
        {
            LogTypeBoost(_logger, type, currentPercentage, targetPercentage, boost);
        }

        return boost;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<MemoryType, float>> GetTypeDistributionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var counts = await GetTypeCountsAsync(userId, cancellationToken);
        var total = counts.Values.Sum();

        if (total == 0)
        {
            return new Dictionary<MemoryType, float>();
        }

        return counts.ToDictionary(
            x => x.Key,
            x => (float)x.Value / total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _store.GetTypeCountsAsync(userId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MemoryTypeBalancer initialized (Phase 23.1). Enabled: {Enabled}, Targets: {Targets}")]
    private static partial void LogBalancerInitialized(ILogger logger, bool enabled, string targets);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Not enough memories for balancing ({TotalCount} < {MinRequired}). Skipping boost.")]
    private static partial void LogNotEnoughMemories(ILogger logger, int totalCount, int minRequired);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Type boost for {Type}: current={Current:F2}, target={Target:F2}, boost={Boost:F2}")]
    private static partial void LogTypeBoost(ILogger logger, MemoryType type, float current, float target, float boost);
}
