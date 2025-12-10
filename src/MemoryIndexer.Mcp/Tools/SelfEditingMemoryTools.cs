using System.ComponentModel;
using MemoryIndexer.Intelligence.SelfEditing;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Mcp.Tools;

/// <summary>
/// MCP tools for self-editing memory operations (MemGPT pattern).
/// Enables LLM to manage its own working and archival memory.
/// </summary>
[McpServerToolType]
public sealed class SelfEditingMemoryTools
{
    private readonly ISelfEditingMemoryService _selfEditingService;

    private const string DefaultSessionId = "default";

    public SelfEditingMemoryTools(ISelfEditingMemoryService selfEditingService)
    {
        _selfEditingService = selfEditingService;
    }

    /// <summary>
    /// Replace content in working memory at a specified location.
    /// Use this to update context that the LLM maintains about the conversation.
    /// </summary>
    /// <param name="location">Memory location key (e.g., "user_preferences", "current_task").</param>
    /// <param name="newContent">New content to store at this location.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Result of the replace operation.</returns>
    [McpServerTool, Description("Replace content in working memory at a specified location")]
    public async Task<MemoryReplaceToolResult> MemoryReplace(
        [Description("Memory location key (e.g., 'user_preferences', 'current_task')")] string location,
        [Description("New content to store at this location")] string newContent,
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return new MemoryReplaceToolResult
            {
                Success = false,
                Message = "Location cannot be empty"
            };
        }

        var result = await _selfEditingService.ReplaceWorkingMemoryAsync(
            $"{sessionId ?? DefaultSessionId}:{location}",
            newContent);

        return new MemoryReplaceToolResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Successfully updated working memory at '{location}'"
                : result.Message,
            PreviousContent = result.PreviousContent,
            Timestamp = result.Timestamp
        };
    }

    /// <summary>
    /// Insert content into archival memory for long-term storage.
    /// Archival memory persists beyond the current session.
    /// </summary>
    /// <param name="content">Content to archive.</param>
    /// <param name="category">Category for the archived content.</param>
    /// <param name="tags">Optional tags for organization.</param>
    /// <returns>Result with the archived memory ID.</returns>
    [McpServerTool, Description("Insert content into archival memory for long-term storage")]
    public async Task<ArchivalInsertToolResult> ArchivalMemoryInsert(
        [Description("Content to archive for long-term storage")] string content,
        [Description("Category (e.g., 'preferences', 'facts', 'procedures')")] string? category = null,
        [Description("Comma-separated tags for organization")] string? tags = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ArchivalInsertToolResult
            {
                Success = false,
                Message = "Content cannot be empty"
            };
        }

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(category))
        {
            metadata["category"] = category;
        }
        if (!string.IsNullOrWhiteSpace(tags))
        {
            metadata["tags"] = tags;
        }

        var result = await _selfEditingService.InsertArchivalMemoryAsync(content, metadata);

        return new ArchivalInsertToolResult
        {
            Success = result.Success,
            MemoryId = result.MemoryId,
            TokenCount = result.TokenCount,
            Message = result.Success
                ? $"Successfully archived content (ID: {result.MemoryId})"
                : "Failed to archive content"
        };
    }

    /// <summary>
    /// Search archival memory for relevant content.
    /// Use this to recall information from long-term storage.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <returns>Matching archival memories.</returns>
    [McpServerTool, Description("Search archival memory for relevant content")]
    public async Task<ArchivalSearchToolResult> ArchivalMemorySearch(
        [Description("Search query for archival memory")] string query,
        [Description("Maximum results to return")] int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ArchivalSearchToolResult
            {
                Success = false,
                Message = "Query cannot be empty"
            };
        }

        var results = await _selfEditingService.SearchArchivalMemoryAsync(query, maxResults);

        return new ArchivalSearchToolResult
        {
            Success = true,
            Results = results.Select(r => new ArchivalMemoryItem
            {
                MemoryId = r.MemoryId,
                Content = r.Content,
                Relevance = r.RelevanceScore,
                CreatedAt = r.CreatedAt,
                Category = r.Metadata.GetValueOrDefault("category"),
                Tags = r.Metadata.GetValueOrDefault("tags")
            }).ToList(),
            Message = $"Found {results.Count} matching archival memories"
        };
    }

    /// <summary>
    /// Get current working memory state for a session.
    /// Shows what the LLM currently has in its working memory.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Current working memory snapshot.</returns>
    [McpServerTool, Description("Get current working memory state")]
    public async Task<WorkingMemoryToolResult> GetWorkingMemory(
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        var snapshot = await _selfEditingService.GetWorkingMemoryAsync(sessionId ?? DefaultSessionId);

        return new WorkingMemoryToolResult
        {
            Success = true,
            SessionId = snapshot.SessionId,
            CoreMemory = snapshot.CoreMemory,
            ConversationContext = snapshot.ConversationContext,
            RecentSummaries = snapshot.RecentSummaries,
            CurrentTokenCount = snapshot.CurrentTokenCount,
            MaxTokenCapacity = snapshot.MaxTokenCapacity,
            AccumulatedImportance = snapshot.AccumulatedImportance,
            UtilizationPercent = snapshot.MaxTokenCapacity > 0
                ? (float)snapshot.CurrentTokenCount / snapshot.MaxTokenCapacity * 100
                : 0
        };
    }

    /// <summary>
    /// Update working memory with new context from conversation.
    /// May trigger reflection if importance threshold is exceeded.
    /// </summary>
    /// <param name="newContext">New context to incorporate.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Update result with potential reflection trigger.</returns>
    [McpServerTool, Description("Update working memory with new conversation context")]
    public async Task<UpdateWorkingMemoryToolResult> UpdateWorkingMemory(
        [Description("New context to incorporate")] string newContext,
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(newContext))
        {
            return new UpdateWorkingMemoryToolResult
            {
                Success = false,
                Message = "Context cannot be empty"
            };
        }

        var result = await _selfEditingService.UpdateWorkingMemoryAsync(
            sessionId ?? DefaultSessionId,
            newContext);

        return new UpdateWorkingMemoryToolResult
        {
            Success = result.Success,
            NewTokenCount = result.NewTokenCount,
            WasTruncated = result.WasTruncated,
            ArchivedContent = result.ArchivedContent,
            ReflectionRecommended = result.ReflectionRecommended,
            Message = result.Success
                ? $"Working memory updated ({result.NewTokenCount} tokens)"
                : "Failed to update working memory"
        };
    }

    /// <summary>
    /// Check if reflection should be triggered based on accumulated importance.
    /// Reflection consolidates and summarizes recent memories.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Whether reflection is recommended.</returns>
    [McpServerTool, Description("Check if reflection should be triggered")]
    public async Task<ShouldReflectToolResult> ShouldReflect(
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        var result = await _selfEditingService.ShouldTriggerReflectionAsync(sessionId ?? DefaultSessionId);

        return new ShouldReflectToolResult
        {
            ShouldReflect = result.ShouldReflect,
            AccumulatedImportance = result.AccumulatedImportance,
            Threshold = result.Threshold,
            Reason = result.Reason,
            Message = result.ShouldReflect
                ? "Reflection recommended - importance threshold exceeded"
                : "Reflection not needed yet"
        };
    }

    /// <summary>
    /// Perform a reflection operation to consolidate memories.
    /// Creates summaries and archives important content.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Result of the reflection process.</returns>
    [McpServerTool, Description("Perform reflection to consolidate and summarize memories")]
    public async Task<ReflectionToolResult> PerformReflection(
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        var result = await _selfEditingService.PerformReflectionAsync(sessionId ?? DefaultSessionId);

        return new ReflectionToolResult
        {
            Success = result.Success,
            MemoriesConsolidated = result.MemoriesConsolidated,
            MemoriesArchived = result.MemoriesArchived,
            TokensFreed = result.TokensFreed,
            Insights = result.Insights,
            Message = result.Success
                ? $"Reflection complete: consolidated {result.MemoriesConsolidated} memories, archived {result.MemoriesArchived}"
                : "Reflection failed"
        };
    }

    /// <summary>
    /// Manage context window by pruning or archiving old content.
    /// Ensures working memory stays within token limits.
    /// </summary>
    /// <param name="maxTokens">Maximum tokens to retain in working memory.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>Result of the management operation.</returns>
    [McpServerTool, Description("Manage context window by pruning or archiving old content")]
    public async Task<ContextManagementToolResult> ManageContextWindow(
        [Description("Maximum tokens to retain (default: 4000)")] int maxTokens = 4000,
        [Description("Session ID (optional)")] string? sessionId = null)
    {
        var result = await _selfEditingService.ManageContextWindowAsync(
            sessionId ?? DefaultSessionId,
            maxTokens);

        return new ContextManagementToolResult
        {
            Success = result.Success,
            ActionTaken = result.ActionTaken.ToString(),
            TokensBefore = result.TokensBefore,
            TokensAfter = result.TokensAfter,
            ProcessedContentCount = result.ProcessedContent.Count,
            Message = result.Success
                ? $"Context managed: {result.ActionTaken} ({result.TokensBefore} -> {result.TokensAfter} tokens)"
                : "Context management failed"
        };
    }
}

