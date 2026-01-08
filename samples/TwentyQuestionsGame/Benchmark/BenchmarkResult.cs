namespace TwentyQuestionsGame.Benchmark;

/// <summary>
/// Comprehensive benchmark result following cognitive science principles
/// and aligned with MemoryBench evaluation dimensions.
/// </summary>
public sealed record GameBenchmarkResult
{
    // === Game Outcome ===
    public required string Secret { get; init; }
    public required bool BetaWon { get; init; }
    public required int RoundsPlayed { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }

    // === Effectiveness Metrics (MemoryBench) ===
    /// <summary>Win rate across multiple games (single game = 1.0 or 0.0)</summary>
    public double SuccessRate => BetaWon ? 1.0 : 0.0;

    /// <summary>Step efficiency: fewer rounds = better</summary>
    public double StepEfficiency => BetaWon ? (20.0 - RoundsPlayed) / 20.0 : 0.0;

    /// <summary>Recall precision: relevant results / total results</summary>
    public required double RecallPrecision { get; init; }

    /// <summary>Number of duplicate questions asked (quality indicator)</summary>
    public required int DuplicateQuestions { get; init; }

    // === Efficiency Metrics ===
    public required int TotalTokens { get; init; }
    public required int BetaTokens { get; init; }
    public required int AlphaTokens { get; init; }
    public required double AvgTokensPerRound { get; init; }

    public required long TotalLlmMs { get; init; }
    public required long TotalRecallMs { get; init; }
    public required long TotalDurationMs { get; init; }

    /// <summary>Memory overhead ratio: recall time / LLM time</summary>
    public double RecallOverheadRatio => TotalLlmMs > 0 ? (double)TotalRecallMs / TotalLlmMs : 0;

    // === 3-Axis Memory Metrics (Memory Indexer specific) ===
    public required TierMetrics TierStats { get; init; }
    public required int MemoryStoreCount { get; init; }
    public required int MemoryRecallCount { get; init; }
    public required int RecallHits { get; init; }
    public required int RecallMisses { get; init; }

    // === Cognitive Science Alignment ===
    /// <summary>
    /// Phase 56: Per-user working memory compliance.
    /// Baddeley's 7±2 model applies to each user (mind) independently.
    /// Each user's Short tier must be ≤9 items.
    /// </summary>
    public bool WorkingMemoryCompliant =>
        TierStats.Alpha.Short <= 9 &&
        TierStats.Beta.Short <= 9;

    /// <summary>
    /// Phase 56: Per-user healthy cognitive memory distribution.
    /// - Buffer minimal (≤2): Items staged briefly before processing
    /// - Short bounded (≤9): Working memory capacity constraint per user
    /// - Long has critical items (≥1): Persistent episodic storage
    /// Note: S > L is valid when working memory is active (Q&A reasoning)
    /// </summary>
    public bool HealthyTierFlow =>
        TierStats.Alpha.Buffer <= 2 &&
        TierStats.Alpha.Short <= 9 &&
        TierStats.Beta.Buffer <= 2 &&
        TierStats.Beta.Short <= 9 &&
        TierStats.LongCount >= 1;
}

/// <summary>
/// Memory distribution across tiers (Atkinson-Shiffrin model alignment)
/// Phase 56: Enhanced with per-user metrics for cognitive compliance.
/// </summary>
public sealed record TierMetrics
{
    /// <summary>T0: Sensory buffer (should be low - items promoted quickly)</summary>
    public required int BufferCount { get; init; }

    /// <summary>T1: Working memory (Baddeley 7±2 capacity)</summary>
    public required int ShortCount { get; init; }

    /// <summary>T2: Episodic memory (session-level events)</summary>
    public required int LongCount { get; init; }

    /// <summary>T3: Semantic memory (long-term facts)</summary>
    public required int ArchiveCount { get; init; }

    /// <summary>Number of tier promotions during game</summary>
    public required int PromotionCount { get; init; }

    /// <summary>Phase 56: Per-user tier distribution for cognitive compliance.</summary>
    public required PerUserTierMetrics Alpha { get; init; }

    /// <summary>Phase 56: Per-user tier distribution for cognitive compliance.</summary>
    public required PerUserTierMetrics Beta { get; init; }

    public int Total => BufferCount + ShortCount + LongCount + ArchiveCount;
}

/// <summary>
/// Phase 56: Per-user tier distribution metrics.
/// Each user (agent) has its own working memory capacity (Baddeley's 7±2).
/// </summary>
public sealed record PerUserTierMetrics
{
    public required int Buffer { get; init; }
    public required int Short { get; init; }
    public required int Long { get; init; }
    public required int Archive { get; init; }
    public int Total => Buffer + Short + Long + Archive;
}

/// <summary>
/// Aggregated results from multiple benchmark runs
/// </summary>
public sealed record AggregateBenchmarkResult
{
    public required int TotalGames { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }

    // === Aggregate Effectiveness ===
    public double WinRate => TotalGames > 0 ? (double)Wins / TotalGames : 0;
    public required double AvgRoundsToWin { get; init; }
    public required double AvgRecallPrecision { get; init; }

    // === Aggregate Efficiency ===
    public required double AvgTokensPerGame { get; init; }
    public required double AvgLlmMs { get; init; }
    public required double AvgRecallMs { get; init; }
    public required double AvgRecallOverhead { get; init; }

    // === Aggregate Memory Health ===
    public required double WorkingMemoryComplianceRate { get; init; }
    public required double HealthyTierFlowRate { get; init; }

    // === Individual Results ===
    public required IReadOnlyList<GameBenchmarkResult> Games { get; init; }
}
