using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Caching;

public class CachedEmbeddingServiceTests
{
    private readonly MockEmbeddingService _mockService;
    private readonly CachedEmbeddingService _cachedService;
    private readonly MemoryIndexerOptions _options;

    public CachedEmbeddingServiceTests()
    {
        _options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                EmbeddingCacheEnabled = true,
                EmbeddingCacheSize = 100,
                EmbeddingCacheTtlMinutes = 60
            }
        };

        _mockService = new MockEmbeddingService();
        _cachedService = new CachedEmbeddingService(
            _mockService,
            null, // No profiler for these tests
            NullLogger<CachedEmbeddingService>.Instance,
            Options.Create(_options));
    }

    [Fact]
    public void Dimensions_ShouldReturnInnerServiceDimensions()
    {
        // Arrange
        const int expectedDimensions = 1024;
        _mockService.Dimensions = expectedDimensions;

        // Act
        var dimensions = _cachedService.Dimensions;

        // Assert
        Assert.Equal(expectedDimensions, dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_FirstCall_ShouldCallInnerService()
    {
        // Arrange
        const string text = "test text";

        // Act
        var embedding = await _cachedService.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, _mockService.CallCount);
        Assert.Equal(1024, embedding.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SameTextTwice_ShouldUseCacheOnSecondCall()
    {
        // Arrange
        const string text = "test text";

        // Act
        var embedding1 = await _cachedService.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        var embedding2 = await _cachedService.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, _mockService.CallCount); // Only called once
        Assert.Equal(embedding1.ToArray(), embedding2.ToArray());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_DifferentTexts_ShouldCallInnerServiceForEach()
    {
        // Arrange
        const string text1 = "first text";
        const string text2 = "second text";

        // Act
        await _cachedService.GenerateEmbeddingAsync(text1, TestContext.Current.CancellationToken);
        await _cachedService.GenerateEmbeddingAsync(text2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _mockService.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_CacheDisabled_ShouldAlwaysCallInnerService()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions { EmbeddingCacheEnabled = false }
        };
        var service = new CachedEmbeddingService(
            _mockService,
            null,
            NullLogger<CachedEmbeddingService>.Instance,
            Options.Create(options));

        const string text = "test text";

        // Act
        await service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        await service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _mockService.CallCount); // Called twice
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_LRUEviction_ShouldEvictLeastRecentlyUsed()
    {
        // Arrange
        var mockService = new MockEmbeddingService(); // Fresh mock for this test
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                EmbeddingCacheEnabled = true,
                EmbeddingCacheSize = 2, // Cache can hold 2 items
                EmbeddingCacheTtlMinutes = 60
            }
        };
        var service = new CachedEmbeddingService(
            mockService,
            null,
            NullLogger<CachedEmbeddingService>.Instance,
            Options.Create(options));

        // Act - Fill cache with text1 and text2
        await service.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken); // Cache: [text1], Calls: 1
        await service.GenerateEmbeddingAsync("text2", TestContext.Current.CancellationToken); // Cache: [text2, text1], Calls: 2
        Assert.Equal(2, mockService.CallCount);

        // Act - Add text3, should evict text1 (LRU)
        await service.GenerateEmbeddingAsync("text3", TestContext.Current.CancellationToken); // Cache: [text3, text2], text1 evicted, Calls: 3
        Assert.Equal(3, mockService.CallCount);

        // Act - Access text2 and text3 (both should be in cache)
        await service.GenerateEmbeddingAsync("text2", TestContext.Current.CancellationToken); // In cache, Calls: 3
        await service.GenerateEmbeddingAsync("text3", TestContext.Current.CancellationToken); // In cache, Calls: 3
        Assert.Equal(3, mockService.CallCount);

        // Act - Access text1 (was evicted, should regenerate)
        await service.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken); // Not in cache, Calls: 4
        Assert.Equal(4, mockService.CallCount);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_AccessUpdatesLRU_ShouldPreventEviction()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions
            {
                EmbeddingCacheEnabled = true,
                EmbeddingCacheSize = 2,
                EmbeddingCacheTtlMinutes = 60
            }
        };
        var service = new CachedEmbeddingService(
            _mockService,
            null,
            NullLogger<CachedEmbeddingService>.Instance,
            Options.Create(options));

        // Act
        await service.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken); // Cache: [text1]
        await service.GenerateEmbeddingAsync("text2", TestContext.Current.CancellationToken); // Cache: [text2, text1]
        await service.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken); // Cache: [text1, text2] (text1 moved to front)
        await service.GenerateEmbeddingAsync("text3", TestContext.Current.CancellationToken); // Cache: [text3, text1] (text2 evicted)

        _mockService.CallCount = 0;

        await service.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken); // Should use cache
        await service.GenerateEmbeddingAsync("text2", TestContext.Current.CancellationToken); // Should call inner (evicted)

        // Assert
        Assert.Equal(1, _mockService.CallCount); // Only text2 was regenerated
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_AllUncached_ShouldCallInnerService()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };

        // Act
        var embeddings = await _cachedService.GenerateBatchEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, _mockService.BatchCallCount);
        Assert.Equal(3, embeddings.Count);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_AllCached_ShouldNotCallInnerService()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };

        // Pre-populate cache
        foreach (var text in texts)
        {
            await _cachedService.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        }

        _mockService.CallCount = 0;
        _mockService.BatchCallCount = 0;

        // Act
        var embeddings = await _cachedService.GenerateBatchEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, _mockService.CallCount);
        Assert.Equal(0, _mockService.BatchCallCount);
        Assert.Equal(3, embeddings.Count);
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_PartiallyCached_ShouldOnlyGenerateUncached()
    {
        // Arrange
        var texts = new[] { "text1", "text2", "text3" };

        // Pre-cache text1 and text2
        await _cachedService.GenerateEmbeddingAsync("text1", TestContext.Current.CancellationToken);
        await _cachedService.GenerateEmbeddingAsync("text2", TestContext.Current.CancellationToken);

        _mockService.CallCount = 0;
        _mockService.BatchCallCount = 0;

        // Act
        var embeddings = await _cachedService.GenerateBatchEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, _mockService.CallCount); // No single calls
        Assert.Equal(1, _mockService.BatchCallCount); // One batch call for text3
        Assert.Equal(3, embeddings.Count);

        // Verify all embeddings are valid
        foreach (var embedding in embeddings)
        {
            Assert.Equal(1024, embedding.Length);
        }
    }

    [Fact]
    public async Task GenerateBatchEmbeddingsAsync_CacheDisabled_ShouldAlwaysCallInnerService()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions { EmbeddingCacheEnabled = false }
        };
        var service = new CachedEmbeddingService(
            _mockService,
            null,
            NullLogger<CachedEmbeddingService>.Instance,
            Options.Create(options));

        var texts = new[] { "text1", "text2" };

        // Act
        await service.GenerateBatchEmbeddingsAsync(texts, TestContext.Current.CancellationToken);
        await service.GenerateBatchEmbeddingsAsync(texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _mockService.BatchCallCount); // Called twice
    }

    // Mock embedding service for testing
    private sealed class MockEmbeddingService : IEmbeddingService
    {
        public int Dimensions { get; set; } = 1024;
        public int CallCount { get; set; }
        public int BatchCallCount { get; set; }

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var embedding = new float[Dimensions];
            for (int i = 0; i < Dimensions; i++)
            {
                embedding[i] = (float)(text.GetHashCode() % 1000) / 1000f;
            }
            return Task.FromResult<ReadOnlyMemory<float>>(embedding);
        }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            var embeddings = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
            {
                var embedding = new float[Dimensions];
                for (int i = 0; i < Dimensions; i++)
                {
                    embedding[i] = (float)(text.GetHashCode() % 1000) / 1000f;
                }
                embeddings.Add(embedding);
            }
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(embeddings);
        }
    }
}
