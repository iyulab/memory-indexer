using System.Collections.Concurrent;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Summarization;

/// <summary>
/// Manages rolling summaries for active sessions with periodic consolidation.
/// Implements a sliding window approach to maintain fresh, up-to-date summaries.
/// </summary>
public sealed partial class RollingSummaryManager : IRollingSummaryManager
{
    private readonly ISummarizationService _summarizer;
    private readonly ILogger<RollingSummaryManager> _logger;
    private readonly ConcurrentDictionary<string, RollingSummaryState> _states = new();

    public RollingSummaryManager(
        ISummarizationService summarizer,
        ILogger<RollingSummaryManager> logger)
    {
        _summarizer = summarizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Initialize(string sessionId, string userId, RollingSummaryConfig? config = null)
    {
        var state = new RollingSummaryState
        {
            SessionId = sessionId,
            UserId = userId,
            Config = config ?? RollingSummaryConfig.Default
        };

        if (!_states.TryAdd(sessionId, state))
        {
            LogRollingSummaryAlreadyInitialized(_logger, sessionId);
            _states[sessionId] = state;
        }

        LogInitializedRollingSummary(_logger, sessionId, state.Config.TurnInterval, state.Config.TimeInterval, state.Config.MaxWindowSize);
    }

    /// <inheritdoc />
    public async Task<MemorySummary?> RecordAsync(
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            LogSessionNotInitialized(_logger, sessionId);
            return null;
        }

        // Add to window
        state.WindowMemories.Add(memory);
        state.TotalMemoriesProcessed++;

        // Estimate token count (rough: 4 chars = 1 token)
        var estimatedTokens = memory.Content.Length / 4;
        state.WindowTokenCount += estimatedTokens;

        LogAddedMemoryToWindow(_logger, sessionId, state.WindowMemories.Count, state.WindowTokenCount);

        // Check if we should trigger summarization
        if (state.ShouldTriggerSummary())
        {
            return await UpdateSummaryAsync(state, cancellationToken);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<MemorySummary?> RecordTurnAsync(
        string sessionId,
        int turnTokens,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            return null;
        }

        state.TurnsSinceLastSummary++;
        state.WindowTokenCount += turnTokens;

        if (state.ShouldTriggerSummary())
        {
            return await UpdateSummaryAsync(state, cancellationToken);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<MemorySummary> ForceUpdateAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"Session {sessionId} not initialized for rolling summary");
        }

        return await UpdateSummaryAsync(state, cancellationToken);
    }

    /// <inheritdoc />
    public MemorySummary? GetCurrentSummary(string sessionId)
    {
        return _states.TryGetValue(sessionId, out var state) ? state.CurrentSummary : null;
    }

    /// <inheritdoc />
    public RollingSummaryState? GetState(string sessionId)
    {
        return _states.TryGetValue(sessionId, out var state) ? state : null;
    }

