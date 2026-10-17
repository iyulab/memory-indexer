using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// <see cref="Models.MemoryContext.Ranked"/>: a recall hands back every recalled memory with its score, highest first, so a
/// consumer can apply its own threshold. Runs the registered classifier, primitives and store with the mock embedder; what is
/// under test is that the scores reach the caller, not ranking quality.
/// </summary>
public class RecallScoresTests
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
    public async Task Recall_carries_every_memory_with_its_score_highest_first()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();
        const string query = "User: my project codename is Teal Whale 1005";
        await memory.RememberAsync("user-1", "session-1", query, "user", @namespace: "chat", cancellationToken: Ct);
        await memory.RememberAsync("user-1", "session-1", "User: I prefer dark mode in every editor", "user", @namespace: "chat", cancellationToken: Ct);
        await memory.RememberAsync("user-1", "session-1", "User: my car plate is 12-3456", "user", @namespace: "chat", cancellationToken: Ct);

        var context = await memory.RecallAsync("user-1", "session-2", query, @namespace: "chat", cancellationToken: Ct);

        Assert.NotEmpty(context.Ranked);
        Assert.Equal(
            context.AllMemories().Select(m => m.Id).Order(),
            context.Ranked.Select(r => r.Memory.Id).Order());
        Assert.Equal(context.Ranked.Select(r => r.Score).OrderByDescending(s => s), context.Ranked.Select(r => r.Score));
        Assert.Equal(query, context.Ranked[0].Memory.Content);
        Assert.All(context.Ranked, r => Assert.True(r.Breakdown.VectorScore > 0, "each result carries its score parts"));
    }

    [Fact]
    public async Task An_empty_recall_has_no_ranked_results()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();

        var context = await memory.RecallAsync("user-1", "session-1", "anything at all", @namespace: "chat", cancellationToken: Ct);

        Assert.Empty(context.Ranked);
        Assert.Equal(0, context.TotalCount);
    }
}
