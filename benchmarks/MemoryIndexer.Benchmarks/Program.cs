using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using MemoryIndexer.Benchmarks;

namespace MemoryIndexer.Benchmarks;

/// <summary>
/// Benchmark runner for Memory Indexer performance testing.
/// Supports CI integration with JSON export for regression detection.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Check for CI mode
        var isCi = args.Contains("--ci") || Environment.GetEnvironmentVariable("CI") == "true";

        if (isCi)
        {
            RunCiBenchmarks(args);
            return;
        }

        // Run all benchmarks if no arguments provided
        if (args.Length == 0)
        {
            Console.WriteLine("Memory Indexer Performance Benchmarks");
            Console.WriteLine("=====================================");
            Console.WriteLine();
            Console.WriteLine("Available benchmark suites:");
            Console.WriteLine("  1. MemoryOperationsBenchmark - Core memory operations");
            Console.WriteLine("  2. TieredMemoryBenchmark - 4-tier workflow benchmarks");
            Console.WriteLine("  3. TierPromotionBenchmark - Tier promotion pipeline");
            Console.WriteLine("  4. ConcurrencyBenchmark - Concurrency and load tests");
            Console.WriteLine();
            Console.WriteLine("Running all benchmark suites...");
            Console.WriteLine();

            BenchmarkRunner.Run<MemoryOperationsBenchmark>();
            BenchmarkRunner.Run<TieredMemoryBenchmark>();
            BenchmarkRunner.Run<TierPromotionBenchmark>();
            // ConcurrencyBenchmark takes longer due to parameterization
            // BenchmarkRunner.Run<ConcurrencyBenchmark>();
        }
        else
        {
            // Allow running specific benchmarks via command line
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }

    /// <summary>
    /// Run benchmarks in CI mode with JSON export for regression detection.
    /// </summary>
    private static void RunCiBenchmarks(string[] args)
    {
        Console.WriteLine("Running benchmarks in CI mode...");
        Console.WriteLine();

        var config = DefaultConfig.Instance
            .WithArtifactsPath("BenchmarkDotNet.Artifacts")
            .AddExporter(JsonExporter.Full)
            .AddExporter(MarkdownExporter.GitHub);

        // Filter out --ci flag
        var benchArgs = args.Where(a => a != "--ci").ToArray();

        if (benchArgs.Length == 0 || benchArgs.Contains("--quick"))
        {
            // Quick CI run: only core operations
            Console.WriteLine("Quick CI mode: Running MemoryOperationsBenchmark only");
            BenchmarkRunner.Run<MemoryOperationsBenchmark>(config);
        }
        else if (benchArgs.Contains("--full"))
        {
            // Full CI run: all benchmarks
            Console.WriteLine("Full CI mode: Running all benchmark suites");
            BenchmarkRunner.Run<MemoryOperationsBenchmark>(config);
            BenchmarkRunner.Run<TieredMemoryBenchmark>(config);
            BenchmarkRunner.Run<TierPromotionBenchmark>(config);
        }
        else
        {
            // Specific benchmarks via filter
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchArgs, config);
        }

        Console.WriteLine();
        Console.WriteLine("CI benchmark run complete. Results exported to BenchmarkDotNet.Artifacts/");
    }
}
