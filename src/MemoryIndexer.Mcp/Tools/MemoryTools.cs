using System.ComponentModel;
using System.Text.Json;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Services;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Mcp.Tools;

/// <summary>
/// MCP tools for memory operations.
/// </summary>
[McpServerToolType]
public sealed class MemoryTools(MemoryService memoryService, IMemoryStore memoryStore)
{
    private const string DefaultUserId = "default";

    /// <summary>
    /// Stores new content in long-term memory with semantic indexing.
    /// </summary>
    /// <param name="content">The content to memorize.</param>
    /// <param name="type">Memory type: episodic (events), semantic (facts), procedural (how-to), fact (specific facts).</param>
    /// <param name="importance">Importance score from 0.0 (trivial) to 1.0 (critical). Default: 0.5</param>
    /// <param name="tags">Optional comma-separated tags for categorization.</param>
    /// <param name="sessionId">Optional session ID to group related memories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the stored memory ID.</returns>
    [McpServerTool]
    [Description("Store content in long-term memory with semantic indexing. Use this to remember important information, facts, preferences, or events from conversations.")]
    public async Task<StoreMemoryResult> StoreMemory(
        [Description("The content to memorize")] string content,
        [Description("Memory type: episodic, semantic, procedural, or fact")] string type = "episodic",
        [Description("Importance score (0.0 to 1.0)")] float importance = 0.5f,
        [Description("Comma-separated tags for categorization")] string? tags = null,
        [Description("Session ID to group related memories")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var memoryType = ParseMemoryType(type);
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(tags))
        {
            metadata["tags"] = tags;
        }

        var memory = await memoryService.StoreAsync(
            DefaultUserId,
            content,
            memoryType,
            sessionId,
            Math.Clamp(importance, 0f, 1f),
            metadata,
            cancellationToken);

        return new StoreMemoryResult
        {
            Success = true,
            MemoryId = memory.Id.ToString(),
            Message = $"Memory stored successfully with ID {memory.Id}"
        };
    }

    /// <summary>
    /// Searches memories using semantic similarity to find relevant past information.
    /// </summary>
    /// <param name="query">The search query to find relevant memories.</param>
    /// <param name="limit">Maximum number of results to return (1-20).</param>
    /// <param name="type">Optional filter by memory type.</param>
    /// <param name="sessionId">Optional filter by session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of relevant memories with similarity scores.</returns>
    [McpServerTool]
    [Description("Search memories using semantic similarity. Use this to recall relevant past information, facts, or events based on a query.")]
    public async Task<RecallMemoryResult> RecallMemory(
        [Description("Search query to find relevant memories")] string query,
        [Description("Maximum results (1-20)")] int limit = 5,
        [Description("Filter by memory type: episodic, semantic, procedural, or fact")] string? type = null,
        [Description("Filter by session ID")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        MemoryType[]? types = null;

        if (!string.IsNullOrWhiteSpace(type))
        {
            types = [ParseMemoryType(type)];
        }

        var results = await memoryService.RecallAsync(
            DefaultUserId,
            query,
            limit,
            sessionId,
            types,
            cancellationToken);

        return new RecallMemoryResult
        {
            Success = true,
            Count = results.Count,
            Memories = results.Select(r => new MemoryItem
            {
                Id = r.Memory.Id.ToString(),
                Content = r.Memory.Content,
                Type = r.Memory.Type.ToString().ToLowerInvariant(),
                Score = r.Score,
                Importance = r.Memory.ImportanceScore,
                CreatedAt = r.Memory.CreatedAt,
                AccessCount = r.Memory.AccessCount
            }).ToList()
        };
    }

    /// <summary>
    /// Retrieves all stored memories with optional filtering.
    /// </summary>
    /// <param name="limit">Maximum number of memories to return.</param>
    /// <param name="type">Optional filter by memory type.</param>
    /// <param name="sessionId">Optional filter by session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all matching memories.</returns>
    [McpServerTool]
    [Description("Get all stored memories with optional filtering. Use this to review what has been memorized.")]
    public async Task<GetAllMemoriesResult> GetAllMemories(
        [Description("Maximum memories to return")] int limit = 50,
        [Description("Filter by memory type")] string? type = null,
        [Description("Filter by session ID")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var options = new MemoryFilterOptions
        {
            Limit = Math.Clamp(limit, 1, 100),
            SessionId = sessionId,
            OrderBy = MemoryOrderBy.CreatedAtDesc
        };

        if (!string.IsNullOrWhiteSpace(type))
        {
            options.Types = [ParseMemoryType(type)];
        }

        var memories = await memoryService.GetAllAsync(DefaultUserId, options, cancellationToken);
        var count = await memoryStore.GetCountAsync(DefaultUserId, cancellationToken);

        return new GetAllMemoriesResult
        {
            Success = true,
            TotalCount = count,
            ReturnedCount = memories.Count,
            Memories = memories.Select(m => new MemoryItem
            {
                Id = m.Id.ToString(),
                Content = m.Content,
                Type = m.Type.ToString().ToLowerInvariant(),
                Score = m.ImportanceScore,
                Importance = m.ImportanceScore,
                CreatedAt = m.CreatedAt,
                AccessCount = m.AccessCount
            }).ToList()
        };
    }

    /// <summary>
    /// Updates an existing memory's content or metadata.
    /// </summary>
    /// <param name="memoryId">The ID of the memory to update.</param>
    /// <param name="content">New content (leave empty to keep existing).</param>
    /// <param name="importance">New importance score (leave null to keep existing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    [McpServerTool]
    [Description("Update an existing memory's content or importance. Use this to correct or enhance stored information.")]
    public async Task<UpdateMemoryResult> UpdateMemory(
        [Description("Memory ID to update")] string memoryId,
        [Description("New content (empty to keep existing)")] string? content = null,
        [Description("New importance score (null to keep existing)")] float? importance = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(memoryId, out var id))
        {
            return new UpdateMemoryResult
            {
                Success = false,
                Message = "Invalid memory ID format"
            };
        }

        var updated = false;

        if (!string.IsNullOrWhiteSpace(content))
        {
            updated = await memoryService.UpdateContentAsync(id, content, cancellationToken);
        }

        if (importance.HasValue)
        {
            updated = await memoryService.UpdateImportanceAsync(id, importance.Value, cancellationToken) || updated;
        }

        return new UpdateMemoryResult
        {
            Success = updated,
            Message = updated ? "Memory updated successfully" : "Memory not found or no changes made"
        };
    }

    /// <summary>
    /// Deletes a memory by its ID.
    /// </summary>
    /// <param name="memoryId">The ID of the memory to delete.</param>
    /// <param name="permanent">If true, permanently removes. If false, soft delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [McpServerTool]
    [Description("Delete a memory by its ID. Use this to remove outdated or incorrect information.")]
    public async Task<DeleteMemoryResult> DeleteMemory(
        [Description("Memory ID to delete")] string memoryId,
        [Description("Permanently delete (true) or soft delete (false)")] bool permanent = false,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(memoryId, out var id))
        {
            return new DeleteMemoryResult
            {
                Success = false,
                Message = "Invalid memory ID format"
            };
        }

        var deleted = await memoryService.DeleteAsync(id, permanent, cancellationToken);

        return new DeleteMemoryResult
        {
            Success = deleted,
            Message = deleted
                ? (permanent ? "Memory permanently deleted" : "Memory marked as deleted")
                : "Memory not found"
        };
    }

