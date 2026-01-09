using System.Diagnostics;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Completion.Providers;

/// <summary>
/// Text completion service using LMSupply.Generator for local ONNX-based model inference.
/// Supports models like Phi-4, Phi-3.5, Llama 3.2, and other ONNX generation models.
/// </summary>
/// <remarks>
/// LMSupply.Generator is an open-source library by iyulab that provides fast,
/// local text generation using ONNX Runtime GenAI. Models are downloaded
/// automatically on first use and cached locally.
/// </remarks>
public sealed class LocalTextCompletionService : ITextCompletionService, IAsyncDisposable
{
    private readonly ILogger<LocalTextCompletionService> _logger;
    private readonly string _modelId;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IGeneratorModel? _model;
    private bool _disposed;

    /// <summary>
    /// Default model ID if not specified in configuration.
    /// Phi-4-mini is a good balance of speed and quality (MIT license).
    /// </summary>
    public const string DefaultModelId = "microsoft/Phi-4-mini-instruct-onnx";

    /// <summary>
    /// Supported local generation models with their context lengths.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> SupportedModels = new Dictionary<string, int>
    {
        ["microsoft/Phi-4-mini-instruct-onnx"] = 16384,
        ["microsoft/phi-4-onnx"] = 16384,
        ["microsoft/Phi-3.5-mini-instruct-onnx"] = 131072,
        ["onnx-community/Llama-3.2-1B-Instruct-ONNX"] = 131072,
        ["onnx-community/Llama-3.2-3B-Instruct-ONNX"] = 131072,
        ["Qwen/Qwen2.5-3B-Instruct-ONNX"] = 131072,
        ["google/gemma-2-2b-it-onnx"] = 8192
    };

    public LocalTextCompletionService(
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalTextCompletionService> logger)
    {
        _logger = logger;

        var completionOptions = options.Value.Completion;

        _modelId = !string.IsNullOrEmpty(completionOptions.Model)
            ? completionOptions.Model
            : DefaultModelId;

        _logger.LogInformation(
            "LocalTextCompletionService initialized with model {ModelId}",
            _modelId);
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("TextCompletion", "completion");
        activity?.SetTag("completion.provider", "local");
        activity?.SetTag("completion.model", _modelId);
        activity?.SetTag("completion.prompt_length", prompt?.Length ?? 0);

        var sw = Stopwatch.StartNew();

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                activity?.SetTag("completion.empty_input", true);
                return string.Empty;
            }

            await EnsureModelLoadedAsync(cancellationToken);

            var generationOptions = MapToGenerationOptions(options);
            var result = await _model!.GenerateCompleteAsync(prompt, generationOptions, cancellationToken);

            activity?.SetTag("completion.output_length", result.Length);
            MemoryIndexerTelemetry.CompleteOperation(activity, success: true);

            _logger.LogDebug(
                "Generated completion: {OutputLength} chars in {ElapsedMs}ms",
                result.Length, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            MemoryIndexerTelemetry.CompleteOperation(activity, success: false, exception: ex);
            _logger.LogError(ex, "Text completion failed for prompt of length {Length}", prompt?.Length ?? 0);
            throw;
        }
        finally
        {
            sw.Stop();
            MemoryIndexerTelemetry.RecordCompletionOperation(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("TextCompletionBatch", "completion");
        activity?.SetTag("completion.provider", "local");
        activity?.SetTag("completion.model", _modelId);

        var sw = Stopwatch.StartNew();

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var promptList = prompts.ToList();
            activity?.SetTag("completion.batch_size", promptList.Count);

            if (promptList.Count == 0)
            {
                activity?.SetTag("completion.empty_batch", true);
                return [];
            }

            _logger.LogDebug("Generating batch completions for {Count} prompts", promptList.Count);

            await EnsureModelLoadedAsync(cancellationToken);

            var generationOptions = MapToGenerationOptions(options);
            var results = new List<string>(promptList.Count);

            // Process sequentially as local models typically don't support parallel inference
            foreach (var prompt in promptList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    results.Add(string.Empty);
                    continue;
                }

                var result = await _model!.GenerateCompleteAsync(prompt, generationOptions, cancellationToken);
                results.Add(result);
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
            MemoryIndexerTelemetry.RecordCompletionOperation(sw.Elapsed.TotalMilliseconds);
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

            using var activity = MemoryIndexerTelemetry.StartOperation("GeneratorModelLoad", "completion");
            activity?.SetTag("completion.provider", "local");
            activity?.SetTag("completion.model", _modelId);

            _logger.LogInformation("Loading local generator model: {ModelId}", _modelId);
            var sw = Stopwatch.StartNew();

            try
            {
                _model = await TextGeneratorBuilder.Create()
                    .WithHuggingFaceModel(_modelId)
                    .BuildAsync(cancellationToken);

                sw.Stop();
                activity?.SetTag("completion.load_time_ms", sw.ElapsedMilliseconds);
                activity?.SetTag("completion.max_context_length", _model.MaxContextLength);

                _logger.LogInformation(
                    "Model {ModelId} loaded in {ElapsedMs}ms, max context: {MaxContext}",
                    _modelId, sw.ElapsedMilliseconds, _model.MaxContextLength);

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
    /// Maps MemoryIndexer's TextCompletionOptions to LMSupply's GenerationOptions.
    /// </summary>
    private static GenerationOptions MapToGenerationOptions(TextCompletionOptions? options)
    {
        if (options is null)
            return GenerationOptions.Default;

        var generationOptions = new GenerationOptions
        {
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            StopSequences = options.StopSequences
        };

        if (options.TopP.HasValue)
            generationOptions.TopP = options.TopP.Value;

        // Map presence/frequency penalty to repetition penalty if specified
        // LMSupply uses RepetitionPenalty instead of separate presence/frequency penalties
        if (options.PresencePenalty.HasValue || options.FrequencyPenalty.HasValue)
        {
            // Average the penalties and convert to repetition penalty scale (1.0 = no penalty)
            var avgPenalty = ((options.PresencePenalty ?? 0) + (options.FrequencyPenalty ?? 0)) / 2;
            // Convert from -2.0 to 2.0 range to 0.8 to 1.5 range
            generationOptions.RepetitionPenalty = 1.0f + (avgPenalty * 0.25f);
        }

        return generationOptions;
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
