using System.Collections.Concurrent;
using System.Diagnostics;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Summarization;

/// <summary>
/// Orchestrates session-aware summarization by integrating triggers, services, and memory storage.
/// </summary>
public sealed class SummarizationOrchestrator : ISummarizationOrchestrator
{
    private readonly ISummarizationTrigger _trigger;
    private readonly ISummarizationService _summarizer;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<SummarizationOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public SummarizationOrchestrator(
        ISummarizationTrigger trigger,
        ISummarizationService summarizer,
        IMemoryStore memoryStore,
        ILogger<SummarizationOrchestrator> logger)
    {
        _trigger = trigger;
        _summarizer = summarizer;
        _memoryStore = memoryStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public void StartSession(string sessionId, string userId, int maxTokenBudget = 100000)
    {
        var state = new SessionState
        {
            SessionId = sessionId,
            UserId = userId,
            MaxTokenBudget = maxTokenBudget
        };

        if (!_sessions.TryAdd(sessionId, state))
        {
            _logger.LogWarning("Session {SessionId} already exists, resetting state", sessionId);
            _sessions[sessionId] = state;
        }

        _trigger.RegisterEvent(sessionId, SessionEventType.SessionStart);
        _logger.LogInformation("Started tracking session {SessionId} for user {UserId}", sessionId, userId);
    }

    /// <inheritdoc />
    public async Task<MemorySummary?> EndSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            _logger.LogWarning("Cannot end unknown session {SessionId}", sessionId);
            return null;
        }

        state.IsEnding = true;
        _trigger.RegisterEvent(sessionId, SessionEventType.SessionEnd);

        // Evaluate if final summarization is needed
        var evaluation = await _trigger.EvaluateAsync(state.ToContext(), cancellationToken);

        MemorySummary? finalSummary = null;
        if (evaluation.ShouldSummarize || state.MemoriesCreated > 0)
        {
            var result = await ExecuteSummarizationAsync(state, evaluation.RecommendedStrategy, cancellationToken);
            finalSummary = result.Summary;
        }

        _sessions.TryRemove(sessionId, out _);
        _logger.LogInformation(
            "Ended session {SessionId}: {MemoryCount} memories, {SummaryCount} summaries generated",
            sessionId, state.MemoriesCreated, state.Summaries.Count);

