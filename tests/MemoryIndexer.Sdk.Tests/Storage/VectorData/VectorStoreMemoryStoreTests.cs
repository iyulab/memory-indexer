using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Tests;
using MemoryIndexer.VectorData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.VectorData;

/// <summary>
/// <see cref="VectorStoreMemoryStore"/> passes the store contract over a connector-shaped store that refuses any filter a
/// real connector could not translate, with a page size small enough that every walk crosses pages.
/// </summary>
public sealed class VectorStoreMemoryStoreTests : MemoryStoreContractTests, IDisposable
{
    private const int Dimensions = 768;
    private readonly TranslatingInMemoryVectorStore _vectorStore = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _vectorStore.Dispose();

    protected override IMemoryStore CreateStore() => CreateStore(pageSize: 2);

    private VectorStoreMemoryStore CreateStore(int pageSize) => new(
        _vectorStore,
        new VectorStoreMemoryStoreOptions { Dimensions = Dimensions, PageSize = pageSize },
        NullLogger<VectorStoreMemoryStore>.Instance);

    [Fact]
    public async Task EveryCall_SendsOnlyTranslatableFilters()
    {
        var store = CreateStore(pageSize: 2);
        for (var i = 0; i < 5; i++)
            await store.StoreAsync(TestHelpers.CreateTestMemory("alice", $"m{i}", embedding: TestHelpers.CreateTestEmbedding(Dimensions, i)), Ct);

        await store.GetAllAsync("alice", new MemoryFilterOptions
        {
            SessionId = "s", Namespace = "n", Types = [MemoryType.Episodic, MemoryType.Semantic], Roles = ["user"],
            Tiers = [Tier.Short], Scopes = [Scope.Session], CreatedAfter = DateTime.UtcNow.AddDays(-1),
            CreatedBefore = DateTime.UtcNow.AddDays(1), MetadataFilter = new() { ["k"] = "v" }, OrderBy = MemoryOrderBy.ImportanceDesc
        }, Ct);
        await store.SearchAsync(TestHelpers.CreateTestEmbedding(Dimensions, 1), new MemorySearchOptions
        {
            UserId = "alice", Types = [MemoryType.Episodic], Roles = ["user"], MetadataFilter = new() { ["k"] = "v" }
        }, Ct);
        await store.GetCountAsync("alice", Ct);
        await store.GetTypeCountsAsync("alice", Ct);
        await store.GetUserIdsAsync(Ct);
        await store.DeleteBySessionAsync("alice", "s", cancellationToken: Ct);
        await store.DeleteByNamespaceAsync("alice", "n", cancellationToken: Ct);

        _vectorStore.ExpressionsChecked.Should().BeGreaterThan(10, "every call above went through the guard");
    }

    [Fact]
    public async Task TheGuard_RefusesWhatAConnectorCannotTranslate()
    {
        var collection = _vectorStore.GetCollection<Guid, MemoryRecord>("probe", MemoryRecord.CreateDefinition(Dimensions, DistanceFunction.CosineSimilarity));
        var userId = "alice";

        var negation = async () => await collection.GetAsync(r => !r.IsDeleted, 1, cancellationToken: Ct).ToListAsync(Ct);
        var unindexed = async () => await collection.GetAsync(r => r.Content == "x", 1, cancellationToken: Ct).ToListAsync(Ct);
        var method = async () => await collection.GetAsync(r => r.UserId.StartsWith('a'), 1, cancellationToken: Ct).ToListAsync(Ct);
        var captured = async () => await collection.GetAsync(r => r.UserId == userId, 1, cancellationToken: Ct).ToListAsync(Ct);

        await negation.Should().ThrowAsync<NotSupportedException>();
        await unindexed.Should().ThrowAsync<NotSupportedException>();
        await method.Should().ThrowAsync<NotSupportedException>();
        await captured.Should().ThrowAsync<NotSupportedException>("the store sends constants, not captured variables");
    }

    [Fact]
    public async Task MetadataTiersAndScopes_AreFilteredByTheDatabase()
    {
        var store = CreateStore(pageSize: 2);
        var match = TestHelpers.CreateTestMemory("alice", "match", embedding: TestHelpers.CreateTestEmbedding(Dimensions, 1));
        match.Metadata = new() { ["project"] = "apollo", ["lang"] = "ko" };
        match.Tier = Tier.Long;
        match.Scope = Scope.User;
        var otherMetadata = TestHelpers.CreateTestMemory("alice", "other", embedding: TestHelpers.CreateTestEmbedding(Dimensions, 2));
        otherMetadata.Metadata = new() { ["project"] = "gemini" };
        otherMetadata.Tier = Tier.Long;
        otherMetadata.Scope = Scope.User;
        await store.StoreBatchAsync([match, otherMetadata], Ct);

        var found = await store.GetAllAsync("alice", new MemoryFilterOptions
        {
            MetadataFilter = new() { ["project"] = "apollo" }, Tiers = [Tier.Long], Scopes = [Scope.User]
        }, Ct);

        found.Should().ContainSingle().Which.Content.Should().Be("match");
        found[0].Metadata.Should().BeEquivalentTo(match.Metadata, "the dictionary round-trips");
    }

    [Fact]
    public async Task TheDimension_ComesFromTheOptions()
    {
        var store = new VectorStoreMemoryStore(
            _vectorStore, new VectorStoreMemoryStoreOptions { CollectionName = "small", Dimensions = 4 },
            NullLogger<VectorStoreMemoryStore>.Instance);

        await store.StoreAsync(TestHelpers.CreateTestMemory("alice", "four", embedding: TestHelpers.CreateTestEmbedding(4, 1)), Ct);
        var hits = await store.SearchAsync(TestHelpers.CreateTestEmbedding(4, 1), new MemorySearchOptions { UserId = "alice" }, Ct);

        hits.Should().ContainSingle().Which.Score.Should().BeApproximately(1f, 1e-4f);
        var wrong = () => store.StoreAsync(TestHelpers.CreateTestMemory("alice", "768", embedding: TestHelpers.CreateTestEmbedding(768, 1)), Ct);
        await wrong.Should().ThrowAsync<VectorStoreException>();
    }

    [Fact]
    public void WithoutADimension_TheStoreRefusesToStart()
    {
        var act = () => new VectorStoreMemoryStore(_vectorStore, new VectorStoreMemoryStoreOptions(), NullLogger<VectorStoreMemoryStore>.Instance);

        act.Should().Throw<ArgumentException>().WithMessage("*Dimensions*");
    }

    [Fact]
    public async Task AWalkAcrossPages_SeesEveryRecordOnce()
    {
        var store = CreateStore(pageSize: 2);
        for (var i = 0; i < 7; i++)
            await store.StoreAsync(TestHelpers.CreateTestMemory("alice", $"m{i}"), Ct);

        (await store.GetCountAsync("alice", Ct)).Should().Be(7);
        (await store.GetAllAsync("alice", cancellationToken: Ct)).Select(m => m.Content).Should().OnlyHaveUniqueItems().And.HaveCount(7);
        (await store.DeleteByUserAsync("alice", hardDelete: true, Ct)).Should().Be(7);
        (await store.GetCountAsync("alice", Ct)).Should().Be(0);
    }
}
