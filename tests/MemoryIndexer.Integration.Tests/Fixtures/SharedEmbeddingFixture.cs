using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#if !SKIP_ONNX_TESTS
using LocalAI.Embedder;
using MemoryIndexer.Embedding.Providers;
#endif

namespace MemoryIndexer.Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture for embedding service that is reused across all test classes.
/// This prevents multiple model loads and significantly reduces test execution time.
/// </summary>
/// <remarks>
/// The embedding model (~90MB) is loaded once and shared across all tests in the collection.
/// This reduces total test time from ~3+ minutes to ~30 seconds.
///
/// When SKIP_ONNX_TESTS is defined, the fixture will not load the ONNX model to avoid
/// native binary incompatibility issues with .NET 10 (ONNX Runtime crash).
/// </remarks>
public sealed class SharedEmbeddingFixture : IAsyncLifetime, IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _initializationFailed;
    private string? _initializationError;

    /// <summary>
    /// Gets whether the fixture is available for use.
    /// Returns false if ONNX tests are skipped or initialization failed.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Gets the initialization error message if initialization failed.
    /// </summary>
    public string? InitializationError => _initializationError;

#if !SKIP_ONNX_TESTS
    /// <summary>
    /// Gets the shared embedding model instance.
    /// </summary>
    public IEmbeddingModel? EmbeddingModel { get; private set; }
#endif

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

#if SKIP_ONNX_TESTS
            _initializationFailed = true;
            _initializationError = "ONNX tests are skipped due to native binary incompatibility with .NET 10. " +
                                   "The SKIP_ONNX_TESTS compile constant is defined.";
            IsAvailable = false;
            _initialized = true;
            return;
#else
            try
            {
                // Load the shared embedding model
                EmbeddingModel = await LocalEmbedder.LoadAsync(ModelId);

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

                IsAvailable = true;
                _initialized = true;
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = $"Failed to initialize ONNX embedding model: {ex.Message}. " +
                                      "This is likely due to ONNX Runtime native binary incompatibility with the current .NET version.";
                IsAvailable = false;
                _initialized = true;
            }
#endif
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task DisposeAsync()
    {
#if !SKIP_ONNX_TESTS
        if (EmbeddingModel != null)
        {
            await EmbeddingModel.DisposeAsync();
        }
#else
        await Task.CompletedTask;
#endif
        MemoryCache.Dispose();
        _initLock.Dispose();
    }

    public void Dispose()
    {
        // IAsyncDisposable is handled in DisposeAsync
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
#if !SKIP_ONNX_TESTS
                Provider = EmbeddingProvider.Local,
#else
                Provider = EmbeddingProvider.Mock,
#endif
                Model = ModelId,
                Dimensions = Dimensions,
                CacheTtlMinutes = 30
            }
        });

    /// <summary>
    /// Throws an exception if the fixture is not available.
    /// Use at the beginning of tests to fail fast when ONNX is unavailable.
    /// </summary>
    /// <remarks>
    /// When SKIP_ONNX_TESTS is defined, all test classes using this fixture are
    /// excluded via conditional compilation, so this method won't be called.
    /// When not defined, if ONNX initialization fails at runtime, tests will fail
    /// with a clear error message.
    /// </remarks>
    public void EnsureAvailable()
    {
#if SKIP_ONNX_TESTS
        // This branch should never execute because all test classes
        // using this fixture are excluded when SKIP_ONNX_TESTS is defined.
        throw new InvalidOperationException(
            "SKIP_ONNX_TESTS is defined but a test tried to use EnsureAvailable(). " +
            "Ensure all ONNX-dependent tests are wrapped in #if !SKIP_ONNX_TESTS.");
#else
        if (!IsAvailable)
        {
            throw new InvalidOperationException(_initializationError ??
                "ONNX embedding fixture is not available. " +
                "Check if ONNX Runtime is compatible with the current .NET version.");
        }
#endif
    }
}
