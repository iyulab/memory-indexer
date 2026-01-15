using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services.ContextStrategies;

/// <summary>
/// Semantic-heavy context strategy prioritizing user knowledge.
/// Allocation: 15% recent, 45% semantic, 15% episodic, 25% facts.
///
/// Best for: RAG applications, Q&A systems, knowledge retrieval
/// where finding relevant user information is more important than recency.
/// - Semantic: User-scoped knowledge via semantic search
/// - Facts: User-scoped facts by importance
/// - Episodic: Minimal session context for continuity
/// </summary>
public class SemanticHeavyStrategy : ContextStrategyBase
{
    /// <inheritdoc />
    public override string Name => "SemanticHeavy";

    /// <summary>
    /// Creates a new semantic-heavy strategy.
    /// </summary>
    public SemanticHeavyStrategy()
    {
        RecentPercent = 0.15;
        SemanticPercent = 0.45;
        EpisodicPercent = 0.15;
        FactPercent = 0.25;
    }
}
