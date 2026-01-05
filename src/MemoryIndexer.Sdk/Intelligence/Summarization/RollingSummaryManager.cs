using System.Collections.Concurrent;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Summarization;

/// <summary>
/// Manages rolling summaries for active sessions with periodic consolidation.
/// Implements a sliding window approach to maintain fresh, up-to-date summaries.
/// </summary>
public sealed class RollingSummaryManager : IRollingSummaryManager
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
            _logger.LogWarning("Rolling summary already initialized for session {SessionId}, resetting", sessionId);
            _states[sessionId] = state;
        }

        _logger.LogDebug(
            "Initialized rolling summary for session {SessionId}: turnInterval={TurnInterval}, " +
            "timeInterval={TimeInterval}, maxWindow={MaxWindow}",
            sessionId, state.Config.TurnInterval, state.Config.TimeInterval, state.Config.MaxWindowSize);
    }

    /// <inheritdoc />
    public async Task<MemorySummary?> RecordAsync(
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            _logger.LogDebug("Session {SessionId} not initialized for rolling summary", sessionId);
            return null;
        }

        // Add to window
        state.WindowMemories.Add(memory);
        state.TotalMemoriesProcessed++;

        // Estimate token count (rough: 4 chars = 1 token)
        var estimatedTokens = memory.Content.Length / 4;
        state.WindowTokenCount += estimatedTokens;

        _logger.LogDebug(
            "Added memory to rolling window for session {SessionId}: " +
            "windowSize={WindowSize}, windowTokens={WindowTokens}",
            sessionId, state.WindowMemories.Count, state.WindowTokenCount);

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

        _logger.LogInformation(
            "Finalized rolling summary for session {SessionId}: " +
            "{TotalSummaries} summaries, {TotalMemories} memories processed",
            sessionId, state.TotalSummariesGenerated, state.TotalMemoriesProcessed);

        return finalSummary;
    }

    /// <inheritdoc />
    public void Remove(string sessionId)
    {
        if (_states.TryRemove(sessionId, out var state))
        {
            _logger.LogDebug(
                "Removed rolling summary state for session {SessionId}: " +
                "{TotalSummaries} summaries, {TotalMemories} memories",
                sessionId, state.TotalSummariesGenerated, state.TotalMemoriesProcessed);
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

            _logger.LogDebug(
                "Incremental summary update for session {SessionId}: " +
                "{NewMemories} new memories merged",
                state.SessionId, state.WindowMemories.Count);
        }
        else if (state.WindowMemories.Count > 0)
        {
            // Full summarization of window memories
            newSummary = await _summarizer.SummarizeAsync(
                state.WindowMemories,
                options,
                cancellationToken);

            _logger.LogDebug(
                "Full summary generated for session {SessionId}: " +
                "{MemoryCount} memories → {TokenCount} tokens",
                state.SessionId, state.WindowMemories.Count, newSummary.SummarizedTokenCount);
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

        _logger.LogInformation(
            "Rolling summary updated for session {SessionId}: " +
            "compressionRatio={Ratio:P0}, summaryCount={Count}",
            state.SessionId, newSummary.CompressionRatio, state.TotalSummariesGenerated);

        return newSummary;
    }
}
