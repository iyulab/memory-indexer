using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Evaluation;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Evaluation;

/// <summary>
/// The NIAH runner as docs/EVALUATION.md describes it: registered by <c>AddMemoryEvaluation</c>, it stores a synthetic
/// haystack with the needle inserted, recalls with the needle query and reports whether the needle came back. The embedder
/// here places the needle and its query together and every haystack segment elsewhere, so the run's own bookkeeping is what
/// is measured, not a model.
/// </summary>
public class NiahTestRunnerRunTests
{
    private const string Needle = "The secret code is ALPHA-BRAVO-CHARLIE.";

    private sealed class NeedleEmbedder : IEmbeddingService
    {
        private static readonly float[] NeedleSide = [1f, 0f, 0f, 0f];
        private static readonly float[] HaystackSide = [0f, 1f, 0f, 0f];

        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(
                text.Contains("secret code", StringComparison.OrdinalIgnoreCase) ? NeedleSide : HaystackSide);

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
                list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return list;
        }
    }

    [Fact]
    public async Task ASyntheticHaystackRun_StoresTheSegments_AndFindsTheNeedle()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 4;
            o.Storage.Type = StorageType.InMemory;
        });
        services.RemoveAll<IEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(new NeedleEmbedder());
        services.AddMemoryEvaluation();
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<NiahTestRunner>().RunTestAsync(new NiahTestConfig
        {
            Needle = Needle,
            NeedleQuery = "What is the secret code?",
            TargetHaystackTokens = 2_000,
            RandomSeed = 7,
        }, TestContext.Current.CancellationToken);

        Assert.True(result.HaystackTokens >= 2_000, $"haystack {result.HaystackTokens} tokens");
        Assert.True(result.MemoriesStored > 10, $"{result.MemoriesStored} segments");
        Assert.True(result.NeedleFound);
        Assert.Equal(1, result.NeedleRank);
    }
}
