using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Intelligence.Summarization;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.SelfEditing;

/// <summary>
/// Implements MemGPT-style self-editing memory management.
/// Enables LLMs to manage their own context through structured memory operations.
/// </summary>
public sealed class MemGPTStyleMemoryManager : ISelfEditingMemoryService
{
    private readonly MemoryService _memoryService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISummarizationService _summarizationService;
    private readonly ILogger<MemGPTStyleMemoryManager> _logger;

    // Working memory cache per session
    private readonly Dictionary<string, WorkingMemoryState> _workingMemoryCache = [];
    private readonly object _cacheLock = new();

    // Configuration
    private const string DefaultUserId = "system";
    private const int DefaultMaxTokens = 128000;
    private const float ReflectionThreshold = 10.0f; // Sum of importance scores
    private const int MaxRecentSummaries = 5;
    private const float TokensPerWord = 1.3f; // Approximate

    public MemGPTStyleMemoryManager(
        MemoryService memoryService,
        IEmbeddingService embeddingService,
        ISummarizationService summarizationService,
        ILogger<MemGPTStyleMemoryManager> logger)
    {
        _memoryService = memoryService;
        _embeddingService = embeddingService;
        _summarizationService = summarizationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<MemoryOperationResult> ReplaceWorkingMemoryAsync(
        string location,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory("default");
        string? previousContent = null;

        lock (_cacheLock)
        {
            switch (location.ToLowerInvariant())
            {
                case "core":
                case "core_memory":
                    previousContent = state.CoreMemory;
                    state.CoreMemory = newContent;
                    break;

                case "context":
                case "conversation":
                case "conversation_context":
                    previousContent = state.ConversationContext;
                    state.ConversationContext = newContent;
                    break;

                default:
                    return Task.FromResult(new MemoryOperationResult
                    {
                        Success = false,
                        Message = $"Unknown memory location: {location}. Valid locations: core, context"
                    });
            }

            state.LastUpdated = DateTime.UtcNow;
            UpdateTokenCount(state);
        }

        _logger.LogDebug("Replaced working memory at {Location}, previous length: {PrevLen}, new length: {NewLen}",
            location, previousContent?.Length ?? 0, newContent.Length);

        return Task.FromResult(new MemoryOperationResult
        {
            Success = true,
            Message = $"Successfully replaced content at {location}",
            PreviousContent = previousContent
        });
    }

    /// <inheritdoc />
    public async Task<ArchivalInsertResult> InsertArchivalMemoryAsync(
        string content,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var storedMemory = await _memoryService.StoreAsync(
            userId: DefaultUserId,
            content: content,
            type: MemoryType.Semantic, // Use Semantic for archival (long-term knowledge)
            sessionId: null,
            importance: 0.7f,
            metadata: metadata,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Archived memory {MemoryId} with {Tokens} estimated tokens",
            storedMemory.Id, EstimateTokens(content));

        return new ArchivalInsertResult
        {
            Success = true,
            MemoryId = storedMemory.Id,
            TokenCount = EstimateTokens(content)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArchivalSearchResult>> SearchArchivalMemoryAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var searchResults = await _memoryService.RecallAsync(
            userId: DefaultUserId,
            query: query,
            limit: maxResults,
            cancellationToken: cancellationToken);

        var results = searchResults
            .Select(r => new ArchivalSearchResult
            {
                MemoryId = r.Memory.Id,
                Content = r.Memory.Content,
                RelevanceScore = r.Score,
                CreatedAt = r.Memory.CreatedAt,
                Metadata = r.Memory.Metadata.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.ToString() ?? string.Empty)
            })
            .ToList();

        _logger.LogDebug("Searched archival memory for '{Query}', found {Count} results",
            query, results.Count);

        return results;
    }

    /// <inheritdoc />
    public Task<WorkingMemorySnapshot> GetWorkingMemoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory(sessionId);

        lock (_cacheLock)
        {
            return Task.FromResult(new WorkingMemorySnapshot
            {
                SessionId = sessionId,
                CoreMemory = state.CoreMemory,
                ConversationContext = state.ConversationContext,
                RecentSummaries = [.. state.RecentSummaries],
                CurrentTokenCount = state.CurrentTokenCount,
                MaxTokenCapacity = state.MaxTokenCapacity,
                AccumulatedImportance = state.AccumulatedImportance,
                LastUpdated = state.LastUpdated
            });
        }
    }

