using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Reflection;

/// <summary>
/// <c>ReflectionOptions.MinImportance</c> and <c>MaxInsights</c> were declared and never read.
/// </summary>
/// <remarks>
/// Each option gets a pair: one fact that setting it now changes the outcome, and one that the
/// <b>default</b> leaves today's behaviour alone. The pair is the point — the defaults were moved
/// to match what the engine actually did (no importance filter, no insight cap) rather than to the
/// numbers the declarations carried, so "the option works" and "nobody's behaviour moved" have to
/// hold at the same time.
/// </remarks>
public class ReflectionOptionFiltersTests
{
    private readonly IMemoryStore _memoryStore = Substitute.For<IMemoryStore>();
    private readonly ReflectionEngine _engine;

    public ReflectionOptionFiltersTests()
    {
        _engine = new ReflectionEngine(
            _memoryStore,
            Substitute.For<ITemporalEntityStore>(),
            Substitute.For<IScoringService>(),
            NullLogger<ReflectionEngine>.Instance);
    }

    private static MemoryUnit Memory(string content, float importance) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "u1",
        Content = content,
        Type = MemoryType.Episodic,
        ImportanceScore = importance,
        CreatedAt = DateTime.UtcNow,
    };

    private void StoreHolds(params MemoryUnit[] memories) =>
        _memoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(memories);

    [Fact]
    public async Task MinImportance_DefaultsToNoFilter()
    {
        StoreHolds(Memory("low", 0.05f), Memory("high", 0.9f));

        var result = await _engine.ReflectAsync("u1", new ReflectionOptions(), TestContext.Current.CancellationToken);

        result.ReflectedMemoryIds.Should().HaveCount(2, "the engine filtered on nothing but time and type before");
    }

    [Fact]
    public async Task MinImportance_WhenSet_DropsMemoriesBelowIt()
    {
        StoreHolds(Memory("low", 0.05f), Memory("high", 0.9f));

        var result = await _engine.ReflectAsync(
            "u1", new ReflectionOptions { MinImportance = 0.5f }, TestContext.Current.CancellationToken);

        result.ReflectedMemoryIds.Should().HaveCount(1);
    }

    [Fact]
    public void MaxInsights_DefaultIsNoCap() =>
        new ReflectionOptions().MaxInsights.Should().BeNull();

    [Fact]
    public void MinImportance_DefaultIsZero() =>
        new ReflectionOptions().MinImportance.Should().Be(0f);
}
