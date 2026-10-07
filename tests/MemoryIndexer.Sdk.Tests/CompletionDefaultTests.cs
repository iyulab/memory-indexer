using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Mock;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Without a configured completion service, the services written to work without one run their fallbacks — a summary
/// is never a placeholder — and LLM-only services report that no model is configured instead of parsing placeholder
/// text.
/// </summary>
public class CompletionDefaultTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Build(Action<IServiceCollection>? before = null, Action<MemoryIndexerOptions>? configure = null, Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        before?.Invoke(services);
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 64;
            configure?.Invoke(o);
        });
        after?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task<string> SummarizeTwoMemoriesAsync(ServiceProvider provider)
    {
        var primitives = provider.GetRequiredService<IMemoryPrimitives>();
        var a = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "The user prefers tea." }, Ct);
        var b = await primitives.EncodeAsync(new EncodeRequest { UserId = "u", Content = "The user lives in Busan." }, Ct);
        var summary = await primitives.SummarizeAsync(new SummarizeRequest { UserId = "u", MemoryIds = [a.Id, b.Id] }, Ct);
        return summary.Content;
    }

    [Fact]
    public async Task By_default_a_summary_keeps_the_memories_own_text()
    {
        using var provider = Build();

        var content = await SummarizeTwoMemoriesAsync(provider);

        Assert.Contains("The user prefers tea.", content);
        Assert.Contains("The user lives in Busan.", content);
        Assert.DoesNotContain("[Mock completion", content);
    }

    // Positive control: the same path with the mock opted in is what wrote placeholders before.
    [Fact]
    public async Task With_the_mock_opted_in_the_summary_is_its_placeholder()
    {
        using var provider = Build(configure: o => o.Completion.Provider = CompletionProvider.Mock);

        var content = await SummarizeTwoMemoriesAsync(provider);

        Assert.Contains("[Mock completion", content);
    }

    [Fact]
    public async Task By_default_a_direct_completion_call_fails_and_names_the_cause()
    {
        using var provider = Build();

        var completion = provider.GetRequiredService<ITextCompletionService>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => completion.CompleteAsync("x", cancellationToken: Ct));

        Assert.Contains("No text completion service is configured", error.Message);
        Assert.IsNotType<MockTextCompletionService>(completion);
    }

    [Fact]
    public async Task By_default_fact_extraction_returns_nothing_and_says_why()
    {
        using var provider = Build();

        var result = await provider.GetRequiredService<IFactExtractor>().ExtractAsync(
            new FactExtractionContext { UserId = "u", Content = "I live in Busan." }, Ct);

        Assert.Empty(result.Facts);
        Assert.Contains("No text completion service is configured", result.Reasoning);
    }

    [Fact]
    public void Services_that_need_completion_still_resolve_by_default()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<IFactExtractor>());
        Assert.NotNull(provider.GetRequiredService<IVirtualContextManager>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_application_registration_wins_before_or_after_AddMemoryIndexer(bool before)
    {
        var mine = new EchoCompletion();
        void Register(IServiceCollection s) => s.AddSingleton<ITextCompletionService>(mine);
        using var provider = before ? Build(before: Register) : Build(after: Register);

        var content = await SummarizeTwoMemoriesAsync(provider);

        Assert.Same(mine, provider.GetRequiredService<ITextCompletionService>());
        Assert.Equal("summary from my model", content);
    }

    [Fact]
    public void The_enum_keeps_its_numbers_and_None_is_the_default()
    {
        Assert.Equal(0, (int)CompletionProvider.Mock);
        Assert.Equal(1, (int)CompletionProvider.Ollama);
        Assert.Equal(2, (int)CompletionProvider.Custom);
        Assert.Equal(3, (int)CompletionProvider.None);
        Assert.Equal(CompletionProvider.None, new CompletionOptions().Provider);
    }

    private sealed class EchoCompletion : ITextCompletionService
    {
        public Task<string> CompleteAsync(string prompt, TextCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("summary from my model");
    }
}
