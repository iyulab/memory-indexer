using BenchmarkDotNet.Running;
using MemoryIndexer.Benchmarks;

namespace MemoryIndexer.Benchmarks;

/// <summary>
/// Benchmark runner for Memory Indexer performance testing
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Run all benchmarks if no arguments provided
        if (args.Length == 0)
        {
            Console.WriteLine("Memory Indexer Performance Benchmarks");
            Console.WriteLine("=====================================");
            Console.WriteLine();
            Console.WriteLine("Running all benchmark suites...");
            Console.WriteLine();

            BenchmarkRunner.Run<MemoryOperationsBenchmark>();
            BenchmarkRunner.Run<TieredMemoryBenchmark>();
        }
        else
        {
            // Allow running specific benchmarks via command line
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
