namespace MemoryIndexer.Models;

/// <summary>
/// Metrics for memory growth rate monitoring.
/// Phase 22.1: Memory Growth Rate Control.
/// </summary>
public sealed class MemoryGrowthMetrics
{
    /// <summary>
    /// User ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Current round number.
    /// </summary>
    public int CurrentRound { get; init; }

    /// <summary>
    /// Total memories stored in current round.
    /// </summary>
    public int MemoriesStoredThisRound { get; init; }

    /// <summary>
    /// Total memories filtered (not stored) in current round.
    /// </summary>
    public int MemoriesFilteredThisRound { get; init; }

    /// <summary>
    /// Average memories stored per round (lifetime).
    /// </summary>
    public float AverageMemoriesPerRound { get; init; }

    /// <summary>
    /// Current growth rate (memories/round).
    /// </summary>
    public float CurrentGrowthRate { get; init; }

    /// <summary>
    /// Whether current growth rate exceeds threshold.
    /// </summary>
    public bool ExceedsThreshold { get; init; }

    /// <summary>
    /// Maximum allowed growth rate (from configuration).
    /// </summary>
    public float MaxAllowedGrowthRate { get; init; }

    /// <summary>
    /// Filtering reasons breakdown.
    /// </summary>
    public Dictionary<string, int> FilterReasons { get; init; } = [];

    /// <summary>
    /// Timestamp of metrics calculation.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
