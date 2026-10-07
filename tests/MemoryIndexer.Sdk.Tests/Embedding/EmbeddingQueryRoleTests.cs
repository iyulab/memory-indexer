using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Scoring;
using MemoryIndexer.Sdk.Embedding;
using MemoryIndexer.Sdk.Intelligence.Caching;
using MemoryIndexer.Sdk.Intelligence.Deduplication;
using MemoryIndexer.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Embedding;

/// <summary>
/// An asymmetric embedding model (E5 <c>query: </c>/<c>passage: </c>, BGE or Qwen3-Embedding query instructions)
/// embeds a search query differently from the text it is compared against. Recall has to ask for the query
/// embedding, and every caching layer has to keep the two apart — a query vector served from the document cache
/// silently undoes the convention.
/// </summary>
public sealed class EmbeddingQueryRoleTests
{
    private static readonly float[] Stored = [1f, 0f, 0f, 0f];
    private static readonly float[] Other = [0f, 1f, 0f, 0f];

    /// <summary>Documents and queries embed to different vectors, so a mixed-up role is visible.</summary>
    private sealed class AsymmetricEmbedder : IEmbeddingService
    {
        public List<string> DocumentCalls { get; } = [];
        public List<string> QueryCalls { get; } = [];

        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            DocumentCalls.Add(text);
            return Task.FromResult<ReadOnlyMemory<float>>(text.StartsWith("find", StringComparison.Ordinal) ? Other : Stored);
        }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(t => (ReadOnlyMemory<float>)Stored)]);

        public Task<ReadOnlyMemory<float>> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
        {
            QueryCalls.Add(query);
            return Task.FromResult<ReadOnlyMemory<float>>(Stored);
        }
    }

    /// <summary>Implements only the document method — a symmetric model.</summary>
    private sealed class SymmetricEmbedder : IEmbeddingService
    {
        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult<ReadOnlyMemory<float>>(Stored);

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(_ => (ReadOnlyMemory<float>)Stored)]);
    }

    /// <summary>A provider on the caching base class with its own query convention.</summary>
    private sealed class PrefixingProvider(IMemoryCache cache) : CachedEmbeddingServiceBase(cache, NullLogger.Instance, new EmbeddingOptions())
    {
        public int SingleCalls { get; private set; }

        public override int Dimensions => 4;

        protected override string CacheKeyPrefix => "test";

        protected override Task<ReadOnlyMemory<float>> GenerateSingleEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            SingleCalls++;
            return Task.FromResult<ReadOnlyMemory<float>>(Stored);
        }

        protected override Task<ReadOnlyMemory<float>> GenerateSingleQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
        {
            SingleCalls++;
            return Task.FromResult<ReadOnlyMemory<float>>(Other);
        }
    }

    [Fact]
    public async Task Recall_EmbedsTheQueryAsAQuery_AndFindsTheMemory()
    {
        var embedder = new AsymmetricEmbedder();
        var options = Options.Create(new MemoryIndexerOptions { Embedding = new EmbeddingOptions { Dimensions = 4 } });
        var store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        var scoring = new DefaultScoringService(options, null);
        var service = new MemoryService(
            store,
            embedder,
            scoring,
            new DeduplicationService(store, embedder, scoring, NullLogger<DeduplicationService>.Instance, options),
            new MemoryPressureMonitorService(),
            new InMemoryGrowthMonitor(options),
            null,
            options);
        var ct = TestContext.Current.CancellationToken;
        await service.StoreAsync("u1", "the meeting moved to Thursday", importance: 0.9f, cancellationToken: ct);

        var results = await service.RecallAsync("u1", "find the meeting day", cancellationToken: ct);

        Assert.Contains("find the meeting day", embedder.QueryCalls);
        Assert.DoesNotContain("find the meeting day", embedder.DocumentCalls);
        Assert.Contains(results, r => r.Memory.Content == "the meeting moved to Thursday");
    }

    [Fact]
    public async Task ASymmetricModel_NeedsNothing_QueryDefaultsToTheDocumentEmbedding()
    {
        IEmbeddingService embedder = new SymmetricEmbedder();

        var query = await embedder.GenerateQueryEmbeddingAsync("anything", TestContext.Current.CancellationToken);

        Assert.Equal(Stored, query.ToArray());
    }

    [Fact]
    public async Task TheCachingBase_CachesQueriesApartFromDocuments()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new PrefixingProvider(cache);
        var ct = TestContext.Current.CancellationToken;

        var document = await provider.GenerateEmbeddingAsync("same text", ct);
        var query = await provider.GenerateQueryEmbeddingAsync("same text", ct);
        var queryAgain = await provider.GenerateQueryEmbeddingAsync("same text", ct);

        Assert.Equal(Stored, document.ToArray());
        Assert.Equal(Other, query.ToArray());
        Assert.Equal(Other, queryAgain.ToArray());
        Assert.Equal(2, provider.SingleCalls);
    }

    [Fact]
    public async Task TheLatencyCache_ForwardsTheQueryRole_AndKeepsTheRolesApart()
    {
        var inner = new AsymmetricEmbedder();
        var options = Options.Create(new MemoryIndexerOptions
        {
            Latency = new LatencyOptions { EmbeddingCacheEnabled = true, EmbeddingCacheSize = 10, EmbeddingCacheTtlMinutes = 60 }
        });
        var cached = new CachedEmbeddingService(inner, null, NullLogger<CachedEmbeddingService>.Instance, options);
        var ct = TestContext.Current.CancellationToken;

        var document = await cached.GenerateEmbeddingAsync("find me", ct);
        var query = await cached.GenerateQueryEmbeddingAsync("find me", ct);
        await cached.GenerateQueryEmbeddingAsync("find me", ct);

        Assert.Equal(Other, document.ToArray());
        Assert.Equal(Stored, query.ToArray());
        Assert.Equal(["find me"], inner.QueryCalls);
    }

    [Fact]
    public async Task TheDecoratorCache_ForwardsTheQueryRole_AndKeepsTheRolesApart()
    {
        var inner = new AsymmetricEmbedder();
        using var cached = new CachingEmbeddingService(inner);
        var ct = TestContext.Current.CancellationToken;

        var document = await cached.GenerateEmbeddingAsync("find me", ct);
        var query = await cached.GenerateQueryEmbeddingAsync("find me", ct);
        await cached.GenerateQueryEmbeddingAsync("find me", ct);

        Assert.Equal(Other, document.ToArray());
        Assert.Equal(Stored, query.ToArray());
        Assert.Equal(["find me"], inner.QueryCalls);
    }
}
