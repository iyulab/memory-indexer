using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Services.ContextStrategies;

/// <summary>
/// Custom context strategy with user-defined allocation percentages.
/// </summary>
public class CustomStrategy : ContextStrategyBase
{
    private readonly string _name;

    /// <inheritdoc />
    public override string Name => _name;

    /// <summary>
    /// Creates a custom strategy with specified allocation percentages.
    /// </summary>
    /// <param name="name">Name for this custom strategy.</param>
    /// <param name="recentPercent">Percentage for recent conversation (0.0 - 1.0).</param>
    /// <param name="semanticPercent">Percentage for semantic memories (0.0 - 1.0).</param>
    /// <param name="episodicPercent">Percentage for episodic memories (0.0 - 1.0).</param>
    /// <param name="factPercent">Percentage for user facts (0.0 - 1.0).</param>
    /// <exception cref="ArgumentException">Thrown if percentages don't sum to ~1.0.</exception>
    public CustomStrategy(
        string name,
        double recentPercent,
        double semanticPercent,
        double episodicPercent = 0.0,
        double factPercent = 0.0)
    {
        var total = recentPercent + semanticPercent + episodicPercent + factPercent;
        if (Math.Abs(total - 1.0) > 0.01)
        {
            throw new ArgumentException(
                $"Allocation percentages must sum to 1.0, got {total:F2}",
                nameof(recentPercent));
        }

        _name = name;
        RecentPercent = recentPercent;
        SemanticPercent = semanticPercent;
        EpisodicPercent = episodicPercent;
        FactPercent = factPercent;
    }

    /// <summary>
    /// Creates a custom strategy that mirrors another strategy's allocation.
    /// </summary>
    public static CustomStrategy From(IContextStrategy source, string? name = null)
    {
        var (recent, semantic, episodic, fact) = source.GetAllocation();
        return new CustomStrategy(
            name ?? $"Custom_{source.Name}",
            recent, semantic, episodic, fact);
    }
}
