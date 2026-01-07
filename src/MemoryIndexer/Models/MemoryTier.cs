namespace MemoryIndexer.Models;

/// <summary>
/// Memory tier - storage layer based on persistence and lifespan.
/// BREAKING CHANGE (v0.4.x): Enum renamed from Tier to Tier.
/// This file will be renamed to Tier.cs in the next commit.
/// </summary>
/// <remarks>
/// Design reference: docs/DESIGN_V0.4.md Section 4 "Tier Axis (Lifespan)"
///
/// Cognitive Science Foundation:
/// - Atkinson-Shiffrin Multi-Store Model (sensory → short-term → long-term)
/// - Baddeley's Working Memory Model (phonological loop, visuospatial sketchpad)
///
/// v0.3.x Migration:
/// - Sensory → Buffer (clearer storage semantics)
/// - Working → Short (more intuitive for developers)
/// - Episodic → Long (avoids confusion with Type.Episodic)
/// - Semantic → Archive (avoids confusion with Type.Semantic)
/// </remarks>
public enum Tier
{
    /// <summary>
    /// T0: Buffer - Immediate processing tier
    /// - Lifespan: Seconds to minutes
    /// - Capacity: Unlimited (time-based expiry)
    /// - Expiry: 60s idle OR 500 tokens OR 3 turns (OR logic)
    /// - Access: ~microseconds (in-memory)
    /// - Storage: IBuffer (in-memory staging)
    /// - Use case: Raw conversation buffer, async processing queue
    /// </summary>
    /// <remarks>
    /// Cognitive: Sensory register (Atkinson-Shiffrin)
    /// RENAMED from "Sensory" (v0.3.x) to emphasize storage/staging semantics.
    /// </remarks>
    Buffer = 0,

    /// <summary>
    /// T1: Short - Active processing tier (short-term memory)
    /// - Lifespan: Minutes to hours
    /// - Capacity: 4-7 chunks (Miller's Law, Cowan's 4±1)
    /// - Expiry: 10min OR 2K tokens OR topic change (OR logic)
    /// - Access: ~milliseconds (in-memory with indexing)
    /// - Storage: IShortTermMemory (memory cache)
    /// - Use case: Active conversation context, topic-scoped memory
    /// </summary>
    /// <remarks>
    /// Cognitive: Working memory (Baddeley)
    /// RENAMED from "Working" (v0.3.x) to "Short" (short-term) for clarity.
    /// </remarks>
    Short = 1,

    /// <summary>
    /// T2: Long - Session persistence tier (long-term memory, active)
    /// - Lifespan: Hours to days
    /// - Capacity: Configurable (default 10,000/user)
    /// - Expiry: Ebbinghaus decay + importance weighting
    /// - Access: ~milliseconds (vector DB query)
    /// - Storage: ILongTermStore (SQLite-vec, Qdrant)
    /// - Use case: Session-scoped memories, conversation history
    /// </summary>
    /// <remarks>
    /// Cognitive: Long-term memory (declarative, active retrieval)
    /// RENAMED from "Episodic" (v0.3.x) to avoid confusion with Type.Episodic.
    /// </remarks>
    Long = 2,

    /// <summary>
    /// T3: Archive - Permanent storage tier (long-term memory, consolidated)
    /// - Lifespan: Permanent (until explicit deletion)
    /// - Capacity: Configurable (default 1,000/user, high-value only)
    /// - Expiry: Explicit ForgetUserAsync() only (GDPR compliance)
    /// - Access: ~milliseconds (vector DB query)
    /// - Storage: IArchiveStore (same backend as Long, different namespace)
    /// - Promotion: confidence >= 0.8 AND confirmCount >= 3
    /// - Use case: User profile, core facts, learned preferences
    /// </summary>
    /// <remarks>
    /// Cognitive: Long-term memory (declarative, consolidated)
    /// RENAMED from "Semantic" (v0.3.x) to avoid confusion with Type.Semantic.
    /// "Archive" emphasizes permanence and high selectivity.
    /// </remarks>
    Archive = 3
}

/// <summary>
/// Memory stability level based on Ebbinghaus forgetting curve.
/// Higher stability = longer retention without reinforcement.
/// </summary>
/// <remarks>
/// Research reference: intentional-forgetting-mechanisms.md
/// Formula: R = e^(-t/S), where S = Stability
///
/// NOTE: This enum is independent of Tier.
/// A memory can be in any Tier with any Stability level.
/// Example: Buffer tier memory can have Permanent stability (locked system prompt).
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
