using LMSupply.Embedder;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Embedding;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpServer;

/// <summary>
/// The server's embedder for <c>Embedding:Provider = Custom</c>: an LMSupply model run in-process (ONNX), named by
/// <c>Embedding:Model</c> as an LMSupply id or alias (<c>"default"</c> is BAAI/bge-m3, 1024 dimensions). The library
/// builds no embedding client; this is the bundled application supplying one, so the server works without an external
/// embedding service.
/// </summary>
/// <remarks>
/// The model is downloaded on first use; <see cref="WarmUpAsync"/> starts that at server start so the first tool call
/// does not wait for it. A catalog model whose dimensions differ from <c>Embedding:Dimensions</c> stops the server at
/// start (the store's vector size is fixed by that setting); a model outside the catalog is checked on its first embedding.
/// </remarks>
public sealed partial class LocalEmbeddingService : CachedEmbeddingServiceBase, IAsyncDisposable, IDisposable
{
    private readonly string _modelId;
    private readonly TimeSpan _timeout;
    private readonly Lazy<Task<IEmbeddingModel>> _model;

    /// <summary>Creates the service; fails when a catalog model's dimensions differ from the configured ones.</summary>
    public LocalEmbeddingService(IMemoryCache cache, ILogger<LocalEmbeddingService> logger, IOptions<MemoryIndexerOptions> options)
        : base(cache, logger, options.Value.Embedding)
    {
        var embedding = options.Value.Embedding;
        _modelId = embedding.Model;
        _timeout = TimeSpan.FromSeconds(embedding.TimeoutSeconds);
        Dimensions = embedding.Dimensions;

        if (LocalEmbedder.Registry.TryResolve(_modelId, out var known) && known is not null && known.Dimensions != Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding:Model '{_modelId}' produces {known.Dimensions}-dimensional vectors but Embedding:Dimensions is " +
                $"{Dimensions}. Set Embedding:Dimensions (and the store's vector dimensions) to {known.Dimensions}, or choose " +
                "a model of that size.");
        }

        _model = new Lazy<Task<IEmbeddingModel>>(LoadAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public override int Dimensions { get; }

    /// <inheritdoc />
    protected override string CacheKeyPrefix => "lmsupply:" + _modelId;

    /// <summary>Starts loading (and on first run downloading) the model in the background.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _model.Value.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Reported again by the first embedding that needs the model
            LogWarmUpFailed(Logger, ex, _modelId);
        }
    }

    /// <inheritdoc />
    protected override async Task<ReadOnlyMemory<float>> GenerateSingleEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var model = await _model.Value.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        return await model.EmbedAsync(text, timeout.Token);
    }

    /// <inheritdoc />
    protected override async Task ProcessUncachedBatchAsync(
        List<string> allTexts,
        ReadOnlyMemory<float>[] results,
        List<(int Index, string Text)> uncached,
        CancellationToken cancellationToken)
    {
        var model = await _model.Value.WaitAsync(cancellationToken);
        foreach (var batch in uncached.Chunk(BatchSize))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            var vectors = await model.EmbedAsync([.. batch.Select(item => item.Text)], timeout.Token);
            for (var i = 0; i < batch.Length; i++)
            {
                results[batch[i].Index] = vectors[i];
                CacheEmbedding(batch[i].Text, vectors[i]);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_model.IsValueCreated && _model.Value.IsCompletedSuccessfully)
        {
            await _model.Value.Result.DisposeAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>A container that disposes synchronously (a plain <c>using</c> scope) releases the model here.</remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task<IEmbeddingModel> LoadAsync()
    {
        var model = await LocalEmbedder.LoadAsync(_modelId);
        if (model.Dimensions != Dimensions)
        {
            await model.DisposeAsync();
            throw new InvalidOperationException(
                $"Embedding:Model '{_modelId}' produces {model.Dimensions}-dimensional vectors but Embedding:Dimensions is " +
                $"{Dimensions}. Set Embedding:Dimensions (and the store's vector dimensions) to {model.Dimensions}.");
        }

        LogModelLoaded(Logger, _modelId, model.Dimensions);
        return model;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local embedding model {ModelId} loaded ({Dimensions} dimensions)")]
    private static partial void LogModelLoaded(ILogger logger, string modelId, int dimensions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local embedding model {ModelId} could not be loaded at start")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception, string modelId);
}

/// <summary>Registers the server's local embedder when the configuration asks for the application's own service.</summary>
public static class LocalEmbeddingRegistration
{
    /// <summary>
    /// With <c>MemoryIndexer:Embedding:Provider = Custom</c>, registers <see cref="LocalEmbeddingService"/> as the
    /// <see cref="IEmbeddingService"/>. Call before <c>AddMemoryIndexer()</c>; Mock stays the library's.
    /// </summary>
    public static IServiceCollection AddServerEmbedder(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var provider = configuration[$"{MemoryIndexerOptions.SectionName}:Embedding:Provider"];
        var custom = provider is null || string.Equals(provider, nameof(EmbeddingProvider.Custom), StringComparison.OrdinalIgnoreCase);
        if (custom)
        {
            services.TryAddSingleton<LocalEmbeddingService>();
            services.TryAddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<LocalEmbeddingService>());
        }

        return services;
    }

    /// <summary>Starts loading the local model in the background, when the server uses it.</summary>
    public static void StartEmbedderWarmUp(IServiceProvider services)
    {
        if (services.GetService<IEmbeddingService>() is LocalEmbeddingService local)
        {
            _ = local.WarmUpAsync();
        }
    }
}
