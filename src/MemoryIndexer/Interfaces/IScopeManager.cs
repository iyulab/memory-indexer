using MemoryIndexer.Models;

namespace MemoryIndexer.Interfaces;

/// <summary>
/// Resolves the scope dimension of the 3-axis memory model.
/// Scope represents temporal containment: Turn → Topic → Session → User.
/// </summary>
/// <remarks>
/// 3-Axis Memory Model (DESIGN_V0.4.md):
/// - Type: What (Episodic, Semantic, Procedural, Fact)
/// - Scope: When (Turn, Topic, Session, User)
/// - Tier: Where (Buffer, Short, Long, Archive)
///
/// The scope of a memory is a function of its type and importance; resolving it needs no conversation state.
/// </remarks>
public interface IScopeManager
{
    /// <summary>
    /// Resolves the appropriate scope for a memory based on its type and importance.
    /// </summary>
    /// <param name="content">Memory content.</param>
    /// <param name="type">Memory type.</param>
    /// <param name="importance">Importance score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommended scope for the memory.</returns>
    Task<Scope> ResolveScopeAsync(
        string content,
        MemoryType type,
        float importance = 0.5f,
        CancellationToken cancellationToken = default);
}
