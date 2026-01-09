using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LMSupply.Embedder;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Embedding.Providers;

/// <summary>
/// Embedding service using LMSupply.Embedder for local ONNX-based model inference.
/// Supports models like all-MiniLM-L6-v2 (384 dims), bge-small-en-v1.5 (384 dims),
/// bge-base-en-v1.5 (768 dims), and other ONNX embedding models.
/// </summary>
/// <remarks>
/// LMSupply.Embedder is an open-source library by iyulab that provides fast,
/// local embedding generation using ONNX Runtime. Models are downloaded
/// automatically on first use and cached locally.
///
/// This package is the successor to the archived LocalEmbedder package.
///
/// Note: This service doesn't extend CachedEmbeddingServiceBase because it requires
/// IAsyncDisposable for model cleanup and has lazy model loading that differs
/// from the HTTP-based providers.
/// </remarks>
public sealed class LocalEmbeddingService : IEmbeddingService, IAsyncDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LocalEmbeddingService> _logger;
    private readonly string _modelId;
    private readonly TimeSpan _cacheTtl;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IEmbeddingModel? _model;
    private bool _disposed;

    private const string CacheKeyPrefix = "local";

    /// <summary>
    /// Default model ID if not specified in configuration.
    /// all-MiniLM-L6-v2 is a good balance of speed and quality.
    /// </summary>
    public const string DefaultModelId = "all-MiniLM-L6-v2";

    /// <summary>
    /// Supported local embedding models with their dimensions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> SupportedModels = new Dictionary<string, int>
    {
        ["all-MiniLM-L6-v2"] = 384,
        ["bge-small-en-v1.5"] = 384,
        ["bge-base-en-v1.5"] = 768,
        ["bge-large-en-v1.5"] = 1024,
        ["nomic-embed-text-v1"] = 768,
        ["gte-small"] = 384,
        ["gte-base"] = 768,
        ["gte-large"] = 1024
    };

    /// <inheritdoc />
    public int Dimensions { get; }

    public LocalEmbeddingService(
        IMemoryCache cache,
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalEmbeddingService> logger)
    {
        _cache = cache;
        _logger = logger;

        var embeddingOptions = options.Value.Embedding;

        _modelId = !string.IsNullOrEmpty(embeddingOptions.Model)
            ? embeddingOptions.Model
            : DefaultModelId;

        Dimensions = SupportedModels.TryGetValue(_modelId, out var knownDims)
            ? knownDims
            : embeddingOptions.Dimensions;

        _cacheTtl = TimeSpan.FromMinutes(embeddingOptions.CacheTtlMinutes);

        _logger.LogInformation(
            "LocalEmbeddingService initialized with model {ModelId}, dimensions {Dimensions}",
            _modelId, Dimensions);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("EmbeddingGenerate", "embedding");
        activity?.SetTag("embedding.provider", CacheKeyPrefix);
        activity?.SetTag("embedding.model", _modelId);
        activity?.SetTag("embedding.dimensions", Dimensions);
        activity?.SetTag("embedding.text_length", text?.Length ?? 0);

        var sw = Stopwatch.StartNew();
        var cacheHit = false;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (string.IsNullOrWhiteSpace(text))
            {
                activity?.SetTag("embedding.empty_input", true);
                return new float[Dimensions];
            }

            var cacheKey = GetCacheKey(text);
            if (_cacheTtl > TimeSpan.Zero && _cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
            {
                cacheHit = true;
                activity?.SetTag("embedding.cache_hit", true);
                MemoryIndexerTelemetry.EmbeddingCacheHits.Add(1,
                    new KeyValuePair<string, object?>("provider", CacheKeyPrefix));
                _logger.LogDebug("Cache hit for embedding");
                return cached;
            }

            activity?.SetTag("embedding.cache_hit", false);
            await EnsureModelLoadedAsync(cancellationToken);

            var embedding = await _model!.EmbedAsync(text);
            ReadOnlyMemory<float> result = embedding;

            if (_cacheTtl > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, result, _cacheTtl);
            }

            MemoryIndexerTelemetry.CompleteOperation(activity, success: true);
            return result;
        }
        catch (Exception ex)
        {
            MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
            throw;
        }
        finally
        {
            sw.Stop();
            if (!cacheHit)
            {
                MemoryIndexerTelemetry.RecordEmbeddingOperation(sw.Elapsed.TotalMilliseconds);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("EmbeddingBatchGenerate", "embedding");
        activity?.SetTag("embedding.provider", CacheKeyPrefix);
        activity?.SetTag("embedding.model", _modelId);
        activity?.SetTag("embedding.dimensions", Dimensions);

        var sw = Stopwatch.StartNew();

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var textList = texts.ToList();
            activity?.SetTag("embedding.batch_size", textList.Count);

            if (textList.Count == 0)
            {
                activity?.SetTag("embedding.empty_batch", true);
                return [];
            }

            _logger.LogDebug("Generating batch embeddings for {Count} texts", textList.Count);

            await EnsureModelLoadedAsync(cancellationToken);

            var results = new ReadOnlyMemory<float>[textList.Count];
            var uncached = new List<(int Index, string Text)>();
            var cacheHits = 0;

            // Check cache first
            for (var i = 0; i < textList.Count; i++)
            {
                var text = textList[i];
                if (string.IsNullOrWhiteSpace(text))
                {
                    results[i] = new float[Dimensions];
                    continue;
                }

                var cacheKey = GetCacheKey(text);
                if (_cacheTtl > TimeSpan.Zero && _cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cached))
                {
                    results[i] = cached;
                    cacheHits++;
                }
                else
                {
                    uncached.Add((i, text));
                }
            }

            activity?.SetTag("embedding.cache_hits", cacheHits);
            activity?.SetTag("embedding.uncached_count", uncached.Count);

            // Record cache hits
            if (cacheHits > 0)
            {
                MemoryIndexerTelemetry.EmbeddingCacheHits.Add(cacheHits,
                    new KeyValuePair<string, object?>("provider", CacheKeyPrefix));
            }

            if (uncached.Count == 0)
            {
                _logger.LogDebug("All {Count} embeddings found in cache", textList.Count);
                MemoryIndexerTelemetry.CompleteOperation(activity, success: true);
                return results;
            }

            // Use native batch API for uncached texts
            var uncachedTexts = uncached.Select(x => x.Text).ToArray();
            var embeddings = await _model!.EmbedAsync(uncachedTexts);

            for (var j = 0; j < uncached.Count; j++)
            {
                var (index, text) = uncached[j];
                ReadOnlyMemory<float> embedding = embeddings[j];
                results[index] = embedding;

                if (_cacheTtl > TimeSpan.Zero)
                {
                    _cache.Set(GetCacheKey(text), embedding, _cacheTtl);
                }
            }

            MemoryIndexerTelemetry.CompleteOperation(activity, success: true);
            return results;
        }
        catch (Exception ex)
        {
            MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
            throw;
        }
        finally
        {
            sw.Stop();
            MemoryIndexerTelemetry.RecordEmbeddingOperation(sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task EnsureModelLoadedAsync(CancellationToken cancellationToken)
    {
        if (_model != null)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_model != null)
                return;

            using var activity = MemoryIndexerTelemetry.StartOperation("EmbeddingModelLoad", "embedding");
            activity?.SetTag("embedding.provider", CacheKeyPrefix);
            activity?.SetTag("embedding.model", _modelId);

            _logger.LogInformation("Loading local embedding model: {ModelId}", _modelId);
            var sw = Stopwatch.StartNew();

            try
            {
                _model = await LocalEmbedder.LoadAsync(_modelId);

                sw.Stop();
                activity?.SetTag("embedding.load_time_ms", sw.ElapsedMilliseconds);
                activity?.SetTag("embedding.model_dimensions", _model.Dimensions);

                _logger.LogInformation(
                    "Model {ModelId} loaded in {ElapsedMs}ms, dimensions: {Dimensions}",
                    _modelId, sw.ElapsedMilliseconds, _model.Dimensions);

                if (_model.Dimensions != Dimensions)
                {
                    activity?.SetTag("embedding.dimension_mismatch", true);
                    _logger.LogWarning(
                        "Model dimensions ({ModelDims}) differ from configured dimensions ({ConfigDims})",
                        _model.Dimensions, Dimensions);
                }

                MemoryIndexerTelemetry.CompleteOperation(activity, success: true);
            }
            catch (Exception ex)
            {
                MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
                throw;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets a consistent cache key using SHA256 hash. Same approach as CachedEmbeddingServiceBase.
    /// </summary>
    private string GetCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"emb:{CacheKeyPrefix}:{Convert.ToHexString(hash)}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_model != null)
        {
            await _model.DisposeAsync();
        }
        _initLock.Dispose();
    }
}