    /// <inheritdoc />
    public async Task<MemorySummary> FinalizeAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            throw new InvalidOperationException($"Session {sessionId} not initialized for rolling summary");
        }

        // Generate final summary including any remaining window memories
        var finalSummary = await UpdateSummaryAsync(state, cancellationToken, isFinal: true);

        // Remove state after finalization
        _states.TryRemove(sessionId, out _);

        LogFinalizedRollingSummary(_logger, sessionId, state.TotalSummariesGenerated, state.TotalMemoriesProcessed);

        return finalSummary;
    }

    /// <inheritdoc />
    public void Remove(string sessionId)
    {
        if (_states.TryRemove(sessionId, out var state))
        {
            LogRemovedRollingSummaryState(_logger, sessionId, state.TotalSummariesGenerated, state.TotalMemoriesProcessed);
        }
    }

    private async Task<MemorySummary> UpdateSummaryAsync(
        RollingSummaryState state,
        CancellationToken cancellationToken,
        bool isFinal = false)
    {
        MemorySummary newSummary;

        if (state.WindowMemories.Count == 0 && state.CurrentSummary != null)
        {
            // No new memories, return existing summary
            return state.CurrentSummary;
        }

        var options = new SummarizationOptions
        {
            TargetCompressionRatio = state.Config.TargetCompressionRatio,
            Style = isFinal ? SummaryStyle.Hybrid : SummaryStyle.Extractive,
            PreserveEntities = true,
            PreserveTimestamps = true
        };

        if (state.Config.UseIncrementalUpdates && state.CurrentSummary != null && state.WindowMemories.Count > 0)
        {
            // Incremental update: merge new memories with existing summary
            newSummary = await _summarizer.IncrementalUpdateAsync(
                state.CurrentSummary,
                state.WindowMemories,
                cancellationToken);

            LogIncrementalSummaryUpdate(_logger, state.SessionId, state.WindowMemories.Count);
        }
        else if (state.WindowMemories.Count > 0)
        {
            // Full summarization of window memories
            newSummary = await _summarizer.SummarizeAsync(
                state.WindowMemories,
                options,
                cancellationToken);

            LogFullSummaryGenerated(_logger, state.SessionId, state.WindowMemories.Count, newSummary.SummarizedTokenCount);
        }
        else if (state.CurrentSummary != null)
        {
            // No window memories, return current summary
            return state.CurrentSummary;
        }
        else
        {
            // No memories at all - return empty summary
            newSummary = new MemorySummary
            {
                Content = "No memories recorded in this session.",
                OriginalTokenCount = 0,
                SummarizedTokenCount = 0
            };
        }

        // Update state
        state.CurrentSummary = newSummary;
        state.WindowMemories.Clear();
        state.WindowTokenCount = 0;
        state.TurnsSinceLastSummary = 0;
        state.LastSummaryAt = DateTime.UtcNow;
        state.TotalSummariesGenerated++;

        LogRollingSummaryUpdated(_logger, state.SessionId, newSummary.CompressionRatio, state.TotalSummariesGenerated);

        return newSummary;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rolling summary already initialized for session {SessionId}, resetting")]
    private static partial void LogRollingSummaryAlreadyInitialized(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initialized rolling summary for session {SessionId}: turnInterval={TurnInterval}, timeInterval={TimeInterval}, maxWindow={MaxWindow}")]
    private static partial void LogInitializedRollingSummary(ILogger logger, string sessionId, int turnInterval, TimeSpan timeInterval, int maxWindow);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session {SessionId} not initialized for rolling summary")]
    private static partial void LogSessionNotInitialized(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added memory to rolling window for session {SessionId}: windowSize={WindowSize}, windowTokens={WindowTokens}")]
    private static partial void LogAddedMemoryToWindow(ILogger logger, string sessionId, int windowSize, int windowTokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finalized rolling summary for session {SessionId}: {TotalSummaries} summaries, {TotalMemories} memories processed")]
    private static partial void LogFinalizedRollingSummary(ILogger logger, string sessionId, int totalSummaries, int totalMemories);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed rolling summary state for session {SessionId}: {TotalSummaries} summaries, {TotalMemories} memories")]
    private static partial void LogRemovedRollingSummaryState(ILogger logger, string sessionId, int totalSummaries, int totalMemories);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Incremental summary update for session {SessionId}: {NewMemories} new memories merged")]
    private static partial void LogIncrementalSummaryUpdate(ILogger logger, string sessionId, int newMemories);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Full summary generated for session {SessionId}: {MemoryCount} memories -> {TokenCount} tokens")]
    private static partial void LogFullSummaryGenerated(ILogger logger, string sessionId, int memoryCount, int tokenCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rolling summary updated for session {SessionId}: compressionRatio={Ratio:P0}, summaryCount={Count}")]
    private static partial void LogRollingSummaryUpdated(ILogger logger, string sessionId, float ratio, int count);
}
