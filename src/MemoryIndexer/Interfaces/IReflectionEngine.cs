using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Reflection Engine for higher-order memory synthesis and insight generation.
/// Implements Generative Agents-style reflection for memory consolidation.
/// </summary>
/// <remarks>
/// Research basis:
/// - Generative Agents: Periodic reflection generating higher-level insights
/// - MemInsight: Autonomous memory augmentation with contextual info
/// - A-MEM: Zettelkasten-style memory linking and evolution
/// </remarks>
public interface IReflectionEngine
{
    /// <summary>
    /// Triggers a reflection cycle over recent memories.
    /// Generates higher-level insights and connections.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="options">Reflection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reflection result with generated insights.</returns>
    Task<ReflectionResult> ReflectAsync(
        string userId,
        ReflectionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if reflection should be triggered based on memory state.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether reflection is recommended and why.</returns>
    Task<ReflectionTriggerResult> ShouldReflectAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates insights from a set of memories.
    /// </summary>
    /// <param name="memories">Memories to synthesize.</param>
    /// <param name="context">Optional context for insight generation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated insights.</returns>
    Task<IReadOnlyList<MemoryInsight>> GenerateInsightsAsync(
        IReadOnlyList<MemoryUnit> memories,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesizes a question from accumulated memories.
    /// Useful for identifying what information is still needed.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="topic">Topic to focus on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Synthesized questions.</returns>
    Task<IReadOnlyList<SynthesizedQuestion>> SynthesizeQuestionsAsync(
        string userId,
        string? topic = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links related memories based on reflection analysis.
    /// Creates Zettelkasten-style connections.
    /// </summary>
    /// <param name="memories">Memories to analyze for links.</param>
    /// <param name="options">Linking options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered links.</returns>
    Task<IReadOnlyList<MemoryLink>> DiscoverLinksAsync(
        IReadOnlyList<MemoryUnit> memories,
        LinkDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a summary of recent memory activity.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="timeWindow">Time window to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Activity summary.</returns>
    Task<MemoryActivitySummary> SummarizeActivityAsync(
        string userId,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets reflection history for a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="limit">Maximum records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reflection history.</returns>
    Task<IReadOnlyList<ReflectionRecord>> GetReflectionHistoryAsync(
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

#region Reflection Types

/// <summary>
/// Options for reflection process.
/// </summary>
public sealed class ReflectionOptions
{
    /// <summary>
    /// Time window of memories to reflect on.
    /// </summary>
    public TimeSpan TimeWindow { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Maximum memories to include in reflection.
    /// </summary>
    public int MaxMemories { get; init; } = 100;

    /// <summary>
    /// Minimum importance score for inclusion.
    /// </summary>
    public float MinImportance { get; init; } = 0.3f;

    /// <summary>
    /// Types of memories to include.
    /// </summary>
    public IReadOnlyList<MemoryType>? IncludeTypes { get; init; }

    /// <summary>
    /// Focus topic (optional).
    /// </summary>
    public string? FocusTopic { get; init; }

    /// <summary>
    /// Whether to generate insights.
    /// </summary>
    public bool GenerateInsights { get; init; } = true;

    /// <summary>
    /// Whether to discover memory links.
    /// </summary>
    public bool DiscoverLinks { get; init; } = true;

    /// <summary>
    /// Whether to identify patterns.
    /// </summary>
    public bool IdentifyPatterns { get; init; } = true;

    /// <summary>
    /// Maximum insights to generate.
    /// </summary>
    public int MaxInsights { get; init; } = 10;

    /// <summary>
    /// Depth of reflection (1-3).
    /// </summary>
    public int ReflectionDepth { get; init; } = 2;
}

/// <summary>
/// Result of a reflection cycle.
/// </summary>
public sealed class ReflectionResult
{
    /// <summary>
    /// Unique identifier for this reflection.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Whether reflection was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Memories that were reflected upon.
    /// </summary>
    public IReadOnlyList<Guid> ReflectedMemoryIds { get; set; } = [];

    /// <summary>
    /// Generated insights.
    /// </summary>
    public IReadOnlyList<MemoryInsight> Insights { get; set; } = [];

    /// <summary>
    /// Discovered memory links.
    /// </summary>
    public IReadOnlyList<MemoryLink> DiscoveredLinks { get; set; } = [];

    /// <summary>
    /// Identified patterns.
    /// </summary>
    public IReadOnlyList<MemoryPattern> Patterns { get; set; } = [];

    /// <summary>
    /// Questions arising from reflection.
    /// </summary>
    public IReadOnlyList<SynthesizedQuestion> Questions { get; set; } = [];

    /// <summary>
    /// New memories created from reflection.
    /// </summary>
    public IReadOnlyList<Guid> CreatedMemoryIds { get; set; } = [];

    /// <summary>
    /// Summary of the reflection.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// When the reflection occurred.
    /// </summary>
    public DateTime ReflectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the reflection process.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Statistics about the reflection.
    /// </summary>
    public ReflectionStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Result of reflection trigger check.
/// </summary>
public sealed class ReflectionTriggerResult
{
    /// <summary>
    /// Whether reflection is recommended.
    /// </summary>
    public bool ShouldReflect { get; set; }

    /// <summary>
    /// Reasons for the recommendation.
    /// </summary>
    public IReadOnlyList<string> Reasons { get; set; } = [];

    /// <summary>
    /// Priority of the reflection (0-1).
    /// </summary>
    public float Priority { get; set; }

    /// <summary>
    /// Suggested focus topics.
    /// </summary>
    public IReadOnlyList<string> SuggestedTopics { get; set; } = [];

    /// <summary>
    /// Last reflection time.
    /// </summary>
    public DateTime? LastReflection { get; set; }

    /// <summary>
    /// Memories accumulated since last reflection.
    /// </summary>
    public int AccumulatedMemories { get; set; }

    /// <summary>
    /// Accumulated importance score.
    /// </summary>
    public float AccumulatedImportance { get; set; }
}

/// <summary>
/// Insight generated from reflection.
/// </summary>
public sealed class MemoryInsight
{
    /// <summary>
    /// Unique identifier for this insight.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The insight content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Type of insight.
    /// </summary>
    public InsightType Type { get; set; }

    /// <summary>
    /// Confidence in this insight (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Source memory IDs that contributed to this insight.
    /// </summary>
    public IReadOnlyList<Guid> SourceMemoryIds { get; set; } = [];

    /// <summary>
    /// Related entities mentioned in the insight.
    /// </summary>
    public IReadOnlyList<string> RelatedEntities { get; set; } = [];

    /// <summary>
    /// Importance score (0-1).
    /// </summary>
    public float ImportanceScore { get; set; }

    /// <summary>
    /// Whether this insight was stored as a new memory.
    /// </summary>
    public bool StoredAsMemory { get; set; }

    /// <summary>
    /// New memory ID if stored.
    /// </summary>
    public Guid? MemoryId { get; set; }

    /// <summary>
    /// When the insight was generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Question synthesized from memory gaps.
/// </summary>
public sealed class SynthesizedQuestion
{
    /// <summary>
    /// The question.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Why this question was generated.
    /// </summary>
    public string Rationale { get; set; } = string.Empty;

    /// <summary>
    /// Related entities the question concerns.
    /// </summary>
    public IReadOnlyList<string> RelatedEntities { get; set; } = [];

    /// <summary>
    /// Memories that prompted this question.
    /// </summary>
    public IReadOnlyList<Guid> SourceMemoryIds { get; set; } = [];

    /// <summary>
    /// Priority of answering this question (0-1).
    /// </summary>
    public float Priority { get; set; }

    /// <summary>
    /// Category of the question.
    /// </summary>
    public QuestionCategory Category { get; set; }
}

/// <summary>
/// Link between memories.
/// </summary>
public sealed class MemoryLink
{
    /// <summary>
    /// Unique identifier for this link.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Source memory ID.
    /// </summary>
    public Guid SourceMemoryId { get; set; }

    /// <summary>
    /// Target memory ID.
    /// </summary>
    public Guid TargetMemoryId { get; set; }

    /// <summary>
    /// Type of link.
    /// </summary>
    public LinkType Type { get; set; }

    /// <summary>
    /// Strength of the link (0-1).
    /// </summary>
    public float Strength { get; set; }

    /// <summary>
    /// Reason for the link.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a bidirectional link.
    /// </summary>
    public bool IsBidirectional { get; set; }

    /// <summary>
    /// When the link was discovered.
    /// </summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Pattern identified in memories.
/// </summary>
public sealed class MemoryPattern
{
    /// <summary>
    /// Unique identifier for this pattern.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Description of the pattern.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Type of pattern.
    /// </summary>
    public PatternType Type { get; set; }

    /// <summary>
    /// Memories that exhibit this pattern.
    /// </summary>
    public IReadOnlyList<Guid> MemoryIds { get; set; } = [];

    /// <summary>
    /// Confidence in this pattern (0-1).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Frequency of occurrence.
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// Related entities.
    /// </summary>
    public IReadOnlyList<string> RelatedEntities { get; set; } = [];
}

/// <summary>
/// Options for link discovery.
/// </summary>
public sealed class LinkDiscoveryOptions
{
    /// <summary>
    /// Minimum similarity for semantic links.
    /// </summary>
    public float MinSimilarity { get; init; } = 0.6f;

    /// <summary>
    /// Whether to find entity-based links.
    /// </summary>
    public bool FindEntityLinks { get; init; } = true;

    /// <summary>
    /// Whether to find temporal links.
    /// </summary>
    public bool FindTemporalLinks { get; init; } = true;

    /// <summary>
    /// Whether to find semantic links.
    /// </summary>
    public bool FindSemanticLinks { get; init; } = true;

    /// <summary>
    /// Whether to find causal links.
    /// </summary>
    public bool FindCausalLinks { get; init; }

    /// <summary>
    /// Maximum links to discover.
    /// </summary>
    public int MaxLinks { get; init; } = 50;
}

/// <summary>
/// Summary of memory activity.
/// </summary>
public sealed class MemoryActivitySummary
{
    /// <summary>
    /// Time period covered.
    /// </summary>
    public TimeSpan Period { get; set; }

    /// <summary>
    /// Total memories created.
    /// </summary>
    public int MemoriesCreated { get; set; }

    /// <summary>
    /// Total memories accessed.
    /// </summary>
    public int MemoriesAccessed { get; set; }

    /// <summary>
    /// Total memories updated.
    /// </summary>
    public int MemoriesUpdated { get; set; }

    /// <summary>
    /// Most active topics.
    /// </summary>
    public IReadOnlyList<TopicActivity> TopTopics { get; set; } = [];

    /// <summary>
    /// Most mentioned entities.
    /// </summary>
    public IReadOnlyList<EntityActivity> TopEntities { get; set; } = [];

    /// <summary>
    /// Memory type distribution.
    /// </summary>
    public IReadOnlyDictionary<MemoryType, int> TypeDistribution { get; set; } = new Dictionary<MemoryType, int>();

    /// <summary>
    /// Average importance of new memories.
    /// </summary>
    public float AverageImportance { get; set; }

    /// <summary>
    /// Summary text.
    /// </summary>
    public string TextSummary { get; set; } = string.Empty;
}

/// <summary>
/// Topic activity information.
/// </summary>
public sealed class TopicActivity
{
    /// <summary>
    /// Topic name.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Memory count for this topic.
    /// </summary>
    public int MemoryCount { get; set; }

    /// <summary>
    /// Access count for this topic.
    /// </summary>
    public int AccessCount { get; set; }
}

/// <summary>
/// Entity activity information.
/// </summary>
public sealed class EntityActivity
{
    /// <summary>
    /// Entity name.
    /// </summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// Mention count.
    /// </summary>
    public int MentionCount { get; set; }

    /// <summary>
    /// Related memory count.
    /// </summary>
    public int MemoryCount { get; set; }
}

/// <summary>
/// Record of a past reflection.
/// </summary>
public sealed class ReflectionRecord
{
    /// <summary>
    /// Reflection ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// When the reflection occurred.
    /// </summary>
    public DateTime ReflectedAt { get; set; }

    /// <summary>
    /// Number of memories reflected upon.
    /// </summary>
    public int MemoryCount { get; set; }

    /// <summary>
    /// Number of insights generated.
    /// </summary>
    public int InsightCount { get; set; }

    /// <summary>
    /// Number of links discovered.
    /// </summary>
    public int LinkCount { get; set; }

    /// <summary>
    /// Summary of the reflection.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Duration of reflection.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Statistics about a reflection.
/// </summary>
public sealed class ReflectionStatistics
{
    /// <summary>
    /// Number of memories processed.
    /// </summary>
    public int MemoriesProcessed { get; set; }

    /// <summary>
    /// Number of insights generated.
    /// </summary>
    public int InsightsGenerated { get; set; }

    /// <summary>
    /// Number of links discovered.
    /// </summary>
    public int LinksDiscovered { get; set; }

    /// <summary>
    /// Number of patterns identified.
    /// </summary>
    public int PatternsIdentified { get; set; }

    /// <summary>
    /// Number of questions synthesized.
    /// </summary>
    public int QuestionsSynthesized { get; set; }

    /// <summary>
    /// Number of new memories created.
    /// </summary>
    public int MemoriesCreated { get; set; }

    /// <summary>
    /// Total tokens processed (estimated).
    /// </summary>
    public int TokensProcessed { get; set; }
}

#endregion

#region Enums

/// <summary>
/// Types of insights.
/// </summary>
public enum InsightType
{
    /// <summary>Generalization from multiple memories.</summary>
    Generalization,
    /// <summary>Connection between concepts.</summary>
    Connection,
    /// <summary>Trend or pattern.</summary>
    Trend,
    /// <summary>Contradiction or conflict.</summary>
    Contradiction,
    /// <summary>Summary of topic.</summary>
    Summary,
    /// <summary>Prediction or inference.</summary>
    Inference,
    /// <summary>Cause-effect relationship.</summary>
    Causal,
    /// <summary>Temporal observation.</summary>
    Temporal
}

/// <summary>
/// Categories of synthesized questions.
/// </summary>
public enum QuestionCategory
{
    /// <summary>Clarification needed.</summary>
    Clarification,
    /// <summary>Missing information.</summary>
    MissingInfo,
    /// <summary>Verification needed.</summary>
    Verification,
    /// <summary>Elaboration desired.</summary>
    Elaboration,
    /// <summary>Contradiction resolution.</summary>
    ContradictionResolution,
    /// <summary>Future prediction.</summary>
    Prediction
}

/// <summary>
/// Types of memory links.
/// </summary>
public enum LinkType
{
    /// <summary>Semantic similarity.</summary>
    Semantic,
    /// <summary>Shared entity.</summary>
    Entity,
    /// <summary>Temporal proximity.</summary>
    Temporal,
    /// <summary>Causal relationship.</summary>
    Causal,
    /// <summary>Contradictory content.</summary>
    Contradictory,
    /// <summary>Supporting evidence.</summary>
    Supporting,
    /// <summary>Sequential relationship.</summary>
    Sequential,
    /// <summary>Hierarchical (parent-child).</summary>
    Hierarchical
}

/// <summary>
/// Types of memory patterns.
/// </summary>
public enum PatternType
{
    /// <summary>Recurring topic.</summary>
    RecurringTopic,
    /// <summary>Behavioral pattern.</summary>
    Behavioral,
    /// <summary>Temporal pattern.</summary>
    Temporal,
    /// <summary>Entity co-occurrence.</summary>
    EntityCooccurrence,
    /// <summary>Sentiment trend.</summary>
    SentimentTrend,
    /// <summary>Information evolution.</summary>
    Evolution
}

#endregion
