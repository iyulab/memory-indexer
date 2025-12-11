namespace MemoryIndexer.Intelligence.Evaluation;

/// <summary>
/// Service for evaluating memory retrieval quality using RAGAS-style metrics.
/// Integrates with FluxImprover for LLM-based evaluation.
/// </summary>
public interface IRetrievalEvaluator
{
    /// <summary>
    /// Evaluates a single retrieval operation.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="retrievedContexts">The retrieved memory contents.</param>
    /// <param name="generatedAnswer">The answer generated using the contexts (optional).</param>
    /// <param name="groundTruth">The expected answer (optional, for ground truth comparison).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evaluation metrics.</returns>
    Task<RetrievalEvaluationResult> EvaluateAsync(
        string query,
        IReadOnlyList<string> retrievedContexts,
        string? generatedAnswer = null,
        string? groundTruth = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a batch of retrieval operations.
    /// </summary>
    /// <param name="evaluations">The evaluation cases.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated evaluation results.</returns>
    Task<BatchEvaluationResult> EvaluateBatchAsync(
        IReadOnlyList<EvaluationCase> evaluations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a Needle-in-Haystack test.
    /// </summary>
    /// <param name="needleContent">The content to find (needle).</param>
    /// <param name="contextSize">Number of distractor memories (haystack).</param>
    /// <param name="iterations">Number of test iterations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test results.</returns>
    Task<NeedleInHaystackResult> RunNeedleInHaystackTestAsync(
        string needleContent,
        int contextSize,
        int iterations = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a retrieval evaluation.
/// </summary>
public sealed class RetrievalEvaluationResult
{
    /// <summary>
    /// Context Relevance: How relevant are the retrieved contexts to the query?
    /// Range: 0.0 to 1.0 (higher is better)
    /// </summary>
    public float ContextRelevance { get; init; }

    /// <summary>
    /// Context Recall: What proportion of ground truth is covered by retrieved contexts?
    /// Range: 0.0 to 1.0 (higher is better)
    /// Requires ground truth to be provided.
    /// </summary>
    public float? ContextRecall { get; init; }

    /// <summary>
    /// Faithfulness: Is the generated answer grounded in the retrieved contexts?
    /// Range: 0.0 to 1.0 (higher is better)
    /// Requires generated answer to be provided.
    /// </summary>
    public float? Faithfulness { get; init; }

    /// <summary>
    /// Answer Relevance: Does the answer address the original question?
    /// Range: 0.0 to 1.0 (higher is better)
    /// Requires generated answer to be provided.
    /// </summary>
    public float? AnswerRelevance { get; init; }

    /// <summary>
    /// Answerability: Can the question be answered from the contexts?
    /// Range: 0.0 to 1.0 (higher is better)
    /// </summary>
    public float Answerability { get; init; }

    /// <summary>
    /// Context Precision: Are the most relevant contexts ranked higher?
    /// Range: 0.0 to 1.0 (higher is better)
    /// </summary>
    public float ContextPrecision { get; init; }

    /// <summary>
    /// Semantic Similarity: How semantically similar are contexts to the query?
    /// Range: 0.0 to 1.0 (higher is better)
    /// </summary>
    public float SemanticSimilarity { get; init; }

    /// <summary>
    /// Overall quality score (weighted average of all metrics).
    /// Range: 0.0 to 1.0 (higher is better)
    /// </summary>
    public float OverallScore { get; init; }

    /// <summary>
    /// Number of contexts evaluated.
    /// </summary>
    public int ContextCount { get; init; }

    /// <summary>
    /// Individual context scores.
    /// </summary>
    public IReadOnlyList<ContextScore> ContextScores { get; init; } = [];

    /// <summary>
    /// Evaluation metadata.
    /// </summary>
    public EvaluationMetadata Metadata { get; init; } = new();
}

/// <summary>
/// Score for an individual context.
/// </summary>
public sealed class ContextScore
{
    /// <summary>
    /// Index of the context in the retrieved list.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Relevance score for this context.
    /// </summary>
    public float Relevance { get; init; }

    /// <summary>
    /// Whether this context contributes to answering the question.
    /// </summary>
    public bool IsUseful { get; init; }

    /// <summary>
    /// Explanation of the score.
    /// </summary>
    public string? Explanation { get; init; }
}

/// <summary>
/// Metadata about the evaluation.
/// </summary>
public sealed class EvaluationMetadata
{
    /// <summary>
    /// Time taken for evaluation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Evaluator version.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Whether LLM-based evaluation was used.
    /// </summary>
    public bool UsedLlmEvaluation { get; init; }

    /// <summary>
    /// Model used for LLM evaluation (if applicable).
    /// </summary>
    public string? LlmModel { get; init; }

    /// <summary>
    /// Timestamp of evaluation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An evaluation case for batch processing.
/// </summary>
public sealed class EvaluationCase
{
    /// <summary>
    /// Case identifier.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// The retrieved contexts.
    /// </summary>
    public required IReadOnlyList<string> RetrievedContexts { get; init; }

    /// <summary>
    /// Generated answer (optional).
    /// </summary>
    public string? GeneratedAnswer { get; init; }

    /// <summary>
    /// Ground truth answer (optional).
    /// </summary>
    public string? GroundTruth { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Result of batch evaluation.
/// </summary>
public sealed class BatchEvaluationResult
{
    /// <summary>
    /// Number of cases evaluated.
    /// </summary>
    public int TotalCases { get; init; }

    /// <summary>
    /// Number of successful evaluations.
    /// </summary>
    public int SuccessfulEvaluations { get; init; }

    /// <summary>
    /// Number of failed evaluations.
    /// </summary>
    public int FailedEvaluations { get; init; }

    /// <summary>
    /// Aggregated metrics (averages).
    /// </summary>
    public AggregatedMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Individual case results.
    /// </summary>
    public IReadOnlyList<CaseResult> CaseResults { get; init; } = [];

    /// <summary>
    /// Distribution of scores.
    /// </summary>
    public ScoreDistribution Distribution { get; init; } = new();

    /// <summary>
    /// Total evaluation time.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }
}

/// <summary>
/// Aggregated metrics from batch evaluation.
/// </summary>
public sealed class AggregatedMetrics
{
    /// <summary>
    /// Average context relevance.
    /// </summary>
    public float AvgContextRelevance { get; init; }

    /// <summary>
    /// Average context recall (if applicable).
    /// </summary>
    public float? AvgContextRecall { get; init; }

    /// <summary>
    /// Average faithfulness (if applicable).
    /// </summary>
    public float? AvgFaithfulness { get; init; }

    /// <summary>
    /// Average answer relevance (if applicable).
    /// </summary>
    public float? AvgAnswerRelevance { get; init; }

    /// <summary>
    /// Average answerability.
    /// </summary>
    public float AvgAnswerability { get; init; }

    /// <summary>
    /// Average overall score.
    /// </summary>
    public float AvgOverallScore { get; init; }

    /// <summary>
    /// Cases meeting target metrics.
    /// </summary>
    public int CasesMeetingTargets { get; init; }

    /// <summary>
    /// Percentage of cases meeting targets.
    /// </summary>
    public float PercentMeetingTargets { get; init; }
}

/// <summary>
/// Result for an individual case.
/// </summary>
public sealed class CaseResult
{
    /// <summary>
    /// Case ID.
    /// </summary>
    public string CaseId { get; init; } = string.Empty;

    /// <summary>
    /// Whether evaluation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Evaluation result (if successful).
    /// </summary>
    public RetrievalEvaluationResult? Result { get; init; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Distribution of scores across evaluations.
/// </summary>
public sealed class ScoreDistribution
{
    /// <summary>
    /// Minimum overall score.
    /// </summary>
    public float Min { get; init; }

    /// <summary>
    /// Maximum overall score.
    /// </summary>
    public float Max { get; init; }

    /// <summary>
    /// Median overall score.
    /// </summary>
    public float Median { get; init; }

    /// <summary>
    /// 25th percentile.
    /// </summary>
    public float P25 { get; init; }

    /// <summary>
    /// 75th percentile.
    /// </summary>
    public float P75 { get; init; }

    /// <summary>
    /// 95th percentile.
    /// </summary>
    public float P95 { get; init; }

    /// <summary>
    /// Standard deviation.
    /// </summary>
    public float StdDev { get; init; }
}

/// <summary>
/// Result of Needle-in-Haystack test.
/// </summary>
public sealed class NeedleInHaystackResult
{
    /// <summary>
    /// Number of test iterations.
    /// </summary>
    public int TotalIterations { get; init; }

    /// <summary>
    /// Number of successful retrievals (needle found).
    /// </summary>
    public int SuccessfulRetrievals { get; init; }

    /// <summary>
    /// Success rate (0.0 to 1.0).
    /// </summary>
    public float SuccessRate { get; init; }

    /// <summary>
    /// Average rank of the needle when found.
    /// </summary>
    public float AvgNeedleRank { get; init; }

    /// <summary>
    /// Average similarity score of the needle.
    /// </summary>
    public float AvgNeedleScore { get; init; }

    /// <summary>
    /// Context size (haystack size).
    /// </summary>
    public int ContextSize { get; init; }

    /// <summary>
    /// Individual iteration results.
    /// </summary>
    public IReadOnlyList<NeedleIterationResult> IterationResults { get; init; } = [];
}

/// <summary>
/// Result of a single Needle-in-Haystack iteration.
/// </summary>
public sealed class NeedleIterationResult
{
    /// <summary>
    /// Iteration number.
    /// </summary>
    public int Iteration { get; init; }

    /// <summary>
    /// Whether the needle was found.
    /// </summary>
    public bool NeedleFound { get; init; }

    /// <summary>
    /// Rank of the needle in results (1-based, 0 if not found).
    /// </summary>
    public int NeedleRank { get; init; }

    /// <summary>
    /// Similarity score of the needle.
    /// </summary>
    public float NeedleScore { get; init; }

    /// <summary>
    /// Query used for this iteration.
    /// </summary>
    public string Query { get; init; } = string.Empty;
}

/// <summary>
/// Target metrics for quality gates.
/// </summary>
public sealed class QualityTargets
{
    /// <summary>
    /// Target context relevance.
    /// </summary>
    public float ContextRelevance { get; set; } = 0.7f;

    /// <summary>
    /// Target context recall.
    /// </summary>
    public float ContextRecall { get; set; } = 0.8f;

    /// <summary>
    /// Target faithfulness.
    /// </summary>
    public float Faithfulness { get; set; } = 0.85f;

    /// <summary>
    /// Target answer relevance.
    /// </summary>
    public float AnswerRelevance { get; set; } = 0.8f;

    /// <summary>
    /// Target overall score.
    /// </summary>
    public float OverallScore { get; set; } = 0.75f;

    /// <summary>
    /// Target retrieval latency in milliseconds.
    /// </summary>
    public int RetrievalLatencyMs { get; set; } = 100;
}