        return finalSummary;
    }

    /// <inheritdoc />
    public async Task<SummarizationResult?> RecordMemoryAsync(
        string sessionId,
        MemoryUnit memory,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            _logger.LogDebug("Session {SessionId} not tracked, skipping memory recording", sessionId);
            return null;
        }

        // Update session state
        state.MemoriesCreated++;
        state.MemoryIds.Add(memory.Id);
        state.AccumulatedImportance += memory.ImportanceScore;

        // Estimate token count (rough: 4 chars = 1 token)
        var estimatedTokens = memory.Content.Length / 4;
        state.CurrentTokenCount += estimatedTokens;

        // Register event with importance metadata
        _trigger.RegisterEvent(sessionId, SessionEventType.MemoryStored, new Dictionary<string, string>
        {
            ["importance"] = memory.ImportanceScore.ToString("F2"),
            ["memoryId"] = memory.Id.ToString()
        });

        _logger.LogDebug(
            "Recorded memory {MemoryId} for session {SessionId}: importance={Importance:F2}",
            memory.Id, sessionId, memory.ImportanceScore);

        // Evaluate if summarization should be triggered
        var evaluation = await _trigger.EvaluateAsync(state.ToContext(), cancellationToken);

        if (!evaluation.ShouldSummarize)
        {
            return SummarizationResult.Skipped(evaluation);
        }

        _logger.LogInformation(
            "Summarization triggered for session {SessionId}: {Condition} - {Priority}",
            sessionId, evaluation.Condition, evaluation.Priority);

        return await ExecuteSummarizationAsync(state, evaluation.RecommendedStrategy, cancellationToken);
    }

    /// <inheritdoc />
    public void RecordMessage(string sessionId, int tokenCount, bool isUserMessage)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return;
        }

        state.MessageCount++;
        state.CurrentTokenCount += tokenCount;

        var eventType = isUserMessage ? SessionEventType.UserMessage : SessionEventType.AssistantResponse;
        _trigger.RegisterEvent(sessionId, eventType, new Dictionary<string, string>
        {
            ["tokenCount"] = tokenCount.ToString()
        });
    }

    /// <inheritdoc />
    public async Task<SummarizationResult> TriggerSummarizationAsync(
        string sessionId,
        SummarizationStrategy? strategy = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return SummarizationResult.Failed($"Session {sessionId} not found");
        }

        _trigger.RegisterEvent(sessionId, SessionEventType.ManualRequest);

        // Use provided strategy or evaluate to get recommended
        var useStrategy = strategy;
        if (!useStrategy.HasValue)
        {
            var evaluation = await _trigger.EvaluateAsync(state.ToContext(), cancellationToken);
            useStrategy = evaluation.RecommendedStrategy;
        }

        return await ExecuteSummarizationAsync(state, useStrategy.Value, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TriggerEvaluation> EvaluateTriggerAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return new TriggerEvaluation
            {
                ShouldSummarize = false,
                Explanation = $"Session {sessionId} not found"
            };
        }

        return await _trigger.EvaluateAsync(state.ToContext(), cancellationToken);
    }

    /// <inheritdoc />
    public SessionState? GetSessionState(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var state) ? state : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetActiveSessionIds()
    {
        return _sessions.Keys.ToList().AsReadOnly();
    }

    private async Task<SummarizationResult> ExecuteSummarizationAsync(
        SessionState state,
        SummarizationStrategy strategy,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Fetch memories to summarize
            var memories = await _memoryStore.GetByIdsAsync(state.MemoryIds, cancellationToken);

            if (memories.Count == 0)
            {
                return new SummarizationResult
                {
                    Summarized = false,
                    Strategy = strategy,
                    Error = "No memories to summarize"
                };
            }

            var tokensBefore = state.CurrentTokenCount;
            MemorySummary summary;

            // Choose summarization approach based on strategy
            switch (strategy)
            {
                case SummarizationStrategy.Archive:
                    // For archive, create a hierarchical summary
                    var hierarchy = await _summarizer.CreateHierarchyAsync(memories, 2, cancellationToken);
                    summary = hierarchy.RootSummary;
                    break;

                case SummarizationStrategy.Reflection:
                    // For reflection, use incremental update if we have existing summaries
                    if (state.Summaries.Count > 0)
                    {
                        var latestSummary = state.Summaries[^1];
                        summary = await _summarizer.IncrementalUpdateAsync(latestSummary, memories, cancellationToken);
                    }
                    else
                    {
                        summary = await _summarizer.SummarizeAsync(memories, new SummarizationOptions
                        {
                            Style = SummaryStyle.Hybrid,
                            PreserveEntities = true
                        }, cancellationToken);
                    }
                    break;

                case SummarizationStrategy.Compression:
                    // Compression: aggressive token reduction
                    summary = await _summarizer.SummarizeAsync(memories, new SummarizationOptions
                    {
                        TargetCompressionRatio = 0.3f, // Aggressive compression
                        Style = SummaryStyle.Extractive
                    }, cancellationToken);
                    break;

                case SummarizationStrategy.Hybrid:
                    // Hybrid: balanced approach
                    summary = await _summarizer.SummarizeAsync(memories, new SummarizationOptions
                    {
                        TargetCompressionRatio = 0.5f,
                        Style = SummaryStyle.Hybrid,
                        PreserveEntities = true
                    }, cancellationToken);
                    break;

                case SummarizationStrategy.Extractive:
                default:
                    // Extractive: preserve key sentences
                    summary = await _summarizer.SummarizeAsync(memories, new SummarizationOptions
                    {
                        Style = SummaryStyle.Extractive
                    }, cancellationToken);
                    break;
            }

            stopwatch.Stop();

            // Update session state
            state.Summaries.Add(summary);
            state.LastSummarizedAt = DateTime.UtcNow;

            // Reset accumulated metrics after summarization
            var summarizedMemoryIds = summary.SourceMemoryIds.ToHashSet();
            state.MemoryIds.RemoveAll(id => summarizedMemoryIds.Contains(id));
            state.AccumulatedImportance = 0;

            // Update token count based on compression
            var tokensAfter = tokensBefore - summary.OriginalTokenCount + summary.SummarizedTokenCount;
            state.CurrentTokenCount = Math.Max(0, tokensAfter);

            _logger.LogInformation(
                "Summarization completed for session {SessionId}: {Strategy}, {MemoriesProcessed} memories, " +
                "{TokensBefore} -> {TokensAfter} tokens ({CompressionRatio:P0} compression) in {Duration}ms",
                state.SessionId, strategy, memories.Count,
                tokensBefore, tokensAfter, summary.CompressionRatio, stopwatch.ElapsedMilliseconds);

            return new SummarizationResult
            {
                Summarized = true,
                Summary = summary,
                Strategy = strategy,
                TokensBefore = tokensBefore,
                TokensAfter = tokensAfter,
                MemoriesProcessed = memories.Count,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Summarization failed for session {SessionId}", state.SessionId);

            return new SummarizationResult
            {
                Summarized = false,
                Strategy = strategy,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }
}
