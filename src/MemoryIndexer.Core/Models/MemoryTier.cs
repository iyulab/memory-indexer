namespace MemoryIndexer.Core.Models;

/// <summary>
/// Memory storage tier based on VCM (Virtual Context Management) architecture.
/// Inspired by OS memory hierarchy: CPU Cache -> RAM -> Disk.
/// </summary>
/// <remarks>
/// Research reference: research-03.md, research-04.md Section 3.1
/// </remarks>
public enum MemoryTier
{
    /// <summary>
    /// L1: Working Memory (In-Context)
    /// - Capacity: 4-7 chunks (Baddeley's Working Memory Model)
    /// - Access: ~microseconds
    /// - Storage: IMemoryCache
    /// - Scope: Current task context
    /// </summary>
    Working = 0,

    /// <summary>
    /// L2: Session Memory
    /// - Capacity: Session-scoped
    /// - Access: ~milliseconds
    /// - Storage: Vector DB (Qdrant/SQLite-vec)
    /// - Scope: Current conversation session
    /// </summary>
    Session = 1,

    /// <summary>
    /// L3: User Memory (Long-term)
    /// - Capacity: Unlimited
    /// - Access: ~milliseconds to seconds
    /// - Storage: Hybrid (Vector + Graph DB)
    /// - Scope: Cross-session persistent knowledge
    /// </summary>
    User = 2
}

/// <summary>
/// Memory stability level based on Ebbinghaus forgetting curve.
/// Higher stability = longer retention without reinforcement.
/// </summary>
/// <remarks>
/// Research reference: intentional-forgetting-mechanisms.md
/// Formula: R = e^(-t/S), where S = Stability
/// </remarks>
public enum MemoryStability
{
    /// <summary>
    /// Newly encoded, high forgetting rate.
    /// Base stability ~1 day.
    /// </summary>
    Volatile = 0,

    /// <summary>
    /// Accessed 2-3 times, moderate retention.
    /// Base stability ~7 days.
    /// </summary>
    Stabilizing = 1,

    /// <summary>
    /// Frequently accessed, strong retention.
    /// Base stability ~30 days.
    /// </summary>
    Stable = 2,

    /// <summary>
    /// Core knowledge, minimal forgetting.
    /// Base stability ~365 days.
    /// </summary>
    Consolidated = 3,

    /// <summary>
    /// Locked memory, no automatic decay.
    /// System prompts, core facts.
    /// </summary>
    Permanent = 4
}

/// <summary>
/// Context saturation levels for VCM management.
/// </summary>
/// <remarks>
/// Research reference: research-04.md Section 1.2 "Fragility Tipping Point"
/// </remarks>
public enum ContextSaturationLevel
{
    /// <summary>
    /// Normal operation, no action needed.
    /// Usage: 0-75%
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Approaching capacity, consider optimization.
    /// Usage: 75-85%
    /// </summary>
    Elevated = 1,

    /// <summary>
    /// Near capacity, active management required.
    /// Usage: 85-95%
    /// </summary>
    High = 2,

    /// <summary>
    /// At capacity, immediate eviction needed.
    /// Usage: 95%+
    /// </summary>
    Critical = 3
}