    /// <inheritdoc />
    public async Task<WorkingMemoryUpdateResult> UpdateWorkingMemoryAsync(
        string sessionId,
        string newContext,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory(sessionId);
        var result = new WorkingMemoryUpdateResult { Success = true };
        string? archivedContent = null;

        lock (_cacheLock)
        {
            // Append new context
            if (!string.IsNullOrEmpty(state.ConversationContext))
            {
                state.ConversationContext += "\n\n" + newContext;
            }
            else
            {
                state.ConversationContext = newContext;
            }

            // Update importance (estimate based on content characteristics)
            var importance = EstimateImportance(newContext);
            state.AccumulatedImportance += importance;

            UpdateTokenCount(state);

            // Check if we need to truncate
            if (state.CurrentTokenCount > state.MaxTokenCapacity * 0.9)
            {
                result.WasTruncated = true;
                archivedContent = TruncateOldContent(state);
            }

            state.LastUpdated = DateTime.UtcNow;
            result.NewTokenCount = state.CurrentTokenCount;
        }

        // Archive truncated content if any
        if (!string.IsNullOrEmpty(archivedContent))
        {
            await InsertArchivalMemoryAsync(archivedContent, new Dictionary<string, string>
            {
                ["source"] = "truncation",
                ["session_id"] = sessionId
            }, cancellationToken);
            result.ArchivedContent = archivedContent;
        }

        // Check if reflection is needed
        var reflectionCheck = await ShouldTriggerReflectionAsync(sessionId, cancellationToken);
        result.ReflectionRecommended = reflectionCheck.ShouldReflect;

        return result;
    }

    /// <inheritdoc />
    public Task<ReflectionCheckResult> ShouldTriggerReflectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory(sessionId);

