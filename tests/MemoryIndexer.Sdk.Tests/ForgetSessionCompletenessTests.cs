using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Forgetting a session removes every Session- and Topic-scoped memory of it — however many there are, locked or
/// soft-deleted — and nothing else. It used to collect them with a search (wildcard query, at most 10,000 results), so a
/// larger session, or a memory the search did not return, survived «forget».
/// </summary>
public class ForgetSessionCompletenessTests
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

    private static MemoryUnit Memory(string session, Scope scope, string content) =>
        new() { UserId = "u", SessionId = session, Scope = scope, Content = content, Embedding = new float[8] };

    [Fact]
    public async Task ForgetSession_RemovesEverySessionMemory_BeyondTenThousand_LockedAndSoftDeletedIncluded()
    {
        using var provider = Build();
        var store = provider.GetRequiredService<IMemoryStore>();
        var memory = provider.GetRequiredService<IMemoryService>();

        const int many = 10_050;
        for (var i = 0; i < many; i++)
            await store.StoreAsync(Memory("s1", i % 2 == 0 ? Scope.Session : Scope.Topic, $"turn {i}"), Ct);

        var locked = Memory("s1", Scope.Session, "locked turn");
        locked.IsLocked = true;
        await store.StoreAsync(locked, Ct);

        var softDeleted = Memory("s1", Scope.Session, "soft-deleted turn");
        await store.StoreAsync(softDeleted, Ct);
        await store.DeleteAsync("u", softDeleted.Id, hardDelete: false, Ct);

        var userScoped = Memory("s1", Scope.User, "a fact about the user");
        var otherSession = Memory("s2", Scope.Session, "another session");
        await store.StoreAsync(userScoped, Ct);
        await store.StoreAsync(otherSession, Ct);

        await memory.ForgetSessionAsync("u", "s1", Ct);

        var left = await store.GetAllAsync("u", new MemoryFilterOptions
        {
            SessionId = "s1",
            Scopes = [Scope.Session, Scope.Topic],
            IncludeDeleted = true
        }, Ct);
        Assert.Empty(left);

        var kept = await store.GetAllAsync("u", new MemoryFilterOptions { IncludeDeleted = true }, Ct);
        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, m => m.Id == userScoped.Id);
        Assert.Contains(kept, m => m.Id == otherSession.Id);
    }
}