#region Result Types

public sealed class MemoryReplaceToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? PreviousContent { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class ArchivalInsertToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid MemoryId { get; set; }
    public int TokenCount { get; set; }
}

public sealed class ArchivalSearchToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<ArchivalMemoryItem> Results { get; set; } = [];
}

public sealed class ArchivalMemoryItem
{
    public Guid MemoryId { get; set; }
    public string Content { get; set; } = default!;
    public float Relevance { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
}

public sealed class WorkingMemoryToolResult
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = default!;
    public string CoreMemory { get; set; } = default!;
    public string ConversationContext { get; set; } = default!;
    public List<string> RecentSummaries { get; set; } = [];
    public int CurrentTokenCount { get; set; }
    public int MaxTokenCapacity { get; set; }
    public float AccumulatedImportance { get; set; }
    public float UtilizationPercent { get; set; }
}

public sealed class UpdateWorkingMemoryToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int NewTokenCount { get; set; }
    public bool WasTruncated { get; set; }
    public string? ArchivedContent { get; set; }
    public bool ReflectionRecommended { get; set; }
}

public sealed class ShouldReflectToolResult
{
    public bool ShouldReflect { get; set; }
    public float AccumulatedImportance { get; set; }
    public float Threshold { get; set; }
    public string Reason { get; set; } = default!;
    public string? Message { get; set; }
}

public sealed class ReflectionToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int MemoriesConsolidated { get; set; }
    public int MemoriesArchived { get; set; }
    public int TokensFreed { get; set; }
    public List<string> Insights { get; set; } = [];
}

public sealed class ContextManagementToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string ActionTaken { get; set; } = default!;
    public int TokensBefore { get; set; }
    public int TokensAfter { get; set; }
    public int ProcessedContentCount { get; set; }
}

#endregion
