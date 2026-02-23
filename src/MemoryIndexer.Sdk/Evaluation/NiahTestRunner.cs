using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Needle In A Haystack (NIAH) test runner.
/// Tests memory system's ability to recall specific information
/// from large context without full history injection.
/// Reference: https://github.com/gkamradt/LLMTest_NeedleInAHaystack
/// </summary>
public class NiahTestRunner
{
    private readonly IMemoryService _memoryService;
    private readonly IEvaluationService _evaluationService;

    public NiahTestRunner(
        IMemoryService memoryService,
        IEvaluationService evaluationService)
    {
        _memoryService = memoryService;
        _evaluationService = evaluationService;
    }

    /// <summary>
    /// Runs a NIAH test with the specified configuration.
    /// </summary>
    /// <param name="config">Test configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test result with metrics.</returns>
    public async Task<NiahTestResult> RunTestAsync(
        NiahTestConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new NiahTestResult
        {
            Config = config,
            StartedAt = DateTimeOffset.UtcNow
        };

        var userId = config.UserId ?? $"niah-test-{Guid.NewGuid():N}";
        var sessionId = config.SessionId ?? $"niah-session-{Guid.NewGuid():N}";

        try
        {
            // Phase 1: Generate haystack content
            var haystackSegments = GenerateHaystack(config);
            result.HaystackTokens = haystackSegments.Sum(s => EstimateTokens(s));

            // Phase 2: Insert needle at specified position
            var needlePosition = CalculateNeedlePosition(haystackSegments.Count, config.NeedlePosition);
            haystackSegments.Insert(needlePosition, config.Needle);

            // Phase 3: Store all segments as memories
            var storeStopwatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var segment in haystackSegments)
            {
                await _memoryService.RememberAsync(
                    userId,
                    sessionId,
                    segment,
                    cancellationToken: cancellationToken);
            }
            storeStopwatch.Stop();
            result.StoreLatencyMs = storeStopwatch.ElapsedMilliseconds;
            result.MemoriesStored = haystackSegments.Count;

            // Phase 4: Recall using needle query
            var recallStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var recalled = await _memoryService.RecallAsync(
                userId,
                sessionId,
                config.NeedleQuery,
                config.RecallLimit,
                cancellationToken: cancellationToken);
            recallStopwatch.Stop();
            result.RecallLatencyMs = recallStopwatch.ElapsedMilliseconds;

            // Phase 5: Validate recall success
            var recalledContents = recalled.AllMemories().Select(m => m.Content).ToList();
            result.NeedleFound = recalledContents.Any(c =>
                c.Contains(config.Needle, StringComparison.OrdinalIgnoreCase) ||
                config.Needle.Contains(c, StringComparison.OrdinalIgnoreCase));

            result.NeedleRank = recalledContents
                .Select((c, i) => new { Content = c, Index = i })
                .FirstOrDefault(x =>
                    x.Content.Contains(config.Needle, StringComparison.OrdinalIgnoreCase) ||
                    config.Needle.Contains(x.Content, StringComparison.OrdinalIgnoreCase))
                ?.Index + 1;

            // Phase 6: Compute metrics
            var recalledTokens = recalled.AllMemories().Sum(m => EstimateTokens(m.Content));
            result.RecalledTokens = recalledTokens;

            result.Ccr = _evaluationService.ComputeCcr(recalledTokens, result.HaystackTokens);
            result.RecallAtK = _evaluationService.ComputeRecallAtK(
                result.NeedleFound ? 1 : 0,
                config.RecallLimit);

            result.Success = result.NeedleFound && result.Ccr < config.TargetCcr;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            result.CompletedAt = DateTimeOffset.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Runs multiple NIAH tests with varying needle positions.
    /// </summary>
    /// <param name="baseConfig">Base configuration.</param>
    /// <param name="positions">Needle positions to test (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated test results.</returns>
    public async Task<NiahTestSuite> RunTestSuiteAsync(
        NiahTestConfig baseConfig,
        IEnumerable<double>? positions = null,
        CancellationToken cancellationToken = default)
    {
        positions ??= [0.25, 0.50, 0.75];
        var results = new List<NiahTestResult>();

        foreach (var position in positions)
        {
            var config = baseConfig with
            {
                NeedlePosition = position,
                UserId = $"{baseConfig.UserId ?? "niah"}-pos{position:F2}",
                SessionId = $"{baseConfig.SessionId ?? "session"}-pos{position:F2}"
            };

            var result = await RunTestAsync(config, cancellationToken);
            results.Add(result);
        }

        return new NiahTestSuite
        {
            Results = results,
            OverallSuccessRate = results.Count > 0
                ? (double)results.Count(r => r.Success) / results.Count
                : 0,
            AverageCcr = results.Count > 0
                ? results.Average(r => r.Ccr)
                : 0,
            AverageRecallLatencyMs = results.Count > 0
                ? results.Average(r => r.RecallLatencyMs)
                : 0
        };
    }

    /// <summary>
    /// Runs a multi-needle NIAH test (RULER-style).
    /// Tests memory system's ability to recall multiple pieces of information.
    /// </summary>
    /// <param name="config">Multi-needle test configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Multi-needle test result.</returns>
    public async Task<MultiNeedleTestResult> RunMultiNeedleTestAsync(
        MultiNeedleTestConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiNeedleTestResult
        {
            Config = config,
            TotalNeedles = config.Needles.Count,
            StartedAt = DateTimeOffset.UtcNow
        };

        var userId = config.UserId ?? $"multi-niah-{Guid.NewGuid():N}";
        var sessionId = config.SessionId ?? $"multi-niah-session-{Guid.NewGuid():N}";
        var random = new Random(config.RandomSeed ?? 42);

        try
        {
            // Phase 1: Generate haystack content
            var haystackSegments = GenerateMultiNeedleHaystack(config);
            result.HaystackTokens = haystackSegments.Sum(s => EstimateTokens(s));

            // Phase 2: Insert needles at specified or random positions
            var needlePositions = new List<(int Index, NeedleInfo Needle)>();
            foreach (var needle in config.Needles)
            {
                var position = needle.Position ?? random.NextDouble();
                var index = CalculateNeedlePosition(haystackSegments.Count, position);
                needlePositions.Add((index, needle));
            }

            // Sort by index descending to insert without shifting issues
            foreach (var (index, needle) in needlePositions.OrderByDescending(x => x.Index))
            {
                haystackSegments.Insert(index, needle.Content);
            }

            // Phase 3: Store all segments as memories
            var storeStopwatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var segment in haystackSegments)
            {
                await _memoryService.RememberAsync(
                    userId,
                    sessionId,
                    segment,
                    cancellationToken: cancellationToken);
            }
            storeStopwatch.Stop();
            result.StoreLatencyMs = storeStopwatch.ElapsedMilliseconds;
            result.MemoriesStored = haystackSegments.Count;

            // Phase 4: Recall based on strategy
            var needleResults = new List<NeedleRecoveryResult>();
            var allRecalledContents = new HashSet<string>();
            long totalRecallLatency = 0;

            switch (config.QueryStrategy)
            {
                case MultiNeedleQueryStrategy.CombinedQuery:
                    {
                        // Combine all needle queries into one
                        var combinedQuery = string.Join(" ", config.Needles.Select(n => n.Query));
                        var recallStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var recalled = await _memoryService.RecallAsync(
                            userId,
                            sessionId,
                            combinedQuery,
                            config.RecallLimit * config.Needles.Count, // Increase limit for multiple needles
                            cancellationToken: cancellationToken);
                        recallStopwatch.Stop();
                        totalRecallLatency = recallStopwatch.ElapsedMilliseconds;

                        var recalledContents = recalled.AllMemories().Select(m => m.Content).ToList();
                        allRecalledContents = recalledContents.ToHashSet();

                        // Check each needle
                        foreach (var needle in config.Needles)
                        {
                            var found = recalledContents.Any(c =>
                                c.Contains(needle.Content, StringComparison.OrdinalIgnoreCase) ||
                                needle.Content.Contains(c, StringComparison.OrdinalIgnoreCase));

                            var rank = recalledContents
                                .Select((c, i) => new { Content = c, Index = i })
                                .FirstOrDefault(x =>
                                    x.Content.Contains(needle.Content, StringComparison.OrdinalIgnoreCase) ||
                                    needle.Content.Contains(x.Content, StringComparison.OrdinalIgnoreCase))
                                ?.Index + 1;

                            needleResults.Add(new NeedleRecoveryResult
                            {
                                NeedleId = needle.Id,
                                Found = found,
                                Rank = rank,
                                Query = combinedQuery,
                                RecallLatencyMs = totalRecallLatency / config.Needles.Count
                            });
                        }
                        break;
                    }

                case MultiNeedleQueryStrategy.SeparateQueries:
                case MultiNeedleQueryStrategy.SequentialQueries:
                    {
                        // Query each needle separately
                        foreach (var needle in config.Needles)
                        {
                            var recallStopwatch = System.Diagnostics.Stopwatch.StartNew();
                            var recalled = await _memoryService.RecallAsync(
                                userId,
                                sessionId,
                                needle.Query,
                                config.RecallLimit,
                                cancellationToken: cancellationToken);
                            recallStopwatch.Stop();
                            var queryLatency = recallStopwatch.ElapsedMilliseconds;
                            totalRecallLatency += queryLatency;

                            var recalledContents = recalled.AllMemories().Select(m => m.Content).ToList();
                            foreach (var content in recalledContents)
                            {
                                allRecalledContents.Add(content);
                            }

                            var found = recalledContents.Any(c =>
                                c.Contains(needle.Content, StringComparison.OrdinalIgnoreCase) ||
                                needle.Content.Contains(c, StringComparison.OrdinalIgnoreCase));

                            var rank = recalledContents
                                .Select((c, i) => new { Content = c, Index = i })
                                .FirstOrDefault(x =>
                                    x.Content.Contains(needle.Content, StringComparison.OrdinalIgnoreCase) ||
                                    needle.Content.Contains(x.Content, StringComparison.OrdinalIgnoreCase))
                                ?.Index + 1;

                            needleResults.Add(new NeedleRecoveryResult
                            {
                                NeedleId = needle.Id,
                                Found = found,
                                Rank = rank,
                                Query = needle.Query,
                                RecallLatencyMs = queryLatency
                            });
                        }
                        break;
                    }
            }

            result.TotalRecallLatencyMs = totalRecallLatency;
            result.NeedleResults = needleResults;
            result.NeedlesFound = needleResults.Count(r => r.Found);

            // Phase 5: Compute metrics
            var recalledTokens = allRecalledContents.Sum(EstimateTokens);
            result.RecalledTokens = recalledTokens;
            result.Ccr = _evaluationService.ComputeCcr(recalledTokens, result.HaystackTokens);

            // Success: recovery rate meets target AND CCR is acceptable
            result.Success = result.RecoveryRate >= config.MinNeedleRecoveryRate &&
                             result.Ccr <= config.TargetCcr;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            result.CompletedAt = DateTimeOffset.UtcNow;
        }

        return result;
    }

