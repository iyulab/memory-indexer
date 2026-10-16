using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Search;

/// <summary>
/// How <see cref="IMemoryPrimitives.RetrieveAsync"/> orders what it found: an old memory that matches is not pushed out by
/// recency (it decayed over a day, so a fresh unrelated memory ranked level with it), and a candidate the vector ranked
/// below twice the limit can still win on its combined score (the reranking stage cut there, by vector rank alone).
/// </summary>
public class RetrieveRankingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The query embeds to (1,0,0,0); each memory's vector is given, so its cosine is chosen.</summary>
    private sealed class FixedEmbedder(Dictionary<string, float[]> vectors) : IEmbeddingService
    {
        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(vectors.TryGetValue(text, out var v) ? v : [1f, 0f, 0f, 0f]);

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
                list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return list;
        }
    }

    // A unit vector at the given cosine to (1,0,0,0)
    private static float[] At(float cosine) => [cosine, MathF.Sqrt(1 - (cosine * cosine)), 0f, 0f];

    private static (ServiceProvider Provider, IMemoryStore Store, IMemoryPrimitives Primitives) Build(Dictionary<string, float[]> vectors)
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
        services.AddSingleton<IEmbeddingService>(new FixedEmbedder(vectors));
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<IMemoryStore>(), provider.GetRequiredService<IMemoryPrimitives>());
    }

    private static MemoryUnit Memory(string content, float[] vector, DateTime createdAt) => new()
    {
        UserId = "u",
        Content = content,
        Embedding = vector,
        Type = MemoryType.Semantic,
        CreatedAt = createdAt,
        ImportanceScore = 0.5f,
    };

    [Fact]
    public async Task AnOldMemoryThatMatches_OutranksAFreshOneThatDoesNot()
    {
        const string old = "My sister lives in Daegu and works at the university hospital.";
        const string fresh = "The weather was cloudy this afternoon.";
        var (provider, store, primitives) = Build(new() { [old] = At(0.9f), [fresh] = At(0.45f) });
        using var _ = provider;
        await store.StoreAsync(Memory(old, At(0.9f), DateTime.UtcNow.AddDays(-40)), Ct);
        await store.StoreAsync(Memory(fresh, At(0.45f), DateTime.UtcNow), Ct);

        var results = await primitives.RetrieveAsync(new RetrieveRequest { UserId = "u", Query = "Where does she live?", Limit = 1, RecordAccess = false }, Ct);

        Assert.Equal(old, Assert.Single(results).Memory.Content);
    }

    [Fact]
    public async Task ACandidateTheVectorRankedThird_CanWinOnItsCombinedScore_WithALimitOfOne()
    {
        // Limit 1 searches 4 candidates; the cut kept 2 by vector rank, so the third (every query word in it) never scored
        var (provider, store, primitives) = Build([]);
        using var _ = provider;
        var now = DateTime.UtcNow;
        await store.StoreAsync(Memory("Notes about lunch plans with colleagues.", At(0.80f), now), Ct);
        await store.StoreAsync(Memory("Reminder to water the office plants.", At(0.79f), now), Ct);
        await store.StoreAsync(Memory("The parking permit renewal deadline is Friday.", At(0.78f), now), Ct);
        await store.StoreAsync(Memory("A list of birthdays in the team.", At(0.77f), now), Ct);

        var results = await primitives.RetrieveAsync(
            new RetrieveRequest { UserId = "u", Query = "parking permit renewal deadline", Limit = 1, RecordAccess = false }, Ct);

        Assert.Equal("The parking permit renewal deadline is Friday.", Assert.Single(results).Memory.Content);
    }
}
