using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Observability;

namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Default implementation of evaluation metrics collection service.
/// Integrates with OpenTelemetry for metrics export.
/// </summary>
public class EvaluationService : IEvaluationService
{
    private readonly IMemoryService _memoryService;
    private readonly ConcurrentDictionary<string, TierPromotionLatencyTracker> _latencyTrackers = new();

    // OpenTelemetry metrics
    private static readonly Meter EvaluationMeter = new("MemoryIndexer.Evaluation", "1.0.0");

    private static readonly Histogram<double> CcrHistogram = EvaluationMeter.CreateHistogram<double>(
        "memory.evaluation.ccr",
        "ratio",
        "Context Compression Ratio - recalled_tokens / full_history_tokens");

    private static readonly Histogram<double> RecallAtKHistogram = EvaluationMeter.CreateHistogram<double>(
        "memory.evaluation.recall_at_k",
        "ratio",
        "Recall@K Efficiency - relevant_in_top_k / k");

    private static readonly Histogram<double> RetentionScoreHistogram = EvaluationMeter.CreateHistogram<double>(
        "memory.evaluation.retention_score",
        "ratio",
        "Information Retention Score - correctly_recalled / total_stored");

    private static readonly Histogram<double> TierPromotionHistogram = EvaluationMeter.CreateHistogram<double>(
        "memory.evaluation.tier_promotion_latency",
        "ms",
        "Tier promotion latency in milliseconds");

    private static readonly Counter<long> NiahTestsCounter = EvaluationMeter.CreateCounter<long>(
        "memory.evaluation.niah_tests",
        "tests",
        "Number of NIAH tests executed");

    public EvaluationService(IMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    /// <inheritdoc />
    public async Task<EvaluationMetrics> CollectMetricsAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var tracker = GetOrCreateTracker(userId);

        // Get current memory state via recall
        var context = await _memoryService.RecallAsync(
            userId,
            sessionId,
            "*",  // Wildcard to get general context
            100,
            cancellationToken);

        var memories = context.AllMemories().ToList();
        var recalledTokens = memories.Sum(m => EstimateTokens(m.Content));

        return new EvaluationMetrics
        {
            UserId = userId,
            SessionId = sessionId,
            Timestamp = DateTimeOffset.UtcNow,
            RecalledTokens = recalledTokens,
            FullHistoryTokens = tracker.TotalStoredTokens,
            ContextCompressionRatio = ComputeCcr(recalledTokens, tracker.TotalStoredTokens),
            RecallAtKEfficiency = tracker.LastRecallAtK,
            RecallK = 5,
            TierPromotionLatency = tracker.GetLatency(),
            InformationRetentionScore = tracker.GetRetentionScore(),
            MemoriesStored = tracker.TotalMemoriesStored,
            SuccessfulRecalls = tracker.SuccessfulRecalls,
            TotalRecallAttempts = tracker.TotalRecallAttempts
        };
    }

    /// <inheritdoc />
    public double ComputeCcr(long recalledTokens, long fullHistoryTokens)
    {
        if (fullHistoryTokens <= 0) return 0;
        var ccr = (double)recalledTokens / fullHistoryTokens;

        CcrHistogram.Record(ccr);
        return ccr;
    }

    /// <inheritdoc />
    public double ComputeRecallAtK(int relevantInTopK, int k)
    {
        if (k <= 0) return 0;
        var recallAtK = (double)relevantInTopK / k;

        RecallAtKHistogram.Record(recallAtK);
        return recallAtK;
    }

    /// <inheritdoc />
    public void RecordTierPromotionLatency(string fromTier, string toTier, double latencyMs)
    {
        TierPromotionHistogram.Record(latencyMs, new KeyValuePair<string, object?>("from_tier", fromTier),
            new KeyValuePair<string, object?>("to_tier", toTier));

        // Update all trackers with promotion latency (global metric)
        foreach (var tracker in _latencyTrackers.Values)
        {
            tracker.RecordPromotion(fromTier, toTier, latencyMs);
        }
    }

    /// <inheritdoc />
    public double ComputeRetentionScore(int correctlyRecalled, int totalStored)
    {
        if (totalStored <= 0) return 0;
        var score = (double)correctlyRecalled / totalStored;

        RetentionScoreHistogram.Record(score);
        return score;
    }

    /// <inheritdoc />
    public async Task<CognitiveComplianceMetrics> GetCognitiveComplianceAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        // Get memory distribution across tiers
        var context = await _memoryService.RecallAsync(
            userId,
            sessionId,
            "*",
            100,
            cancellationToken);

        var memories = context.AllMemories().ToList();

        var bufferCount = memories.Count(m => m.Tier == Tier.Buffer);
        var shortCount = memories.Count(m => m.Tier == Tier.Short);
        var longCount = memories.Count(m => m.Tier == Tier.Long);
        var archiveCount = memories.Count(m => m.Tier == Tier.Archive);

        // Working Memory (7±2) compliance: Short tier should have 5-9 items
        var workingMemoryCompliant = shortCount >= 5 && shortCount <= 9;

        // Healthy Tier Flow: Buffer ≤ 2, Short ≤ 9, Long ≥ 1
        var healthyTierFlow = bufferCount <= 2 && shortCount <= 9 && (longCount >= 1 || memories.Count == 0);

