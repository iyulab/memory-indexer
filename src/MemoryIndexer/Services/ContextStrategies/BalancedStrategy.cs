using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services.ContextStrategies;

/// <summary>
/// Balanced context strategy with equal emphasis on recent, semantic, and session context.
/// Allocation: 30% recent, 25% semantic, 25% episodic, 20% facts.
///
/// Best for: General-purpose conversations, mixed workloads.
/// - Recent: Buffer + Short-term (current session's immediate context)
/// - Semantic: User-scoped facts via semantic search
/// - Episodic: Current session's long-term context (e.g., game progress)
/// - Facts: User-scoped facts by importance
/// </summary>
public class BalancedStrategy : ContextStrategyBase
{
    /// <inheritdoc />
    public override string Name => "Balanced";

    /// <summary>
    /// Creates a new balanced strategy with default allocation.
    /// </summary>
    public BalancedStrategy()
    {
        RecentPercent = 0.30;
        SemanticPercent = 0.25;
        EpisodicPercent = 0.25;
        FactPercent = 0.20;
    }
}
