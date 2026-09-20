using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Conflict;

/// <summary>
/// Tests for <see cref="SemanticContradictionDetector"/> triple contradiction detection.
/// </summary>
public class SemanticContradictionDetectorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly SemanticContradictionDetector _detector = new(
        Substitute.For<IEmbeddingService>(),
        NullLogger<SemanticContradictionDetector>.Instance);

    // An open-ended new value, and an existing value whose validity ended a year ago.
    // Their periods overlap (the new triple has no ValidFrom), so they contradict unless the
    // comparison is restricted to a point in time at which the existing value no longer held.
    private static EntityTriple NewTriple() => new()
    {
        Subject = "user",
        Predicate = "lives_in",
        ObjectValue = "Busan",
        UserId = "user1",
        Confidence = 1.0f
    };

    private static EntityTriple ExpiredTriple() => new()
    {
        Subject = "user",
        Predicate = "lives_in",
        ObjectValue = "Seoul",
        UserId = "user1",
        Confidence = 1.0f,
        ValidFrom = Now.AddYears(-3),
        ValidTo = Now.AddYears(-1)
    };

    [Fact]
    public async Task DetectTripleContradictionAsync_NoAsOfDate_ComparesAgainstExpiredTriples()
    {
        // Act
        var result = await _detector.DetectTripleContradictionAsync(
            NewTriple(), [ExpiredTriple()], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.HasContradiction.Should().BeTrue();
        result.ConflictingItem!.ObjectValue.Should().Be("Seoul");
    }

    [Fact]
    public async Task DetectTripleContradictionAsync_AsOfDateAfterExpiry_SkipsTriplesNotValidThen()
    {
        // Arrange
        var options = new ContradictionDetectionOptions { AsOfDate = Now };

        // Act
        var result = await _detector.DetectTripleContradictionAsync(
            NewTriple(), [ExpiredTriple()], options, TestContext.Current.CancellationToken);

        // Assert
        result.HasContradiction.Should().BeFalse();
    }

    [Fact]
    public async Task DetectTripleContradictionAsync_AsOfDateInsideValidity_StillReportsContradiction()
    {
        // Arrange
        var options = new ContradictionDetectionOptions { AsOfDate = Now.AddYears(-2) };

        // Act
        var result = await _detector.DetectTripleContradictionAsync(
            NewTriple(), [ExpiredTriple()], options, TestContext.Current.CancellationToken);

        // Assert
        result.HasContradiction.Should().BeTrue();
    }
}
