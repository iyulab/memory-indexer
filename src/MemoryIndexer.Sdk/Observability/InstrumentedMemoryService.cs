using System.Diagnostics;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Observability;

/// <summary>
/// Instrumented wrapper for MemoryService that automatically records telemetry.
/// </summary>
public sealed class InstrumentedMemoryService
{
    private readonly MemoryService _inner;
    private readonly ILogger<InstrumentedMemoryService> _logger;

    public InstrumentedMemoryService(
        MemoryService inner,
        ILogger<InstrumentedMemoryService> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    /// <summary>
    /// Stores a new memory with automatic embedding generation.
    /// </summary>
    public async Task<MemoryUnit> StoreAsync(
        string userId,
        string content,
        MemoryType type = MemoryType.Episodic,
        string? sessionId = null,
        float? importance = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryStore", "store");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.type", type.ToString());
            activity?.SetTag("memory.importance", importance ?? 0.5f);
            activity?.SetTag("memory.content_length", content.Length);
            activity?.SetTag("memory.user_id", userId);
            if (sessionId != null)
            {
                activity?.SetTag("memory.session_id", sessionId);
            }

            var result = await _inner.StoreAsync(userId, content, type, sessionId, importance, metadata, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.RecordStoreOperation(sw.Elapsed.TotalMilliseconds, 1, content.Length);
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.id", result.Id.ToString());

            _logger.LogDebug(
                "Stored memory {MemoryId} of type {Type} in {ElapsedMs:F2}ms",
                result.Id, type, sw.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);

            _logger.LogError(ex, "Failed to store memory after {ElapsedMs:F2}ms", sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Recalls memories relevant to the given query.
    /// </summary>
    public async Task<IReadOnlyList<MemorySearchResult>> RecallAsync(
        string userId,
        string query,
        int limit = 5,
        string? sessionId = null,
        MemoryType[]? types = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryRecall", "recall");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.query_length", query.Length);
            activity?.SetTag("memory.limit", limit);
            activity?.SetTag("memory.user_id", userId);
            if (sessionId != null)
            {
                activity?.SetTag("memory.session_id", sessionId);
            }
            if (types != null)
            {
                activity?.SetTag("memory.type_filter", string.Join(",", types));
            }

            var results = await _inner.RecallAsync(userId, query, limit, sessionId, types, cancellationToken);

            sw.Stop();
            var topScore = results.Count > 0 ? results[0].Score : (double?)null;
            MemoryIndexerTelemetry.RecordRecallOperation(sw.Elapsed.TotalMilliseconds, results.Count, topScore);
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.result_count", results.Count);
            if (topScore.HasValue)
            {
                activity?.SetTag("memory.top_score", topScore.Value);
            }

            _logger.LogDebug(
                "Recalled {Count} memories for query in {ElapsedMs:F2}ms (top score: {TopScore:F3})",
                results.Count, sw.Elapsed.TotalMilliseconds, topScore ?? 0);

            return results;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);

            _logger.LogError(ex, "Failed to recall memories after {ElapsedMs:F2}ms", sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Gets a memory by ID.
    /// </summary>
    public async Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryGet", "get");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.id", id.ToString());

            var result = await _inner.GetByIdAsync(id, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "get"));
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.found", result != null);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);
            throw;
        }
    }

    /// <summary>
    /// Gets all memories for a user.
    /// </summary>
    public async Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryList", "list");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.user_id", userId);

            var results = await _inner.GetAllAsync(userId, options, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "list"));
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.result_count", results.Count);

            return results;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);
            throw;
        }
    }

    /// <summary>
    /// Updates a memory's content with new embedding.
    /// </summary>
    public async Task<bool> UpdateContentAsync(
        Guid id,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryUpdate", "update");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.id", id.ToString());
            activity?.SetTag("memory.content_length", content.Length);

            var result = await _inner.UpdateContentAsync(id, content, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "update"));
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.success", result);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);
            throw;
        }
    }

    /// <summary>
    /// Updates a memory's importance score.
    /// </summary>
    public async Task<bool> UpdateImportanceAsync(
        Guid id,
        float importance,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryUpdateImportance", "update");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.id", id.ToString());
            activity?.SetTag("memory.importance", importance);

            var result = await _inner.UpdateImportanceAsync(id, importance, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "update_importance"));
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.success", result);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);
            throw;
        }
    }

    /// <summary>
    /// Deletes a memory.
    /// </summary>
    public async Task<bool> DeleteAsync(
        Guid id,
        bool hardDelete = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = MemoryIndexerTelemetry.StartOperation("MemoryDelete", "delete");
        var sw = Stopwatch.StartNew();

        try
        {
            activity?.SetTag("memory.id", id.ToString());
            activity?.SetTag("memory.hard_delete", hardDelete);

            var result = await _inner.DeleteAsync(id, hardDelete, cancellationToken);

            sw.Stop();
            MemoryIndexerTelemetry.MemoryOperations.Add(1, new KeyValuePair<string, object?>("operation", "delete"));
            MemoryIndexerTelemetry.CompleteOperation(activity, true);

            activity?.SetTag("memory.success", result);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            MemoryIndexerTelemetry.CompleteOperation(activity, false, ex);
            throw;
        }
    }
}
