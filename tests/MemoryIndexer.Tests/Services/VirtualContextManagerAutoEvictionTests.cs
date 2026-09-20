using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Tests.Services;

/// <summary>
/// <see cref="VCMOptions.EnableAutoEviction"/> decides whether a page-in that saturates working
/// memory evicts anything. Both directions are asserted, and the "off" direction reads the
/// declared default rather than restating it, so flipping <see cref="VCMOptions.EnableAutoEviction"/>
/// turns this fact red. That default is the load-bearing half - nothing read this option before
/// 0.18.0, so a default of <c>true</c> would make every consumer start losing paged-in memories on
/// a version bump.
/// </summary>
public class VirtualContextManagerAutoEvictionTests
{
    // EstimateTokens is Content.Length / 4, so each memory below is 40 tokens against a
    // 100-token capacity: three of them saturate to 120%, which is above AutoEvictionTrigger
    // (High). DefensiveEvictAsync then drains down to its Normal target of 75%.
    private const int TokenCapacity = 100;
    private const int MemoryContentLength = 160;
    private const int PagedInCount = 3;

    [Fact]
    public async Task Declared_Default_Leaves_Paged_In_Memories_In_Working_Memory()
    {
        // Deliberately does NOT set the option: the value under test IS the declared default, so
        // flipping VCMOptions.EnableAutoEviction to true turns this fact red.
        var harness = new Harness(configure: null);

        new VCMOptions().EnableAutoEviction.Should().BeFalse(
            "nothing read this option before 0.18.0 - a default of true would make every consumer "
            + "start losing paged-in memories on a version bump");

        await harness.InitializeAndPageInAsync();

        harness.WorkingMemory.Count.Should().Be(
            PagedInCount,
            "under the declared default a saturating page-in must not evict");
        harness.State.SaturationLevel.Should().Be(
            ContextSaturationLevel.Critical,
            "the fixture is only meaningful if the page-in actually saturated working memory");
    }

    [Fact]
    public async Task Enabled_Evicts_Down_To_The_Defensive_Target()
    {
        var harness = new Harness(configure: o => o.EnableAutoEviction = true);

        await harness.InitializeAndPageInAsync();

        harness.WorkingMemory.Count.Should().BeLessThan(
            PagedInCount,
            "EnableAutoEviction = true must evict once saturation reaches AutoEvictionTrigger");
        harness.State.SaturationPercentage.Should().BeLessThanOrEqualTo(
            75f,
            "DefensiveEvictAsync targets ContextSaturationLevel.Normal");
    }

    private sealed class Harness
    {
        private readonly VirtualContextManager _manager;
        private readonly List<MemoryUnit> _candidates;

        public Harness(Action<VCMOptions>? configure)
        {
            WorkingMemory = new ShortTermMemoryService(
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(new WorkingMemoryOptions { Capacity = 7 }));

            _candidates = Enumerable.Range(0, PagedInCount)
                .Select(i => TestHelpers.CreateTestMemory(
                    content: new string((char)('a' + i), MemoryContentLength)))
                .ToList();

            var memoryStore = Substitute.For<IMemoryStore>();
            memoryStore
                .SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
                .Returns(_candidates
                    .Select(m => new MemorySearchResult { Memory = m, Score = 0.9f })
                    .ToList());
            memoryStore
                .GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
                .Returns([]);

            var embeddingService = Substitute.For<IEmbeddingService>();
            embeddingService
                .GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ReadOnlyMemory<float>(new float[8]));

            // The tier manager is not under test here: it hands back whatever it was given, so the
            // only thing that decides whether a memory survives is the option.
            var tierManager = Substitute.For<ITierManager>();
            tierManager
                .PromoteAsync(Arg.Any<MemoryUnit>(), Arg.Any<Tier>(), Arg.Any<PromotionReason>(), Arg.Any<CancellationToken>())
                .Returns(call => new TierPromotionResult { Success = true, UpdatedMemory = call.Arg<MemoryUnit>() });
            tierManager
                .DemoteAsync(Arg.Any<MemoryUnit>(), Arg.Any<Tier>(), Arg.Any<PromotionReason>(), Arg.Any<CancellationToken>())
                .Returns(call => new TierPromotionResult { Success = true, UpdatedMemory = call.Arg<MemoryUnit>() });

            _manager = new VirtualContextManager(
                WorkingMemory,
                memoryStore,
                embeddingService,
                Substitute.For<IScoringService>(),
                Substitute.For<IScopeManager>(),
                tierManager,
                Options.Create(BuildOptions(configure)),
                NullLogger<VirtualContextManager>.Instance);
        }

        private static VCMOptions BuildOptions(Action<VCMOptions>? configure)
        {
            var options = new VCMOptions { MaxTokenCapacity = TokenCapacity };
            configure?.Invoke(options);
            return options;
        }

        public ShortTermMemoryService WorkingMemory { get; }

        public VirtualContextState State => _manager.State;

        public async Task InitializeAndPageInAsync()
        {
            await _manager.InitializeAsync("test-user", "test-session");
            await _manager.PageInAsync("anything", PagedInCount);
        }
    }
}
