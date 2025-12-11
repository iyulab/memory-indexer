using MemoryIndexer.Core.Interfaces;

namespace MemoryIndexer.Intelligence.Evaluation;

/// <summary>
/// Interface for LoCoMo (Long-term Conversation Memory) benchmark evaluation.
/// Tests memory retrieval across long conversation contexts with temporal,
/// multi-hop reasoning, and cross-session queries.
/// </summary>
public interface ILoCoMoEvaluator
{
    /// <summary>
    /// Evaluates a complete LoCoMo test suite against the memory store.
    /// </summary>
    /// <param name="memoryStore">Memory store to evaluate.</param>
    /// <param name="testSuite">Test suite containing queries and expected results.</param>
    /// <param name="userId">User ID for tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation result with aggregate and per-query metrics.</returns>
    Task<LoCoMoEvaluationResult> EvaluateAsync(
        IMemoryStore memoryStore,
        LoCoMoTestSuite testSuite,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a single LoCoMo test query.
    /// </summary>
    /// <param name="memoryStore">Memory store to evaluate.</param>
    /// <param name="testQuery">Test query with expected results.</param>
    /// <param name="userId">User ID for tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with metrics.</returns>
    Task<LoCoMoQueryResult> EvaluateQueryAsync(
        IMemoryStore memoryStore,
        LoCoMoTestQuery testQuery,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a synthetic test suite for evaluation.
    /// </summary>
    /// <param name="conversationTurns">Number of conversation turns to generate.</param>
    /// <param name="queriesPerType">Number of queries per type to generate.</param>
    /// <returns>Generated test suite.</returns>
    LoCoMoTestSuite GenerateSyntheticTestSuite(int conversationTurns = 50, int queriesPerType = 5);
}
