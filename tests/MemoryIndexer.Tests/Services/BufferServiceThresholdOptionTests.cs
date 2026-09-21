using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Tests.Services;

/// <summary>
/// The three promotion thresholds on <see cref="SensoryBufferOptions"/> decide when a buffer that a caller
/// feeds through <c>IBuffer.EnqueueAsync</c> becomes due. The sibling suite builds its service with the
/// declared defaults, so it would stay green if the service compared against constants instead of the
/// options. Each fact here feeds the same input to two services that differ in exactly one threshold and
/// expects two different answers; the default values are read from the options type, never restated.
/// </summary>
public class BufferServiceThresholdOptionTests
{
    private const string UserId = "user-1";

    private static BufferService Create(Action<SensoryBufferOptions>? configure = null)
    {
        var options = new MemoryIndexerOptions();
        configure?.Invoke(options.SensoryBuffer);
        return new BufferService(Options.Create(options), NullLogger<BufferService>.Instance);
    }

    [Fact]
    public async Task TurnThreshold_DecidesWhetherTheSameTurnsAreDue()
    {
        var ct = TestContext.Current.CancellationToken;
        var defaults = new SensoryBufferOptions();
        var turns = defaults.TurnThreshold;

        var atDefault = Create();
        var raised = Create(o => o.TurnThreshold = turns + 5);

        for (var i = 0; i < turns; i++)
        {
            await atDefault.EnqueueAsync("a", UserId, cancellationToken: ct);
            await raised.EnqueueAsync("a", UserId, cancellationToken: ct);
        }

        (await atDefault.CheckTriggerAsync(UserId, ct)).Should().Be(PromotionTriggerType.TurnThreshold);
        (await raised.CheckTriggerAsync(UserId, ct)).Should().BeNull();

        // The stats probe carries its own copy of the comparison; the two must give one answer.
        atDefault.GetStats(UserId).SatisfiedTrigger.Should().Be(PromotionTriggerType.TurnThreshold);
        raised.GetStats(UserId).SatisfiedTrigger.Should().BeNull();
    }

    [Fact]
    public async Task TokenThreshold_DecidesWhetherTheSameContentIsDue()
    {
        var ct = TestContext.Current.CancellationToken;

        // One enqueue, so the turn threshold cannot be what answers.
        var content = new string('x', 400);

        var atDefault = Create();
        var lowered = Create(o => o.TokenThreshold = 10);

        await atDefault.EnqueueAsync(content, UserId, cancellationToken: ct);
        await lowered.EnqueueAsync(content, UserId, cancellationToken: ct);

        atDefault.GetTokenCount(UserId).Should().BeLessThan(new SensoryBufferOptions().TokenThreshold,
            "the control must sit under the declared default for the comparison to mean anything");

        (await atDefault.CheckTriggerAsync(UserId, ct)).Should().BeNull();
        (await lowered.CheckTriggerAsync(UserId, ct)).Should().Be(PromotionTriggerType.TokenThreshold);

        atDefault.GetStats(UserId).SatisfiedTrigger.Should().BeNull();
        lowered.GetStats(UserId).SatisfiedTrigger.Should().Be(PromotionTriggerType.TokenThreshold);
    }

    [Fact]
    public async Task IdleTimeout_DecidesWhetherTheSameQuietBufferIsDue()
    {
        var ct = TestContext.Current.CancellationToken;

        var atDefault = Create();
        var shortened = Create(o => o.IdleTimeout = TimeSpan.FromMilliseconds(1));

        await atDefault.EnqueueAsync("a", UserId, cancellationToken: ct);
        await shortened.EnqueueAsync("a", UserId, cancellationToken: ct);

        // A lower bound only: the shortened service needs at least 1 ms of quiet, the default one a minute.
        await Task.Delay(50, ct);

        (await atDefault.CheckTriggerAsync(UserId, ct)).Should().BeNull();
        (await shortened.CheckTriggerAsync(UserId, ct)).Should().Be(PromotionTriggerType.IdleTimeout);

        atDefault.GetStats(UserId).SatisfiedTrigger.Should().BeNull();
        shortened.GetStats(UserId).SatisfiedTrigger.Should().Be(PromotionTriggerType.IdleTimeout);
    }
}
