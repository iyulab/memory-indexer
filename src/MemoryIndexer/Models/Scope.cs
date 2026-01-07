namespace MemoryIndexer.Models;

/// <summary>
/// Memory scope dimension based on temporal and contextual granularity.
/// Orthogonal to Type and Tier - defines the "reach" of a memory across conversations.
/// </summary>
/// <remarks>
/// Design reference: docs/DESIGN_V0.4.md Section 2.1 "Scope Dimension"
///
/// Cognitive Science Basis:
/// - Tulving's Episodic/Semantic distinction (context-bound vs context-free)
/// - Cowan's Short-Term Memory Model (attention focus vs background context)
/// - Oberauer's Concentric Model (focus of attention -> activated LTM)
///
/// Scope Hierarchy (narrowest to broadest):
/// - Turn (S3): Single conversational turn buffer
/// - Topic (S2): Current topic/conversation thread
/// - Session (S1): Current conversation session
/// - User (S0): Cross-session persistent knowledge
///
/// API Visibility:
/// - User and Session scopes are exposed to API users
/// - Topic and Turn scopes are internal to VCM management
/// </remarks>
public enum Scope
{
    /// <summary>
    /// S3: Turn-level scope (internal only)
    /// - Transient buffer for current conversational turn
    /// - Auto-cleared after turn completion or promotion to Topic
    /// - Used for: Immediate context staging, turn-specific state
    /// - Lifetime: ~seconds (until turn ends)
    /// - Storage: In-memory buffer
    /// </summary>
    /// <remarks>
    /// NOT exposed to API - internal VCM management only.
    /// Roughly equivalent to CPU cache in OS memory hierarchy.
    /// </remarks>
    Turn = 0,

    /// <summary>
    /// S2: Topic-level scope (internal only)
    /// - Auto-detected topic clusters within a session
    /// - Groups related memories by semantic similarity
    /// - Used for: Topic-aware context retrieval, conversation threading
    /// - Lifetime: ~minutes (topic duration)
    /// - Storage: Working memory tier
    /// </summary>
    /// <remarks>
    /// NOT exposed to API - internal VCM management only.
    /// Auto-managed via topic detection (Phase 36.1).
    /// TopicId field links memories to detected topics.
    /// </remarks>
    Topic = 1,

    /// <summary>
    /// S1: Session-level scope (API exposed)
    /// - Conversation session boundary (e.g., single chat conversation)
    /// - Cleared when session ends via EndSessionAsync()
    /// - Used for: Session context, conversation-specific facts
    /// - Lifetime: ~hours (session duration)
    /// - Storage: Long-term tier (vector DB)
    /// </summary>
    /// <remarks>
    /// Primary scope for most applications.
    /// Exposed to API: RememberAsync(userId, sessionId, content)
    /// Cleared explicitly by user via EndSessionAsync().
    /// </remarks>
    Session = 2,

    /// <summary>
    /// S0: User-level scope (API exposed)
    /// - Cross-session persistent knowledge about the user
    /// - Never auto-cleared, only via ForgetUserAsync() (GDPR)
    /// - Used for: User preferences, identity facts, learned patterns
    /// - Lifetime: ~forever (until explicit deletion)
    /// - Storage: Archive tier (long-term persistent storage)
    /// </summary>
    /// <remarks>
    /// Highest scope level - survives session boundaries.
    /// Exposed to API: RememberAsync(userId, content) [no sessionId]
    /// Requires explicit promotion from Session (confidence >= 0.8, confirmCount >= 3).
    /// </remarks>
    User = 3
}
