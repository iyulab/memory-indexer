using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Retention;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Retention;

/// <summary>
/// Tests for DefaultRetentionPolicy.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class DefaultRetentionPolicyTests
{
    private readonly DefaultRetentionPolicy _policy;
    private readonly IConfidenceDecayStrategy _mockDecayStrategy;

    public DefaultRetentionPolicyTests()
    {
        _mockDecayStrategy = Substitute.For<IConfidenceDecayStrategy>();
        _policy = new DefaultRetentionPolicy(decayStrategy: _mockDecayStrategy);
    }

    #region GetRule Tests

    [Theory]
    [InlineData(SemanticStoreCategory.Fact, null)] // Never expires
    [InlineData(SemanticStoreCategory.Preference, 365)]
    [InlineData(SemanticStoreCategory.Skill, 730)]
    [InlineData(SemanticStoreCategory.Interest, 365)]
    [InlineData(SemanticStoreCategory.Relationship, null)] // Never expires
    [InlineData(SemanticStoreCategory.Work, 730)]
    [InlineData(SemanticStoreCategory.Goal, 180)]
    [InlineData(SemanticStoreCategory.Behavior, 365)]
    [InlineData(SemanticStoreCategory.Communication, null)] // Never expires
    [InlineData(SemanticStoreCategory.Other, 365)]
    public void GetRule_ShouldReturnCorrectMaxAgeDays(SemanticStoreCategory category, int? expectedMaxAgeDays)
    {
        // Act
        var rule = _policy.GetRule(category);

        // Assert
        rule.Category.Should().Be(category);
        rule.MaxAgeDays.Should().Be(expectedMaxAgeDays);
    }

    [Fact]
    public void GetAllRules_ShouldReturnAllCategories()
    {
        // Act
        var rules = _policy.GetAllRules();

        // Assert
        rules.Should().HaveCount(10); // All SemanticStoreCategory values
        rules.Keys.Should().Contain(SemanticStoreCategory.Fact);
        rules.Keys.Should().Contain(SemanticStoreCategory.Preference);
        rules.Keys.Should().Contain(SemanticStoreCategory.Goal);
    }

    [Fact]
    public void GetRule_FactCategory_ShouldHaveLowMinConfidence()
    {
        // Act
        var rule = _policy.GetRule(SemanticStoreCategory.Fact);

        // Assert
        rule.MinConfidence.Should().Be(0.05f);
        rule.ProtectedConfirmationCount.Should().Be(2);
    }

    [Fact]
    public void GetRule_GoalCategory_ShouldHaveShortRetention()
    {
        // Act
        var rule = _policy.GetRule(SemanticStoreCategory.Goal);

        // Assert
        rule.MaxAgeDays.Should().Be(180);
        rule.StaleAfterDays.Should().Be(90);
        rule.MinConfidence.Should().Be(0.3f);
    }

    #endregion

    #region Evaluate Tests

    [Fact]
    public void Evaluate_AlreadyArchived_ShouldKeep()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            IsActive = false, // Already archived
            CreatedAt = DateTime.UtcNow.AddYears(-2)
        };

        // Act
        var decision = _policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeTrue();
        decision.Reason.Should().Be(RetentionReason.AlreadyArchived);
        decision.Action.Should().Be(RetentionAction.Keep);
    }

    [Fact]
    public void Evaluate_ProtectedByConfirmations_ShouldKeep()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference, // ProtectedConfirmationCount = 3
            ConfirmationCount = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddYears(-2)
        };

        // Act
        var decision = _policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeTrue();
        decision.Reason.Should().Be(RetentionReason.ProtectedByConfirmations);
    }

    [Fact]
    public void Evaluate_AgeExceeded_ShouldArchive()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.8f,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-400) // Exceeds 365 days
        };

        // Act
        var decision = _policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeFalse();
        decision.Reason.Should().Be(RetentionReason.AgeExceeded);
        decision.Action.Should().Be(RetentionAction.Archive);
    }

    [Fact]
    public void Evaluate_Stale_ShouldArchive()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.8f,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-100),
            LastAccessedAt = DateTime.UtcNow.AddDays(-200) // Stale after 180 days
        };

        // Act
        var decision = _policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeFalse();
        decision.Reason.Should().Be(RetentionReason.Stale);
        decision.Action.Should().Be(RetentionAction.Archive);
    }

    [Fact]
    public void Evaluate_LowConfidence_ShouldArchive()
    {
        // Arrange - Use policy without decay strategy for this test
        var policyWithoutDecay = new DefaultRetentionPolicy();

        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.1f, // Below 0.2f threshold
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        // Act
        var decision = policyWithoutDecay.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeFalse();
        decision.Reason.Should().Be(RetentionReason.LowConfidence);
    }

    [Fact]
    public void Evaluate_WithinPolicy_ShouldKeep()
    {
        // Arrange - Use policy without decay strategy for this test
        var policyWithoutDecay = new DefaultRetentionPolicy();

        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.8f,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            LastAccessedAt = DateTime.UtcNow.AddDays(-5)
        };

        // Act
        var decision = policyWithoutDecay.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeTrue();
        decision.Reason.Should().Be(RetentionReason.WithinPolicy);
        decision.DaysUntilEligible.Should().BeGreaterThan(300);
    }

    [Fact]
    public void Evaluate_FactNeverExpires_ShouldKeep()
    {
        // Arrange - Use policy without decay strategy for this test
        var policyWithoutDecay = new DefaultRetentionPolicy();

        var entry = new SemanticStoreEntry
        {
            Key = "name",
            Value = "John Doe",
            Category = SemanticStoreCategory.Fact,
            ConfirmationCount = 1,
            Confidence = 0.9f,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddYears(-5) // Very old
        };

        // Act
        var decision = policyWithoutDecay.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeTrue();
        decision.Reason.Should().Be(RetentionReason.NeverExpires);
    }

    [Fact]
    public void Evaluate_FactLowConfidence_ShouldArchive()
    {
        // Arrange - Use policy without decay strategy for this test
        var policyWithoutDecay = new DefaultRetentionPolicy();

        var entry = new SemanticStoreEntry
        {
            Key = "name",
            Value = "John Doe",
            Category = SemanticStoreCategory.Fact,
            ConfirmationCount = 1,
            Confidence = 0.01f, // Below 0.05f threshold
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        };

        // Act
        var decision = policyWithoutDecay.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeFalse();
        decision.Reason.Should().Be(RetentionReason.LowConfidence);
    }

    #endregion

    #region Confidence Decay Integration Tests

    [Fact]
    public void Evaluate_WithDecayStrategy_ShouldUseDecayedConfidence()
    {
        // Arrange
        var options = Options.Create(new RetentionPolicyOptions { UseConfidenceDecay = true });

        _mockDecayStrategy.CalculateDecayedConfidence(Arg.Any<SemanticStoreEntry>(), Arg.Any<DateTime>())
            .Returns(0.1f); // Below threshold

        var policy = new DefaultRetentionPolicy(options, _mockDecayStrategy);

        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.8f, // Original is high
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };

        // Act
        var decision = policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeFalse();
        decision.Reason.Should().Be(RetentionReason.LowConfidence);
        decision.DecayedConfidence.Should().Be(0.1f);
    }

    [Fact]
    public void Evaluate_WithoutDecayStrategy_ShouldUseOriginalConfidence()
    {
        // Arrange
        var policy = new DefaultRetentionPolicy();

        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Category = SemanticStoreCategory.Preference,
            ConfirmationCount = 1,
            Confidence = 0.8f,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };

        // Act
        var decision = policy.Evaluate(entry);

        // Assert
        decision.ShouldRetain.Should().BeTrue();
        decision.DecayedConfidence.Should().Be(0.8f);
    }

    #endregion

    #region EvaluateAll Tests

    [Fact]
    public void EvaluateAll_ShouldEvaluateAllEntries()
    {
        // Arrange - Use policy without decay strategy to test basic evaluation
        var policyWithoutDecay = new DefaultRetentionPolicy();

        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "keep", Value = "Keep", Category = SemanticStoreCategory.Fact, Confidence = 0.9f, IsActive = true, CreatedAt = DateTime.UtcNow },
            new() { Key = "archive", Value = "Archive", Category = SemanticStoreCategory.Preference, Confidence = 0.1f, IsActive = true, CreatedAt = DateTime.UtcNow }
        };

        // Act
        var decisions = policyWithoutDecay.EvaluateAll(entries);

        // Assert
        decisions.Should().HaveCount(2);
        decisions.Should().Contain(d => d.Entry.Key == "keep" && d.ShouldRetain);
        decisions.Should().Contain(d => d.Entry.Key == "archive" && !d.ShouldRetain);
    }

    #endregion

    #region Custom Rules Tests

    [Fact]
    public void Constructor_WithCustomRules_ShouldOverrideDefaults()
    {
        // Arrange
        var customRule = new RetentionRule
        {
            Category = SemanticStoreCategory.Preference,
            MaxAgeDays = 30, // Much shorter than default
            MinConfidence = 0.5f,
            Action = RetentionAction.Delete
        };

        var options = Options.Create(new RetentionPolicyOptions
        {
            CategoryRules = new Dictionary<SemanticStoreCategory, RetentionRule>
            {
                [SemanticStoreCategory.Preference] = customRule
            }
        });

        var policy = new DefaultRetentionPolicy(options);

        // Act
        var rule = policy.GetRule(SemanticStoreCategory.Preference);

        // Assert
        rule.MaxAgeDays.Should().Be(30);
        rule.MinConfidence.Should().Be(0.5f);
        rule.Action.Should().Be(RetentionAction.Delete);
    }

    #endregion
}