    /// <summary>
    /// Gets detailed information about a specific memory.
    /// </summary>
    /// <param name="memoryId">The ID of the memory to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory details.</returns>
    [McpServerTool]
    [Description("Get detailed information about a specific memory by its ID.")]
    public async Task<GetMemoryResult> GetMemory(
        [Description("Memory ID to retrieve")] string memoryId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(memoryId, out var id))
        {
            return new GetMemoryResult
            {
                Success = false,
                Message = "Invalid memory ID format"
            };
        }

        var memory = await memoryService.GetByIdAsync(id, cancellationToken);

        if (memory is null)
        {
            return new GetMemoryResult
            {
                Success = false,
                Message = "Memory not found"
            };
        }

        return new GetMemoryResult
        {
            Success = true,
            Memory = new MemoryDetail
            {
                Id = memory.Id.ToString(),
                Content = memory.Content,
                Type = memory.Type.ToString().ToLowerInvariant(),
                Importance = memory.ImportanceScore,
                CreatedAt = memory.CreatedAt,
                UpdatedAt = memory.UpdatedAt,
                LastAccessedAt = memory.LastAccessedAt,
                AccessCount = memory.AccessCount,
                SessionId = memory.SessionId,
                Topics = memory.Topics,
                Entities = memory.Entities,
                Metadata = memory.Metadata
            }
        };
    }

    private static MemoryType ParseMemoryType(string type) => type.ToLowerInvariant() switch
    {
        "episodic" => MemoryType.Episodic,
        "semantic" => MemoryType.Semantic,
        "procedural" => MemoryType.Procedural,
        "fact" => MemoryType.Fact,
        _ => MemoryType.Episodic
    };
}

#region Result DTOs

public sealed class StoreMemoryResult
{
    public bool Success { get; set; }
    public string? MemoryId { get; set; }
    public string? Message { get; set; }
}

public sealed class RecallMemoryResult
{
    public bool Success { get; set; }
    public int Count { get; set; }
    public List<MemoryItem> Memories { get; set; } = [];
}

public sealed class GetAllMemoriesResult
{
    public bool Success { get; set; }
    public long TotalCount { get; set; }
    public int ReturnedCount { get; set; }
    public List<MemoryItem> Memories { get; set; } = [];
}

public sealed class UpdateMemoryResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class DeleteMemoryResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class GetMemoryResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public MemoryDetail? Memory { get; set; }
}

public sealed class MemoryItem
{
    public string Id { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Type { get; set; } = default!;
    public float Score { get; set; }
    public float Importance { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AccessCount { get; set; }
}

public sealed class MemoryDetail
{
    public string Id { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Type { get; set; } = default!;
    public float Importance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public string? SessionId { get; set; }
    public List<string> Topics { get; set; } = [];
    public List<string> Entities { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

#endregion
