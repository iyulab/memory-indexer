using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MemoryIndexer.Benchmarks;

/// <summary>
/// Benchmarks for concurrent memory operations and load testing.
/// Tests: Multi-user scenarios, parallel operations, throughput under load.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConcurrencyBenchmark
{
    private ServiceProvider? _serviceProvider;
    private MemoryService? _memoryService;
    private IMemoryStore? _memoryStore;
    private const string SessionId = "benchmark-session";

    [Params(10, 50, 100)]
    public int ConcurrentOperations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMemoryIndexer(options =>
        {
            options.Storage.Type = StorageType.InMemory;
            options.Embedding.Provider = EmbeddingProvider.Mock;
        });

        _serviceProvider = services.BuildServiceProvider();
        _memoryService = _serviceProvider.GetRequiredService<MemoryService>();
        _memoryStore = _serviceProvider.GetRequiredService<IMemoryStore>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    // ==================== Parallel Store Operations ====================

    [Benchmark(Description = "Parallel: Store N memories (same user)")]
    public async Task ParallelStoreSameUser()
    {
        var userId = "single-user";
        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(i =>
            _memoryService!.StoreAsync(
                userId,
                $"Parallel memory content {i} for load testing",
                MemoryType.Episodic,
                SessionId,
                0.5f));

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Parallel: Store N memories (N users)")]
    public async Task ParallelStoreMultipleUsers()
    {
        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(i =>
            _memoryService!.StoreAsync(
                $"user-{i}",
                $"Memory content for user {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f));

        await Task.WhenAll(tasks);
    }

    // ==================== Parallel Recall Operations ====================

    [Benchmark(Description = "Parallel: Recall N queries (same user)")]
    public async Task ParallelRecallSameUser()
    {
        var userId = "recall-user";

        // Seed some data first
        for (int i = 0; i < 20; i++)
        {
            await _memoryService!.StoreAsync(
                userId,
                $"Seed memory {i} for recall test with various keywords topic{i % 5}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }

        var queries = new[] { "topic0", "topic1", "topic2", "topic3", "topic4" };
        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(i =>
            _memoryService!.RecallAsync(
                userId,
                queries[i % queries.Length],
                limit: 5));

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Parallel: Recall N queries (N users)")]
    public async Task ParallelRecallMultipleUsers()
    {
        // Seed data for multiple users
        for (int u = 0; u < ConcurrentOperations; u++)
        {
            await _memoryService!.StoreAsync(
                $"recall-user-{u}",
                $"Seed data for user {u}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }

        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(i =>
            _memoryService!.RecallAsync(
                $"recall-user-{i}",
                "seed data",
                limit: 5));

        await Task.WhenAll(tasks);
    }

    // ==================== Mixed Read/Write Workload ====================

    [Benchmark(Description = "Mixed: N parallel (70% read, 30% write)")]
    public async Task MixedReadWriteWorkload()
    {
        var userId = "mixed-workload-user";
        var random = new Random(42); // Deterministic for benchmarking

        // Seed initial data
        for (int i = 0; i < 10; i++)
        {
            await _memoryService!.StoreAsync(
                userId,
                $"Initial data {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }

        var tasks = new List<Task>();
        for (int i = 0; i < ConcurrentOperations; i++)
        {
            if (random.NextDouble() < 0.7)
            {
                // 70% reads
                tasks.Add(_memoryService!.RecallAsync(userId, $"query {i % 10}", limit: 5));
            }
            else
            {
                // 30% writes
                tasks.Add(_memoryService!.StoreAsync(
                    userId,
                    $"New memory {i}",
                    MemoryType.Episodic,
                    SessionId,
                    0.5f));
            }
        }

        await Task.WhenAll(tasks);
    }

    // ==================== Vector Search Load ====================

    [Benchmark(Description = "Parallel: Vector search N queries")]
    public async Task ParallelVectorSearch()
    {
        var userId = "vector-search-user";
        var queryEmbedding = new float[768];

        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(_ =>
            _memoryStore!.SearchAsync(
                queryEmbedding,
                new MemorySearchOptions
                {
                    UserId = userId,
                    Limit = 10
                }));

        await Task.WhenAll(tasks);
    }

    // ==================== Throughput Tests ====================

    [Benchmark(Description = "Throughput: Sequential baseline")]
    public async Task ThroughputSequential()
    {
        var userId = "throughput-user";
        for (int i = 0; i < ConcurrentOperations; i++)
        {
            await _memoryService!.StoreAsync(
                userId,
                $"Sequential memory {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }
    }

    [Benchmark(Description = "Throughput: Batched parallel")]
    public async Task ThroughputBatchedParallel()
    {
        var userId = "throughput-batch-user";
        const int batchSize = 10;

        for (int batch = 0; batch < ConcurrentOperations / batchSize; batch++)
        {
            var tasks = Enumerable.Range(0, batchSize).Select(i =>
                _memoryService!.StoreAsync(
                    userId,
                    $"Batch {batch} memory {i}",
                    MemoryType.Episodic,
                    SessionId,
                    0.5f));

            await Task.WhenAll(tasks);
        }
    }

    // ==================== Contention Tests ====================

    [Benchmark(Description = "Contention: N updates to same memory")]
    public async Task ContentionSameMemory()
    {
        var userId = "contention-user";

        // Create a single memory
        var memory = await _memoryService!.StoreAsync(
            userId,
            "Original content",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        // Parallel updates (tests thread-safety)
        var tasks = Enumerable.Range(0, ConcurrentOperations).Select(i =>
            _memoryService!.UpdateContentAsync(memory.Id, $"Updated content {i}"));

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Contention: N deletes + stores")]
    public async Task ContentionDeleteAndStore()
    {
        var userId = "contention-ds-user";
        var memoryIds = new ConcurrentBag<Guid>();

        // Create memories
        for (int i = 0; i < ConcurrentOperations; i++)
        {
            var m = await _memoryService!.StoreAsync(
                userId,
                $"To delete {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
            memoryIds.Add(m.Id);
        }

        // Parallel delete and store (use hardDelete to prevent GC pressure from soft-deleted entries)
        var deleteTasks = memoryIds.Take(ConcurrentOperations / 2)
            .Select(id => _memoryService!.DeleteAsync(id, hardDelete: true));
        var storeTasks = Enumerable.Range(0, ConcurrentOperations / 2)
            .Select(i => _memoryService!.StoreAsync(
                userId,
                $"New memory {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f));

        await Task.WhenAll(deleteTasks.Concat(storeTasks.Select(t => t.ContinueWith(_ => true))));
    }
}
