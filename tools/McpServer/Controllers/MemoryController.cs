using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// REST API controller for memory operations (non-MCP clients).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MemoryController : ControllerBase
{
    private readonly MemoryService _memoryService;
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<MemoryController> _logger;
    private const string DefaultUserId = "default";

    public MemoryController(
        MemoryService memoryService,
        IMemoryStore memoryStore,
        ILogger<MemoryController> logger)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Store a new memory.
    /// </summary>
    /// <param name="request">Memory content and metadata</param>
    /// <returns>Stored memory with ID</returns>
    [HttpPost]
    [ProducesResponseType(typeof(MemoryStoreResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StoreMemory([FromBody] MemoryStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Content is required" });
        }

        try
        {
            var userId = request.UserId ?? DefaultUserId;
            var metadata = request.Metadata ?? new Dictionary<string, object>();

            var memory = await _memoryService.StoreAsync(
                userId: userId,
                content: request.Content,
                type: request.Type ?? MemoryType.Episodic,
                sessionId: request.SessionId,
                importance: request.Importance ?? 0.5f,
                metadata: metadata.ToDictionary(k => k.Key, v => v.Value.ToString() ?? string.Empty));

            _logger.LogInformation("Stored memory {MemoryId} for user {UserId}",
                memory.Id, userId);

            return CreatedAtAction(
                nameof(GetMemory),
                new { id = memory.Id },
                new MemoryStoreResponse
                {
                    Id = memory.Id,
                    Message = "Memory stored successfully"
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store memory");
            return StatusCode(500, new { error = "Failed to store memory", details = ex.Message });
        }
    }

    /// <summary>
    /// Search memories using semantic similarity.
    /// </summary>
    /// <param name="request">Search parameters</param>
    /// <returns>Matching memories</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(MemorySearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchMemories([FromBody] MemorySearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Query is required" });
        }

        try
        {
            var userId = request.UserId ?? DefaultUserId;
            MemoryType[]? types = request.Type.HasValue ? [request.Type.Value] : null;

            var results = await _memoryService.RecallAsync(
                userId: userId,
                query: request.Query,
                limit: request.Limit ?? 5,
                sessionId: request.SessionId,
                types: types);

            _logger.LogInformation("Searched memories for user {UserId}, found {Count} results",
                userId, results.Count);

            return Ok(new MemorySearchResponse
            {
                Query = request.Query,
                Results = results.Select(r => new MemoryResult
                {
                    Id = r.Memory.Id,
                    Content = r.Memory.Content,
                    Type = r.Memory.Type,
                    Importance = r.Memory.ImportanceScore,
                    Score = r.Score,
                    CreatedAt = r.Memory.CreatedAt,
                    Metadata = r.Memory.Metadata?.ToDictionary(k => k.Key, v => (object)v.Value)
                }).ToList(),
                TotalCount = results.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search memories");
            return StatusCode(500, new { error = "Failed to search memories", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all memories for a user.
    /// </summary>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="sessionId">Optional session ID filter</param>
    /// <param name="type">Optional memory type filter</param>
    /// <param name="limit">Maximum number of results</param>
    /// <returns>List of memories</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MemoryListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllMemories(
        [FromQuery] string? userId = null,
        [FromQuery] string? sessionId = null,
        [FromQuery] MemoryType? type = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var options = new MemoryFilterOptions
            {
                Limit = Math.Clamp(limit, 1, 100),
                SessionId = sessionId,
                OrderBy = MemoryOrderBy.CreatedAtDesc
            };

            if (type.HasValue)
            {
                options.Types = [type.Value];
            }

            var memories = await _memoryService.GetAllAsync(userId ?? DefaultUserId, options);
            var count = await _memoryStore.GetCountAsync(userId ?? DefaultUserId);

            return Ok(new MemoryListResponse
            {
                Memories = memories.Select(m => new MemoryResult
                {
                    Id = m.Id,
                    Content = m.Content,
                    Type = m.Type,
                    Importance = m.ImportanceScore,
                    CreatedAt = m.CreatedAt,
                    Metadata = m.Metadata?.ToDictionary(k => k.Key, v => (object)v.Value)
                }).ToList(),
                TotalCount = (int)count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memories");
            return StatusCode(500, new { error = "Failed to get memories", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific memory by ID.
    /// </summary>
    /// <param name="id">Memory ID</param>
    /// <returns>Memory details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(MemoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemory(Guid id)
    {
        try
        {
            var memory = await _memoryService.GetByIdAsync(id);
            if (memory == null)
            {
                return NotFound(new { error = "Memory not found", id });
            }

            return Ok(new MemoryResult
            {
                Id = memory.Id,
                Content = memory.Content,
                Type = memory.Type,
                Importance = memory.ImportanceScore,
                CreatedAt = memory.CreatedAt,
                UpdatedAt = memory.UpdatedAt,
                Metadata = memory.Metadata?.ToDictionary(k => k.Key, v => (object)v.Value)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memory {MemoryId}", id);
            return StatusCode(500, new { error = "Failed to get memory", details = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing memory.
    /// </summary>
    /// <param name="id">Memory ID</param>
    /// <param name="request">Updated memory data</param>
    /// <returns>Success status</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemory(Guid id, [FromBody] MemoryUpdateRequest request)
    {
        try
        {
            var updated = false;

            if (!string.IsNullOrWhiteSpace(request.Content))
            {
                updated = await _memoryService.UpdateContentAsync(id, request.Content);
            }

            if (request.Importance.HasValue)
            {
                updated = await _memoryService.UpdateImportanceAsync(id, request.Importance.Value) || updated;
            }

            if (!updated)
            {
                return NotFound(new { error = "Memory not found or no changes made", id });
            }

            _logger.LogInformation("Updated memory {MemoryId}", id);
            return Ok(new { message = "Memory updated successfully", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update memory {MemoryId}", id);
            return StatusCode(500, new { error = "Failed to update memory", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete a memory.
    /// </summary>
    /// <param name="id">Memory ID</param>
    /// <returns>Success status</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMemory(Guid id)
    {
        try
        {
            var success = await _memoryService.DeleteAsync(id, hardDelete: false);
            if (!success)
            {
                return NotFound(new { error = "Memory not found", id });
            }

            _logger.LogInformation("Deleted memory {MemoryId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete memory {MemoryId}", id);
            return StatusCode(500, new { error = "Failed to delete memory", details = ex.Message });
        }
    }
}

#region Request/Response Models

public record MemoryStoreRequest
{
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public required string Content { get; init; }
    public MemoryType? Type { get; init; }
    public float? Importance { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record MemoryStoreResponse
{
    public Guid Id { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record MemorySearchRequest
{
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public required string Query { get; init; }
    public int? Limit { get; init; }
    public MemoryType? Type { get; init; }
}

public record MemorySearchResponse
{
    public string Query { get; init; } = string.Empty;
    public List<MemoryResult> Results { get; init; } = new();
    public int TotalCount { get; init; }
}

public record MemoryListResponse
{
    public List<MemoryResult> Memories { get; init; } = new();
    public int TotalCount { get; init; }
}

public record MemoryResult
{
    public Guid Id { get; init; }
    public string Content { get; init; } = string.Empty;
    public MemoryType Type { get; init; }
    public float? Importance { get; init; }
    public float? Score { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record MemoryUpdateRequest
{
    public string? Content { get; init; }
    public float? Importance { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

#endregion
