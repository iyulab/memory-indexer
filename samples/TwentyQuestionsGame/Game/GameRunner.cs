using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using TwentyQuestionsGame.Agents;

namespace TwentyQuestionsGame.Game;

/// <summary>
/// Orchestrates the 20 Questions game loop.
/// </summary>
public sealed class GameRunner(
    AlphaAgent alpha,
    BetaAgent beta,
    GameState state,
    IMemoryPrimitives memoryPrimitives)
{
    private readonly List<RoundMetrics> _roundMetrics = [];
    private string? _winningGuess;

    public IReadOnlyList<RoundMetrics> RoundMetrics => _roundMetrics;

    public async Task RunAsync(CancellationToken ct = default)
    {
        GameConsole.WriteSystem("\n🎮 Game starting! Alpha will think of a secret on Round 1...\n");

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

    private async Task RunRoundAsync(CancellationToken ct)
    {
        var roundStart = DateTime.UtcNow;
        GameConsole.WriteRoundHeader(state.CurrentRound, GameConfiguration.MaxRounds);

        // Beta's turn: ask a question
        GameConsole.WriteBeta($"Thinking... (last response: \"{Truncate(state.LastAlphaResponse, 40)}\")");

        var betaResult = await beta.GenerateQuestionAsync(
            state.LastAlphaResponse,
            state.CurrentRound,
            state.QuestionHistory,
            ct);

        GameConsole.WriteBeta($">>> {betaResult.Question}");
        GameConsole.WriteStats("⏱️ LLM", $"{betaResult.LatencyMs}ms | 🔧 Tool calls: {betaResult.ToolCallIterations}");

        // Warn if duplicate question detected
        if (betaResult.IsDuplicate)
        {
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
        var totalLatency = _roundMetrics.Sum(r => r.BetaLatencyMs + r.AlphaLatencyMs);
        var totalDuration = _roundMetrics.Sum(r => r.TotalDurationMs);

        Console.WriteLine();
        GameConsole.WriteSystem("📊 Game Statistics:");
        GameConsole.WriteSystem($"   Rounds played: {_roundMetrics.Count}");
        GameConsole.WriteSystem($"   Total tokens: {totalTokens:N0}");
        GameConsole.WriteSystem($"   LLM time: {totalLatency:N0}ms");
        GameConsole.WriteSystem($"   Total time: {totalDuration:N0}ms");
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
    public int BetaTokens { get; init; }
    public int AlphaTokens { get; init; }
    public long TotalDurationMs { get; init; }
}
