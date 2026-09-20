using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Services;

/// <summary>
/// Tests for the switches of <see cref="MemoryPromotionBackgroundService"/>.
/// </summary>
public class MemoryPromotionBackgroundServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ISensoryPromoter _sensoryPromoter = Substitute.For<ISensoryPromoter>();
    private readonly IShortTermMemoryOrchestrator _orchestrator = Substitute.For<IShortTermMemoryOrchestrator>();
    private readonly ILongTermPromoter _longTermPromoter = Substitute.For<ILongTermPromoter>();

    // Completes once the last phase of a promotion cycle has been reached, and holds the loop there.
    private readonly TaskCompletionSource _cycleCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MemoryPromotionBackgroundServiceTests()
    {
        _sensoryPromoter.CheckPendingPromotionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserPromotionCheck>>([]));
        _orchestrator.GetActiveUserIds().Returns([]);
        _longTermPromoter.GetUsersWithCandidatesAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _cycleCompleted.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, call.Arg<CancellationToken>());
                return (IReadOnlyList<string>)[];
            });
    }

    private MemoryPromotionBackgroundService CreateService(
        MemoryPromotionBackgroundOptions backgroundOptions,
        MemoryIndexerOptions? indexerOptions = null) =>
        new(
            _sensoryPromoter,
            _orchestrator,
            _longTermPromoter,
            Options.Create(backgroundOptions),
            Options.Create(indexerOptions ?? new MemoryIndexerOptions()),
            NullLogger<MemoryPromotionBackgroundService>.Instance);

    private async Task RunOneCycleAsync(MemoryPromotionBackgroundService service)
    {
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _cycleCompleted.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultOptions_RunsEveryPromotionPhase()
    {
        // Arrange
        using var service = CreateService(new MemoryPromotionBackgroundOptions { CheckIntervalSeconds = 0 });

        // Act
        await RunOneCycleAsync(service);

        // Assert
        await _sensoryPromoter.Received().CheckPendingPromotionsAsync(Arg.Any<CancellationToken>());
        _orchestrator.Received().GetActiveUserIds();
        await _longTermPromoter.Received().GetUsersWithCandidatesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EnabledFalse_ReturnsWithoutRunningAnyPhase()
    {
        // Arrange
        using var service = CreateService(
            new MemoryPromotionBackgroundOptions { CheckIntervalSeconds = 0, Enabled = false });

        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(service.ExecuteTask!, _cycleCompleted.Task)
            .WaitAsync(Timeout, TestContext.Current.CancellationToken);

        // Assert - the worker returned instead of reaching the end of a promotion cycle
        Assert.Same(service.ExecuteTask, first);
        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        await _sensoryPromoter.DidNotReceive().CheckPendingPromotionsAsync(Arg.Any<CancellationToken>());
        _orchestrator.DidNotReceive().GetActiveUserIds();
        await _longTermPromoter.DidNotReceive().GetUsersWithCandidatesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SensoryBufferBackgroundWorkerDisabled_SkipsOnlyTheBufferPhase()
    {
        // Arrange
        var indexerOptions = new MemoryIndexerOptions();
        indexerOptions.SensoryBuffer.EnableBackgroundWorker = false;
        using var service = CreateService(
            new MemoryPromotionBackgroundOptions { CheckIntervalSeconds = 0 }, indexerOptions);

        // Act
        await RunOneCycleAsync(service);

        // Assert
        await _sensoryPromoter.DidNotReceive().CheckPendingPromotionsAsync(Arg.Any<CancellationToken>());
        _orchestrator.Received().GetActiveUserIds();
        await _longTermPromoter.Received().GetUsersWithCandidatesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ResolvedFromTheContainer_HonoursTheConfiguredSensoryBufferSwitch()
    {
        // Arrange - the registration the SDK ships, with the collaborators replaced
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddMemoryIndexer(options =>
        {
            options.Embedding.Provider = EmbeddingProvider.Mock;
            options.SensoryBuffer.EnableBackgroundWorker = false;
        });
        services.AddSingleton(_sensoryPromoter);
        services.AddSingleton(_orchestrator);
        services.AddSingleton(_longTermPromoter);
        services.Configure<MemoryPromotionBackgroundOptions>(o => o.CheckIntervalSeconds = 0);

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().OfType<MemoryPromotionBackgroundService>().Single();

        // Act
        await RunOneCycleAsync(service);

        // Assert
        await _sensoryPromoter.DidNotReceive().CheckPendingPromotionsAsync(Arg.Any<CancellationToken>());
        _orchestrator.Received().GetActiveUserIds();
    }
}
