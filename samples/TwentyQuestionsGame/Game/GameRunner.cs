using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using TwentyQuestionsGame.Agents;
using TwentyQuestionsGame.Benchmark;
using TwentyQuestionsGame.Game;

namespace TwentyQuestionsGame.Game;

/// <summary>
/// Orchestrates the 20 Questions game loop.
/// </summary>
public sealed class GameRunner(
    AlphaAgent alpha,
    BetaAgent beta,
    GameState state,
    IMemoryPrimitives memoryPrimitives,
    IMemoryStore memoryStore)
{
    private readonly List<RoundMetrics> _roundMetrics = [];
    private string? _winningGuess;
    private string? _detectedSecret;
    private int _duplicateCount;

    public IReadOnlyList<RoundMetrics> RoundMetrics => _roundMetrics;

    public async Task RunAsync(CancellationToken ct = default)
    {
        GameConsole.WriteSystem("\n🎮 Game starting! Alpha will think of a secret on Round 1...\n");

        try
        {
            while (!state.IsGameOver && state.CurrentRound <= GameConfiguration.MaxRounds)
            {
                await RunRoundAsync(ct);

                if (!state.IsGameOver)
                {
                    state.NextRound();
                }
            }

            PrintGameResult();
        }
        catch (Exception ex)
        {
            GameConsole.WriteError($"\n❌ GAME ERROR: {ex.GetType().Name}");
            GameConsole.WriteError($"   Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                GameConsole.WriteError($"   Inner: {ex.InnerException.Message}");
            }
            GameConsole.WriteError($"   Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()}");
            throw;
        }
    }

    private async Task RunRoundAsync(CancellationToken ct)
    {
        var roundStart = DateTime.UtcNow;
        GameConsole.WriteRoundHeader(state.CurrentRound, GameConfiguration.MaxRounds);

        // Beta's turn: ask a question
        GameConsole.WriteBeta($"Thinking... (last response: \"{Truncate(state.LastAlphaResponse, 40)}\")");

        BetaQuestionResult betaResult;
        try
        {
            betaResult = await beta.GenerateQuestionAsync(
                state.LastAlphaResponse,
                state.CurrentRound,
                state.QuestionHistory,
                ct);
        }
        catch (Exception ex)
        {
            GameConsole.WriteError($"   ❌ Beta failed: {ex.Message}");
            throw;
        }

        GameConsole.WriteBeta($">>> {betaResult.Question}");
        GameConsole.WriteStats("⏱️ LLM", $"{betaResult.LatencyMs}ms | 🔧 Tool calls: {betaResult.ToolCallIterations}");

        // Warn if duplicate question detected
        if (betaResult.IsDuplicate)
        {
            _duplicateCount++;
            GameConsole.WriteWarning($"   ⚠️ Duplicate detected! Similar to R{betaResult.DuplicateOfRound} (similarity: {betaResult.SimilarityScore:P0})");
        }

        // Note if early guess made
        if (betaResult.IsEarlyGuess)
        {
            GameConsole.WriteSuccess($"   🎯 Early guess! Beta is highly confident on Round {state.CurrentRound}");
        }

        state.RecordBetaQuestion(betaResult.Question);

        // Alpha's turn: answer the question (on Round 1, also chooses secret)
        Console.WriteLine();
        if (state.CurrentRound == 1)
        {
            GameConsole.WriteAlpha("Choosing secret & answering...");
        }
        else
        {
            GameConsole.WriteAlpha("Recalling secret & answering...");
        }

        var alphaResult = await alpha.AnswerAsync(
            betaResult.Question,
            state.CurrentRound,
            ct);

        GameConsole.WriteAlpha($">>> {alphaResult.Answer}");
        GameConsole.WriteStats("⏱️ LLM", $"{alphaResult.LatencyMs}ms | 🎯 IsGuess: {alphaResult.IsGuess}");

        state.RecordAlphaResponse(alphaResult.Answer);

        // Check for game end - use code-level verification, not LLM judgment
        if (alphaResult.IsGuess)
        {
            var isCorrect = await VerifyGuessAsync(betaResult.Question, ct);

            if (isCorrect)
            {
                _winningGuess = betaResult.Question;
                GameConsole.WriteSuccess("   ✅ Guess verified correct!");
            }
            else if (alphaResult.GuessCorrect)
            {
                // Alpha said correct but code verification failed - log warning
                GameConsole.WriteWarning("   ⚠️ Alpha said correct but code verification failed");
            }
            else if (!isCorrect && !alphaResult.GuessCorrect)
            {
                // Both agree it's wrong - verify we're not missing a match
                var secret = await GetAlphaSecretAsync(ct);
                if (secret != null)
                {
                    GameConsole.WriteSystem($"   📝 Secret was: \"{secret}\"");
                }
            }

            state.EndGame(betaWins: isCorrect);
        }

        // Store Q&A pair in Beta's memory for next round
        await StoreQAPairAsync(betaResult.Question, alphaResult.Answer, ct);

        // Record metrics
        _roundMetrics.Add(new RoundMetrics
        {
            Round = state.CurrentRound,
            BetaQuestion = betaResult.Question,
            AlphaAnswer = alphaResult.Answer,
            BetaLatencyMs = betaResult.LatencyMs,
            AlphaLatencyMs = alphaResult.LatencyMs,
            BetaRecallMs = betaResult.MemoryRecallMs,
            AlphaRecallMs = alphaResult.MemoryRecallMs,
            BetaTokens = betaResult.PromptTokens + betaResult.CompletionTokens,
            AlphaTokens = alphaResult.PromptTokens + alphaResult.CompletionTokens,
            TotalDurationMs = (long)(DateTime.UtcNow - roundStart).TotalMilliseconds
        });
    }

    private void PrintGameResult()
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 70));

        if (state.BetaWon)
        {
            GameConsole.WriteSuccess($"🎉 BETA WINS in round {state.CurrentRound}!");
            if (_winningGuess != null)
            {
                GameConsole.WriteSuccess($"   Winning guess: \"{_winningGuess}\"");
            }
        }
        else
        {
            GameConsole.WriteAlpha("🏆 ALPHA WINS! Beta couldn't guess the secret in 20 rounds.");
        }

        // Print statistics
        var totalTokens = _roundMetrics.Sum(r => r.BetaTokens + r.AlphaTokens);
        var totalLlmLatency = _roundMetrics.Sum(r => r.BetaLatencyMs + r.AlphaLatencyMs);
        var totalRecallTime = _roundMetrics.Sum(r => r.BetaRecallMs + r.AlphaRecallMs);
        var totalDuration = _roundMetrics.Sum(r => r.TotalDurationMs);

        Console.WriteLine();
        GameConsole.WriteSystem("📊 Game Statistics:");
        GameConsole.WriteSystem($"   Rounds played: {_roundMetrics.Count}");
        GameConsole.WriteSystem($"   Total tokens: {totalTokens:N0}");
        GameConsole.WriteSystem($"   LLM time: {totalLlmLatency:N0}ms");
        GameConsole.WriteSystem($"   Memory Recall time: {totalRecallTime:N0}ms");
        GameConsole.WriteSystem($"   Total time: {totalDuration:N0}ms");
        
        // Per-agent breakdown
        var betaTokens = _roundMetrics.Sum(r => r.BetaTokens);
        var alphaTokens = _roundMetrics.Sum(r => r.AlphaTokens);
        var betaLlm = _roundMetrics.Sum(r => r.BetaLatencyMs);
        var alphaLlm = _roundMetrics.Sum(r => r.AlphaLatencyMs);
        var betaRecall = _roundMetrics.Sum(r => r.BetaRecallMs);
        var alphaRecall = _roundMetrics.Sum(r => r.AlphaRecallMs);
        
        Console.WriteLine();
        GameConsole.WriteSystem("   Agent Breakdown:");
        GameConsole.WriteBeta($"     Tokens: {betaTokens:N0} | LLM: {betaLlm:N0}ms | Recall: {betaRecall:N0}ms");
        GameConsole.WriteAlpha($"     Tokens: {alphaTokens:N0} | LLM: {alphaLlm:N0}ms | Recall: {alphaRecall:N0}ms");

        // Phase 49: Print tier statistics for cognitive compliance verification
        // Phase 56: Enhanced with per-user tier distribution
        var tierStats = GetTierMetricsAsync().GetAwaiter().GetResult();
        Console.WriteLine();
        GameConsole.WriteSystem("🧠 Cognitive Memory Tier Distribution:");
        GameConsole.WriteSystem($"   Total: Buffer={tierStats.BufferCount}, Short={tierStats.ShortCount}, Long={tierStats.LongCount}, Archive={tierStats.ArchiveCount}");
        GameConsole.WriteAlpha($"   Alpha: B={tierStats.Alpha.Buffer}, S={tierStats.Alpha.Short}, L={tierStats.Alpha.Long}, A={tierStats.Alpha.Archive}");
        GameConsole.WriteBeta($"   Beta:  B={tierStats.Beta.Buffer}, S={tierStats.Beta.Short}, L={tierStats.Beta.Long}, A={tierStats.Beta.Archive}");

        // Phase 56: Per-user cognitive compliance check (correct Baddeley's 7±2 model)
        // Each user (mind) has its own working memory capacity, not shared globally.
        // - WorkingMemory: Each user's Short tier must be within 7±2 (5-9 items)
        // - HealthyFlow: Per-user: Buffer minimal (≤2), Short bounded (≤9), Long has items (≥0)
        var alphaWorkingMemoryOk = tierStats.Alpha.Short <= 9;
        var betaWorkingMemoryOk = tierStats.Beta.Short <= 9;
        var workingMemoryOk = alphaWorkingMemoryOk && betaWorkingMemoryOk;

        var alphaHealthyFlow = tierStats.Alpha.Buffer <= 2 && tierStats.Alpha.Short <= 9;
        var betaHealthyFlow = tierStats.Beta.Buffer <= 2 && tierStats.Beta.Short <= 9;
        var hasLongTermMemory = tierStats.LongCount >= 1;
        var healthyFlow = alphaHealthyFlow && betaHealthyFlow && hasLongTermMemory;

        Console.WriteLine();
        GameConsole.WriteSystem("✅ Cognitive Compliance (Phase 56: Per-User):");
        GameConsole.WriteSystem($"   WorkingMemory(≤9): {(workingMemoryOk ? "✓ PASS" : "✗ FAIL")} (Alpha.S={tierStats.Alpha.Short}≤9:{(alphaWorkingMemoryOk ? "✓" : "✗")}, Beta.S={tierStats.Beta.Short}≤9:{(betaWorkingMemoryOk ? "✓" : "✗")})");
        GameConsole.WriteSystem($"   HealthyTierFlow: {(healthyFlow ? "✓ PASS" : "✗ FAIL")} (α.B≤2:{tierStats.Alpha.Buffer}, α.S≤9:{tierStats.Alpha.Short}, β.B≤2:{tierStats.Beta.Buffer}, β.S≤9:{tierStats.Beta.Short}, L≥1:{tierStats.LongCount})");

        Console.WriteLine(new string('═', 70));
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Stores Q&A pair in Beta's memory.
    /// </summary>
    private async Task StoreQAPairAsync(string question, string answer, CancellationToken ct)
    {
        var content = $"Round {state.CurrentRound}: Q=\"{question}\" → A=\"{answer}\"";

        await memoryPrimitives.EncodeAsync(new EncodeRequest
        {
            UserId = GameConfiguration.BetaUserId,
            SessionId = GameConfiguration.BetaSessionId,
            Content = content,
            ImportanceScore = 0.99f,
            Scope = Scope.Session,
            Tier = Tier.Short
        }, ct);
    }

    /// <summary>
    /// Retrieves Alpha's stored secret from memory.
    /// </summary>
    private async Task<string?> GetAlphaSecretAsync(CancellationToken ct)
    {
        var results = await memoryPrimitives.RetrieveAsync(new RetrieveRequest
        {
            UserId = GameConfiguration.AlphaUserId,
            SessionId = GameConfiguration.AlphaSessionId,
            Query = "MY_SECRET",
            Limit = 5,
            MinScore = 0.5f
        }, ct);

        // Find the MY_SECRET memory
        var secretMemory = results.FirstOrDefault(r =>
            r.Memory.Content.Contains("MY_SECRET:", StringComparison.OrdinalIgnoreCase));

        if (secretMemory == null) return null;

        // Extract secret from "MY_SECRET: The Eiffel Tower"
        var content = secretMemory.Memory.Content;
        var colonIndex = content.IndexOf(':');
        return colonIndex >= 0 ? content[(colonIndex + 1)..].Trim() : null;
    }

    /// <summary>
    /// Verifies Beta's guess against Alpha's stored secret.
    /// Uses normalized string comparison to handle case/article variations.
    /// </summary>
    private async Task<bool> VerifyGuessAsync(string betaQuestion, CancellationToken ct)
    {
        var secret = await GetAlphaSecretAsync(ct);
        if (secret == null)
        {
            GameConsole.WriteWarning("   ⚠️ Could not retrieve Alpha's secret for verification");
            return false;
        }

        // Track secret for benchmark metrics
        _detectedSecret = secret;

        var normalizedSecret = NormalizeGuess(secret);
        var normalizedGuess = NormalizeGuess(betaQuestion);

        GameConsole.WriteSystem($"   🔍 Verifying: \"{normalizedGuess}\" vs \"{normalizedSecret}\"");

        // Check for match (flexible matching)
        return string.Equals(normalizedSecret, normalizedGuess, StringComparison.OrdinalIgnoreCase) ||
               normalizedSecret.Contains(normalizedGuess, StringComparison.OrdinalIgnoreCase) ||
               normalizedGuess.Contains(normalizedSecret, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a guess or secret for comparison.
    /// Removes articles, prefixes, punctuation, and converts to lowercase.
    /// </summary>
    private static string NormalizeGuess(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var normalized = input.Trim().ToLowerInvariant();

        // Remove common prefixes from guesses
        var prefixes = new[]
        {
            "my final guess is:",
            "my final guess is",
            "my guess is:",
            "my guess is",
            "is it",
            "it is",
            "it's"
        };

        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..].Trim();
            }
        }

        // Remove articles (a, an, the)
        normalized = Regex.Replace(normalized, @"^(a|an|the)\s+", "", RegexOptions.IgnoreCase);

        // Remove trailing punctuation
        normalized = normalized.TrimEnd('?', '.', '!', ',');

        return normalized.Trim();
    }

    /// <summary>
    /// Generates benchmark result after game completion.
    /// </summary>
    public GameBenchmarkResult GenerateBenchmarkResult(DateTime startTime, DateTime endTime)
    {
        var totalTokens = _roundMetrics.Sum(r => r.BetaTokens + r.AlphaTokens);
        var betaTokens = _roundMetrics.Sum(r => r.BetaTokens);
        var alphaTokens = _roundMetrics.Sum(r => r.AlphaTokens);
        var totalLlmMs = _roundMetrics.Sum(r => r.BetaLatencyMs + r.AlphaLatencyMs);
        var totalRecallMs = _roundMetrics.Sum(r => r.BetaRecallMs + r.AlphaRecallMs);
        var totalDurationMs = _roundMetrics.Sum(r => r.TotalDurationMs);

        // Get tier counts from memory store
        var tierStats = GetTierMetricsAsync().GetAwaiter().GetResult();

        // Calculate recall precision (simplified: ratio of useful recalls)
        var recallHits = _roundMetrics.Count(r => r.BetaRecallMs > 0);
        var recallMisses = _roundMetrics.Count - recallHits;

        return new GameBenchmarkResult
        {
            Secret = _detectedSecret ?? "unknown",
            BetaWon = state.BetaWon,
            RoundsPlayed = _roundMetrics.Count,
            StartTime = startTime,
            EndTime = endTime,
            RecallPrecision = recallHits > 0 ? (double)recallHits / _roundMetrics.Count : 0,
            DuplicateQuestions = _duplicateCount,
            TotalTokens = totalTokens,
            BetaTokens = betaTokens,
            AlphaTokens = alphaTokens,
            AvgTokensPerRound = _roundMetrics.Count > 0 ? (double)totalTokens / _roundMetrics.Count : 0,
            TotalLlmMs = totalLlmMs,
            TotalRecallMs = totalRecallMs,
            TotalDurationMs = totalDurationMs,
            TierStats = tierStats,
            MemoryStoreCount = tierStats.Total,
            MemoryRecallCount = recallHits + recallMisses,
            RecallHits = recallHits,
            RecallMisses = recallMisses
        };
    }

    /// <summary>
    /// Gets tier distribution metrics from memory store.
    /// Phase 56: Enhanced with per-user metrics for cognitive compliance.
    /// </summary>
    private async Task<TierMetrics> GetTierMetricsAsync()
    {
        var betaMemories = await memoryStore.GetAllAsync(
            GameConfiguration.BetaUserId,
            new MemoryFilterOptions { SessionId = GameConfiguration.BetaSessionId });

        var alphaMemories = await memoryStore.GetAllAsync(
            GameConfiguration.AlphaUserId,
            new MemoryFilterOptions { SessionId = GameConfiguration.AlphaSessionId });

        var allMemories = betaMemories.Concat(alphaMemories).ToList();

        // Phase 56: Calculate per-user tier metrics for cognitive compliance
        var alphaMetrics = new PerUserTierMetrics
        {
            Buffer = alphaMemories.Count(m => m.Tier == Tier.Buffer),
            Short = alphaMemories.Count(m => m.Tier == Tier.Short),
            Long = alphaMemories.Count(m => m.Tier == Tier.Long),
            Archive = alphaMemories.Count(m => m.Tier == Tier.Archive)
        };

        var betaMetrics = new PerUserTierMetrics
        {
            Buffer = betaMemories.Count(m => m.Tier == Tier.Buffer),
            Short = betaMemories.Count(m => m.Tier == Tier.Short),
            Long = betaMemories.Count(m => m.Tier == Tier.Long),
            Archive = betaMemories.Count(m => m.Tier == Tier.Archive)
        };

        return new TierMetrics
        {
            BufferCount = allMemories.Count(m => m.Tier == Tier.Buffer),
            ShortCount = allMemories.Count(m => m.Tier == Tier.Short),
            LongCount = allMemories.Count(m => m.Tier == Tier.Long),
            ArchiveCount = allMemories.Count(m => m.Tier == Tier.Archive),
            PromotionCount = 0,  // Would need promotion tracking to measure
            Alpha = alphaMetrics,
            Beta = betaMetrics
        };
    }
}

/// <summary>
/// Metrics for a single round.
/// </summary>
public sealed record RoundMetrics
{
    public int Round { get; init; }
    public string BetaQuestion { get; init; } = "";
    public string AlphaAnswer { get; init; } = "";
    public long BetaLatencyMs { get; init; }
    public long AlphaLatencyMs { get; init; }
    public long BetaRecallMs { get; init; }
    public long AlphaRecallMs { get; init; }
    public int BetaTokens { get; init; }
    public int AlphaTokens { get; init; }
    public long TotalDurationMs { get; init; }
}
