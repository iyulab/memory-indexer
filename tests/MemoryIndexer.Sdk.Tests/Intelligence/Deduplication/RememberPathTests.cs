using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Deduplication;

/// <summary>
/// What remembering a turn costs and keeps, on the path a consumer calls (<see cref="IMemoryService.RememberAsync"/>): one
/// embedding per turn (the scope manager used to embed every turn a second time for a topic it never stored), and a turn
/// similar to an earlier one is stored linked to it (the relation the duplicate check recommends was dropped on this path).
/// </summary>
public class RememberPathTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Vectors by the text's first word, so similarity is chosen per fact.</summary>
    private sealed class MappedEmbedder(Dictionary<string, float[]> vectors) : IEmbeddingService
    {
        public int Calls;

        public List<string> Texts { get; } = [];

        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            Texts.Add(text);
            var key = text.Split(' ')[0];
            return Task.FromResult<ReadOnlyMemory<float>>(vectors.TryGetValue(key, out var v) ? v : [0f, 0f, 0f, 1f]);
        }

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
                list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return list;
        }
    }

    private static (ServiceProvider Provider, MappedEmbedder Embedder) Build(Dictionary<string, float[]> vectors)
    {
        var embedder = new MappedEmbedder(vectors);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 4;
        });
        services.RemoveAll<IEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(embedder);
        return (services.BuildServiceProvider(), embedder);
    }

    [Fact]
    public async Task RememberingATurn_EmbedsItOnce()
    {
        var (provider, embedder) = Build(new() { ["Alice"] = [1f, 0f, 0f, 0f], ["Bob"] = [0f, 1f, 0f, 0f] });
        using var _ = provider;
        var memory = provider.GetRequiredService<IMemoryService>();
        await memory.RememberAsync("u", "s", "Alice moved to Busan in May and started a new job there.", "user", cancellationToken: Ct);

        const string turn = "Bob prefers green tea over coffee in the mornings.";
        await memory.RememberAsync("u", "s", turn, "user", cancellationToken: Ct);

        // Other texts may be embedded on the way (a session summary when the topic moves); this turn's text, once
        Assert.Equal(1, embedder.Texts.Count(t => t == turn));
    }

    [Fact]
    public async Task ATurnSimilarToAnEarlierOne_IsStoredLinkedToIt()
    {
        // cos(a, b) = 0.8: similar, not the same - stored, with the relation the duplicate check recommends
        var (provider, _) = Build(new() { ["Alice"] = [1f, 0f, 0f, 0f], ["Alice's"] = [0.8f, 0.6f, 0f, 0f] });
        using var _ = provider;
        var primitives = provider.GetRequiredService<IMemoryPrimitives>();
        var first = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Alice moved to Busan in May." }, Ct);

        var second = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Alice's new office is near Haeundae." }, Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotNull(second.Metadata);
        Assert.Equal(first.Id.ToString(), second.Metadata!["RelatedMemoryId"]);
        Assert.Equal("similar", second.Metadata["RelationshipType"]);
    }
}
