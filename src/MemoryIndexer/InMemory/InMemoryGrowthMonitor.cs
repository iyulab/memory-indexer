using System.Collections.Concurrent;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.InMemory;

/// <summary>
/// In-memory implementation of memory growth monitor.
/// Phase 22.1: Memory Growth Rate Control.
/// </summary>
public sealed class InMemoryGrowthMonitor : IMemoryGrowthMonitor
{
    private readonly MemoryIndexerOptions _options;
    private readonly ConcurrentDictionary<string, UserGrowthState> _userStates = new();

    public InMemoryGrowthMonitor(IOptions<MemoryIndexerOptions> options)
    {
        _options = options.Value;
    }

    public Task<MemoryGrowthMetrics> GetGrowthMetricsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var state = _userStates.GetOrAdd(userId, _ => new UserGrowthState());
        var maxGrowthRate = _options.MemoryGrowth.MaxGrowthRatePerRound;

        var metrics = new MemoryGrowthMetrics
        {
            UserId = userId,
            CurrentRound = state.CurrentRound,
            MemoriesStoredThisRound = state.MemoriesStoredThisRound,
            MemoriesFilteredThisRound = state.MemoriesFilteredThisRound,
            AverageMemoriesPerRound = state.TotalRounds > 0
                ? (float)state.TotalMemoriesStored / state.TotalRounds
                : 0f,
            CurrentGrowthRate = state.MemoriesStoredThisRound,
            ExceedsThreshold = state.MemoriesStoredThisRound > maxGrowthRate,
            MaxAllowedGrowthRate = maxGrowthRate,
            FilterReasons = new Dictionary<string, int>(state.FilterReasons)
        };

        return Task.FromResult(metrics);
    }

    public Task RecordMemoryStorageAsync(string userId, bool filtered, string? reason = null, CancellationToken cancellationToken = default)
    {
        var state = _userStates.GetOrAdd(userId, _ => new UserGrowthState());

        if (filtered)
        {
            state.MemoriesFilteredThisRound++;
            if (!string.IsNullOrEmpty(reason))
            {
                state.FilterReasons.AddOrUpdate(reason, 1, (_, count) => count + 1);
            }
        }
        else
        {
            state.MemoriesStoredThisRound++;
            state.TotalMemoriesStored++;
        }

        return Task.CompletedTask;
    }

    public Task EndRoundAsync(string userId, CancellationToken cancellationToken = default)
    {
        var state = _userStates.GetOrAdd(userId, _ => new UserGrowthState());

        state.CurrentRound++;
        state.TotalRounds++;
        state.MemoriesStoredThisRound = 0;
        state.MemoriesFilteredThisRound = 0;
        state.FilterReasons.Clear();

        return Task.CompletedTask;
    }

    private sealed class UserGrowthState
    {
        public int CurrentRound { get; set; } = 1;
        public int TotalRounds { get; set; }
        public int MemoriesStoredThisRound { get; set; }
        public int MemoriesFilteredThisRound { get; set; }
        public int TotalMemoriesStored { get; set; }
        public ConcurrentDictionary<string, int> FilterReasons { get; } = new();
    }
}
