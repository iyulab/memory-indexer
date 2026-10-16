using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Deduplication;

/// <summary>
/// Remembering a new memory embeds its content once. The duplicate check embeds it to compare it with recent memories;
/// storing it used to embed the same text again, which doubled the embedding cost of every ingested turn.
/// </summary>
public class EmbedOnceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CountingEmbedder : IEmbeddingService
    {
        public int Calls;

        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            // Distinct, deterministic vectors: texts differ in their first character here.
            var seed = text[0] % 4;
            var vector = new float[4];
            vector[seed] = 1f;
            return Task.FromResult<ReadOnlyMemory<float>>(vector);
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

    private static (ServiceProvider Provider, CountingEmbedder Embedder) Build(bool deduplication)
    {
        var embedder = new CountingEmbedder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 4;
            o.Deduplication.Enabled = deduplication;
        });
        services.RemoveAll<IEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(embedder);
        return (services.BuildServiceProvider(), embedder);
    }

    [Fact]
    public async Task ANewMemory_AfterOthers_IsEmbeddedOnce()
    {
        var (provider, embedder) = Build(deduplication: true);
        using var _ = provider;
        var primitives = provider.GetRequiredService<IMemoryPrimitives>();
        await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Alice moved to Busan in May." }, Ct);

        var before = embedder.Calls;
        var memory = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Bob prefers green tea." }, Ct);

        Assert.Equal(1, embedder.Calls - before);
        Assert.True(memory.Embedding.HasValue);
        Assert.Equal(1f, memory.Embedding!.Value.Span['B' % 4]);
    }

    // Control: without the duplicate check the store's own embedding is the only one, so the count above is not an
    // artefact of a path that never embeds.
    [Fact]
    public async Task WithoutDeduplication_ANewMemory_IsEmbeddedOnce()
    {
        var (provider, embedder) = Build(deduplication: false);
        using var _ = provider;
        var primitives = provider.GetRequiredService<IMemoryPrimitives>();
        await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Alice moved to Busan in May." }, Ct);

        var before = embedder.Calls;
        await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "Bob prefers green tea." }, Ct);

        Assert.Equal(1, embedder.Calls - before);
    }
}
