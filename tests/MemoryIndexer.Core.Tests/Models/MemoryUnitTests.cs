using FluentAssertions;
using MemoryIndexer.Core.Models;
using Xunit;

namespace MemoryIndexer.Core.Tests.Models;

public class MemoryUnitTests
{
    [Fact]
    public void Constructor_ShouldInitializeDefaults()
    {
        // Arrange & Act
        var memory = new MemoryUnit();

        // Assert
        memory.Id.Should().NotBeEmpty();
        memory.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        memory.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        memory.ImportanceScore.Should().Be(0.5f);
        memory.AccessCount.Should().Be(0);
        memory.Type.Should().Be(MemoryType.Episodic);
        memory.IsDeleted.Should().BeFalse();
        memory.Topics.Should().BeEmpty();
        memory.Entities.Should().BeEmpty();
        memory.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void RecordAccess_ShouldUpdateAccessCountAndTimestamp()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "test-user",
            Content = "Test content"
        };
        var initialAccessCount = memory.AccessCount;

        // Act
        memory.RecordAccess();

        // Assert
        memory.AccessCount.Should().Be(initialAccessCount + 1);
        memory.LastAccessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RecordAccess_MultipleCalls_ShouldIncrementAccessCount()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "test-user",
            Content = "Test content"
        };

        // Act
        memory.RecordAccess();
        memory.RecordAccess();
        memory.RecordAccess();

        // Assert
        memory.AccessCount.Should().Be(3);
    }

    [Fact]
    public void MarkUpdated_ShouldUpdateTimestamp()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "test-user",
            Content = "Test content"
        };
        var initialUpdatedAt = memory.UpdatedAt;

        // Wait a tiny bit to ensure timestamp difference
        Thread.Sleep(10);

        // Act
        memory.MarkUpdated();

        // Assert
        memory.UpdatedAt.Should().BeAfter(initialUpdatedAt);
        memory.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }
}
