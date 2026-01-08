using System.Text.Json;
using System.Text.Json.Serialization;
using TwentyQuestionsGame.Game;

namespace TwentyQuestionsGame.Benchmark;

/// <summary>
/// Runs multiple game iterations and aggregates benchmark results.
/// </summary>
public sealed class BenchmarkRunner(
    GameRunner gameRunner,
    Func<Task> resetGameState)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Run benchmark with specified number of iterations.
    /// </summary>
    public async Task<AggregateBenchmarkResult> RunAsync(int iterations, CancellationToken ct = default)
    {
        var results = new List<GameBenchmarkResult>();

        Console.WriteLine($"\n{'═'.ToString().PadRight(70, '═')}");
        Console.WriteLine($"  BENCHMARK MODE: Running {iterations} game(s)");
        Console.WriteLine($"{'═'.ToString().PadRight(70, '═')}\n");

        for (int i = 0; i < iterations; i++)
        {
            if (ct.IsCancellationRequested) break;

            Console.WriteLine($"\n[BENCHMARK] Game {i + 1}/{iterations}");
            Console.WriteLine(new string('-', 50));

            // Reset game state for new game
            await resetGameState();

            // Run single game
            var startTime = DateTime.UtcNow;
            await gameRunner.RunAsync(ct);
            var endTime = DateTime.UtcNow;

            // Collect results
            var result = gameRunner.GenerateBenchmarkResult(startTime, endTime);
            results.Add(result);

            Console.WriteLine($"[BENCHMARK] Game {i + 1} complete: {(result.BetaWon ? "WIN" : "LOSS")} in {result.RoundsPlayed} rounds");
        }

        return AggregateResults(results);
    }

    /// <summary>
    /// Output results to console and optionally to JSON file.
    /// </summary>
    public async Task OutputResultsAsync(AggregateBenchmarkResult result, string? outputPath)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 70));
        Console.WriteLine("  BENCHMARK RESULTS");
        Console.WriteLine(new string('═', 70));

        // === Effectiveness ===
        Console.WriteLine("\n📊 EFFECTIVENESS (MemoryBench aligned):");
        Console.WriteLine($"   Win Rate:           {result.WinRate:P1} ({result.Wins}/{result.TotalGames})");
        Console.WriteLine($"   Avg Rounds to Win:  {result.AvgRoundsToWin:F1}");
        Console.WriteLine($"   Avg Recall Precision: {result.AvgRecallPrecision:P1}");

        // === Efficiency ===
        Console.WriteLine("\n⚡ EFFICIENCY:");
        Console.WriteLine($"   Avg Tokens/Game:    {result.AvgTokensPerGame:N0}");
        Console.WriteLine($"   Avg LLM Time:       {result.AvgLlmMs:N0}ms");
        Console.WriteLine($"   Avg Recall Time:    {result.AvgRecallMs:N0}ms");
        Console.WriteLine($"   Recall Overhead:    {result.AvgRecallOverhead:P1}");

        // === Cognitive Science Compliance ===
        Console.WriteLine("\n🧠 COGNITIVE SCIENCE COMPLIANCE:");
        Console.WriteLine($"   Working Memory (7±2): {result.WorkingMemoryComplianceRate:P1}");
        Console.WriteLine($"   Healthy Tier Flow:    {result.HealthyTierFlowRate:P1}");

        // === Per-Game Summary ===
        if (result.Games.Count > 1)
        {
            Console.WriteLine("\n📋 PER-GAME SUMMARY:");
            foreach (var game in result.Games)
            {
                var status = game.BetaWon ? "✅ WIN " : "❌ LOSS";
                Console.WriteLine($"   {status} | R{game.RoundsPlayed:D2} | {game.TotalTokens:N0} tok | {game.Secret}");
            }
        }

        Console.WriteLine(new string('═', 70));

        // === JSON Output ===
        if (!string.IsNullOrEmpty(outputPath))
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"\n📁 Results saved to: {outputPath}");
        }
    }

    private static AggregateBenchmarkResult AggregateResults(List<GameBenchmarkResult> games)
    {
        var wins = games.Count(g => g.BetaWon);
        var winningGames = games.Where(g => g.BetaWon).ToList();

        return new AggregateBenchmarkResult
        {
            TotalGames = games.Count,
            Wins = wins,
            Losses = games.Count - wins,
            AvgRoundsToWin = winningGames.Any() ? winningGames.Average(g => g.RoundsPlayed) : 20,
            AvgRecallPrecision = games.Average(g => g.RecallPrecision),
            AvgTokensPerGame = games.Average(g => g.TotalTokens),
            AvgLlmMs = games.Average(g => g.TotalLlmMs),
            AvgRecallMs = games.Average(g => g.TotalRecallMs),
            AvgRecallOverhead = games.Average(g => g.RecallOverheadRatio),
            WorkingMemoryComplianceRate = games.Average(g => g.WorkingMemoryCompliant ? 1.0 : 0.0),
            HealthyTierFlowRate = games.Average(g => g.HealthyTierFlow ? 1.0 : 0.0),
            Games = games
        };
    }
}