    private List<string> GenerateMultiNeedleHaystack(MultiNeedleTestConfig config)
    {
        var segments = new List<string>();
        var random = new Random(config.RandomSeed ?? 42);

        if (config.HaystackContent != null)
        {
            var words = config.HaystackContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentSegment = new List<string>();
            var currentTokens = 0;

            foreach (var word in words)
            {
                currentSegment.Add(word);
                currentTokens++;

                if (currentTokens >= config.SegmentSize)
                {
                    segments.Add(string.Join(" ", currentSegment));
                    currentSegment.Clear();
                    currentTokens = 0;
                }
            }

            if (currentSegment.Count > 0)
            {
                segments.Add(string.Join(" ", currentSegment));
            }
        }
        else
        {
            var totalTokens = 0;
            while (totalTokens < config.TargetHaystackTokens)
            {
                var segment = GenerateSyntheticSegment(random, config.SegmentSize);
                segments.Add(segment);
                totalTokens += EstimateTokens(segment);
            }
        }

        return segments;
    }

    private List<string> GenerateHaystack(NiahTestConfig config)
    {
        var segments = new List<string>();
        var random = new Random(config.RandomSeed ?? 42);

        // Use provided haystack content or generate synthetic
        if (config.HaystackContent != null)
        {
            // Split provided content into segments
            var words = config.HaystackContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentSegment = new List<string>();
            var currentTokens = 0;

            foreach (var word in words)
            {
                currentSegment.Add(word);
                currentTokens++;

                if (currentTokens >= config.SegmentSize)
                {
                    segments.Add(string.Join(" ", currentSegment));
                    currentSegment.Clear();
                    currentTokens = 0;
                }
            }

            if (currentSegment.Count > 0)
            {
                segments.Add(string.Join(" ", currentSegment));
            }
        }
        else
        {
            // Generate synthetic haystack
            var totalTokens = 0;
            while (totalTokens < config.TargetHaystackTokens)
            {
                var segment = GenerateSyntheticSegment(random, config.SegmentSize);
                segments.Add(segment);
                totalTokens += EstimateTokens(segment);
            }
        }

        return segments;
    }

