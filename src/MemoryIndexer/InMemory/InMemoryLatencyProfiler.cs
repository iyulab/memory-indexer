using System.Collections.Concurrent;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.InMemory;

/// <summary>
/// In-memory implementation of latency profiler.
/// Phase 22.2: Recall Latency Optimization
/// </summary>
public sealed class InMemoryLatencyProfiler : ILatencyProfiler
{
    private readonly ConcurrentDictionary<string, UserLatencyState> _userStates = new();
    private readonly LatencyOptions _options;

    public InMemoryLatencyProfiler(IOptions<MemoryIndexerOptions> options)
    {
        _options = options.Value.Latency;
    }

    public Task RecordLatencyAsync(
        string userId,
        string tier,
        double latencyMs,
        Dictionary<string, double>? componentLatencies = null,
        CancellationToken cancellationToken = default)
    {
        var state = _userStates.GetOrAdd(userId, _ => new UserLatencyState());
        var tierState = state.GetOrCreateTierState(tier);

        tierState.Latencies.Add(latencyMs);
        tierState.TotalQueries++;

        if (componentLatencies != null)
        {
            foreach (var (component, latency) in componentLatencies)
            {
                if (!tierState.ComponentTotals.ContainsKey(component))
                {
                    tierState.ComponentTotals[component] = 0.0;
                }
                tierState.ComponentTotals[component] += latency;
            }
        }

        var budget = GetLatencyBudget(tier);
        if (latencyMs > budget)
        {
            tierState.BudgetExceededCount++;
        }

        return Task.CompletedTask;
    }

    public Task RecordCacheAccessAsync(
        string userId,
        string cacheType,
        bool hit,
        CancellationToken cancellationToken = default)
    {
        var state = _userStates.GetOrAdd(userId, _ => new UserLatencyState());

        if (cacheType == "Embedding")
        {
            if (hit)
                state.EmbeddingCacheHits++;
            else
                state.EmbeddingCacheMisses++;
        }
        else if (cacheType == "Query")
        {
            if (hit)
                state.QueryCacheHits++;
            else
                state.QueryCacheMisses++;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LatencyMetrics>> GetMetricsAsync(
        string userId,
        string? tier = null,
        CancellationToken cancellationToken = default)
    {
        if (!_userStates.TryGetValue(userId, out var state))
        {
            return Task.FromResult<IReadOnlyList<LatencyMetrics>>(Array.Empty<LatencyMetrics>());
        }

        var results = new List<LatencyMetrics>();

        foreach (var (tierName, tierState) in state.TierStates)
        {
            if (tier != null && tierName != tier)
                continue;

            var metrics = CalculateMetrics(userId, tierName, tierState, state);
            results.Add(metrics);
        }

        return Task.FromResult<IReadOnlyList<LatencyMetrics>>(results);
    }

    public Task ResetMetricsAsync(string userId, CancellationToken cancellationToken = default)
    {
        _userStates.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public double GetLatencyBudget(string tier)
    {
        return tier switch
        {
            "Working" => _options.WorkingMemoryBudgetMs,
            "Session" => _options.SessionMemoryBudgetMs,
            "User" => _options.UserProfileBudgetMs,
            _ => 500.0 // Default fallback
        };
    }

    private LatencyMetrics CalculateMetrics(
        string userId,
        string tier,
        TierLatencyState tierState,
        UserLatencyState userState)
    {
        var latencies = tierState.Latencies.OrderBy(x => x).ToList();
        var count = latencies.Count;

        var average = count > 0 ? latencies.Average() : 0.0;
        var min = count > 0 ? latencies.Min() : 0.0;
        var max = count > 0 ? latencies.Max() : 0.0;

        var p50 = count > 0 ? latencies[(int)(count * 0.50)] : 0.0;
        var p95 = count > 0 ? latencies[(int)(count * 0.95)] : 0.0;
        var p99 = count > 0 ? latencies[(int)(count * 0.99)] : 0.0;

        var variance = count > 1
            ? latencies.Select(x => Math.Pow(x - average, 2)).Average()
            : 0.0;

        var componentLatencies = new Dictionary<string, double>();
        foreach (var (component, total) in tierState.ComponentTotals)
        {
            componentLatencies[component] = total / tierState.TotalQueries;
        }

        var totalCacheAccesses = userState.EmbeddingCacheHits + userState.EmbeddingCacheMisses +
                                 userState.QueryCacheHits + userState.QueryCacheMisses;
        var totalCacheHits = userState.EmbeddingCacheHits + userState.QueryCacheHits;
        var cacheHitRate = totalCacheAccesses > 0 ? (double)totalCacheHits / totalCacheAccesses : 0.0;

        return new LatencyMetrics
        {
            UserId = userId,
            Tier = tier,
            TotalQueries = tierState.TotalQueries,
            AverageLatencyMs = average,
            MinLatencyMs = min,
            MaxLatencyMs = max,
            P50LatencyMs = p50,
            P95LatencyMs = p95,
            P99LatencyMs = p99,
            VarianceMs = variance,
            CacheHitRate = cacheHitRate,
            EmbeddingCacheHits = userState.EmbeddingCacheHits,
            EmbeddingCacheMisses = userState.EmbeddingCacheMisses,
            QueryCacheHits = userState.QueryCacheHits,
            QueryCacheMisses = userState.QueryCacheMisses,
            BudgetExceededCount = tierState.BudgetExceededCount,
            LatencyBudgetMs = GetLatencyBudget(tier),
            ComponentLatencies = componentLatencies,
            LastUpdated = DateTime.UtcNow
        };
    }

    private sealed class UserLatencyState
    {
        public ConcurrentDictionary<string, TierLatencyState> TierStates { get; } = new();
        public int EmbeddingCacheHits { get; set; }
        public int EmbeddingCacheMisses { get; set; }
        public int QueryCacheHits { get; set; }
        public int QueryCacheMisses { get; set; }

        public TierLatencyState GetOrCreateTierState(string tier)
        {
            return TierStates.GetOrAdd(tier, _ => new TierLatencyState());
        }
    }

    private sealed class TierLatencyState
    {
        public List<double> Latencies { get; } = new();
        public int TotalQueries { get; set; }
        public int BudgetExceededCount { get; set; }
        public Dictionary<string, double> ComponentTotals { get; } = new();
    }
}
