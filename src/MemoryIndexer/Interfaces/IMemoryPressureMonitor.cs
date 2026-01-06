namespace MemoryIndexer.Interfaces;

/// <summary>
/// Monitors system memory pressure and provides adaptive memory management signals.
/// </summary>
public interface IMemoryPressureMonitor
{
    /// <summary>
    /// Gets the current memory pressure level.
    /// </summary>
    MemoryPressureLevel CurrentPressure { get; }

    /// <summary>
    /// Gets the current memory pressure statistics.
    /// </summary>
    MemoryPressureInfo GetMemoryInfo();

    /// <summary>
    /// Checks if the system is under memory pressure.
    /// </summary>
    /// <param name="threshold">Minimum pressure level to consider as "under pressure"</param>
    /// <returns>True if memory pressure is at or above the threshold</returns>
    bool IsUnderPressure(MemoryPressureLevel threshold = MemoryPressureLevel.Medium);

    /// <summary>
    /// Registers a callback to be invoked when memory pressure changes.
    /// </summary>
    /// <param name="callback">Action to invoke with new pressure level</param>
    void OnPressureChanged(Action<MemoryPressureLevel> callback);
}

/// <summary>
/// Memory pressure levels indicating system memory availability.
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>
    /// Memory is abundant (less than 60% utilized).
    /// </summary>
    Low = 0,

    /// <summary>
    /// Memory is moderately utilized (60-80%).
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Memory is highly utilized (80-90%).
    /// </summary>
    High = 2,

    /// <summary>
    /// Memory is critically low (greater than 90% utilized).
    /// </summary>
    Critical = 3
}

/// <summary>
/// Memory pressure statistics from GC.
/// </summary>
public sealed class MemoryPressureInfo
{
    /// <summary>
    /// Current memory pressure level.
    /// </summary>
    public MemoryPressureLevel Level { get; init; }

    /// <summary>
    /// Total available memory in bytes.
    /// </summary>
    public long TotalAvailableMemoryBytes { get; init; }

    /// <summary>
    /// High memory load threshold in bytes.
    /// </summary>
    public long HighMemoryLoadThresholdBytes { get; init; }

    /// <summary>
    /// Current memory load in bytes.
    /// </summary>
    public long MemoryLoadBytes { get; init; }

    /// <summary>
    /// Heap size in bytes.
    /// </summary>
    public long HeapSizeBytes { get; init; }

    /// <summary>
    /// Memory utilization percentage (0.0 to 1.0).
    /// </summary>
    public double UtilizationPercentage { get; init; }

    /// <summary>
    /// Number of Gen0 collections since start.
    /// </summary>
    public int Gen0Collections { get; init; }

    /// <summary>
    /// Number of Gen1 collections since start.
    /// </summary>
    public int Gen1Collections { get; init; }

    /// <summary>
    /// Number of Gen2 collections since start.
    /// </summary>
    public int Gen2Collections { get; init; }
}
