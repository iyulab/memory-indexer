namespace MemoryIndexer.Models;

/// <summary>
/// Scope-structured memory context returned by RecallAsync.
/// Groups memories by their scope for intuitive consumption.
/// </summary>
/// <remarks>
/// Simple API (Level 0-1) returns memories grouped by scope:
/// - UserMemories: Cross-session facts (Scope=User)
/// - SessionMemories: Current session context (Scope=Session)
/// - TopicMemories: Current topic context (Scope=Topic, internal use)
///
/// This structure makes it easy for applications to understand
/// which memories are persistent (User) vs ephemeral (Session/Topic).
/// </remarks>
public sealed class MemoryContext
{
    /// <summary>
    /// Cross-session user facts and preferences (Scope=User).
    /// These memories persist across all sessions for the user.
    /// </summary>
    public IReadOnlyList<MemoryUnit> UserMemories { get; init; } = Array.Empty<MemoryUnit>();

    /// <summary>
    /// Current session memories (Scope=Session).
    /// These memories are relevant only within the current session.
    /// </summary>
    public IReadOnlyList<MemoryUnit> SessionMemories { get; init; } = Array.Empty<MemoryUnit>();

    /// <summary>
    /// Current topic memories (Scope=Topic).
    /// Internal use - typically not exposed to end users.
    /// </summary>
    public IReadOnlyList<MemoryUnit> TopicMemories { get; init; } = Array.Empty<MemoryUnit>();

    /// <summary>
    /// Total count of all memories across all scopes.
    /// </summary>
    public int TotalCount => UserMemories.Count + SessionMemories.Count + TopicMemories.Count;

    /// <summary>
    /// Returns all memories from all scopes as a single enumerable.
    /// Order: User → Session → Topic (broadest to narrowest).
    /// </summary>
    public IEnumerable<MemoryUnit> AllMemories() =>
        UserMemories.Concat(SessionMemories).Concat(TopicMemories);

    /// <summary>
    /// Filters all memories by type (Episodic, Semantic, Procedural, Fact).
    /// </summary>
    public IEnumerable<MemoryUnit> ByType(MemoryType type) =>
        AllMemories().Where(m => m.Type == type);

    /// <summary>
    /// Filters all memories by scope (User, Session, Topic, Turn).
    /// </summary>
    public IEnumerable<MemoryUnit> ByScope(Scope scope) =>
        AllMemories().Where(m => m.Scope == scope);

    /// <summary>
    /// Creates an empty MemoryContext with no memories.
    /// </summary>
    public static MemoryContext Empty => new();
}