    private static string GenerateSyntheticSegment(Random random, int targetTokens)
    {
        // Generate realistic-looking but meaningless text
        var topics = new[]
        {
            "The meeting discussed various aspects of the project timeline and resource allocation.",
            "Analysis of quarterly results showed steady growth in key performance indicators.",
            "The team reviewed the technical specifications and implementation requirements.",
            "Documentation updates were completed for the latest release version.",
            "User feedback indicated several areas for improvement in the interface design.",
            "Security protocols were evaluated against industry best practices.",
            "Performance benchmarks demonstrated significant improvements over baseline.",
            "Integration testing revealed minor issues that require attention.",
            "The roadmap was updated to reflect changing priorities and deadlines.",
            "Code review highlighted opportunities for refactoring and optimization."
        };

        var words = new List<string>();
        while (words.Count < targetTokens)
        {
            var topic = topics[random.Next(topics.Length)];
            words.AddRange(topic.Split(' '));
        }

        return string.Join(" ", words.Take(targetTokens));
    }

    private static int CalculateNeedlePosition(int segmentCount, double position)
    {
        // Position is 0.0 to 1.0, where 0.25 = 25% into content
        var index = (int)(segmentCount * position);
        return Math.Clamp(index, 0, segmentCount);
    }

    private int EstimateTokens(string text)
    {
        // Simple approximation: ~4 characters per token
        return (text.Length + 3) / 4;
    }
}

