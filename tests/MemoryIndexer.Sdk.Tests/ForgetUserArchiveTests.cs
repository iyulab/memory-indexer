using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Forgetting a user removes the user's profile (the archive of promoted facts) too — not only the memories in the store.
/// The profile used to survive <c>ForgetUserAsync</c> and stay readable through <see cref="IArchiveStore"/>.
/// </summary>
public class ForgetUserArchiveTests
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
    public async Task ForgetUser_RemovesTheUsersProfile_AndOnlyThatUsers()
    {
        using var provider = Build();
        var archive = provider.GetRequiredService<IArchiveStore>();
        var memory = provider.GetRequiredService<IMemoryService>();

        await archive.SetAsync("u", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is John" }, Ct);
        await archive.SetAsync("u", new SemanticStoreEntry { Key = "preference:user:drink", Value = "User prefers tea" }, Ct);
        await archive.SetAsync("other", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is Ann" }, Ct);

        await memory.ForgetUserAsync("u", Ct);

        Assert.Empty(await archive.GetAllAsync("u", Ct));
        Assert.False(await archive.HasProfileAsync("u", Ct));
        Assert.Single(await archive.GetAllAsync("other", Ct));
    }

    [Fact]
    public async Task RemoveAll_ReturnsHowManyEntriesWentIncludingSupersededVersions()
    {
        using var provider = Build();
        var archive = provider.GetRequiredService<IArchiveStore>();

        await archive.SetAsync("u", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is John" }, Ct);
        await archive.ArchiveAndUpdateAsync("u", "identity:user:name", "User's name is Mike", Ct);

        Assert.Equal(2, await archive.RemoveAllAsync("u", Ct));
        Assert.Equal(0, await archive.RemoveAllAsync("u", Ct));
    }
}
