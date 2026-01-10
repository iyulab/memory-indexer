using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryIndexer.Benchmarks;

/// <summary>
/// Benchmarks for core memory operations: Store, Recall, Search
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class MemoryOperationsBenchmark
{
    private ServiceProvider? _serviceProvider;
    private MemoryService? _memoryService;
    private IMemoryStore? _memoryStore;
    private const string UserId = "benchmark-user";
    private const string SessionId = "benchmark-session";

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

    [Benchmark(Description = "Store single memory")]
    public async Task<MemoryUnit> StoreMemory()
    {
        return await _memoryService!.StoreAsync(
            UserId,
            "This is a test memory content for benchmarking purposes.",
            MemoryType.Episodic,
            SessionId,
            0.5f);
    }

    [Benchmark(Description = "Store 10 memories in sequence")]
    public async Task Store10Memories()
    {
        for (int i = 0; i < 10; i++)
        {
            await _memoryService!.StoreAsync(
                UserId,
                $"Memory content {i} for batch benchmarking test.",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }
    }

    [Benchmark(Description = "Store 10 memories in parallel")]
    public async Task Store10MemoriesParallel()
    {
        var tasks = Enumerable.Range(0, 10).Select(i =>
            _memoryService!.StoreAsync(
                UserId,
                $"Memory content {i} for parallel benchmarking test.",
                MemoryType.Episodic,
                SessionId,
                0.5f));

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Recall memories (limit 5)")]
    public async Task<IReadOnlyList<MemorySearchResult>> RecallMemories()
    {
        return await _memoryService!.RecallAsync(
            UserId,
            "test query for recall",
            limit: 5);
    }

    [Benchmark(Description = "Recall memories (limit 20)")]
    public async Task<IReadOnlyList<MemorySearchResult>> RecallMemories20()
    {
        return await _memoryService!.RecallAsync(
            UserId,
            "test query for recall",
            limit: 20);
    }

    [Benchmark(Description = "Get all memories (limit 50)")]
    public async Task<IReadOnlyList<MemoryUnit>> GetAllMemories()
    {
        return await _memoryService!.GetAllAsync(
            UserId,
            new MemoryFilterOptions { Limit = 50 });
    }

    [Benchmark(Description = "Get all memories (limit 100)")]
    public async Task<IReadOnlyList<MemoryUnit>> GetAllMemories100()
    {
        return await _memoryService!.GetAllAsync(
            UserId,
            new MemoryFilterOptions { Limit = 100 });
    }

    [Benchmark(Description = "Update memory content")]
    public async Task<bool> UpdateMemory()
    {
        var memory = await _memoryService!.StoreAsync(
            UserId,
            "Original content",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        return await _memoryService.UpdateContentAsync(
            memory.Id,
            "Updated content for benchmarking");
    }

    [Benchmark(Description = "Delete memory")]
    public async Task<bool> DeleteMemory()
    {
        var memory = await _memoryService!.StoreAsync(
            UserId,
            "Content to be deleted",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        // Use hardDelete to actually remove from memory, preventing GC pressure
        // from accumulated soft-deleted entries across benchmark iterations
        return await _memoryService.DeleteAsync(memory.Id, hardDelete: true);
    }
}
