namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents a conversation session for grouping related memories.
/// </summary>
public sealed class Session
{
    /// <summary>
    /// Unique identifier for this session.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user or tenant this session belongs to.
    /// </summary>
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Optional human-readable name for the session.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// When this session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the last message was added to this session.
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>
    /// When this session was ended/closed.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Rolling summary of the session content.
    /// Updated periodically during long conversations.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Total number of turns (user + assistant messages) in this session.
    /// </summary>
    public int TurnCount { get; set; }

    /// <summary>
    /// Estimated total tokens used in this session.
    /// </summary>
    public int EstimatedTokens { get; set; }

    /// <summary>
    /// Primary topics discussed in this session.
    /// </summary>
    public List<string> Topics { get; set; } = [];

    /// <summary>
    /// Additional metadata for the session.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Whether this session is currently active.
    /// </summary>
    public bool IsActive => EndedAt is null;

    /// <summary>
    /// Records activity in this session.
    /// </summary>
    public void RecordActivity()
    {
        LastActivityAt = DateTime.UtcNow;
        TurnCount++;
    }

    /// <summary>
    /// Ends this session.
    /// </summary>
    public void End()
    {
        EndedAt = DateTime.UtcNow;
    }
}
