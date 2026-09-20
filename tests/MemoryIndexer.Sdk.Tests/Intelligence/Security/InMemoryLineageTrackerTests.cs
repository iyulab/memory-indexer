using AwesomeAssertions;
using MemoryIndexer.Sdk.Intelligence.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Security;

/// <summary>
/// Tests for <see cref="InMemoryLineageTracker"/> lineage queries.
/// </summary>
public class InMemoryLineageTrackerTests
{
    private readonly InMemoryLineageTracker _tracker = new(NullLogger<InMemoryLineageTracker>.Instance);

    private async Task<(Guid Merged, Guid Source)> RecordMergeOfOneSourceAsync()
    {
        var source = Guid.NewGuid();
        var merged = Guid.NewGuid();

        await _tracker.RecordCreationAsync(
            source, "user1", MemorySource.UserInput, cancellationToken: TestContext.Current.CancellationToken);
        await _tracker.RecordMergeAsync(merged, [source], "user1", TestContext.Current.CancellationToken);

        return (merged, source);
    }

    [Fact]
    public async Task GetLineageAsync_DefaultOptions_ReturnsOnlyTheMemorysOwnEvents()
    {
        // Arrange
        var (merged, _) = await RecordMergeOfOneSourceAsync();

        // Act
        var lineage = await _tracker.GetLineageAsync(merged, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        lineage.Should().ContainSingle().Which.EventType.Should().Be(LineageEventType.Merged);
    }

    [Fact]
    public async Task GetLineageAsync_IncludeRelated_AddsEventsOfRelatedMemories()
    {
        // Arrange
        var (merged, source) = await RecordMergeOfOneSourceAsync();
        var options = new LineageQueryOptions { IncludeRelated = true };

        // Act
        var lineage = await _tracker.GetLineageAsync(merged, options, TestContext.Current.CancellationToken);

        // Assert
        lineage.Should().HaveCount(2);
        lineage.Should().Contain(e => e.MemoryId == source && e.EventType == LineageEventType.Created);
        lineage.Should().Contain(e => e.MemoryId == merged && e.EventType == LineageEventType.Merged);
    }

    [Fact]
    public async Task GetLineageAsync_IncludeRelated_StillAppliesFiltersAndLimit()
    {
        // Arrange
        var (merged, _) = await RecordMergeOfOneSourceAsync();
        var options = new LineageQueryOptions
        {
            IncludeRelated = true,
            EventTypes = [LineageEventType.Created]
        };

        // Act
        var lineage = await _tracker.GetLineageAsync(merged, options, TestContext.Current.CancellationToken);

        // Assert
        lineage.Should().ContainSingle().Which.EventType.Should().Be(LineageEventType.Created);
    }
}
