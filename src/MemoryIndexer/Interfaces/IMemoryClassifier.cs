using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Service for classifying messages to determine memory placement and type.
/// </summary>
/// <remarks>
/// Classification determines:
/// - Which tier the memory belongs to (Working, Session, User)
/// - What type of memory it is (Episodic, Semantic, Procedural, Fact)
/// - How important the content is (0.0 - 1.0)
/// - Whether the content should persist beyond the session
/// </remarks>
public interface IMemoryClassifier
{
    /// <summary>
    /// Classify a message to determine memory placement and type.
    /// </summary>
    /// <param name="content">The message content to classify.</param>
    /// <param name="context">Optional session context for better classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with tier, type, importance, and topics.</returns>
    Task<MemoryClassification> ClassifyAsync(
        string content,
        ClassificationContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify multiple messages in a batch.
    /// </summary>
    /// <param name="contents">Messages to classify.</param>
    /// <param name="context">Optional session context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification results in same order as input.</returns>
    Task<IReadOnlyList<MemoryClassification>> ClassifyBatchAsync(
        IEnumerable<string> contents,
        ClassificationContext? context = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of memory classification.
/// </summary>
public sealed class MemoryClassification
{
    /// <summary>
    /// Recommended memory tier.
    /// </summary>
    public required Tier Tier { get; init; }

    /// <summary>
    /// Primary detected memory type (highest confidence).
    /// </summary>
    public required MemoryType Type { get; init; }

    /// <summary>
    /// Secondary memory types (confidence >= 0.3).
    /// Enables multi-label classification for nuanced content.
    /// Phase 23.1: Memory Type Distribution Balancing.
    /// </summary>
    public IReadOnlyList<MemoryType> SecondaryTypes { get; init; } = [];

    /// <summary>
    /// Confidence scores for each memory type (0.0 - 1.0).
    /// Phase 23.1: Multi-score classification system.
    /// </summary>
    public IReadOnlyDictionary<MemoryType, float> TypeConfidences { get; init; } =
        new Dictionary<MemoryType, float>();

    /// <summary>
    /// Importance score (0.0 - 1.0).
    /// Higher scores indicate more important content.
    /// </summary>
    public required float Importance { get; init; }

    /// <summary>
    /// Detected topics/categories.
    /// </summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>
    /// Detected entities (people, places, things).
    /// </summary>
    public IReadOnlyList<string> Entities { get; init; } = [];

    /// <summary>
    /// Whether the content should persist beyond the session.
    /// False for transient messages like greetings or acknowledgments.
    /// </summary>
    public bool ShouldPersist { get; init; } = true;

    /// <summary>
    /// Confidence score of the classification (0.0 - 1.0).
    /// </summary>
    public float Confidence { get; init; } = 1.0f;

    /// <summary>
    /// Raw classification reason/explanation.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Default classification for transient content.
    /// </summary>
    public static MemoryClassification Transient => new()
    {
        Tier = Tier.Short,
        Type = MemoryType.Episodic,
        Importance = 0.1f,
        ShouldPersist = false,
        Confidence = 1.0f
    };

    /// <summary>
    /// Default classification for important user facts.
    /// </summary>
    public static MemoryClassification UserFact => new()
    {
        Tier = Tier.Archive,
        Type = MemoryType.Fact,
        Importance = 0.9f,
        ShouldPersist = true,
        Confidence = 1.0f
    };
}

/// <summary>
/// Context for classification to improve accuracy.
/// </summary>
public sealed class ClassificationContext
{
    /// <summary>
    /// User ID for personalization.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Summary of current session topics.
    /// </summary>
    public string? SessionSummary { get; init; }

    /// <summary>
    /// Recent conversation history for context.
    /// </summary>
    public IReadOnlyList<string>? RecentMessages { get; init; }

    /// <summary>
    /// Known user preferences/facts.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UserPreferences { get; init; }

    /// <summary>
    /// Role of the message sender (user, assistant, system).
    /// </summary>
    public string? MessageRole { get; init; }
}