        lock (_cacheLock)
        {
            var shouldReflect = state.AccumulatedImportance >= ReflectionThreshold;
            var reason = shouldReflect
                ? $"Accumulated importance ({state.AccumulatedImportance:F2}) exceeds threshold ({ReflectionThreshold})"
                : $"Accumulated importance ({state.AccumulatedImportance:F2}) below threshold ({ReflectionThreshold})";

            return Task.FromResult(new ReflectionCheckResult
            {
                ShouldReflect = shouldReflect,
                AccumulatedImportance = state.AccumulatedImportance,
                Threshold = ReflectionThreshold,
                Reason = reason
            });
        }
    }

    /// <inheritdoc />
    public async Task<ReflectionResult> PerformReflectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory(sessionId);
        var result = new ReflectionResult { Success = true };

        // Get content to reflect on
        string contentToReflect;
        lock (_cacheLock)
        {
            contentToReflect = state.ConversationContext;
        }

        if (string.IsNullOrWhiteSpace(contentToReflect))
        {
            result.Success = false;
            return result;
        }

        // Generate summary of current context
        var memoryForSummarization = new MemoryUnit
        {
            Content = contentToReflect,
            Type = MemoryType.Episodic, // Conversation context is episodic
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var summary = await _summarizationService.SummarizeAsync(
            [memoryForSummarization],
            options: null,
            cancellationToken: cancellationToken);

        // Extract insights (key sentences from summary)
        var insights = summary.KeyPoints?.ToList() ?? [];
        if (insights.Count == 0 && !string.IsNullOrEmpty(summary.Content))
        {
            insights = summary.Content.Split(". ", StringSplitOptions.RemoveEmptyEntries)
                .Take(3)
                .Select(s => s.Trim())
                .ToList();
        }

        // Archive old context and update working memory
        await InsertArchivalMemoryAsync(contentToReflect, new Dictionary<string, string>
        {
            ["source"] = "reflection",
            ["session_id"] = sessionId,
            ["summary"] = summary.Content ?? ""
        }, cancellationToken);

        lock (_cacheLock)
        {
            // Add summary to recent summaries
            state.RecentSummaries.Add(summary.Content ?? "");
            while (state.RecentSummaries.Count > MaxRecentSummaries)
            {
                state.RecentSummaries.RemoveAt(0);
            }

            // Clear conversation context and reset importance
            var tokensBefore = state.CurrentTokenCount;
            state.ConversationContext = string.Join("\n", state.RecentSummaries.TakeLast(2));
            state.AccumulatedImportance = 0;
            UpdateTokenCount(state);

            result.TokensFreed = tokensBefore - state.CurrentTokenCount;
            result.MemoriesArchived = 1;
        }

        result.MemoriesConsolidated = 1;
        result.Insights = insights;

        _logger.LogInformation("Performed reflection for session {SessionId}, freed {Tokens} tokens",
            sessionId, result.TokensFreed);

        return result;
    }

    /// <inheritdoc />
    public async Task<ContextManagementResult> ManageContextWindowAsync(
        string sessionId,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var state = GetOrCreateWorkingMemory(sessionId);
        var result = new ContextManagementResult
        {
            Success = true,
            ActionTaken = ContextManagementAction.None
        };

        lock (_cacheLock)
        {
            result.TokensBefore = state.CurrentTokenCount;
            state.MaxTokenCapacity = maxTokens;
        }

        // Check if management is needed
        if (result.TokensBefore <= maxTokens * 0.85)
        {
            result.TokensAfter = result.TokensBefore;
            return result;
        }

        // Try summarization first
        var reflectionResult = await PerformReflectionAsync(sessionId, cancellationToken);
        if (reflectionResult.Success)
        {
            result.ActionTaken = ContextManagementAction.Summarized;
            result.ProcessedContent.Add("Conversation context summarized");
        }

        lock (_cacheLock)
        {
            // If still over, perform more aggressive pruning
            if (state.CurrentTokenCount > maxTokens * 0.9)
            {
                var pruned = TruncateOldContent(state);
                if (!string.IsNullOrEmpty(pruned))
                {
                    result.ProcessedContent.Add(pruned);
                    result.ActionTaken = result.ActionTaken == ContextManagementAction.Summarized
                        ? ContextManagementAction.Combined
                        : ContextManagementAction.Pruned;
                }
            }

            result.TokensAfter = state.CurrentTokenCount;
        }

        return result;
    }

    private WorkingMemoryState GetOrCreateWorkingMemory(string sessionId)
    {
        lock (_cacheLock)
        {
            if (!_workingMemoryCache.TryGetValue(sessionId, out var state))
            {
                state = new WorkingMemoryState
                {
                    SessionId = sessionId,
                    MaxTokenCapacity = DefaultMaxTokens
                };
                _workingMemoryCache[sessionId] = state;
            }
            return state;
        }
    }

    private static void UpdateTokenCount(WorkingMemoryState state)
    {
        var totalText = state.CoreMemory + state.ConversationContext +
                        string.Join(" ", state.RecentSummaries);
        state.CurrentTokenCount = EstimateTokens(totalText);
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var wordCount = text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(wordCount * TokensPerWord);
    }

    private static float EstimateImportance(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;

        var importance = 0.5f; // Base importance

        // Longer content is usually more important
        var wordCount = content.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        importance += Math.Min(wordCount / 100f, 2.0f);

        // Check for importance indicators
        var importanceIndicators = new[]
        {
            "important", "critical", "remember", "note", "key",
            "essential", "must", "priority", "urgent", "significant"
        };

        var lowerContent = content.ToLowerInvariant();
        foreach (var indicator in importanceIndicators)
        {
            if (lowerContent.Contains(indicator))
            {
                importance += 0.5f;
            }
        }

        // Check for question indicators (questions often need answers)
        if (content.Contains('?'))
        {
            importance += 0.3f;
        }

        // Check for code/technical content
        if (content.Contains("```") || content.Contains("function") || content.Contains("class"))
        {
            importance += 0.5f;
        }

        return Math.Min(importance, 5.0f); // Cap at 5
    }

    private static string TruncateOldContent(WorkingMemoryState state)
    {
        var context = state.ConversationContext;
        if (string.IsNullOrEmpty(context)) return string.Empty;

        // Find a good split point (paragraph or sentence boundary)
        var splitIndex = context.Length / 3; // Remove first third

        var paragraphEnd = context.IndexOf("\n\n", splitIndex, StringComparison.Ordinal);
        if (paragraphEnd > 0 && paragraphEnd < context.Length * 0.5)
        {
            splitIndex = paragraphEnd + 2;
        }
        else
        {
            var sentenceEnd = context.IndexOf(". ", splitIndex, StringComparison.Ordinal);
            if (sentenceEnd > 0 && sentenceEnd < context.Length * 0.5)
            {
                splitIndex = sentenceEnd + 2;
            }
        }

        var truncated = context[..splitIndex];
        state.ConversationContext = context[splitIndex..];
        UpdateTokenCount(state);

        return truncated;
    }

    /// <summary>
    /// Internal state for working memory.
    /// </summary>
    private sealed class WorkingMemoryState
    {
        public string SessionId { get; set; } = string.Empty;
        public string CoreMemory { get; set; } = string.Empty;
        public string ConversationContext { get; set; } = string.Empty;
        public List<string> RecentSummaries { get; set; } = [];
        public int CurrentTokenCount { get; set; }
        public int MaxTokenCapacity { get; set; }
        public float AccumulatedImportance { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
