namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents the type of memory stored in the system.
/// Based on cognitive science memory classification.
/// </summary>
public enum MemoryType
{
    /// <summary>
    /// Episodic memory - specific events and experiences with temporal context.
    /// Example: "User asked about authentication on Dec 8th"
    /// </summary>
    Episodic = 0,

    /// <summary>
    /// Semantic memory - general facts and knowledge without temporal context.
    /// Example: "User prefers dark mode"
    /// </summary>
    Semantic = 1,

    /// <summary>
    /// Procedural memory - how to do things, patterns and workflows.
    /// Example: "User's deployment process involves staging first"
    /// </summary>
    Procedural = 2,

    /// <summary>
    /// Factual memory - specific verifiable facts.
    /// Example: "User's company is Acme Corp"
    /// </summary>
    Fact = 3
}
