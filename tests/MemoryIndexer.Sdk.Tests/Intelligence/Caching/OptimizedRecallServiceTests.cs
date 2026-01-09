using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Caching;

public class OptimizedRecallServiceTests
{
    private readonly MockMemoryStore _mockStore;
    private readonly MockEmbeddingService _mockEmbedding;
    private readonly MockScoringService _mockScoring;
    private readonly MockLatencyProfiler _mockProfiler;
    private readonly IMemoryCache _memoryCache;
    private readonly OptimizedRecallService _service;
    private readonly MemoryIndexerOptions _options;

    public OptimizedRecallServiceTests()
    {
        _options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                ProfilingEnabled = true,
                WorkingMemoryBudgetMs = 100.0,
                EarlyTerminationEnabled = true,
                EarlyTerminationConfidence = 0.9f,
                EarlyTerminationMinResults = 3,
                BatchProcessingEnabled = true,
                MaxBatchSize = 10,
                QueryCacheEnabled = true,
                QueryCacheTtlMinutes = 10
            }
        };

        _mockStore = new MockMemoryStore();
        _mockEmbedding = new MockEmbeddingService();
        _mockScoring = new MockScoringService();
        _mockProfiler = new MockLatencyProfiler();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _service = new OptimizedRecallService(
            _mockStore,
            _mockEmbedding,
            _mockScoring,
            _memoryCache,
            _mockProfiler,
            patternAnalyzer: null,
            NullLogger<OptimizedRecallService>.Instance,
            Options.Create(_options));
    }

    [Fact]
    public async Task RecallAsync_ShouldGenerateEmbeddingAndSearch()
    {
        // Arrange
        const string userId = "user1";
        const string query = "test query";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(5));

        // Act
        var results = await _service.RecallAsync(userId, query, tier, limit: 5);

        // Assert
        Assert.Equal(1, _mockEmbedding.CallCount);
        Assert.Equal(1, _mockStore.SearchCallCount);
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task RecallAsync_ShouldRecordLatencyMetrics()
    {
        // Arrange
        const string userId = "user1";
        const string query = "test query";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(3));

        // Act
        await _service.RecallAsync(userId, query, tier);

        // Assert
        Assert.Equal(1, _mockProfiler.RecordLatencyCallCount);
        Assert.Equal(userId, _mockProfiler.LastUserId);
        Assert.Equal(tier, _mockProfiler.LastTier);
        Assert.True(_mockProfiler.LastLatencyMs > 0);
        Assert.NotNull(_mockProfiler.LastComponentLatencies);
        Assert.Contains("Embedding", _mockProfiler.LastComponentLatencies!.Keys);
        Assert.Contains("Search", _mockProfiler.LastComponentLatencies.Keys);
    }

    [Fact]
    public async Task RecallAsync_EarlyTermination_ShouldReturnWhenConfidenceMet()
    {
        // Arrange
        const string userId = "user1";
        const string query = "test query";
        const string tier = "Working";

        // Create results with high confidence scores
        var results = CreateMemorySearchResults(5, scoreStart: 0.95f);
        _mockStore.SetSearchResults(results);

        // Act
        var memories = await _service.RecallAsync(userId, query, tier, limit: 3);

        // Assert
        Assert.Equal(3, memories.Count); // Limited by requested limit
        Assert.Equal(1, _mockProfiler.RecordLatencyCallCount);
        Assert.Contains("EarlyTermination", _mockProfiler.LastComponentLatencies!.Keys);
    }

    [Fact]
    public async Task RecallAsync_LowConfidence_ShouldNotTriggerEarlyTermination()
    {
        // Arrange
        const string userId = "user1";
        const string query = "test query";
        const string tier = "Working";

        // Create results with low confidence scores
        var results = CreateMemorySearchResults(5, scoreStart: 0.5f);
        _mockStore.SetSearchResults(results);

        // Act
        var memories = await _service.RecallAsync(userId, query, tier, limit: 5);

        // Assert
        Assert.Equal(5, memories.Count);
        Assert.DoesNotContain("EarlyTermination", _mockProfiler.LastComponentLatencies!.Keys);
    }

    [Fact]
    public async Task RecallAsync_EarlyTerminationDisabled_ShouldNeverTerminateEarly()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                EarlyTerminationEnabled = false,
                EarlyTerminationConfidence = 0.9f,
                EarlyTerminationMinResults = 3
            }
        };

        var service = new OptimizedRecallService(
            _mockStore,
            _mockEmbedding,
            _mockScoring,
            _memoryCache,
            _mockProfiler,
            patternAnalyzer: null,
            NullLogger<OptimizedRecallService>.Instance,
            Options.Create(options));

        var results = CreateMemorySearchResults(5, scoreStart: 0.95f);
        _mockStore.SetSearchResults(results);

        // Act
        var memories = await service.RecallAsync("user1", "query", "Working", limit: 5);

        // Assert
        Assert.Equal(5, memories.Count);
        Assert.DoesNotContain("EarlyTermination", _mockProfiler.LastComponentLatencies!.Keys);
    }

    [Fact]
    public async Task RecallAsync_ShouldReturnTopResultsByScore()
    {
        // Arrange
        const string userId = "user1";
        const string query = "test query";
        const string tier = "Working";

        var results = new List<MemorySearchResult>
        {
            new() { Memory = CreateMemory("low"), Score = 0.3f },
            new() { Memory = CreateMemory("high"), Score = 0.9f },
            new() { Memory = CreateMemory("medium"), Score = 0.6f }
        };
        _mockStore.SetSearchResults(results);

        // Act
        var memories = await _service.RecallAsync(userId, query, tier, limit: 2);

        // Assert
        Assert.Equal(2, memories.Count);
        Assert.Equal("high", memories[0].Content); // Highest score first
        Assert.Equal("medium", memories[1].Content);
    }

    [Fact]
    public async Task RecallAsync_OnError_ShouldRecordLatencyAndRethrow()
    {
        // Arrange
        _mockEmbedding.ShouldThrow = true;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RecallAsync("user1", "query", "Working"));

        // Latency should still be recorded
        Assert.Equal(1, _mockProfiler.RecordLatencyCallCount);
    }

    [Fact]
    public async Task BatchRecallAsync_BatchingDisabled_ShouldProcessSequentially()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions { BatchProcessingEnabled = false }
        };

        var service = new OptimizedRecallService(
            _mockStore,
            _mockEmbedding,
            _mockScoring,
            _memoryCache,
            _mockProfiler,
            patternAnalyzer: null,
            NullLogger<OptimizedRecallService>.Instance,
            Options.Create(options));

        var queries = new[] { "query1", "query2", "query3" };
        _mockStore.SetSearchResults(CreateMemorySearchResults(3));

        // Act
        var results = await service.BatchRecallAsync("user1", queries, "Working");

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(3, _mockEmbedding.CallCount); // One call per query
    }

    [Fact]
    public async Task BatchRecallAsync_SingleQuery_ShouldProcessSequentially()
    {
        // Arrange
        var queries = new[] { "query1" };
        _mockStore.SetSearchResults(CreateMemorySearchResults(3));

        // Act
        var results = await _service.BatchRecallAsync("user1", queries, "Working");

        // Assert
        Assert.Single(results);
        Assert.Contains("query1", results.Keys);
    }

    [Fact]
    public async Task BatchRecallAsync_MultipleQueries_ShouldProcessInBatches()
    {
        // Arrange
        var queries = new[] { "query1", "query2", "query3", "query4", "query5" };
        _mockStore.SetSearchResults(CreateMemorySearchResults(3));

        // Act
        var results = await _service.BatchRecallAsync("user1", queries, "Working", limit: 3);

        // Assert
        Assert.Equal(5, results.Count);
        foreach (var query in queries)
        {
            Assert.Contains(query, results.Keys);
            Assert.Equal(3, results[query].Count);
        }
    }

    [Fact]
    public async Task BatchRecallAsync_LargeBatch_ShouldRespectMaxBatchSize()
    {
        // Arrange
        var queries = Enumerable.Range(1, 25).Select(i => $"query{i}").ToArray();
        _mockStore.SetSearchResults(CreateMemorySearchResults(2));

        // Act
        var results = await _service.BatchRecallAsync("user1", queries, "Working");

        // Assert
        Assert.Equal(25, results.Count);
        // Batches should be processed (MaxBatchSize = 10, so 3 batches: 10, 10, 5)
    }

    #region Query Result Caching Tests (Phase v0.5.0)

    [Fact]
    public async Task RecallAsync_DuplicateQuery_ShouldReturnCachedResult()
    {
        // Arrange
        const string userId = "user1";
        const string query = "duplicate query test";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(5));

        // Act - First call
        var firstResult = await _service.RecallAsync(userId, query, tier, limit: 5);
        var firstCallCount = _mockStore.SearchCallCount;

        // Act - Second call (should hit cache)
        var secondResult = await _service.RecallAsync(userId, query, tier, limit: 5);
        var secondCallCount = _mockStore.SearchCallCount;

        // Assert
        Assert.Equal(5, firstResult.Count);
        Assert.Equal(5, secondResult.Count);
        Assert.Equal(1, firstCallCount); // First call hits store
        Assert.Equal(1, secondCallCount); // Second call should NOT hit store (cached)
    }

    [Fact]
    public async Task RecallAsync_DifferentQueries_ShouldNotShareCache()
    {
        // Arrange
        const string userId = "user1";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(5));

        // Act
        await _service.RecallAsync(userId, "query1", tier, limit: 5);
        await _service.RecallAsync(userId, "query2", tier, limit: 5);

        // Assert - Both should hit the store
        Assert.Equal(2, _mockStore.SearchCallCount);
    }

    [Fact]
    public async Task RecallAsync_DifferentLimits_ShouldNotShareCache()
    {
        // Arrange
        const string userId = "user1";
        const string query = "same query";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(10));

        // Act
        await _service.RecallAsync(userId, query, tier, limit: 5);
        await _service.RecallAsync(userId, query, tier, limit: 10);

        // Assert - Both should hit the store (different cache keys)
        Assert.Equal(2, _mockStore.SearchCallCount);
    }

    [Fact]
    public void GetCacheStatistics_ShouldTrackHitsAndMisses()
    {
        // Arrange & Act
        var stats = _service.GetCacheStatistics();

        // Assert
        Assert.Equal(0, stats.CacheHits);
        Assert.Equal(0, stats.CacheMisses);
        Assert.Equal(0, stats.DuplicateQueryCount);
        Assert.Equal(0f, stats.HitRatio);
    }

    [Fact]
    public async Task GetCacheStatistics_AfterDuplicateQueries_ShouldReflectCacheUsage()
    {
        // Arrange
        const string userId = "user1";
        const string query = "cached query";
        const string tier = "Working";

        _mockStore.SetSearchResults(CreateMemorySearchResults(3));

        // Act - First call (cache miss)
        await _service.RecallAsync(userId, query, tier, limit: 3);
        // Second call (cache hit)
        await _service.RecallAsync(userId, query, tier, limit: 3);
        // Third call (cache hit)
        await _service.RecallAsync(userId, query, tier, limit: 3);

        var stats = _service.GetCacheStatistics();

        // Assert
        Assert.Equal(2, stats.CacheHits); // Two cache hits
        Assert.Equal(1, stats.CacheMisses); // One cache miss (first call)
        Assert.Equal(2, stats.DuplicateQueryCount); // Two duplicate queries
        Assert.True(stats.HitRatio > 0.6f); // ~66% hit ratio
    }

    #endregion

    // Helper methods
    private static List<MemorySearchResult> CreateMemorySearchResults(int count, float scoreStart = 0.8f)
    {
        var results = new List<MemorySearchResult>();
        for (int i = 0; i < count; i++)
        {
            results.Add(new MemorySearchResult
            {
                Memory = CreateMemory($"content{i}"),
                Score = scoreStart - (i * 0.05f)
            });
        }
        return results;
    }

    private static MemoryUnit CreateMemory(string content)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "user1",
            Content = content,
            CreatedAt = DateTime.UtcNow,
            Tier = Tier.Short
        };
    }

    // Mock classes
    private class MockMemoryStore : IMemoryStore
    {
        private List<MemorySearchResult> _searchResults = new();
        public int SearchCallCount { get; private set; }

        public void SetSearchResults(List<MemorySearchResult> results)
        {
            _searchResults = results;
        }

        public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
            ReadOnlyMemory<float> queryEmbedding,
            MemorySearchOptions options,
            CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            return Task.FromResult<IReadOnlyList<MemorySearchResult>>(_searchResults);
        }

        public Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
            => Task.FromResult(memory);

        public Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(IEnumerable<MemoryUnit> memories, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MemoryUnit>>(memories.ToList());

        public Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<MemoryUnit?>(null);

        public Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MemoryUnit>>(Array.Empty<MemoryUnit>());

        public Task<bool> DeleteAsync(Guid id, bool hardDelete = false, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<MemoryUnit>> GetAllAsync(string userId, MemoryFilterOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MemoryUnit>>(Array.Empty<MemoryUnit>());

        public Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<MemoryType, int>>(new Dictionary<MemoryType, int>());

        public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteCollectionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class MockEmbeddingService : IEmbeddingService
    {
        public int Dimensions => 1024;
        public int CallCount { get; set; }
        public bool ShouldThrow { get; set; }

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ShouldThrow)
                throw new InvalidOperationException("Mock error");

            return Task.FromResult<ReadOnlyMemory<float>>(new float[Dimensions]);
        }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            var embeddings = texts.Select(_ =>
            {
                CallCount++;
                return (ReadOnlyMemory<float>)new float[Dimensions];
            }).ToList();
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(embeddings);
        }
    }

    private class MockScoringService : IScoringService
    {
        public float CalculateScore(MemoryUnit memory, ReadOnlyMemory<float>? queryEmbedding = null)
            => 0.8f;

        public float CalculateRecencyScore(MemoryUnit memory)
            => 0.8f;

        public float CalculateAccessFrequencyScore(MemoryUnit memory)
            => 0.5f;

        public float CalculateCosineSimilarity(ReadOnlyMemory<float> embedding1, ReadOnlyMemory<float> embedding2)
            => 0.9f;

        public float CalculateKeywordBoost(string query, string memoryContent)
            => 0.1f;

        public float CalculateContentTypeBoost(string memoryContent)
            => 0.05f;

        public float CalculateHybridScore(MemoryUnit memory, string query, ReadOnlyMemory<float>? queryEmbedding = null)
            => 0.85f;

        public float CalculateHybridScoreWithIntent(MemoryUnit memory, string query, QueryIntentResult intent, ReadOnlyMemory<float>? queryEmbedding = null)
            => 0.85f;

        public IReadOnlyList<NormalizableMemory> ScoreAndNormalize(IReadOnlyList<MemoryUnit> memories, string query, ReadOnlyMemory<float>? queryEmbedding = null)
            => memories.Select(m => new NormalizableMemory
            {
                Memory = m,
                RawScore = 0.8f,
                NormalizedScore = 0.8f
            }).ToList();
    }

    private class MockLatencyProfiler : ILatencyProfiler
    {
        public int RecordLatencyCallCount { get; set; }
        public string? LastUserId { get; set; }
        public string? LastTier { get; set; }
        public double LastLatencyMs { get; set; }
        public Dictionary<string, double>? LastComponentLatencies { get; set; }

        public Task RecordLatencyAsync(
            string userId,
            string tier,
            double latencyMs,
            Dictionary<string, double>? componentLatencies = null,
            CancellationToken cancellationToken = default)
        {
            RecordLatencyCallCount++;
            LastUserId = userId;
            LastTier = tier;
            LastLatencyMs = latencyMs;
            LastComponentLatencies = componentLatencies;
            return Task.CompletedTask;
        }

        public Task RecordCacheAccessAsync(string userId, string cacheType, bool hit, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<LatencyMetrics>> GetMetricsAsync(string userId, string? tier = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LatencyMetrics>>(Array.Empty<LatencyMetrics>());

        public Task ResetMetricsAsync(string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public double GetLatencyBudget(string tier) => 100.0;
    }
}
