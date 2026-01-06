using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services;

/// <summary>
/// Monitors system memory pressure using GC.GetGCMemoryInfo() API.
/// Provides adaptive memory management signals based on current memory utilization.
/// </summary>
public sealed class MemoryPressureMonitorService : IMemoryPressureMonitor
{
    private readonly List<Action<MemoryPressureLevel>> _callbacks = [];
    private MemoryPressureLevel _lastPressure = MemoryPressureLevel.Low;
    private readonly object _lock = new();

    /// <inheritdoc />
    public MemoryPressureLevel CurrentPressure
    {
        get
        {
            var info = GetMemoryInfo();
            return info.Level;
        }
    }

    /// <inheritdoc />
    public MemoryPressureInfo GetMemoryInfo()
    {
        var gcInfo = GC.GetGCMemoryInfo();

        // Calculate utilization percentage
        var totalAvailable = gcInfo.TotalAvailableMemoryBytes;
        var highLoadThreshold = gcInfo.HighMemoryLoadThresholdBytes;
        var memoryLoad = gcInfo.MemoryLoadBytes;
        var heapSize = gcInfo.HeapSizeBytes;

        // Utilization = (MemoryLoad / TotalAvailable)
        var utilization = totalAvailable > 0
            ? (double)memoryLoad / totalAvailable
            : 0.0;

        // Determine pressure level based on utilization
        var level = utilization switch
        {
            < 0.60 => MemoryPressureLevel.Low,
            < 0.80 => MemoryPressureLevel.Medium,
            < 0.90 => MemoryPressureLevel.High,
            _ => MemoryPressureLevel.Critical
        };

        // Notify callbacks if pressure level changed
        if (level != _lastPressure)
        {
            lock (_lock)
            {
                var oldPressure = _lastPressure;
                _lastPressure = level;

                foreach (var callback in _callbacks)
                {
                    try
                    {
                        callback(level);
                    }
                    catch
                    {
                        // Swallow callback exceptions to prevent cascading failures
                    }
                }
            }
        }

        return new MemoryPressureInfo
        {
            Level = level,
            TotalAvailableMemoryBytes = totalAvailable,
            HighMemoryLoadThresholdBytes = highLoadThreshold,
            MemoryLoadBytes = memoryLoad,
            HeapSizeBytes = heapSize,
            UtilizationPercentage = utilization,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    /// <inheritdoc />
    public bool IsUnderPressure(MemoryPressureLevel threshold = MemoryPressureLevel.Medium)
    {
        return CurrentPressure >= threshold;
    }

    /// <inheritdoc />
    public void OnPressureChanged(Action<MemoryPressureLevel> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_lock)
        {
            _callbacks.Add(callback);
        }
    }
}
