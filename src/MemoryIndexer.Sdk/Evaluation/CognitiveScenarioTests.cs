using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Cognitive scenario tests based on memory science principles.
/// Tests memory system behavior in realistic cognitive scenarios.
/// </summary>
public class CognitiveScenarioTests
{
    private readonly IMemoryService _memoryService;
    private readonly IEvaluationService _evaluationService;

    public CognitiveScenarioTests(
        IMemoryService memoryService,
        IEvaluationService evaluationService)
    {
        _memoryService = memoryService;
        _evaluationService = evaluationService;
    }

    /// <summary>
    /// False Memory Test: Tests system's ability to handle conflicting information.
    /// Scenario: Store initial fact → many intervening memories → conflicting update → recall
    /// Expected: System should either detect contradiction or prioritize newer information.
    /// </summary>
    public async Task<FalseMemoryTestResult> RunFalseMemoryTestAsync(
        FalseMemoryTestConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new FalseMemoryTestResult
        {
            Config = config,
            StartedAt = DateTimeOffset.UtcNow
        };

        var userId = config.UserId ?? $"false-memory-test-{Guid.NewGuid():N}";
        var sessionId = config.SessionId ?? $"fm-session-{Guid.NewGuid():N}";

        try
        {
            // Phase 1: Store initial fact
            await _memoryService.RememberAsync(
                userId,
                sessionId,
                config.InitialFact,
                cancellationToken: cancellationToken);
            result.InitialFactStored = true;

            // Phase 2: Store intervening memories (noise)
            for (var i = 0; i < config.InterveningMemoryCount; i++)
            {
                var noise = config.InterveningMemories?.ElementAtOrDefault(i)
                    ?? $"Intervening memory {i + 1}: General conversation about unrelated topic.";

                await _memoryService.RememberAsync(
                    userId,
                    sessionId,
                    noise,
                    cancellationToken: cancellationToken);
            }
            result.InterveningMemoriesStored = config.InterveningMemoryCount;

            // Phase 3: Store conflicting update
            await _memoryService.RememberAsync(
                userId,
                sessionId,
                config.ConflictingFact,
                cancellationToken: cancellationToken);
            result.ConflictingFactStored = true;

            // Phase 4: Recall and check which fact is returned
            var recalled = await _memoryService.RecallAsync(
                userId,
                sessionId,
                config.RecallQuery,
                config.RecallLimit,
                cancellationToken: cancellationToken);

            var recalledContents = recalled.AllMemories().Select(m => m.Content).ToList();

            // Check if initial fact is in results
            result.InitialFactRecalled = recalledContents.Any(c =>
                ContainsSimilar(c, config.InitialFact));

            // Check if conflicting fact is in results
            result.ConflictingFactRecalled = recalledContents.Any(c =>
                ContainsSimilar(c, config.ConflictingFact));

            // Determine outcome
            if (result.ConflictingFactRecalled && !result.InitialFactRecalled)
            {
                // System correctly prioritized newer information
                result.Outcome = FalseMemoryOutcome.NewerPrioritized;
                result.Success = true;
            }
            else if (result.ConflictingFactRecalled && result.InitialFactRecalled)
            {
                // Both facts returned - system detected contradiction
                result.Outcome = FalseMemoryOutcome.ContradictionDetected;
                result.Success = true;  // This is also acceptable behavior
            }
            else if (result.InitialFactRecalled && !result.ConflictingFactRecalled)
            {
                // Older fact prioritized - potentially problematic
                result.Outcome = FalseMemoryOutcome.OlderPrioritized;
                result.Success = false;
            }
            else
            {
                // Neither fact recalled
                result.Outcome = FalseMemoryOutcome.NeitherRecalled;
                result.Success = false;
            }

            result.RecalledMemories = recalledContents;
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
    /// Cross-Session Retention Test: Tests persistence of Archive tier data.
    /// Scenario: Store important fact (User scope) → End session → New session → Recall
    /// Expected: Archive tier memories should persist across sessions.
    /// </summary>
    public async Task<CrossSessionTestResult> RunCrossSessionRetentionTestAsync(
        CrossSessionTestConfig config,
        CancellationToken cancellationToken = default)
    {
        var result = new CrossSessionTestResult
        {
            Config = config,
            StartedAt = DateTimeOffset.UtcNow
        };

        var userId = config.UserId ?? $"cross-session-test-{Guid.NewGuid():N}";
        var session1Id = config.Session1Id ?? $"cs-session1-{Guid.NewGuid():N}";
        var session2Id = config.Session2Id ?? $"cs-session2-{Guid.NewGuid():N}";

        try
        {
            // Phase 1: Store user profile in Session 1
            foreach (var fact in config.UserProfileFacts)
            {
                await _memoryService.RememberAsync(
                    userId,
                    session1Id,
                    fact,
                    cancellationToken: cancellationToken);
            }
            result.FactsStoredInSession1 = config.UserProfileFacts.Count;

            // Phase 2: End Session 1
            await _memoryService.EndSessionAsync(userId, session1Id, cancellationToken);
            result.Session1Ended = true;

            // Phase 3: Start Session 2 and recall user profile
            var recalled = await _memoryService.RecallAsync(
                userId,
                session2Id,  // New session
                config.RecallQuery,
                config.RecallLimit,
                cancellationToken: cancellationToken);

            var recalledContents = recalled.AllMemories().Select(m => m.Content).ToList();

            // Phase 4: Check retention
            var factsRetained = 0;
            foreach (var fact in config.UserProfileFacts)
            {
                if (recalledContents.Any(c => ContainsSimilar(c, fact)))
                {
                    factsRetained++;
                }
            }

            result.FactsRetainedInSession2 = factsRetained;
            result.RetentionRate = config.UserProfileFacts.Count > 0
                ? (double)factsRetained / config.UserProfileFacts.Count
                : 0;

            result.Success = result.RetentionRate >= config.MinRetentionRate;
            result.RecalledMemories = recalledContents;
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
    /// Runs a comprehensive cognitive scenario test suite.
    /// </summary>
    public async Task<CognitiveScenarioSuite> RunTestSuiteAsync(
        CognitiveScenarioSuiteConfig config,
        CancellationToken cancellationToken = default)
    {
        var results = new CognitiveScenarioSuite
        {
            StartedAt = DateTimeOffset.UtcNow
        };

        // Run False Memory Test
        if (config.FalseMemoryConfig != null)
        {
            results.FalseMemoryResult = await RunFalseMemoryTestAsync(
                config.FalseMemoryConfig,
                cancellationToken);
        }

        // Run Cross-Session Test
        if (config.CrossSessionConfig != null)
        {
            results.CrossSessionResult = await RunCrossSessionRetentionTestAsync(
                config.CrossSessionConfig,
                cancellationToken);
        }

        results.CompletedAt = DateTimeOffset.UtcNow;

        // Compute overall success
        var tests = new[] { results.FalseMemoryResult?.Success, results.CrossSessionResult?.Success }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        results.OverallSuccess = tests.Count > 0 && tests.All(x => x);
        results.SuccessRate = tests.Count > 0
            ? (double)tests.Count(x => x) / tests.Count
            : 0;

        return results;
    }

    private static bool ContainsSimilar(string content, string target)
    {
        // Simple similarity check - contains or high overlap
        return content.Contains(target, StringComparison.OrdinalIgnoreCase) ||
               target.Contains(content, StringComparison.OrdinalIgnoreCase) ||
               ComputeOverlap(content, target) > 0.5;
    }

    private static double ComputeOverlap(string a, string b)
    {
        var wordsA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();
        var wordsB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        if (wordsA.Count == 0 || wordsB.Count == 0) return 0;

        var intersection = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();

        return (double)intersection / union;  // Jaccard similarity
    }
}

#region Configuration Models

/// <summary>
/// Configuration for False Memory Test.
/// </summary>
public record FalseMemoryTestConfig
{
    /// <summary>
    /// Initial fact to store (e.g., "User likes apples").
    /// </summary>
    public required string InitialFact { get; init; }

    /// <summary>
    /// Conflicting fact to store later (e.g., "User is allergic to apples").
    /// </summary>
    public required string ConflictingFact { get; init; }

    /// <summary>
    /// Query to recall the fact (e.g., "User food preferences").
    /// </summary>
    public required string RecallQuery { get; init; }

    /// <summary>
    /// Number of intervening memories between initial and conflicting facts.
    /// Default: 100 (simulates ~100 turns of conversation).
    /// </summary>
    public int InterveningMemoryCount { get; init; } = 100;

    /// <summary>
    /// Optional custom intervening memories.
    /// </summary>
    public IReadOnlyList<string>? InterveningMemories { get; init; }

    /// <summary>
    /// Number of memories to recall.
    /// </summary>
    public int RecallLimit { get; init; } = 5;

    /// <summary>
    /// User ID for test isolation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session ID for test isolation.
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Configuration for Cross-Session Retention Test.
/// </summary>
public record CrossSessionTestConfig
{
    /// <summary>
    /// User profile facts to store in Session 1.
    /// </summary>
    public required IReadOnlyList<string> UserProfileFacts { get; init; }

    /// <summary>
    /// Query to recall profile in Session 2.
    /// </summary>
    public required string RecallQuery { get; init; }

    /// <summary>
    /// Minimum retention rate for test to pass (0.0 to 1.0).
    /// Default: 0.8 (80% retention required).
    /// </summary>
    public double MinRetentionRate { get; init; } = 0.8;

    /// <summary>
    /// Number of memories to recall.
    /// </summary>
    public int RecallLimit { get; init; } = 10;

    /// <summary>
    /// User ID for test isolation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Session 1 ID.
    /// </summary>
    public string? Session1Id { get; init; }

    /// <summary>
    /// Session 2 ID.
    /// </summary>
    public string? Session2Id { get; init; }
}

/// <summary>
/// Configuration for cognitive scenario test suite.
/// </summary>
public record CognitiveScenarioSuiteConfig
{
    /// <summary>
    /// False Memory Test configuration.
    /// </summary>
    public FalseMemoryTestConfig? FalseMemoryConfig { get; init; }

    /// <summary>
    /// Cross-Session Retention Test configuration.
    /// </summary>
    public CrossSessionTestConfig? CrossSessionConfig { get; init; }
}

#endregion

#region Result Models

/// <summary>
/// Possible outcomes of False Memory Test.
/// </summary>
public enum FalseMemoryOutcome
{
    /// <summary>
    /// Newer (conflicting) fact was prioritized - expected behavior.
    /// </summary>
    NewerPrioritized,

    /// <summary>
    /// Both facts returned - contradiction detection.
    /// </summary>
    ContradictionDetected,

    /// <summary>
    /// Older (initial) fact was prioritized - potential issue.
    /// </summary>
    OlderPrioritized,

    /// <summary>
    /// Neither fact was recalled - retrieval failure.
    /// </summary>
    NeitherRecalled
}

/// <summary>
/// Result of False Memory Test.
/// </summary>
public record FalseMemoryTestResult
{
    public FalseMemoryTestConfig Config { get; init; } = null!;
    public bool Success { get; set; }
    public FalseMemoryOutcome Outcome { get; set; }
    public bool InitialFactStored { get; set; }
    public bool ConflictingFactStored { get; set; }
    public int InterveningMemoriesStored { get; set; }
    public bool InitialFactRecalled { get; set; }
    public bool ConflictingFactRecalled { get; set; }
    public IReadOnlyList<string> RecalledMemories { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Result of Cross-Session Retention Test.
/// </summary>
public record CrossSessionTestResult
{
    public CrossSessionTestConfig Config { get; init; } = null!;
    public bool Success { get; set; }
    public int FactsStoredInSession1 { get; set; }
    public bool Session1Ended { get; set; }
    public int FactsRetainedInSession2 { get; set; }
    public double RetentionRate { get; set; }
    public IReadOnlyList<string> RecalledMemories { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Aggregated results from cognitive scenario test suite.
/// </summary>
public record CognitiveScenarioSuite
{
    public FalseMemoryTestResult? FalseMemoryResult { get; set; }
    public CrossSessionTestResult? CrossSessionResult { get; set; }
    public bool OverallSuccess { get; set; }
    public double SuccessRate { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

#endregion
