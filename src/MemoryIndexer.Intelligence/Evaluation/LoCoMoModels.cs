namespace MemoryIndexer.Intelligence.Evaluation;

/// <summary>
/// Types of queries in the LoCoMo benchmark.
/// </summary>
public enum LoCoMoQueryType
{
    /// <summary>
    /// Single-hop queries requiring direct memory recall.
    /// </summary>
    SingleHop,

    /// <summary>
    /// Multi-hop queries requiring information aggregation across multiple memories.
    /// </summary>
    MultiHop,

    /// <summary>
    /// Temporal queries testing time-aware retrieval.
    /// </summary>
    Temporal,

    /// <summary>
    /// Cross-session queries testing retrieval across conversation sessions.
    /// </summary>
    CrossSession,

    /// <summary>
    /// Factual queries testing specific fact retrieval.
    /// </summary>
    Factual
}

/// <summary>
/// Test suite for LoCoMo evaluation.
/// </summary>
public sealed class LoCoMoTestSuite
{
    /// <summary>
    /// Unique identifier for the test suite.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name of the test suite.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of the test suite.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Conversation memories to seed for evaluation.
    /// </summary>
    public IReadOnlyList<LoCoMoConversationMemory> ConversationMemories { get; init; } = [];

    /// <summary>
    /// Test queries to evaluate.
    /// </summary>
    public IReadOnlyList<LoCoMoTestQuery> TestQueries { get; init; } = [];
}

/// <summary>
/// A conversation memory entry for LoCoMo evaluation.
/// </summary>
public sealed class LoCoMoConversationMemory
{
    /// <summary>
    /// Unique identifier for the memory.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Content of the memory.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Topic category of the memory.
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// Session ID for cross-session testing.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Timestamp of the memory for temporal testing.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Turn index within the conversation.
    /// </summary>
    public int TurnIndex { get; init; }
}

/// <summary>
/// A test query for LoCoMo evaluation.
/// </summary>
public sealed class LoCoMoTestQuery
{
    /// <summary>
    /// Unique identifier for the query.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Type of query for categorized metrics.
    /// </summary>
    public required LoCoMoQueryType QueryType { get; init; }

    /// <summary>
    /// IDs of memories that are relevant to this query (ground truth).
    /// </summary>
    public required IReadOnlyList<string> RelevantMemoryIds { get; init; }

    /// <summary>
    /// Expected answer content for answer coverage calculation.
    /// </summary>
    public string? ExpectedAnswer { get; init; }

    /// <summary>
    /// Number of top results to retrieve.
    /// </summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Optional temporal filter for time-based queries.
    /// </summary>
    public TemporalFilter? TemporalFilter { get; init; }
}

/// <summary>
/// Temporal filter for time-based queries.
/// </summary>
public sealed class TemporalFilter
{
    /// <summary>
    /// Return memories created after this time.
    /// </summary>
    public DateTime? After { get; init; }

    /// <summary>
    /// Return memories created before this time.
    /// </summary>
    public DateTime? Before { get; init; }
}

/// <summary>
/// Result of evaluating a complete LoCoMo test suite.
/// </summary>
public sealed class LoCoMoEvaluationResult
{
    /// <summary>
    /// ID of the evaluated test suite.
    /// </summary>
    public required string TestSuiteId { get; init; }

    /// <summary>
    /// Name of the evaluated test suite.
    /// </summary>
    public required string TestSuiteName { get; init; }

    /// <summary>
    /// Total number of queries evaluated.
    /// </summary>
    public int TotalQueries { get; init; }

    /// <summary>
    /// Number of successful queries (at least one relevant result).
    /// </summary>
    public int SuccessfulQueries { get; init; }

    /// <summary>
    /// Number of failed queries.
    /// </summary>
    public int FailedQueries { get; init; }

    /// <summary>
    /// Aggregate metrics across all queries.
    /// </summary>
    public required LoCoMoAggregateMetrics Metrics { get; init; }

    /// <summary>
    /// Individual query results.
    /// </summary>
    public required IReadOnlyList<LoCoMoQueryResult> QueryResults { get; init; }

    /// <summary>
    /// Metrics broken down by query type.
    /// </summary>
    public required IReadOnlyDictionary<LoCoMoQueryType, LoCoMoAggregateMetrics> ByQueryType { get; init; }

    /// <summary>
    /// Total evaluation duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of evaluating a single LoCoMo query.
/// </summary>
public sealed class LoCoMoQueryResult
{
    /// <summary>
    /// ID of the evaluated query.
    /// </summary>
    public required string QueryId { get; init; }

    /// <summary>
    /// Type of the query.
    /// </summary>
    public required LoCoMoQueryType QueryType { get; init; }

    /// <summary>
    /// The query text.
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// Whether at least one relevant memory was retrieved.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Recall: fraction of relevant memories retrieved.
    /// </summary>
    public float Recall { get; init; }

    /// <summary>
    /// Precision: fraction of retrieved memories that are relevant.
    /// </summary>
    public float Precision { get; init; }

    /// <summary>
    /// F1 Score: harmonic mean of precision and recall.
    /// </summary>
    public float F1Score { get; init; }

    /// <summary>
    /// Mean Reciprocal Rank: 1/rank of first relevant result.
    /// </summary>
    public float MeanReciprocalRank { get; init; }

    /// <summary>
    /// Normalized Discounted Cumulative Gain.
    /// </summary>
    public float NDCG { get; init; }

    /// <summary>
    /// Answer coverage: semantic similarity between expected and retrieved content.
    /// </summary>
    public float AnswerCoverage { get; init; }

    /// <summary>
    /// Number of memories retrieved.
    /// </summary>
    public int RetrievedCount { get; init; }

    /// <summary>
    /// Number of relevant memories in retrieved results.
    /// </summary>
    public int RelevantRetrieved { get; init; }

    /// <summary>
    /// Query latency.
    /// </summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// IDs of retrieved memories.
    /// </summary>
    public IReadOnlyList<string>? RetrievedMemoryIds { get; init; }

    /// <summary>
    /// Top similarity scores.
    /// </summary>
    public IReadOnlyList<float>? TopScores { get; init; }

    /// <summary>
    /// Error message if the query failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Aggregate metrics for LoCoMo evaluation.
/// </summary>
public sealed class LoCoMoAggregateMetrics
{
    /// <summary>
    /// Average recall across all queries.
    /// </summary>
    public float OverallRecall { get; init; }

    /// <summary>
    /// Average precision across all queries.
    /// </summary>
    public float OverallPrecision { get; init; }

    /// <summary>
    /// Average F1 score across all queries.
    /// </summary>
    public float OverallF1Score { get; init; }

    /// <summary>
    /// Average Mean Reciprocal Rank.
    /// </summary>
    public float OverallMRR { get; init; }

    /// <summary>
    /// Average Normalized Discounted Cumulative Gain.
    /// </summary>
    public float OverallNDCG { get; init; }

    /// <summary>
    /// Average answer coverage.
    /// </summary>
    public float OverallAnswerCoverage { get; init; }

    /// <summary>
    /// Average query latency.
    /// </summary>
    public TimeSpan AverageLatency { get; init; }

    /// <summary>
    /// Success rate (queries with at least one relevant result).
    /// </summary>
    public float SuccessRate { get; init; }
}
