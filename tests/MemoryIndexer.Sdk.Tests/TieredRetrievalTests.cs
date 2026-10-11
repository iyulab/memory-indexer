using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Tier-weighted retrieval resolves from <c>AddMemoryIndexer()</c> and reads each tier from the configured store: a
/// memory is reported under its own tier, and another user's memories never appear.
/// </summary>
public class TieredRetrievalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 64;
        });
        return services.BuildServiceProvider();
    }

    private static async Task<MemoryUnit> StoreAsync(IServiceProvider sp, string userId, string content, Tier tier)
    {
        var embeddings = sp.GetRequiredService<IEmbeddingService>();
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            Tier = tier,
            Embedding = await embeddings.GenerateEmbeddingAsync(content, Ct)
        };
        return await sp.GetRequiredService<IMemoryStore>().StoreAsync(memory, Ct);
    }

    private static Task<TieredRetrievalResult> RetrieveAsync(IServiceProvider sp, string userId) =>
        sp.GetRequiredService<ITieredRetrievalStrategy>().RetrieveAsync(
            new TieredRetrievalRequest { Query = "tea", UserId = userId, MinSimilarity = 0f },
            Ct);

    private static IEnumerable<Guid> IdsIn(TieredRetrievalResult result, Tier tier) =>
        result.TierResults.TryGetValue(tier, out var scored) ? scored.Select(s => s.Memory.Id) : [];

    [Fact]
    public async Task Each_memory_is_reported_under_its_own_tier()
    {
        using var sp = Build();
        var longTerm = await StoreAsync(sp, "alice", "Alice drinks green tea.", Tier.Long);
        var archived = await StoreAsync(sp, "alice", "Alice once drank black tea.", Tier.Archive);

        var result = await RetrieveAsync(sp, "alice");

        // Positive control: both memories are found at all
        Assert.Contains(longTerm.Id, IdsIn(result, Tier.Long));
        Assert.Contains(archived.Id, IdsIn(result, Tier.Archive));
        // The tier filter is honoured: neither shows up under the other tier
        Assert.DoesNotContain(archived.Id, IdsIn(result, Tier.Long));
        Assert.DoesNotContain(longTerm.Id, IdsIn(result, Tier.Archive));
    }

    [Fact]
    public async Task Another_users_memories_stay_out_including_working_memory()
    {
        using var sp = Build();
        var own = await StoreAsync(sp, "alice", "Alice drinks green tea.", Tier.Long);
        var stored = await StoreAsync(sp, "bob", "Bob drinks oolong tea.", Tier.Long);
        var working = await StoreAsync(sp, "bob", "Bob is making tea right now.", Tier.Short);
        await sp.GetRequiredService<IShortTermMemory>().PromoteAsync(working, Ct);

        var result = await RetrieveAsync(sp, "alice");
        var all = result.TierResults.Values.SelectMany(v => v).Select(s => s.Memory.Id).ToList();

        Assert.Contains(own.Id, all);
        Assert.DoesNotContain(stored.Id, all);
        Assert.DoesNotContain(working.Id, all);
    }
}
