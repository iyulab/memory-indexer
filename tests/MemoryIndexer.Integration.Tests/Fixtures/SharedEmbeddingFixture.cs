using LocalEmbedder;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Embedding.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture for embedding service that is reused across all test classes.
/// This prevents multiple model loads and significantly reduces test execution time.
/// </summary>
/// <remarks>
/// The embedding model (~90MB) is loaded once and shared across all tests in the collection.
/// This reduces total test time from ~3+ minutes to ~30 seconds.
/// </remarks>
public sealed class SharedEmbeddingFixture : IAsyncLifetime, IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Gets the shared embedding model instance.
    /// </summary>
    public IEmbeddingModel? EmbeddingModel { get; private set; }

    /// <summary>
    /// Gets the shared embedding service instance.
    /// </summary>
    public IEmbeddingService? EmbeddingService { get; private set; }

    /// <summary>
    /// Gets the shared memory cache instance.
    /// </summary>
    public IMemoryCache MemoryCache { get; } = new MemoryCache(new MemoryCacheOptions());

    /// <summary>
    /// Gets the embedding dimensions.
    /// </summary>
    public int Dimensions => 384;

    /// <summary>
    /// Gets the model ID.
    /// </summary>
    public string ModelId => "all-MiniLM-L6-v2";

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            // Load the shared embedding model
            EmbeddingModel = await LocalEmbedder.LocalEmbedder.LoadAsync(ModelId);

            // Create shared embedding service
            var options = new MemoryIndexerOptions
            {
                Embedding = new EmbeddingOptions
                {
                    Provider = EmbeddingProvider.Local,
                    Model = ModelId,
                    Dimensions = Dimensions,
                    CacheTtlMinutes = 30 // Longer TTL for test reuse
                }
            };

            EmbeddingService = new LocalEmbeddingService(
                MemoryCache,
                Options.Create(options),
                NullLogger<LocalEmbeddingService>.Instance);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        EmbeddingModel?.Dispose();
        MemoryCache.Dispose();
        _initLock.Dispose();
    }

    /// <summary>
    /// Creates a pre-configured options object for test setup.
    /// </summary>
    public IOptions<MemoryIndexerOptions> CreateOptions() =>
        Options.Create(new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Local,
                Model = ModelId,
                Dimensions = Dimensions,
                CacheTtlMinutes = 30
            }
        });
}
