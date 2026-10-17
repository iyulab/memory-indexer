using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Services;

/// <summary>
/// Resolves the scope of a memory (Turn → Topic → Session → User) from its type and importance.
/// Stateless: one instance serves every conversation.
/// </summary>
public sealed partial class ScopeManager : IScopeManager
{
    private readonly ILogger<ScopeManager> _logger;

    public ScopeManager(ILogger<ScopeManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Scope> ResolveScopeAsync(
        string content,
        MemoryType type,
        float importance = 0.5f,
        CancellationToken cancellationToken = default)
    {
        // A function of type and importance only: it needs no conversation state

        // Scope resolution heuristics based on Type × Importance
        var scope = (type, importance) switch
        {
            // Episodic: Contextual experiences, typically Turn/Topic scope
            (MemoryType.Episodic, < 0.3f) => Scope.Turn,
            (MemoryType.Episodic, < 0.7f) => Scope.Topic,
            (MemoryType.Episodic, _) => Scope.Session,

            // Semantic: Facts and knowledge, typically Session/User scope
            (MemoryType.Semantic, < 0.5f) => Scope.Topic,
            (MemoryType.Semantic, < 0.8f) => Scope.Session,
            (MemoryType.Semantic, _) => Scope.User,

            // Procedural: How-to knowledge, typically Session/User scope
            (MemoryType.Procedural, < 0.6f) => Scope.Session,
            (MemoryType.Procedural, _) => Scope.User,

            // Fact: Assertions and preferences, typically User scope
            (MemoryType.Fact, < 0.7f) => Scope.Session,
            (MemoryType.Fact, _) => Scope.User,

            // Default fallback
            _ => Scope.Session
        };

        LogResolvedScope(_logger, scope, type, importance);

        return Task.FromResult(scope);
    }

    [LoggerMessage(Level = LogLevel.Trace, Message = "Resolved scope {Scope} for type {Type} with importance {Importance:F2}")]
    private static partial void LogResolvedScope(ILogger logger, Scope scope, MemoryType type, float importance);
}
