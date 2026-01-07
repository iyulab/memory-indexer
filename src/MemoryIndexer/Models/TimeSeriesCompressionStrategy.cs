namespace MemoryIndexer.Models;

/// <summary>
/// Strategy for compressing time-series metadata in memory consolidation.
/// Phase 29: Prevents metadata bloat from sequential operations.
/// </summary>
/// <remarks>
/// Example use case: Game rounds "Round 1, Round 2, ..., Round 20" → "Range: 1-20, Current: 20"
/// Reduces token consumption while preserving temporal information.
/// </remarks>
public enum TimeSeriesCompressionStrategy
{
    /// <summary>
    /// No compression - keep all individual timestamps/values.
    /// Suitable for low-frequency data or when exact history is critical.
    /// </summary>
    None = 0,

    /// <summary>
    /// Range compression: Collapse sequential values into ranges.
    /// Example: "1, 2, 3, 4, 5" → "1-5"
    /// Best for: Sequential numeric series, game rounds, step counters.
    /// </summary>
    Range = 1,

    /// <summary>
    /// Statistical compression: Keep first, last, count, min, max, avg.
    /// Example: "1, 3, 5, 7, 9" → "Count: 5, Min: 1, Max: 9, Avg: 5, First: 1, Last: 9"
    /// Best for: Numeric metrics, scores, measurements.
    /// </summary>
    Statistical = 2,

    /// <summary>
    /// Windowed compression: Keep recent N items in full, compress older ones.
    /// Example: Last 5 rounds in detail, older rounds as range.
    /// Best for: Mixed recency requirements, detailed recent + summary of past.
    /// </summary>
    Windowed = 3
}