/// <summary>
/// Configuration for NIAH test.
/// </summary>
public record NiahTestConfig
{
    /// <summary>
    /// The needle (target information) to find.
    /// </summary>
    public required string Needle { get; init; }

    /// <summary>
    /// Query to use when recalling the needle.
    /// </summary>
    public required string NeedleQuery { get; init; }

    /// <summary>
    /// Position to insert needle (0.0 = start, 0.5 = middle, 1.0 = end).
    /// </summary>
    public double NeedlePosition { get; init; } = 0.5;

    /// <summary>
    /// Target haystack size in tokens.
    /// Default: 100,000 tokens.
    /// </summary>
    public int TargetHaystackTokens { get; init; } = 100_000;

    /// <summary>
    /// Optional pre-generated haystack content (e.g., from Project Gutenberg).
    /// </summary>
    public string? HaystackContent { get; init; }

    /// <summary>
    /// Size of each segment in tokens.
    /// </summary>
    public int SegmentSize { get; init; } = 100;

    /// <summary>
    /// Number of memories to recall.
    /// </summary>
    public int RecallLimit { get; init; } = 5;

    /// <summary>
    /// Target CCR threshold for success (default: 1% = 0.01).
    /// </summary>
    public double TargetCcr { get; init; } = 0.01;

    /// <summary>
    /// User ID for test isolation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID for test isolation.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Random seed for reproducible tests.
    /// </summary>
    public int? RandomSeed { get; init; }
}

