using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Strategy for building context with token budget allocation.
/// Different strategies can prioritize recent conversation, semantic memories,
/// or other sources based on the use case.
/// </summary>
public interface IContextStrategy
{
    /// <summary>
    /// Name of this strategy for identification.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Allocate the total token budget across different context sources.
    /// </summary>
    /// <param name="totalBudget">The total token budget to allocate.</param>
    /// <returns>Breakdown of token allocation per source.</returns>
    ContextBreakdown AllocateBudget(int totalBudget);

    /// <summary>
    /// Get the percentage allocation for each source.
    /// </summary>
    (double Recent, double Semantic, double Episodic, double Fact) GetAllocation();
}

/// <summary>
/// Base class for context strategies with configurable allocation percentages.
/// </summary>
public abstract class ContextStrategyBase : IContextStrategy
{
    /// <summary>Percentage of budget for recent conversation (0.0 - 1.0).</summary>
    protected double RecentPercent { get; init; }

    /// <summary>Percentage of budget for semantic memories (0.0 - 1.0).</summary>
    protected double SemanticPercent { get; init; }

    /// <summary>Percentage of budget for episodic memories (0.0 - 1.0).</summary>
    protected double EpisodicPercent { get; init; }

    /// <summary>Percentage of budget for user facts (0.0 - 1.0).</summary>
    protected double FactPercent { get; init; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public ContextBreakdown AllocateBudget(int totalBudget)
    {
        return new ContextBreakdown(
            RecentTokens: (int)(totalBudget * RecentPercent),
            SemanticTokens: (int)(totalBudget * SemanticPercent),
            EpisodicTokens: (int)(totalBudget * EpisodicPercent),
            FactTokens: (int)(totalBudget * FactPercent)
        );
    }

    /// <inheritdoc />
    public (double Recent, double Semantic, double Episodic, double Fact) GetAllocation()
        => (RecentPercent, SemanticPercent, EpisodicPercent, FactPercent);
}
