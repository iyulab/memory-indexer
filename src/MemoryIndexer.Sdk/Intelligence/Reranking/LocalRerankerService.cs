using LMSupply.Reranker;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MemoryIndexer.Configuration;

namespace MemoryIndexer.Sdk.Intelligence.Reranking;

/// <summary>
/// Re-ranking service using LMSupply.Reranker for local ONNX-based cross-encoder inference.
/// </summary>
/// <remarks>
/// LMSupply.Reranker is an open-source library by iyulab that provides fast,
/// local re-ranking using ONNX Runtime cross-encoder models.
/// Models are downloaded automatically on first use and cached locally.
///
/// Supported models:
/// - bge-reranker-base: Fast, good quality (default)
/// - bge-reranker-large: Medium speed, better quality
/// - bge-reranker-v2-m3: Slower, best quality (multilingual)
/// </remarks>
public sealed class LocalRerankerService : IRerankerService, IAsyncDisposable
{
    private readonly ILogger<LocalRerankerService> _logger;
    private readonly string _modelId;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IRerankerModel? _model;
    private bool _disposed;

    /// <summary>
    /// Default model ID if not specified in configuration.
    /// bge-reranker-base provides a good balance of speed and quality.
    /// </summary>
    public const string DefaultModelId = "bge-reranker-base";

    /// <summary>
    /// Supported re-ranker models.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedModels =
    [
        "bge-reranker-base",
        "bge-reranker-large",
        "bge-reranker-v2-m3"
    ];

    public LocalRerankerService(
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalRerankerService> logger)
    {
        _logger = logger;

        // Use configured model or default
        var rerankOptions = options.Value.Search;
        _modelId = !string.IsNullOrEmpty(rerankOptions.RerankerModel)
            ? rerankOptions.RerankerModel
            : DefaultModelId;

        _logger.LogInformation(
            "LocalRerankerService initialized with model {ModelId}",
            _modelId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult<TMetadata>>> RerankAsync<TMetadata>(
        string query,
        IReadOnlyList<RerankCandidate<TMetadata>> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty", nameof(query));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        _logger.LogDebug("Re-ranking {Count} candidates for query", candidates.Count);

        await EnsureModelLoadedAsync(cancellationToken);

        // Extract documents for scoring
        var documents = candidates.Select(c => c.Content).ToArray();

        // Score all documents against query
        var scores = await _model!.ScoreAsync(query, documents);

        // Build results with original indices
        var results = candidates
            .Select((candidate, index) => new RerankResult<TMetadata>
            {
                Index = index,
                Score = scores[index],
                OriginalScore = candidate.OriginalScore,
                Content = candidate.Content,
                MemoryId = candidate.MemoryId,
                Metadata = candidate.Metadata
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogDebug(
            "Re-ranked {Total} candidates to {TopK} results. Top score: {TopScore:F4}",
            candidates.Count, results.Count, results.FirstOrDefault()?.Score ?? 0);

        return results;
    }

    /// <inheritdoc />
    public async Task<float> ScoreAsync(
        string query,
        string document,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(document))
        {
            return 0f;
        }

        await EnsureModelLoadedAsync(cancellationToken);

        var scores = await _model!.ScoreAsync(query, [document]);
        return scores[0];
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

            _logger.LogInformation("Loading local re-ranker model: {ModelId}", _modelId);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _model = await LocalReranker.LoadAsync(_modelId);

            sw.Stop();
            _logger.LogInformation(
                "Model {ModelId} loaded in {ElapsedMs}ms",
                _modelId, sw.ElapsedMilliseconds);
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
        if (_model != null)
        {
            await _model.DisposeAsync();
        }
        _initLock.Dispose();
    }
}
