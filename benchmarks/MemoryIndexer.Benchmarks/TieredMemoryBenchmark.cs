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
/// Benchmarks for 4-tier memory architecture integrated workflows
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class TieredMemoryBenchmark
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

    // Workflow: Store → Recall (Tests Recently → Working → Session → Recall)
    [Benchmark(Description = "Workflow: Store → Recall")]
    public async Task WorkflowStoreAndRecall()
    {
        await _memoryService!.StoreAsync(
            UserId,
            "Test memory for workflow benchmarking",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        await _memoryService.RecallAsync(
            UserId,
            "workflow benchmark",
            limit: 5);
    }

    // Workflow: Multiple stores → GetAll (Tests batching and retrieval)
    [Benchmark(Description = "Workflow: Batch store → GetAll")]
    public async Task WorkflowBatchStoreAndGetAll()
    {
        for (int i = 0; i < 5; i++)
        {
            await _memoryService!.StoreAsync(
                UserId,
                $"Memory {i} for batch workflow test",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }

        await _memoryService!.GetAllAsync(
            UserId,
            new MemoryFilterOptions { Limit = 10 });
    }

    // Workflow: Store → Update → Recall (Tests update flow)
    [Benchmark(Description = "Workflow: Store → Update → Recall")]
    public async Task WorkflowStoreUpdateRecall()
    {
        var memory = await _memoryService!.StoreAsync(
            UserId,
            "Original content",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        await _memoryService.UpdateContentAsync(
            memory.Id,
            "Updated content for workflow test");

        await _memoryService.RecallAsync(
            UserId,
            "updated content",
            limit: 5);
    }

    // Storage layer: Direct store with embedding
    [Benchmark(Description = "Storage: Store with embedding")]
    public async Task StorageStoreMemory()
    {
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SessionId = SessionId,
            Content = "Test content for storage benchmark",
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            CreatedAt = DateTime.UtcNow,
            Embedding = new ReadOnlyMemory<float>(new float[768])
        };

        await _memoryStore!.StoreAsync(memory);
    }

    // Storage layer: Vector search
    [Benchmark(Description = "Storage: Vector search")]
    public async Task StorageVectorSearch()
    {
        var queryEmbedding = new float[768];
        await _memoryStore!.SearchAsync(
            queryEmbedding,
            new MemorySearchOptions
            {
                UserId = UserId,
                SessionId = SessionId,
                Limit = 5
            });
    }

    // Mixed type operations
    [Benchmark(Description = "Workflow: Mixed memory types")]
    public async Task WorkflowMixedTypes()
    {
        await _memoryService!.StoreAsync(UserId, "Episodic memory", MemoryType.Episodic, SessionId, 0.5f);
        await _memoryService.StoreAsync(UserId, "Semantic knowledge", MemoryType.Semantic, SessionId, 0.7f);
        await _memoryService.StoreAsync(UserId, "Procedural process", MemoryType.Procedural, SessionId, 0.6f);

        await _memoryService.RecallAsync(UserId, "memory knowledge process", limit: 5);
    }
}
