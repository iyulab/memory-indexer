using LocalEmbedder;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Embedding.Providers;

/// <summary>
/// Embedding service using LocalEmbedder for local ONNX-based model inference.
/// Supports models like all-MiniLM-L6-v2 (384 dims), bge-small-en-v1.5 (384 dims),
/// bge-base-en-v1.5 (768 dims), and other ONNX embedding models.
/// </summary>
/// <remarks>
/// LocalEmbedder is an open-source library by iyulab that provides fast,
/// local embedding generation using ONNX Runtime. Models are downloaded
/// automatically on first use and cached locally.
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

        // Use configured model or default
        _modelId = !string.IsNullOrEmpty(embeddingOptions.Model)
            ? embeddingOptions.Model
            : DefaultModelId;

        // Get dimensions from known models or use configured value
        Dimensions = SupportedModels.TryGetValue(_modelId, out var knownDims)
            ? knownDims
            : embeddingOptions.Dimensions;

        _cacheTtl = TimeSpan.FromMinutes(embeddingOptions.CacheTtlMinutes);

        _logger.LogInformation(
            "LocalEmbeddingService initialized with model {ModelId}, dimensions {Dimensions}",
            _modelId, Dimensions);
    }

    /// <summary>
    /// Creates a LocalEmbeddingService with specific model configuration.
    /// </summary>
    public LocalEmbeddingService(
        string modelId,
        IMemoryCache cache,
        ILogger<LocalEmbeddingService> logger,
        TimeSpan? cacheTtl = null)
    {
        _cache = cache;
        _logger = logger;
        _modelId = modelId;
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(60);

        Dimensions = SupportedModels.TryGetValue(_modelId, out var knownDims)
            ? knownDims
            : 384; // Default fallback

        _logger.LogInformation(
            "LocalEmbeddingService initialized with model {ModelId}, dimensions {Dimensions}",
            _modelId, Dimensions);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        // Check cache first
        var cacheKey = $"local_embed:{_modelId}:{text.GetHashCode()}";
        if (_cache.TryGetValue<float[]>(cacheKey, out var cachedEmbedding) && cachedEmbedding != null)
        {
            return cachedEmbedding;
        }

        await EnsureModelLoadedAsync(cancellationToken);

        try
        {
            var embedding = await _model!.EmbedAsync(text);

            // Cache the result
            if (_cacheTtl > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, embedding, _cacheTtl);
            }

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embedding for text of length {Length}", text.Length);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return Array.Empty<ReadOnlyMemory<float>>();
        }

        await EnsureModelLoadedAsync(cancellationToken);

        var results = new List<ReadOnlyMemory<float>>(textList.Count);
        var uncachedTexts = new List<(int Index, string Text)>();

        // Check cache for each text
        for (var i = 0; i < textList.Count; i++)
        {
            var text = textList[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results.Add(new float[Dimensions]);
                continue;
            }

            var cacheKey = $"local_embed:{_modelId}:{text.GetHashCode()}";
            if (_cache.TryGetValue<float[]>(cacheKey, out var cachedEmbedding) && cachedEmbedding != null)
            {
                results.Add(cachedEmbedding);
            }
            else
            {
                uncachedTexts.Add((i, text));
                results.Add(ReadOnlyMemory<float>.Empty); // Placeholder
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            try
            {
                var uncachedTextArray = uncachedTexts.Select(x => x.Text).ToArray();
                var embeddings = await _model!.EmbedAsync(uncachedTextArray);

                for (var j = 0; j < uncachedTexts.Count; j++)
                {
                    var (index, text) = uncachedTexts[j];
                    var embedding = embeddings[j];

                    results[index] = embedding;

                    // Cache the result
                    if (_cacheTtl > TimeSpan.Zero)
                    {
                        var cacheKey = $"local_embed:{_modelId}:{text.GetHashCode()}";
                        _cache.Set(cacheKey, embedding, _cacheTtl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate batch embeddings for {Count} texts", uncachedTexts.Count);
                throw;
            }
        }

        return results;
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

            _logger.LogInformation("Loading local embedding model: {ModelId}", _modelId);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _model = await LocalEmbedder.LocalEmbedder.LoadAsync(_modelId);

            sw.Stop();
            _logger.LogInformation(
                "Model {ModelId} loaded in {ElapsedMs}ms, dimensions: {Dimensions}",
                _modelId, sw.ElapsedMilliseconds, _model.Dimensions);

            // Verify dimensions match
            if (_model.Dimensions != Dimensions)
            {
                _logger.LogWarning(
                    "Model dimensions ({ModelDims}) differ from configured dimensions ({ConfigDims})",
                    _model.Dimensions, Dimensions);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _model?.Dispose();
        _initLock.Dispose();

        await Task.CompletedTask;
    }
}