/// <summary>
/// Result of a single NIAH test.
/// </summary>
public class NiahTestResult
{
    /// <summary>
    /// Test configuration used.
    /// </summary>
    public NiahTestConfig Config { get; set; } = null!;

    /// <summary>
    /// Whether the test passed (needle found AND CCR below target).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether the needle was found in recalled memories.
    /// </summary>
    public bool NeedleFound { get; set; }

    /// <summary>
    /// Rank at which needle was found (1-based, null if not found).
    /// </summary>
    public int? NeedleRank { get; set; }

    /// <summary>
    /// Context Compression Ratio achieved.
    /// </summary>
    public double Ccr { get; set; }

    /// <summary>
    /// Recall@K efficiency achieved.
    /// </summary>
    public double RecallAtK { get; set; }

    /// <summary>
    /// Total tokens in haystack.
    /// </summary>
    public long HaystackTokens { get; set; }

    /// <summary>
    /// Tokens in recalled content.
    /// </summary>
    public long RecalledTokens { get; set; }

    /// <summary>
    /// Number of memories stored.
    /// </summary>
    public int MemoriesStored { get; set; }

    /// <summary>
    /// Store operation latency in milliseconds.
    /// </summary>
    public long StoreLatencyMs { get; set; }

    /// <summary>
    /// Recall operation latency in milliseconds.
    /// </summary>
    public long RecallLatencyMs { get; set; }

    /// <summary>
    /// Test start timestamp.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Test completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Error message if test failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Aggregated results from multiple NIAH tests.
/// </summary>
public record NiahTestSuite
{
    /// <summary>
    /// Individual test results.
    /// </summary>
    public IReadOnlyList<NiahTestResult> Results { get; init; } = [];

    /// <summary>
    /// Overall success rate (0.0 to 1.0).
    /// </summary>
    public double OverallSuccessRate { get; init; }

    /// <summary>
    /// Average CCR across all tests.
    /// </summary>
    public double AverageCcr { get; init; }

    /// <summary>
    /// Average recall latency across all tests.
    /// </summary>
    public double AverageRecallLatencyMs { get; init; }

    /// <summary>
    /// Suite generation timestamp.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

#region Multi-Needle Test Types (RULER-style)

/// <summary>
/// Configuration for multi-needle NIAH test.
/// Inspired by RULER benchmark: https://arxiv.org/abs/2404.06654
/// Tests memory system's ability to recall multiple pieces of information.
/// </summary>
public record MultiNeedleTestConfig
{
    /// <summary>
    /// Multiple needles (target information) to find.
    /// </summary>
    public required IReadOnlyList<NeedleInfo> Needles { get; init; }

    /// <summary>
    /// Target haystack size in tokens.
    /// Default: 100,000 tokens.
    /// </summary>
    public int TargetHaystackTokens { get; init; } = 100_000;

    /// <summary>
    /// Optional pre-generated haystack content.
    /// </summary>
    public string? HaystackContent { get; init; }

    /// <summary>
    /// Size of each segment in tokens.
    /// </summary>
    public int SegmentSize { get; init; } = 100;

    /// <summary>
    /// Number of memories to recall per query.
    /// </summary>
    public int RecallLimit { get; init; } = 10;

