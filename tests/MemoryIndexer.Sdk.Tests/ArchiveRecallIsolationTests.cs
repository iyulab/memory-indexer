using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// The profile is kept in the memory store under <see cref="ArchiveUserKey"/>: a recall for the user never returns a
/// profile entry, and a store-wide job that visits users does not treat the profile key as a user.
/// </summary>
public class ArchiveRecallIsolationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 8;
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Recall_ReturnsTheUsersTurns_NotTheProfileEntries()
    {
        using var provider = Build();
        var archive = provider.GetRequiredService<IArchiveStore>();
        var memory = provider.GetRequiredService<IMemoryService>();
        var store = provider.GetRequiredService<IMemoryStore>();

        const string turn = "User: we talked about green tea this morning";

        // The profile entry carries the very vector the query embeds to: if recall could reach it, it would rank first
        var queryVector = await provider.GetRequiredService<IEmbeddingService>().GenerateQueryEmbeddingAsync(turn, Ct);
        await archive.SetAsync("u", new SemanticStoreEntry
        {
            Key = "preference:user:drink",
            Value = "User prefers green tea",
            Embedding = queryVector,
        }, Ct);
        await memory.RememberAsync("u", "s", turn, "user", cancellationToken: Ct);

        var recalled = (await memory.RecallAsync("u", "s", turn, cancellationToken: Ct)).AllMemories();

        var only = Assert.Single(recalled);
        Assert.Equal(turn, only.Content);
        Assert.Equal("u", only.UserId);

        // Positive control: the entry is in the store, under the profile key
        Assert.Contains(ArchiveUserKey.For("u"), await store.GetUserIdsAsync(Ct));
        Assert.Contains(await store.GetAllAsync(ArchiveUserKey.For("u"), cancellationToken: Ct),
            m => m.Content == "User prefers green tea");
    }

    [Fact]
    public async Task LongTermPromotion_DoesNotVisitTheProfileKey()
    {
        using var provider = Build();
        var store = provider.GetRequiredService<IMemoryStore>();
        var promoter = provider.GetRequiredService<ILongTermPromoter>();

        // The same shape under a real user and under a profile key: only the user is a candidate
        await store.StoreAsync(new MemoryUnit { UserId = "v", Content = "A long-tier memory", Tier = Tier.Long }, Ct);
        await store.StoreAsync(new MemoryUnit { UserId = ArchiveUserKey.For("v"), Content = "A long-tier memory", Tier = Tier.Long }, Ct);

        Assert.Contains(ArchiveUserKey.For("v"), await store.GetUserIdsAsync(Ct));
        var users = await promoter.GetUsersWithCandidatesAsync(Ct);

        Assert.Equal(["v"], users);
    }
}
