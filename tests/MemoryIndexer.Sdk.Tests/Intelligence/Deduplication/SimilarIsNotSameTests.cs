using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.InMemory;
using MemoryIndexer.Models;
using MemoryIndexer.Scoring;
using MemoryIndexer.Sdk.Intelligence.Deduplication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Deduplication;

/// <summary>
/// Cosine similarity is not sameness. By default only identical content (ignoring case and whitespace) is skipped; a
/// similar memory with different content is stored, related to the one it resembles. Merging and overwriting by
/// similarity happen only with <see cref="DeduplicationOptions.ActOnSimilarity"/>.
/// </summary>
public class SimilarIsNotSameTests
{
    // Two turns of one conversation: different facts, cosine 0.80 — 0.90 on the scoring service's [0, 1] similarity scale
    // ((cos + 1) / 2), which the deduplication thresholds are compared against.
    private const string Earlier = "I graduated with a degree in Business Administration, which helps in my new role.";
    private const string Later = "Do you have any tips on creating a routine for my new job?";

    private static readonly float[] LaterVector = [0.8f, 0.6f];   // cos(Earlier, Later) = 0.80
    private static readonly float[] EarlierVector = [1f, 0f];

    private sealed class FixedVectors : IEmbeddingService
    {
        public int Dimensions => 2;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(text.StartsWith("Do you", StringComparison.Ordinal) ? LaterVector : EarlierVector);

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
                list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return list;
        }
    }

    private static async Task<(DeduplicationService Service, MemoryUnit Earlier)> CreateAsync(bool actOnSimilarity)
    {
        var store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        var embedder = new FixedVectors();
        var earlier = new MemoryUnit
        {
            UserId = "user1",
            Content = Earlier,
            Embedding = await embedder.GenerateEmbeddingAsync(Earlier),
        };
        await store.StoreAsync(earlier, TestContext.Current.CancellationToken);

        var service = new DeduplicationService(
            store,
            embedder,
            new DefaultScoringService(Options.Create(new MemoryIndexerOptions())),
            NullLogger<DeduplicationService>.Instance,
            Options.Create(new MemoryIndexerOptions { Deduplication = new DeduplicationOptions { ActOnSimilarity = actOnSimilarity } }));
        return (service, earlier);
    }

    [Fact]
    public async Task Default_SimilarDifferentContent_IsStoredWithARelation()
    {
        var (service, earlier) = await CreateAsync(actOnSimilarity: false);

        var result = await service.CheckForDuplicateAsync(Later, "user1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateAction.AddWithRelation, result.RecommendedAction);
        Assert.Equal(earlier.Id, result.ExistingMemory?.Id);
    }

    [Theory]
    [InlineData(Earlier)]
    [InlineData("  i graduated with a degree in business administration,   which helps in my new role.  ")]
    public async Task Default_IdenticalContent_IsSkipped(string repeated)
    {
        var (service, earlier) = await CreateAsync(actOnSimilarity: false);

        var result = await service.CheckForDuplicateAsync(repeated, "user1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateAction.Skip, result.RecommendedAction);
        Assert.Equal(DuplicateType.Exact, result.DuplicateType);
        Assert.Equal(earlier.Id, result.ExistingMemory?.Id);
    }

    [Fact]
    public async Task ActOnSimilarity_SimilarContent_FollowsTheTiers()
    {
        var (service, _) = await CreateAsync(actOnSimilarity: true);

        var result = await service.CheckForDuplicateAsync(Later, "user1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(DuplicateAction.Merge, result.RecommendedAction);   // 0.90 >= HighSimilarityThreshold 0.85
    }
}
