using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Mock;

/// <summary>
/// Mock reranker service for development and testing.
/// Returns candidates in original order without performing actual cross-encoder scoring.
/// </summary>
public sealed partial class MockRerankerService : IRerankerService
{
    private readonly ILogger<MockRerankerService> _logger;

    public MockRerankerService(ILogger<MockRerankerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RerankResult<TMetadata>>> RerankAsync<TMetadata>(
        string query,
        IReadOnlyList<RerankCandidate<TMetadata>> candidates,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        LogMockRerank(_logger, candidates.Count, topK);

        // Return candidates in original order, preserving original scores
        var results = candidates
            .Select((c, i) => new RerankResult<TMetadata>
            {
                Index = i,
                Score = c.OriginalScore,
                OriginalScore = c.OriginalScore,
                Content = c.Content,
                MemoryId = c.MemoryId,
                Metadata = c.Metadata
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<RerankResult<TMetadata>>>(results);
    }

    /// <inheritdoc />
    public Task<float> ScoreAsync(
        string query,
        string document,
        CancellationToken cancellationToken = default)
    {
        LogMockScore(_logger, query.Length, document.Length);

        // Return a fixed score (0.5 = neutral)
        return Task.FromResult(0.5f);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mock rerank for {Count} candidates, returning top {TopK}")]
    private static partial void LogMockRerank(ILogger logger, int count, int topK);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mock score for query length {QueryLen} and document length {DocLen}")]
    private static partial void LogMockScore(ILogger logger, int queryLen, int docLen);
}
