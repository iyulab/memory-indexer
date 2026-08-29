using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Unit tests for FactConflictTools MCP endpoints.
/// Phase v0.9.2: Fact Conflict Resolution.
/// </summary>
public class FactConflictToolsTests
{
    private readonly IFactValidator _mockValidator;
    private readonly IArchiveStore _mockArchiveStore;
    private readonly FactConflictTools _tools;

    public FactConflictToolsTests()
    {
        _mockValidator = Substitute.For<IFactValidator>();
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _tools = new FactConflictTools(_mockValidator, _mockArchiveStore, Options.Create(new MemoryIndexerOptions()));
    }

    #region ValidateFact Tests

    [Fact]
    public async Task ValidateFact_WithNoConflicts_ShouldReturnValid()
    {
        // Arrange
        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Valid());

        // Act
        var result = await _tools.ValidateFact("User's name is John", "Identity", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsValid.Should().BeTrue();
        result.RecommendedAction.Should().Be("Add");
        result.ConflictCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidateFact_WithDuplicate_ShouldReturnSkip()
    {
        // Arrange
        var existingFact = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John"
        };

        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry> { existingFact });

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Duplicate(existingFact));

        // Act
        var result = await _tools.ValidateFact("User's name is John", "Identity", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.RecommendedAction.Should().Be("Skip");
        result.ConflictCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateFact_WithCustomUserId_ShouldUseProvidedId()
    {
        // Arrange
        _mockArchiveStore.GetAllAsync("custom-user", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Valid());

        // Act
        await _tools.ValidateFact("Test fact", "General", userId: "custom-user", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await _mockArchiveStore.Received(1).GetAllAsync("custom-user", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateFact_WithInvalidCategory_ShouldUseGeneral()
    {
        // Arrange
        _mockArchiveStore.GetAllAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockValidator.ValidateAsync(
            Arg.Is<UserFact>(f => f.Category == FactCategory.General),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Valid());

        // Act
        await _tools.ValidateFact("Test", "InvalidCategory", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await _mockValidator.Received(1).ValidateAsync(
            Arg.Is<UserFact>(f => f.Category == FactCategory.General),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetFactHistory Tests

    [Fact]
    public async Task GetFactHistory_WithVersions_ShouldReturnAll()
    {
        // Arrange
        var history = new List<SemanticStoreEntry>
        {
            new() { Key = "key", Value = "v2", Version = 2, IsActive = true },
            new() { Key = "key:v1", Value = "v1", Version = 1, IsActive = false }
        };

        _mockArchiveStore.GetHistoryAsync("user1", "key", Arg.Any<CancellationToken>())
            .Returns(history);

        // Act
        var result = await _tools.GetFactHistory("key", userId: "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.VersionCount.Should().Be(2);
        result.Versions.Should().HaveCount(2);
        result.Versions![0].Version.Should().Be(2);
        result.Versions[1].Version.Should().Be(1);
    }

    [Fact]
    public async Task GetFactHistory_NonExistentKey_ShouldReturnEmpty()
    {
        // Arrange
        _mockArchiveStore.GetHistoryAsync("user1", "nonexistent", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        // Act
        var result = await _tools.GetFactHistory("nonexistent", userId: "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.VersionCount.Should().Be(0);
        result.Versions.Should().BeEmpty();
    }

    #endregion

    #region GetFactsValidAt Tests

    [Fact]
    public async Task GetFactsValidAt_WithValidDate_ShouldReturnFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "key1", Value = "value1", Category = SemanticStoreCategory.Fact }
        };

        _mockArchiveStore.GetValidAtAsync("user1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _tools.GetFactsValidAt("2024-01-15", userId: "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.FactCount.Should().Be(1);
        result.Facts.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFactsValidAt_WithInvalidDate_ShouldReturnError()
    {
        // Act
        var result = await _tools.GetFactsValidAt("invalid-date", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid date format");
    }

    #endregion

    #region ArchiveAndUpdateFact Tests

    [Fact]
    public async Task ArchiveAndUpdateFact_ExistingFact_ShouldUpdate()
    {
        // Arrange
        var updatedEntry = new SemanticStoreEntry
        {
            Key = "key",
            Value = "new value",
            Version = 2,
            SupersedesKey = "key:v1"
        };

        _mockArchiveStore.ArchiveAndUpdateAsync("user1", "key", "new value", Arg.Any<CancellationToken>())
            .Returns(updatedEntry);

        // Act
        var result = await _tools.ArchiveAndUpdateFact("key", "new value", userId: "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.NewValue.Should().Be("new value");
        result.Version.Should().Be(2);
        result.ArchivedKey.Should().Be("key:v1");
    }

    [Fact]
    public async Task ArchiveAndUpdateFact_NonExistentFact_ShouldReturnFailure()
    {
        // Arrange
        _mockArchiveStore.ArchiveAndUpdateAsync("user1", "nonexistent", "value", Arg.Any<CancellationToken>())
            .Returns((SemanticStoreEntry?)null);

        // Act
        var result = await _tools.ArchiveAndUpdateFact("nonexistent", "value", userId: "user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    #endregion

    #region GetCategoryRule Tests

    [Fact]
    public async Task GetCategoryRule_ValidCategory_ShouldReturnRule()
    {
        // Arrange
        var rule = new CategoryResolutionRule
        {
            Category = FactCategory.Identity,
            DefaultStrategy = FactResolutionStrategy.RequireConfirmation,
            MinConfidenceDifferential = 0.3f,
            RequiresExplicitConfirmation = true,
            PreserveHistory = true,
            Description = "Identity facts require confirmation"
        };

        _mockValidator.GetCategoryRule(FactCategory.Identity).Returns(rule);

        // Act
        var result = await _tools.GetCategoryRule("Identity");

        // Assert
        result.Success.Should().BeTrue();
        result.Category.Should().Be("Identity");
        result.DefaultStrategy.Should().Be("RequireConfirmation");
        result.RequiresExplicitConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task GetCategoryRule_InvalidCategory_ShouldReturnError()
    {
        // Act
        var result = await _tools.GetCategoryRule("InvalidCategory");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown category");
    }

    #endregion

    #region GetAllCategoryRules Tests

    [Fact]
    public async Task GetAllCategoryRules_ShouldReturnAllRules()
    {
        // Act
        var result = await FactConflictTools.GetAllCategoryRules();

        // Assert
        result.Success.Should().BeTrue();
        result.RuleCount.Should().Be(10);
        result.Rules.Should().HaveCount(10);
        result.Rules.Should().Contain(r => r.Category == "Identity");
        result.Rules.Should().Contain(r => r.Category == "Preference");
        result.Rules.Should().Contain(r => r.Category == "Health");
    }

    #endregion
}
