using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Where a memory came from: <see cref="Models.MemoryUnit.SourceRef"/> (the caller's message or turn id, given when it is
/// remembered) and <see cref="Models.MemoryUnit.DerivedFrom"/> (the memories a summary or a merge was made from).
/// </summary>
public class ProvenanceTests
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

    [Fact]
    public async Task ARecalledMemory_CarriesTheTurnIdItWasRememberedWith()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();
        const string turn = "User: my project codename is Teal Whale 1005";

        await memory.RememberAsync("u", "s", turn, "user", sourceRef: "conversation-7/turn-42", cancellationToken: Ct);
        const string other = "User: I prefer dark mode in every editor";
        await memory.RememberAsync("u", "s", other, "user", cancellationToken: Ct);

        var recalled = (await memory.RecallAsync("u", "s", turn, cancellationToken: Ct)).Ranked.First(r => r.Memory.Content == turn).Memory;
        var withoutRef = (await memory.RecallAsync("u", "s", other, cancellationToken: Ct)).Ranked.First(r => r.Memory.Content == other).Memory;

        Assert.Equal("conversation-7/turn-42", recalled.SourceRef);
        Assert.Null(withoutRef.SourceRef);
        Assert.Empty(recalled.DerivedFrom);
    }

    [Fact]
    public async Task ASummaryAndAMerge_NameTheMemoriesTheyWereMadeFrom()
    {
        using var provider = Build();
        var primitives = provider.GetRequiredService<IMemoryPrimitives>();
        var a = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "The user prefers tea.", SourceRef = "t1" }, Ct);
        var b = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "The user lives in Busan.", SourceRef = "t2" }, Ct);

        var summary = await primitives.SummarizeAsync(new SummarizeRequest { UserId = "u", MemoryIds = [a.Id, b.Id] }, Ct);
        var merged = await primitives.MergeAsync(new MergeRequest { UserId = "u", MemoryIds = [a.Id, b.Id], DeleteSources = false }, Ct);

        Assert.Equal([a.Id, b.Id], summary.DerivedFrom);
        Assert.Equal([a.Id, b.Id], merged.DerivedFrom);
        Assert.Equal("t1", a.SourceRef);
    }
}
