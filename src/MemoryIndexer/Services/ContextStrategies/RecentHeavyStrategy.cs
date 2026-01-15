using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services.ContextStrategies;

/// <summary>
/// Recent-heavy context strategy prioritizing session context.
/// Allocation: 45% recent, 10% semantic, 35% episodic, 10% facts.
///
/// Best for: Games, multi-turn tasks, sequential conversations
/// where maintaining session context is critical.
/// - Recent: Buffer + Short-term (immediate turns)
/// - Episodic: Current session's long-term context (game progress, task state)
/// - Semantic/Facts: Minimal user-scoped info
/// </summary>
public class RecentHeavyStrategy : ContextStrategyBase
{
    /// <inheritdoc />
    public override string Name => "RecentHeavy";

    /// <summary>
    /// Creates a new recent-heavy strategy.
    /// </summary>
    public RecentHeavyStrategy()
    {
        RecentPercent = 0.45;
        SemanticPercent = 0.10;
        EpisodicPercent = 0.35;
        FactPercent = 0.10;
    }
}
