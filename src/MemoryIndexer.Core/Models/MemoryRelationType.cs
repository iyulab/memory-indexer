namespace MemoryIndexer.Core.Models;

/// <summary>
/// Represents the type of relationship between two memories.
/// Used for knowledge graph construction.
/// </summary>
public enum MemoryRelationType
{
    /// <summary>
    /// The source memory supports or provides evidence for the target memory.
    /// </summary>
    Supports = 0,

    /// <summary>
    /// The source memory contradicts or conflicts with the target memory.
    /// </summary>
    Contradicts = 1,

    /// <summary>
    /// The source memory elaborates or provides more detail about the target memory.
    /// </summary>
    Elaborates = 2,

    /// <summary>
    /// The source memory supersedes or replaces the target memory (newer information).
    /// </summary>
    Supersedes = 3,

    /// <summary>
    /// The source memory is related to the target memory (general association).
    /// </summary>
    RelatedTo = 4,

    /// <summary>
    /// The source memory is derived from or based on the target memory.
    /// </summary>
    DerivedFrom = 5
}
