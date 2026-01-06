using Microsoft.Extensions.Diagnostics.HealthChecks;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Health;

/// <summary>
/// Health check for User Profile tier (L3).
/// Monitors profile consistency and storage health.
/// </summary>
public class UserProfileHealthCheck : IHealthCheck
{
    private readonly IUserProfile _userProfile;
    private const int CriticalEntriesPerUser = 1000;  // Way above normal ~500
    private const int WarningEntriesPerUser = 750;

    public UserProfileHealthCheck(IUserProfile userProfile)
    {
        _userProfile = userProfile ?? throw new ArgumentNullException(nameof(userProfile));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _userProfile.GetStats("__health_check_test__");

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
                    $"User profile has critical entry count: {stats.TotalEntries} (expected ~500)",
                    data: data);
            }

            // Degraded: Entry count approaching limit
            if (stats.TotalEntries > WarningEntriesPerUser)
            {
                return HealthCheckResult.Degraded(
                    $"User profile approaching entry limit: {stats.TotalEntries}",
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
                    $"User profile has low confirmation ratio: {confirmationRatio:P1} ({stats.ConfirmedEntries}/{stats.TotalEntries})",
                    data: data);
            }

            // Healthy
            return HealthCheckResult.Healthy(
                $"User profile healthy ({stats.TotalEntries} entries, {stats.ConfirmedEntries} confirmed, avg confidence: {stats.AverageConfidence:F2})",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check User profile health",
                exception: ex);
        }
    }
}
