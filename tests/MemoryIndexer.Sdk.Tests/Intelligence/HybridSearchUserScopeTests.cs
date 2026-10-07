using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence;

/// <summary>
/// The sparse (BM25) half of hybrid search is per user, and its results pass the same filters as the dense half.
/// </summary>
public class HybridSearchUserScopeTests
{
    private readonly InMemoryMemoryStore _store = new(NullLogger<InMemoryMemoryStore>.Instance);
    private readonly HybridSearchService _search;

    public HybridSearchUserScopeTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new MemoryIndexerOptions());
        options.Value.Embedding.Dimensions = 8;
        _search = new HybridSearchService(
            _store,
            new MockEmbeddingService(options, NullLogger<MockEmbeddingService>.Instance),
            options,
            NullLogger<HybridSearchService>.Instance);
    }

    [Fact]
    public async Task OtherUsersDocuments_DoNotCrowdOutTheSparseResults()
    {
        var ct = TestContext.Current.CancellationToken;
        // Bob floods the store with the query term; Alice has one matching memory
        for (var i = 0; i < 50; i++)
        {
            await Store("bob", $"zebra zebra zebra note {i}", ct);
        }

        var alice = await Store("alice", "a zebra crossed the road", ct);

        var results = await _search.SearchAsync("zebra", SearchOptions("alice", limit: 5), ct);

        Assert.Contains(results, r => r.Memory.Id == alice.Id && r.SparseScore > 0);
        Assert.All(results, r => Assert.Equal("alice", r.Memory.UserId));
    }

    [Fact]
    public async Task SoftDeletedMemory_IsNotReturnedThroughTheSparseIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var gone = await Store("alice", "kumquat recipe", ct);
        await _search.RebuildIndexAsync("alice", ct);
        await _store.DeleteAsync("alice", gone.Id, hardDelete: false, ct);

        var results = await _search.SearchAsync("kumquat", SearchOptions("alice", limit: 5), ct);

        Assert.DoesNotContain(results, r => r.Memory.Id == gone.Id);
    }

    [Fact]
    public async Task SessionFilter_AppliesToSparseOnlyResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var other = await Store("alice", "quince jam", ct, sessionId: "s-other");
        await _search.RebuildIndexAsync("alice", ct);

        var options = SearchOptions("alice", limit: 5);
        options.SessionId = "s-1";
        var results = await _search.SearchAsync("quince", options, ct);

        Assert.DoesNotContain(results, r => r.Memory.Id == other.Id);
    }

    private static HybridSearchOptions SearchOptions(string userId, int limit) => new()
    {
        UserId = userId,
        Limit = limit,
        MinScore = 2f, // dense results are excluded so the sparse path is what is tested
    };

    private async Task<MemoryUnit> Store(string userId, string content, CancellationToken ct, string? sessionId = null)
        => await _store.StoreAsync(new MemoryUnit
        {
            UserId = userId,
            SessionId = sessionId,
            Content = content,
            Embedding = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f]),
        }, ct);
}
