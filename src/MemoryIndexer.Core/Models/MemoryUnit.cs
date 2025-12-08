using Microsoft.Extensions.VectorData;

namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents a single unit of memory stored in the system.
/// This is the core entity for all memory operations.
/// </summary>
public sealed class MemoryUnit
{
    /// <summary>
    /// Unique identifier for this memory unit.
    /// </summary>
    [VectorStoreKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user or tenant this memory belongs to.
    /// Used for multi-tenant isolation.
    /// </summary>
    [VectorStoreData]
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Optional session identifier for grouping related memories.
    /// </summary>
    [VectorStoreData]
    public string? SessionId { get; set; }

    /// <summary>
    /// The actual content of the memory.
    /// </summary>
    [VectorStoreData]
    public string Content { get; set; } = default!;

    /// <summary>
    /// Vector embedding of the content for semantic search.
    /// Dimensions based on BGE-M3 (1024) or configurable.
    /// </summary>
    [VectorStoreVector(Dimensions: 1024)]
    public ReadOnlyMemory<float>? Embedding { get; set; }

    /// <summary>
    /// When this memory was originally created.
    /// </summary>
    [VectorStoreData]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this memory was last updated.
    /// </summary>
    [VectorStoreData]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this memory was last accessed (retrieved).
    /// Used for recency scoring and forgetting curve.
    /// </summary>
    [VectorStoreData]
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// LLM-assigned importance score (0.0 to 1.0).
    /// Based on Generative Agents poignancy rating.
    /// </summary>
    [VectorStoreData]
    public float ImportanceScore { get; set; } = 0.5f;

    /// <summary>
    /// Number of times this memory has been retrieved.
    /// Used for access frequency scoring: log(1 + access_count).
    /// </summary>
    [VectorStoreData]
    public int AccessCount { get; set; }

    /// <summary>
    /// The type of memory (episodic, semantic, procedural, fact).
    /// </summary>
    [VectorStoreData]
    public MemoryType Type { get; set; } = MemoryType.Episodic;

    /// <summary>
    /// SHA256 hash of the content for duplicate detection.
    /// </summary>
    [VectorStoreData]
    public string? ContentHash { get; set; }

    /// <summary>
    /// Topic labels extracted from the content.
    /// </summary>
    [VectorStoreData]
    public List<string> Topics { get; set; } = [];

    /// <summary>
    /// Named entities extracted from the content.
    /// </summary>
    [VectorStoreData]
    public List<string> Entities { get; set; } = [];

    /// <summary>
    /// Additional metadata stored as key-value pairs.
    /// </summary>
    [VectorStoreData]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Soft delete flag.
    /// </summary>
    [VectorStoreData]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Records an access to this memory, updating LastAccessedAt and AccessCount.
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
        AccessCount++;
    }

    /// <summary>
    /// Marks this memory as updated.
    /// </summary>
    public void MarkUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
