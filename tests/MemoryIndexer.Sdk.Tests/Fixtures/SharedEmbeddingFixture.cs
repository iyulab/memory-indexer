using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#if !SKIP_ONNX_TESTS
using LMSupply.Embedder;
using MemoryIndexer.Sdk.Intelligence.Caching;
#endif

namespace MemoryIndexer.Sdk.Tests.Integration.Fixtures;

/// <summary>
/// Shared fixture for embedding service that is reused across all test classes.
/// This prevents multiple model loads and significantly reduces test execution time.
/// </summary>
/// <remarks>
/// The embedding model (~90MB) is loaded once and shared across all tests in the collection.
/// This reduces total test time from ~3+ minutes to ~30 seconds.
///
/// When SKIP_ONNX_TESTS is defined, the fixture will not load the ONNX model — see that flag's
/// definition in MemoryIndexer.Sdk.Tests.csproj for the current reason (not a runtime
/// incompatibility; ONNX Runtime + LMSupply.Embedder run fine on .NET 10).
/// </remarks>
public sealed class SharedEmbeddingFixture : IAsyncLifetime, IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private string? _initializationError;

    /// <summary>
    /// Gets whether the fixture is available for use.
    /// Returns false if ONNX tests are skipped or initialization failed.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Gets whether initialization failed.
    /// </summary>
    public bool InitializationFailed { get; private set; }

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
    public static int Dimensions => 384;

    /// <summary>
    /// Gets the model ID.
    /// </summary>
    public static string ModelId => "fast";

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

#if SKIP_ONNX_TESTS
            InitializationFailed = true;
            _initializationError = "SKIP_ONNX_TESTS is defined — see that flag's definition in " +
                                   "MemoryIndexer.Sdk.Tests.csproj for the current reason.";
            IsAvailable = false;
            _initialized = true;
            return;
#else
            try
            {
                // Load the shared embedding model using LMSupply directly
                EmbeddingModel = await LocalEmbedder.LoadAsync(ModelId);

                // Create shared embedding service using LMSupply wrapper, decorated with the SDK's
                // real LRU cache (not a raw wrapper) so caching-behavior tests exercise caching.
                var rawService = new LMSupplyEmbeddingServiceWrapper(EmbeddingModel);
                EmbeddingService = new CachedEmbeddingService(
                    rawService,
                    profiler: null,
                    NullLogger<CachedEmbeddingService>.Instance,
                    CreateOptions());

                IsAvailable = true;
                _initialized = true;
            }
            catch (Exception ex)
            {
                InitializationFailed = true;
                _initializationError = $"Failed to initialize ONNX embedding model: {ex.Message}.";
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
    public static IOptions<MemoryIndexerOptions> CreateOptions() =>
        Options.Create(new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Mock,  // SDK no longer has Local provider
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
    // CA1822: only the SKIP_ONNX_TESTS branch is instance-data-free; the real (non-skip) branch
    // below needs instance state (IsAvailable/_initializationError), so this can't be static.
#pragma warning disable CA1822
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
                "ONNX embedding fixture is not available.");
        }
#endif
    }
#pragma warning restore CA1822

#if !SKIP_ONNX_TESTS
    /// <summary>
    /// Simple wrapper around LMSupply IEmbeddingModel to implement IEmbeddingService for tests.
    /// </summary>
    private sealed class LMSupplyEmbeddingServiceWrapper : IEmbeddingService
    {
        private readonly IEmbeddingModel _model;

        public LMSupplyEmbeddingServiceWrapper(IEmbeddingModel model)
        {
            _model = model;
        }

        public int Dimensions => _model.Dimensions;

        public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var result = await _model.EmbedAsync(text, cancellationToken);
            return result;
        }

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts,
            CancellationToken cancellationToken = default)
        {
            var textList = texts.ToList();
            var results = new List<ReadOnlyMemory<float>>(textList.Count);

            foreach (var text in textList)
            {
                var embedding = await _model.EmbedAsync(text, cancellationToken);
                results.Add(embedding);
            }

            return results;
        }
    }
#endif
}
