using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence;

/// <summary>
/// <see cref="SearchOptions.MaxLimit"/> caps one hybrid search: a request asking for more gets at most MaxLimit results,
/// and a request within the cap is unaffected.
/// </summary>
public class HybridSearchMaxLimitTests
{
    private readonly InMemoryMemoryStore _store = new(NullLogger<InMemoryMemoryStore>.Instance);

    private HybridSearchService Search(int maxLimit)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new MemoryIndexerOptions());
        options.Value.Embedding.Dimensions = 8;
        options.Value.Search.MaxLimit = maxLimit;
        return new HybridSearchService(
            _store,
            new MockEmbeddingService(options, NullLogger<MockEmbeddingService>.Instance),
            options,
            NullLogger<HybridSearchService>.Instance);
    }

    private async Task Seed(int count, CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            await _store.StoreAsync(new MemoryUnit
            {
                UserId = "alice",
                Content = $"zebra note {i}",
                Embedding = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f]),
            }, ct);
        }
    }

    private static HybridSearchOptions Request(int limit) => new()
    {
        UserId = "alice",
        Limit = limit,
        MinScore = 2f, // dense results are excluded; the sparse half alone has more matches than either limit
    };

    [Fact]
    public async Task ARequestAboveMaxLimit_GetsAtMostMaxLimitResults()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed(20, ct);

        var results = await Search(maxLimit: 4).SearchAsync("zebra", Request(limit: 15), ct);

        Assert.Equal(4, results.Count);
    }

    [Fact]
    public async Task ARequestWithinMaxLimit_IsUnaffected()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed(20, ct);

        var results = await Search(maxLimit: 100).SearchAsync("zebra", Request(limit: 15), ct);

        Assert.Equal(15, results.Count);
    }
}
