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
/// Benchmarks for 4-tier memory promotion pipeline.
/// Focus: Buffer → Short → Long → Archive operations.
/// Target: Complete pipeline < 50ms async.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class TierPromotionBenchmark
{
    private ServiceProvider? _serviceProvider;
    private MemoryService? _memoryService;
    private IBuffer? _buffer;
    private IShortTermMemory? _shortTermMemory;
    private IArchiveStore? _archiveStore;
    private VirtualContextManager? _vcm;
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
        _buffer = _serviceProvider.GetRequiredService<IBuffer>();
        _shortTermMemory = _serviceProvider.GetRequiredService<IShortTermMemory>();
        _archiveStore = _serviceProvider.GetRequiredService<IArchiveStore>();
        _vcm = _serviceProvider.GetRequiredService<VirtualContextManager>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    // ==================== Buffer (T0) Operations ====================

    [Benchmark(Description = "T0 Buffer: Enqueue")]
    public async Task<SensoryMemory> BufferEnqueue()
    {
        return await _buffer!.EnqueueAsync(
            content: "Test content for buffer benchmarking",
            userId: UserId,
            sessionId: SessionId,
            role: "user");
    }

    [Benchmark(Description = "T0 Buffer: Enqueue + Drain (5 items)")]
    public async Task<IReadOnlyList<SensoryMemory>> BufferEnqueueAndDrain()
    {
        for (int i = 0; i < 5; i++)
        {
            await _buffer!.EnqueueAsync(
                content: $"Content {i}",
                userId: UserId,
                sessionId: SessionId,
                role: "user");
        }
        return await _buffer!.DrainAsync(UserId);
    }

    [Benchmark(Description = "T0 Buffer: Get stats")]
    public SensoryBufferStats BufferGetStats()
    {
        return _buffer!.GetStats(UserId);
    }

    // ==================== Short-Term (T1) Operations ====================

    [Benchmark(Description = "T1 ShortTerm: Promote memory")]
    public async Task<MemoryUnit> ShortTermPromote()
    {
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            SessionId = SessionId,
            Content = "Test content for short-term promotion",
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            CreatedAt = DateTime.UtcNow,
            Embedding = new ReadOnlyMemory<float>(new float[768])
        };
        return await _shortTermMemory!.PromoteAsync(memory);
    }

    [Benchmark(Description = "T1 ShortTerm: Get all")]
    public async Task<IReadOnlyList<MemoryUnit>> ShortTermGetAll()
    {
        return await _shortTermMemory!.GetAllAsync();
    }

    [Benchmark(Description = "T1 ShortTerm: Capacity check")]
    public bool ShortTermCapacityCheck()
    {
        return _shortTermMemory!.IsFull;
    }

    // ==================== Archive (T3) Operations ====================

    [Benchmark(Description = "T3 Archive: Set entry")]
    public async Task<bool> ArchiveSetEntry()
    {
        var entry = new SemanticStoreEntry
        {
            Key = $"test-key-{Guid.NewGuid():N}",
            Value = "Archived knowledge entry for benchmarking",
            Category = SemanticStoreCategory.Preference,
            Confidence = 0.85f,
            CreatedAt = DateTime.UtcNow
        };
        return await _archiveStore!.SetAsync(UserId, entry);
    }

    [Benchmark(Description = "T3 Archive: Get all")]
    public async Task<IReadOnlyList<SemanticStoreEntry>> ArchiveGetAll()
    {
        return await _archiveStore!.GetAllAsync(UserId);
    }

    [Benchmark(Description = "T3 Archive: Get stats")]
    public SemanticStoreStats ArchiveGetStats()
    {
        return _archiveStore!.GetStats(UserId);
    }

    // ==================== End-to-End Pipeline ====================

    [Benchmark(Description = "Pipeline: Store → Short → Recall")]
    public async Task PipelineStoreToRecall()
    {
        // Store goes through Buffer → Short
        await _memoryService!.StoreAsync(
            UserId,
            "Test memory for pipeline benchmark",
            MemoryType.Episodic,
            SessionId,
            0.5f);

        // Recall retrieves from available tiers
        await _memoryService.RecallAsync(UserId, "pipeline test", limit: 5);
    }

    [Benchmark(Description = "Pipeline: Multi-store → Batch recall")]
    public async Task PipelineMultiStoreRecall()
    {
        // Store multiple memories
        for (int i = 0; i < 5; i++)
        {
            await _memoryService!.StoreAsync(
                UserId,
                $"Multi-store memory {i} with keyword test{i}",
                MemoryType.Episodic,
                SessionId,
                0.5f + (i * 0.1f));
        }

        // Recall with different queries
        await _memoryService!.RecallAsync(UserId, "test0", limit: 3);
        await _memoryService!.RecallAsync(UserId, "test2", limit: 3);
        await _memoryService!.RecallAsync(UserId, "test4", limit: 3);
    }

    [Benchmark(Description = "Pipeline: Mixed types promotion")]
    public async Task PipelineMixedTypes()
    {
        await _memoryService!.StoreAsync(UserId, "Episodic event", MemoryType.Episodic, SessionId, 0.5f);
        await _memoryService!.StoreAsync(UserId, "Semantic fact", MemoryType.Semantic, SessionId, 0.7f);
        await _memoryService!.StoreAsync(UserId, "Procedural process", MemoryType.Procedural, SessionId, 0.6f);
        await _memoryService!.StoreAsync(UserId, "Verified fact", MemoryType.Fact, SessionId, 0.8f);

        await _memoryService!.RecallAsync(UserId, "mixed content", limit: 10);
    }

    [Benchmark(Description = "Pipeline: High-importance path")]
    public async Task PipelineHighImportance()
    {
        // High importance memories get prioritized in retrieval
        await _memoryService!.StoreAsync(UserId, "Critical information", MemoryType.Fact, SessionId, 0.95f);
        await _memoryService!.StoreAsync(UserId, "Important knowledge", MemoryType.Semantic, SessionId, 0.85f);
        await _memoryService!.StoreAsync(UserId, "Routine data", MemoryType.Episodic, SessionId, 0.3f);

        var results = await _memoryService!.RecallAsync(UserId, "information knowledge data", limit: 10);
    }

    // ==================== VCM Operations ====================

    [Benchmark(Description = "VCM: Initialize session")]
    public async Task VcmInitialize()
    {
        await _vcm!.InitializeAsync(UserId, Guid.NewGuid().ToString());
    }

    [Benchmark(Description = "VCM: Get context usage")]
    public async Task<ContextUsageStatistics> VcmGetContextUsage()
    {
        await _vcm!.InitializeAsync(UserId, SessionId);
        return await _vcm.GetContextUsageAsync();
    }

    [Benchmark(Description = "VCM: Consolidate")]
    public async Task<ConsolidationResult> VcmConsolidate()
    {
        await _vcm!.InitializeAsync(UserId, SessionId);

        // Add some data first
        for (int i = 0; i < 3; i++)
        {
            await _memoryService!.StoreAsync(
                UserId,
                $"Consolidation test memory {i}",
                MemoryType.Episodic,
                SessionId,
                0.5f);
        }

        return await _vcm.ConsolidateAsync();
    }
}
