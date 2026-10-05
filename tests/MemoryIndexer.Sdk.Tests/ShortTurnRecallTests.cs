using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// A short chat turn that carries information, stored without a type in one session, is recalled from a later session
/// of the same user and namespace, whatever the language; small talk is still not stored. Runs the registered
/// classifier, primitives and store, not doubles: the turn lands in working memory (<c>Tier.Short</c>), so this also
/// checks that working memory is reachable across sessions. The mock embedder is not semantic, so each recall queries
/// with the stored text itself: what is under test is that the turn was stored and is reachable, not ranking.
/// </summary>
public class ShortTurnRecallTests
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

    [Theory]
    [InlineData("User: 제 프로젝트 코드명은 '청록고래-1005'입니다. 앞으로 이걸 기억해 주세요.")]
    [InlineData("我的项目代号是青鲸一〇〇五，请记住。")]
    [InlineData("User: my project codename is Teal Whale 1005")]
    public async Task A_short_untyped_turn_from_one_session_is_recalled_in_the_next(string turn)
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();

        await memory.RememberAsync("user-1", "session-1", turn, "user", @namespace: "chat", cancellationToken: Ct);
        var context = await memory.RecallAsync("user-1", "session-2", turn, @namespace: "chat", cancellationToken: Ct);

        Assert.Contains(turn, context.AllMemories().Select(m => m.Content));
    }

    [Fact]
    public async Task Small_talk_is_not_stored()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();

        const string greeting = "User: thanks!";
        const string fact = "User: 내 차 번호는 12가 3456이야.";
        await memory.RememberAsync("user-1", "session-1", greeting, "user", @namespace: "chat", cancellationToken: Ct);
        await memory.RememberAsync("user-1", "session-1", fact, "user", @namespace: "chat", cancellationToken: Ct);

        var forGreeting = await memory.RecallAsync("user-1", "session-2", greeting, @namespace: "chat", cancellationToken: Ct);
        var forFact = await memory.RecallAsync("user-1", "session-2", fact, @namespace: "chat", cancellationToken: Ct);

        Assert.DoesNotContain(greeting, forGreeting.AllMemories().Select(m => m.Content));
        // Positive control: the same query shape finds what was stored, so the greeting's absence is not a blind recall.
        Assert.Contains(fact, forFact.AllMemories().Select(m => m.Content));
    }

    [Fact]
    public async Task The_session_less_overload_keeps_a_typed_turn_the_classifier_would_drop()
    {
        using var provider = Build();
        var memory = provider.GetRequiredService<IMemoryService>();

        await memory.RememberAsync("user-1", null, "ok", "user", type: Models.MemoryType.Semantic, cancellationToken: Ct);
        var context = await memory.RecallAsync("user-1", null, "ok", cancellationToken: Ct);

        Assert.Contains("ok", context.AllMemories().Select(m => m.Content));
    }
}
