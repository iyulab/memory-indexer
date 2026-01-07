using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for Semantic Store tier (Tier 3).
/// Monitors profile consistency and storage health.
/// Implements Tulving's Semantic Memory System health monitoring.
/// </summary>
public class SemanticStoreHealthCheck : IHealthCheck
{
    private readonly ISemanticStore _semanticStore;
    private const int CriticalEntriesPerUser = 1000;  // Way above normal ~500
    private const int WarningEntriesPerUser = 750;

    public SemanticStoreHealthCheck(ISemanticStore semanticStore)
    {
        _semanticStore = semanticStore ?? throw new ArgumentNullException(nameof(semanticStore));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _semanticStore.GetStats("__health_check_test__");

            var data = new Dictionary<string, object>
            {
                ["totalEntries"] = stats.TotalEntries,
                ["confirmedEntries"] = stats.ConfirmedEntries,
                ["averageConfidence"] = stats.AverageConfidence
            };

            // Add category distribution
            foreach (var (category, count) in stats.EntriesByCategory)
            {
                data[$"category_{category}"] = count;
            }

            // Critical: Entry count way above expected limit
            if (stats.TotalEntries > CriticalEntriesPerUser)
            {
                return HealthCheckResult.Unhealthy(
                    $"Semantic store has critical entry count: {stats.TotalEntries} (expected ~500)",
                    data: data);
            }

            // Degraded: Entry count approaching limit
            if (stats.TotalEntries > WarningEntriesPerUser)
            {
                return HealthCheckResult.Degraded(
                    $"Semantic store approaching entry limit: {stats.TotalEntries}",
                    data: data);
            }

            // Check for anomalies: very low confirmation ratio
            var confirmationRatio = stats.TotalEntries > 0
                ? (double)stats.ConfirmedEntries / stats.TotalEntries
                : 0;

            if (stats.TotalEntries > 50 && confirmationRatio < 0.1)
            {
                // Many entries but very few confirmed - may indicate promotion issues
                data["confirmationRatio"] = confirmationRatio;
                return HealthCheckResult.Degraded(
                    $"Semantic store has low confirmation ratio: {confirmationRatio:P1} ({stats.ConfirmedEntries}/{stats.TotalEntries})",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"Semantic store healthy ({stats.TotalEntries} entries, {stats.ConfirmedEntries} confirmed, avg confidence: {stats.AverageConfidence:F2})",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Semantic store health",
                exception: ex);
        }
    }
}
