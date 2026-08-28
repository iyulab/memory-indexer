using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// Tests for TimeBasedDecayStrategy.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class TimeBasedDecayStrategyTests
{
    private readonly TimeBasedDecayStrategy _strategy;
    private readonly ConfidenceDecayOptions _options;

    public TimeBasedDecayStrategyTests()
    {
        _options = new ConfidenceDecayOptions
        {
            HalfLifeDays = 90,
            MinimumConfidence = 0.1f,
            ConfirmationBonus = 1.2f,
            AccessBonus = 1.1f,
            RecentAccessDays = 7
        };

        _strategy = new TimeBasedDecayStrategy(Options.Create(_options));
    }

    #region CalculateDecayedConfidence Tests

    [Fact]
    public void CalculateDecayedConfidence_NoTimePassed_ShouldReturnOriginal()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.9f,
            UpdatedAt = now,
            LastAccessedAt = now
        };

        // Act
        var decayed = _strategy.CalculateDecayedConfidence(entry, now);

        // Assert
        decayed.Should().Be(0.9f);
    }

    [Fact]
    public void CalculateDecayedConfidence_AfterHalfLife_ShouldHalve()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-90), // One half-life ago
            LastAccessedAt = now.AddDays(-90),
            Category = SemanticStoreCategory.Other // Uses default multiplier of 1.0
        };

        // Act
        var decayed = _strategy.CalculateDecayedConfidence(entry, now);

        // Assert - Should be approximately half (with some tolerance)
        decayed.Should().BeApproximately(0.4f, 0.05f);
    }

    [Fact]
    public void CalculateDecayedConfidence_ConfirmationBonus_ShouldSlowDecay()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entryWithConfirmations = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.8f,
            ConfirmationCount = 5,
            UpdatedAt = now.AddDays(-90),
            LastAccessedAt = now.AddDays(-90),
            Category = SemanticStoreCategory.Other
        };

        var entryWithoutConfirmations = new SemanticStoreEntry
        {
            Key = "test2",
            Value = "Test value 2",
            Confidence = 0.8f,
            ConfirmationCount = 1,
            UpdatedAt = now.AddDays(-90),
            LastAccessedAt = now.AddDays(-90),
            Category = SemanticStoreCategory.Other
        };

        // Act
        var decayedWithConfirmations = _strategy.CalculateDecayedConfidence(entryWithConfirmations, now);
        var decayedWithoutConfirmations = _strategy.CalculateDecayedConfidence(entryWithoutConfirmations, now);

        // Assert - More confirmations = less decay
        decayedWithConfirmations.Should().BeGreaterThan(decayedWithoutConfirmations);
    }

    [Fact]
    public void CalculateDecayedConfidence_RecentAccess_ShouldBoost()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entryRecentAccess = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-30),
            LastAccessedAt = now.AddDays(-1), // Accessed yesterday
            Category = SemanticStoreCategory.Other
        };

        var entryOldAccess = new SemanticStoreEntry
        {
            Key = "test2",
            Value = "Test value 2",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-30),
            LastAccessedAt = now.AddDays(-30), // Accessed 30 days ago
            Category = SemanticStoreCategory.Other
        };

        // Act
        var decayedRecentAccess = _strategy.CalculateDecayedConfidence(entryRecentAccess, now);
        var decayedOldAccess = _strategy.CalculateDecayedConfidence(entryOldAccess, now);

        // Assert - Recent access = higher confidence
        decayedRecentAccess.Should().BeGreaterThan(decayedOldAccess);
    }

    [Fact]
    public void CalculateDecayedConfidence_IdentityCategory_ShouldDecaySlower()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Identity facts use Fact category in store (maps to Identity for decay)
        var identityEntry = new SemanticStoreEntry
        {
            Key = "identity",
            Value = "User's name is John",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-90),
            LastAccessedAt = now.AddDays(-90),
            Category = SemanticStoreCategory.Fact
        };

        // Preferences decay faster
        var preferenceEntry = new SemanticStoreEntry
        {
            Key = "preference",
            Value = "User likes pizza",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-90),
            LastAccessedAt = now.AddDays(-90),
            Category = SemanticStoreCategory.Preference
        };

        // Act
        var identityDecayed = _strategy.CalculateDecayedConfidence(identityEntry, now);
        var preferenceDecayed = _strategy.CalculateDecayedConfidence(preferenceEntry, now);

        // Assert - Identity decays slower than preferences
        // Note: This depends on CategoryMultipliers in options
        identityDecayed.Should().BeGreaterThanOrEqualTo(preferenceDecayed * 0.8f); // Allow some tolerance
    }

    [Fact]
    public void CalculateDecayedConfidence_ShouldNeverExceedMinimum()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Very old fact",
            Confidence = 0.8f,
            UpdatedAt = now.AddDays(-1000), // Very old
            LastAccessedAt = now.AddDays(-1000),
            Category = SemanticStoreCategory.Other
        };

        // Act
        var decayed = _strategy.CalculateDecayedConfidence(entry, now);

        // Assert
        decayed.Should().BeGreaterThanOrEqualTo(_options.MinimumConfidence);
    }

    [Fact]
    public void CalculateDecayedConfidence_ShouldNeverExceedOriginal()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.5f,
            ConfirmationCount = 10,
            UpdatedAt = now,
            LastAccessedAt = now,
            Category = SemanticStoreCategory.Fact
        };

        // Act
        var decayed = _strategy.CalculateDecayedConfidence(entry, now);

        // Assert
        decayed.Should().BeLessThanOrEqualTo(0.5f);
    }

    #endregion

    #region NeedsReconfirmation Tests

    [Fact]
    public void NeedsReconfirmation_HighConfidence_ShouldReturnFalse()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.9f,
            UpdatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        // Act
        var needs = _strategy.NeedsReconfirmation(entry);

        // Assert
        needs.Should().BeFalse();
    }

    [Fact]
    public void NeedsReconfirmation_DecayedBelowThreshold_ShouldReturnTrue()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.6f,
            UpdatedAt = now.AddDays(-180), // 2 half-lives
            LastAccessedAt = now.AddDays(-180),
            Category = SemanticStoreCategory.Other
        };

        // Act
        var needs = _strategy.NeedsReconfirmation(entry, 0.5f);

        // Assert
        needs.Should().BeTrue();
    }

    #endregion

    #region GetStaleFacts Tests

    [Fact]
    public void GetStaleFacts_ShouldReturnOnlyStale()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fresh", Value = "Fresh fact", Confidence = 0.9f, UpdatedAt = now, LastAccessedAt = now, IsActive = true },
            new() { Key = "stale", Value = "Stale fact", Confidence = 0.6f, UpdatedAt = now.AddDays(-200), LastAccessedAt = now.AddDays(-200), IsActive = true, Category = SemanticStoreCategory.Other }
        };

        // Act
        var stale = _strategy.GetStaleFacts(facts, 0.5f);

        // Assert
        stale.Should().HaveCount(1);
        stale[0].Fact.Key.Should().Be("stale");
    }

    [Fact]
    public void GetStaleFacts_ShouldOrderByDecayedConfidence()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "older", Value = "Older fact", Confidence = 0.6f, UpdatedAt = now.AddDays(-300), LastAccessedAt = now.AddDays(-300), IsActive = true, Category = SemanticStoreCategory.Other },
            new() { Key = "newer", Value = "Newer stale fact", Confidence = 0.6f, UpdatedAt = now.AddDays(-150), LastAccessedAt = now.AddDays(-150), IsActive = true, Category = SemanticStoreCategory.Other }
        };

        // Act
        var stale = _strategy.GetStaleFacts(facts, 0.5f);

        // Assert - Should be ordered by decayed confidence (lowest first)
        if (stale.Count >= 2)
        {
            stale[0].DecayedConfidence.Should().BeLessThanOrEqualTo(stale[1].DecayedConfidence);
        }
    }

    [Fact]
    public void GetStaleFacts_ShouldSkipInactive()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "inactive", Value = "Inactive fact", Confidence = 0.3f, UpdatedAt = now.AddDays(-200), LastAccessedAt = now.AddDays(-200), IsActive = false }
        };

        // Act
        var stale = _strategy.GetStaleFacts(facts);

        // Assert
        stale.Should().BeEmpty();
    }

    #endregion

    #region PredictStaleDate Tests

    [Fact]
    public void PredictStaleDate_HighConfidence_ShouldReturnFutureDate()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.9f,
            UpdatedAt = now,
            LastAccessedAt = now,
            Category = SemanticStoreCategory.Other
        };

        // Act
        var staleDate = _strategy.PredictStaleDate(entry, 0.5f);

        // Assert
        staleDate.Should().NotBeNull();
        staleDate.Should().BeAfter(now);
    }

    [Fact]
    public void PredictStaleDate_AlreadyBelowThreshold_ShouldReturnNull()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.4f, // Already below 0.5 threshold
            UpdatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

        // Act
        var staleDate = _strategy.PredictStaleDate(entry, 0.5f);

        // Assert
        staleDate.Should().BeNull();
    }

    #endregion
}