    /// <summary>
    /// Target CCR threshold for success (default: 5% = 0.05).
    /// Higher for multi-needle since we need more context.
    /// </summary>
    public double TargetCcr { get; init; } = 0.05;

    /// <summary>
    /// Minimum percentage of needles that must be found for success.
    /// Default: 100% (all needles must be found).
    /// </summary>
    public double MinNeedleRecoveryRate { get; init; } = 1.0;

    /// <summary>
    /// Whether to use a single combined query or separate queries per needle.
    /// </summary>
    public MultiNeedleQueryStrategy QueryStrategy { get; init; } = MultiNeedleQueryStrategy.CombinedQuery;

    /// <summary>
    /// User ID for test isolation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID for test isolation.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Random seed for reproducible tests.
    /// </summary>
    public int? RandomSeed { get; init; }
}

/// <summary>
/// Information about a single needle in multi-needle test.
/// </summary>
public record NeedleInfo
{
    /// <summary>
    /// Unique identifier for this needle.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// The needle content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Query to find this needle.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Position to insert needle (0.0 to 1.0).
    /// If null, random position is used.
    /// </summary>
    public double? Position { get; init; }
}

/// <summary>
/// Strategy for querying multiple needles.
/// </summary>
public enum MultiNeedleQueryStrategy
{
    /// <summary>
    /// Use a single combined query for all needles.
    /// </summary>
    CombinedQuery,

    /// <summary>
    /// Use separate queries for each needle, then merge results.
    /// </summary>
    SeparateQueries,

    /// <summary>
    /// Sequential queries, one needle at a time.
    /// </summary>
    SequentialQueries
}

/// <summary>
/// Result of a multi-needle NIAH test.
/// </summary>
public class MultiNeedleTestResult
{
    /// <summary>
    /// Test configuration used.
    /// </summary>
    public MultiNeedleTestConfig Config { get; set; } = null!;

    /// <summary>
    /// Whether the test passed (recovery rate >= target).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Total number of needles.
    /// </summary>
    public int TotalNeedles { get; set; }

    /// <summary>
    /// Number of needles found.
    /// </summary>
    public int NeedlesFound { get; set; }

    /// <summary>
    /// Recovery rate (0.0 to 1.0).
    /// </summary>
    public double RecoveryRate => TotalNeedles > 0 ? (double)NeedlesFound / TotalNeedles : 0;

    /// <summary>
    /// Results for each individual needle.
    /// </summary>
    public IReadOnlyList<NeedleRecoveryResult> NeedleResults { get; set; } = [];

    /// <summary>
    /// Context Compression Ratio achieved.
    /// </summary>
    public double Ccr { get; set; }

    /// <summary>
    /// Total tokens in haystack.
    /// </summary>
    public long HaystackTokens { get; set; }

    /// <summary>
    /// Tokens in recalled content.
    /// </summary>
    public long RecalledTokens { get; set; }

    /// <summary>
    /// Number of memories stored.
    /// </summary>
    public int MemoriesStored { get; set; }

    /// <summary>
    /// Store operation latency in milliseconds.
    /// </summary>
    public long StoreLatencyMs { get; set; }

    /// <summary>
    /// Total recall latency across all queries.
    /// </summary>
    public long TotalRecallLatencyMs { get; set; }

    /// <summary>
    /// Test start timestamp.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Test completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Error message if test failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Recovery result for a single needle.
/// </summary>
public class NeedleRecoveryResult
{
    /// <summary>
    /// Needle identifier.
    /// </summary>
    public string NeedleId { get; init; } = string.Empty;

    /// <summary>
    /// Whether this needle was found.
    /// </summary>
    public bool Found { get; init; }

    /// <summary>
    /// Rank at which needle was found (1-based, null if not found).
    /// </summary>
    public int? Rank { get; init; }

    /// <summary>
    /// Query used to search for this needle.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Recall latency for this specific query.
    /// </summary>
    public long RecallLatencyMs { get; init; }
}

#endregion