        return new CognitiveComplianceMetrics
        {
            WorkingMemoryCompliance = workingMemoryCompliant ? 1.0 : 0.0,
            ShortTierCount = shortCount,
            HealthyTierFlow = healthyTierFlow,
            BufferCount = bufferCount,
            LongCount = longCount,
            ArchiveCount = archiveCount
        };
    }

    /// <inheritdoc />
    public async Task<EvaluationReport> GenerateReportAsync(
        string userId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = await CollectMetricsAsync(userId, sessionId, cancellationToken);
        var cognitive = await GetCognitiveComplianceAsync(userId, sessionId, cancellationToken);

        var observations = new List<string>();

        // Generate observations based on metrics
        if (metrics.ContextCompressionRatio < 0.01)
        {
            observations.Add("Excellent CCR - context is highly compressed (<1%)");
        }
        else if (metrics.ContextCompressionRatio < 0.05)
        {
            observations.Add("Good CCR - context compression is effective (<5%)");
        }
        else
        {
            observations.Add($"CCR of {metrics.ContextCompressionRatio:P1} exceeds target - consider improving retrieval");
        }

        if (metrics.RecallAtKEfficiency >= 0.8)
        {
            observations.Add("High Recall@K efficiency - semantic retrieval is accurate");
        }
        else if (metrics.RecallAtKEfficiency < 0.5)
        {
            observations.Add("Low Recall@K - consider improving embedding model or indexing");
        }

        if (cognitive.WorkingMemoryCompliance > 0.8)
        {
            observations.Add("Working Memory (7±2) compliant - Short tier within optimal range");
        }

        if (!cognitive.HealthyTierFlow)
        {
            observations.Add("Warning: Tier flow is not optimal - check promotion logic");
        }

        // Compute overall score (weighted average)
        var overallScore =
            (1 - Math.Min(metrics.ContextCompressionRatio, 1.0)) * 30 +  // CCR (inverse, 30%)
            metrics.RecallAtKEfficiency * 30 +  // Recall@K (30%)
            metrics.InformationRetentionScore * 20 +  // Retention (20%)
            cognitive.WorkingMemoryCompliance * 10 +  // WM Compliance (10%)
            (cognitive.HealthyTierFlow ? 10 : 0);  // Tier Flow (10%)

        return new EvaluationReport
        {
            Metrics = metrics,
            CognitiveCompliance = cognitive,
            OverallScore = overallScore,
            GeneratedAt = DateTimeOffset.UtcNow,
            Observations = observations
        };
    }

    /// <summary>
    /// Tracks a store operation for metrics.
    /// </summary>
    public void TrackStore(string userId, string content)
    {
        var tracker = GetOrCreateTracker(userId);
        tracker.TotalMemoriesStored++;
        tracker.TotalStoredTokens += EstimateTokens(content);
    }

    /// <summary>
    /// Tracks a recall operation for metrics.
    /// </summary>
    public void TrackRecall(string userId, int relevantFound, int k, bool success)
    {
        var tracker = GetOrCreateTracker(userId);
        tracker.TotalRecallAttempts++;
        if (success) tracker.SuccessfulRecalls++;
        tracker.LastRecallAtK = ComputeRecallAtK(relevantFound, k);
    }

    /// <summary>
    /// Records NIAH test execution.
    /// </summary>
    public static void RecordNiahTest(bool success)
    {
        NiahTestsCounter.Add(1, new KeyValuePair<string, object?>("success", success));
    }

    private TierPromotionLatencyTracker GetOrCreateTracker(string userId)
    {
        return _latencyTrackers.GetOrAdd(userId, _ => new TierPromotionLatencyTracker());
    }

    private static int EstimateTokens(string text)
    {
        // Simple approximation: ~4 characters per token
        return (text.Length + 3) / 4;
    }

    private sealed class TierPromotionLatencyTracker
    {
        public int TotalMemoriesStored { get; set; }
        public long TotalStoredTokens { get; set; }
        public int TotalRecallAttempts { get; set; }
        public int SuccessfulRecalls { get; set; }
        public double LastRecallAtK { get; set; }

        private double _bufferToShortMs;
        private double _shortToLongMs;
        private double _longToArchiveMs;
        private int _bufferToShortCount;
        private int _shortToLongCount;
        private int _longToArchiveCount;

        public void RecordPromotion(string fromTier, string toTier, double latencyMs)
        {
            var key = $"{fromTier}->{toTier}".ToLowerInvariant();

            switch (key)
            {
                case "buffer->short":
                    _bufferToShortMs = (_bufferToShortMs * _bufferToShortCount + latencyMs) / ++_bufferToShortCount;
                    break;
                case "short->long":
                    _shortToLongMs = (_shortToLongMs * _shortToLongCount + latencyMs) / ++_shortToLongCount;
                    break;
                case "long->archive":
                    _longToArchiveMs = (_longToArchiveMs * _longToArchiveCount + latencyMs) / ++_longToArchiveCount;
                    break;
            }
        }

        public TierPromotionLatency GetLatency() => new()
        {
            BufferToShortMs = _bufferToShortMs,
            ShortToLongMs = _shortToLongMs,
            LongToArchiveMs = _longToArchiveMs
        };

        public double GetRetentionScore()
        {
            if (TotalRecallAttempts == 0) return 0;
            return (double)SuccessfulRecalls / TotalRecallAttempts;
        }
    }
}
