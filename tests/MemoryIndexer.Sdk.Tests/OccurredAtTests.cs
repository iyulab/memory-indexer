using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// <c>RememberAsync(…, occurredAt)</c>: a turn imported after the fact keeps the time it was said, and recency reads that
/// time — the later statement wins even when it was stored first.
/// </summary>
public class OccurredAtTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Vectors by the text's first word: both statements are equally similar to the question, and not to each other.</summary>
    private sealed class MappedEmbedder(Dictionary<string, float[]> vectors) : IEmbeddingService
    {
        public int Dimensions => 4;

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult<ReadOnlyMemory<float>>(vectors.TryGetValue(text.Split(' ')[0], out var v) ? v : [0f, 0f, 0f, 1f]);

        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
                list.Add(await GenerateEmbeddingAsync(text, cancellationToken));
            return list;
        }
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 4;
        });
        services.RemoveAll<IEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(new MappedEmbedder(new()
        {
            ["Car:"] = [1f, 0f, 0f, 0f],
            ["Spring:"] = [0.8f, 0.6f, 0f, 0f],
            ["Summer:"] = [0.8f, -0.6f, 0f, 0f],
        }));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AnImportedTurn_KeepsTheTimeItWasSaid()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();
        var said = DateTimeOffset.UtcNow.AddDays(-40);

        await memory.RememberAsync("u", "s", "Spring: I drive a blue car to work every day now.", "user", occurredAt: said, cancellationToken: Ct);

        var context = await memory.RecallAsync("u", "s", "Car: what colour is my car?", cancellationToken: Ct);
        var stored = Assert.Single(context.Ranked).Memory;
        Assert.Equal(said.UtcDateTime, stored.CreatedAt);
        Assert.True(stored.UpdatedAt > said.UtcDateTime.AddDays(39), "UpdatedAt records when it was stored");
    }

    [Fact]
    public async Task TheLaterStatement_RanksFirst_EvenWhenStoredFirst()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();

        // Imported out of order: the newer statement first
        await memory.RememberAsync("u", "s", "Summer: I drive a green car to work every day now.", "user",
            occurredAt: DateTimeOffset.UtcNow.AddDays(-1), cancellationToken: Ct);
        await memory.RememberAsync("u", "s", "Spring: I drive a blue car to work every day now.", "user",
            occurredAt: DateTimeOffset.UtcNow.AddDays(-10), cancellationToken: Ct);

        var context = await memory.RecallAsync("u", "s", "Car: what colour is my car?", cancellationToken: Ct);

        Assert.Equal(2, context.Ranked.Count);
        Assert.StartsWith("Summer:", context.Ranked[0].Memory.Content, StringComparison.Ordinal);
    }
}
