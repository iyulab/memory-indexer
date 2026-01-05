using Microsoft.Extensions.VectorData;

namespace MemoryIndexer.Models;

/// <summary>
/// Represents a single unit of memory stored in the system.
/// Core entity for 3-Tier VCM (Virtual Context Management) architecture.
/// </summary>
/// <remarks>
/// Research reference: research-03.md, research-04.md
/// - Supports Working/Session/User tier placement
/// - Implements Ebbinghaus stability model for forgetting curve
/// - Tracks promotion/demotion history for VCM paging
/// </remarks>
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
    /// Used for recency scoring and Ebbinghaus forgetting curve.
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
    /// Contributes to Stability calculation via Spacing Effect.
    /// </summary>
    [VectorStoreData]
    public int AccessCount { get; set; }

    /// <summary>
    /// The type of memory (episodic, semantic, procedural, fact).
    /// Based on Tulving's memory classification.
    /// </summary>
    [VectorStoreData]
    public MemoryType Type { get; set; } = MemoryType.Episodic;

    /// <summary>
    /// Current storage tier (Working, Session, User).
    /// Determines access latency and persistence behavior.
    /// </summary>
    /// <remarks>
    /// Research reference: research-03.md Section "계층적 저장소 아키텍처"
    /// </remarks>
    [VectorStoreData]
    public MemoryTier Tier { get; set; } = MemoryTier.Session;

    /// <summary>
    /// Memory stability level for forgetting curve calculation.
    /// Higher stability = longer retention without reinforcement.
    /// </summary>
    /// <remarks>
    /// Research reference: intentional-forgetting-mechanisms.md
    /// Formula: R = e^(-t/S), where S is derived from this value.
    /// </remarks>
    [VectorStoreData]
    public MemoryStability Stability { get; set; } = MemoryStability.Volatile;

    /// <summary>
    /// Calculated retention score based on Ebbinghaus curve (0.0 to 1.0).
    /// Updated during access and periodic consolidation.
    /// </summary>
    [VectorStoreData]
    public float RetentionScore { get; set; } = 1.0f;

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
    /// Lock flag to prevent automatic modification/deletion.
    /// Used for system prompts and core facts.
    /// </summary>
    /// <remarks>
    /// Research reference: research-04.md "Lock primitive"
    /// </remarks>
    [VectorStoreData]
    public bool IsLocked { get; set; }

    /// <summary>
    /// Expiration timestamp for TTL-based cleanup.
    /// Null means no expiration.
    /// </summary>
    [VectorStoreData]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// ID of the memory this one supersedes (for fact updates).
    /// Creates a chain for temporal knowledge tracking.
    /// </summary>
    [VectorStoreData]
    public Guid? SupersedesId { get; set; }

    /// <summary>
    /// LLM-assigned confidence score for extracted facts (0.0 to 1.0).
    /// </summary>
    /// <remarks>
    /// Research reference: FACT_EXTRACTION_KNOWLEDGE_GRAPH_RESEARCH.md
    /// </remarks>
    [VectorStoreData]
    public float? ConfidenceScore { get; set; }

    /// <summary>
    /// Records an access to this memory, updating LastAccessedAt, AccessCount, and Stability.
    /// Implements Spacing Effect: repeated access increases stability.
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
        AccessCount++;

        // Spacing Effect: access count thresholds for stability upgrade
        // Research reference: research-03.md "Spacing Effect"
        Stability = AccessCount switch
        {
            >= 10 when Stability < MemoryStability.Consolidated => MemoryStability.Consolidated,
            >= 5 when Stability < MemoryStability.Stable => MemoryStability.Stable,
            >= 2 when Stability < MemoryStability.Stabilizing => MemoryStability.Stabilizing,
            _ => Stability
        };

        // Reset retention score on access (memory reinforcement)
        RetentionScore = 1.0f;
    }

    /// <summary>
    /// Marks this memory as updated.
    /// </summary>
    public void MarkUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Calculates current retention using Ebbinghaus forgetting curve.
    /// R = e^(-t/S), where t = time since last access, S = stability factor.
    /// </summary>
    /// <param name="referenceTime">Time to calculate retention for (defaults to now).</param>
    /// <returns>Retention score between 0.0 and 1.0.</returns>
    public float CalculateRetention(DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.UtcNow;
        var lastAccess = LastAccessedAt ?? CreatedAt;
        var daysSinceAccess = (now - lastAccess).TotalDays;

        // Stability factor based on stability level
        // Research reference: intentional-forgetting-mechanisms.md
        var stabilityFactor = Stability switch
        {
            MemoryStability.Volatile => 1.0,      // ~1 day half-life
            MemoryStability.Stabilizing => 7.0,   // ~7 days half-life
            MemoryStability.Stable => 30.0,       // ~30 days half-life
            MemoryStability.Consolidated => 365.0, // ~1 year half-life
            MemoryStability.Permanent => double.MaxValue, // No decay
            _ => 1.0
        };

        // Ebbinghaus formula: R = e^(-t/S)
        var retention = (float)Math.Exp(-daysSinceAccess / stabilityFactor);
        return Math.Clamp(retention, 0f, 1f);
    }

    /// <summary>
    /// Checks if this memory should be considered for eviction.
    /// </summary>
    /// <param name="retentionThreshold">Minimum retention to avoid eviction.</param>
    /// <returns>True if memory is a candidate for eviction.</returns>
    public bool ShouldEvict(float retentionThreshold = 0.1f)
    {
        if (IsLocked) return false;
        if (Stability == MemoryStability.Permanent) return false;
        return CalculateRetention() < retentionThreshold;
    }
}
