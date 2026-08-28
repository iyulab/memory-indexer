using AwesomeAssertions;
using McpServer.Controllers;
using MemoryIndexer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Controllers;

/// <summary>
/// Unit tests for ConflictController REST API.
/// </summary>
public class ConflictControllerTests
{
    private readonly IFactValidator _mockValidator;
    private readonly IArchiveStore _mockArchiveStore;
    private readonly ILogger<ConflictController> _mockLogger;
    private readonly ConflictController _controller;

    public ConflictControllerTests()
    {
        _mockValidator = Substitute.For<IFactValidator>();
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _mockLogger = Substitute.For<ILogger<ConflictController>>();
        _controller = new ConflictController(
            _mockValidator,
            _mockArchiveStore,
            _mockLogger);
    }

    #region ValidateFact Tests

    [Fact]
    public async Task ValidateFact_WithValidRequest_ReturnsOkResult()
    {
        // Arrange
        var request = new ValidateFactRequest
        {
            Content = "User's name is John",
            Category = "Identity"
        };

        _mockArchiveStore.GetAllAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Valid());

        // Act
        var result = await _controller.ValidateFact(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ValidateFactResponse>().Subject;
        response.IsValid.Should().BeTrue();
        response.RecommendedAction.Should().Be("Add");
        response.ConflictCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidateFact_WithEmptyContent_ReturnsBadRequest()
    {
        // Arrange
        var request = new ValidateFactRequest { Content = "" };

        // Act
        var result = await _controller.ValidateFact(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ValidateFact_WithNullContent_ReturnsBadRequest()
    {
        // Arrange
        var request = new ValidateFactRequest { Content = null! };

        // Act
        var result = await _controller.ValidateFact(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ValidateFact_WithConflict_ReturnsConflictDetails()
    {
        // Arrange
        var existingFact = new SemanticStoreEntry
        {
            Key = "identity:name",
            Value = "User's name is Jane"
        };

        var analysis = new FactConflictAnalysis
        {
            IsValid = false,
            RecommendedAction = FactConflictAction.RequireReview,
            Conflicts =
            [
                new FactConflict
                {
                    ConflictType = FactConflictType.Contradiction,
                    ExistingFact = existingFact,
                    Confidence = 0.9f,
                    Description = "Name contradiction detected"
                }
            ],
            RequiresConfirmation = true
        };

        _mockArchiveStore.GetAllAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry> { existingFact });

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(analysis);

        var request = new ValidateFactRequest
        {
            Content = "User's name is John",
            Category = "Identity"
        };

        // Act
        var result = await _controller.ValidateFact(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ValidateFactResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.ConflictCount.Should().Be(1);
        response.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateFact_WithCustomUserId_UsesProvidedUserId()
    {
        // Arrange
        var request = new ValidateFactRequest
        {
            Content = "Test content",
            UserId = "custom-user-123"
        };

        _mockArchiveStore.GetAllAsync("custom-user-123", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockValidator.ValidateAsync(
            Arg.Any<UserFact>(),
            Arg.Any<IReadOnlyList<SemanticStoreEntry>>(),
            Arg.Any<FactValidationOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(FactConflictAnalysis.Valid());

        // Act
        await _controller.ValidateFact(request, CancellationToken.None);

        // Assert
        await _mockArchiveStore.Received(1).GetAllAsync("custom-user-123", Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetFactHistory Tests

    [Fact]
    public async Task GetFactHistory_WithValidKey_ReturnsHistory()
    {
        // Arrange
        var history = new List<SemanticStoreEntry>
        {
            new() { Key = "name_v2", Value = "John", Version = 2, IsActive = true },
            new() { Key = "name_v1", Value = "Jane", Version = 1, IsActive = false }
        };

        _mockArchiveStore.GetHistoryAsync("default", "name", Arg.Any<CancellationToken>())
            .Returns(history);

        // Act
        var result = await _controller.GetFactHistory("name", cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<FactHistoryResponse>().Subject;
        response.VersionCount.Should().Be(2);
        response.Versions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFactHistory_WithEmptyKey_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetFactHistory("", cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region GetFactsValidAt Tests

    [Fact]
    public async Task GetFactsValidAt_WithValidDate_ReturnsFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "name", Value = "John", ValidFrom = DateTime.UtcNow.AddDays(-10), IsActive = true }
        };

        _mockArchiveStore.GetValidAtAsync("default", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _controller.GetFactsValidAt("2024-01-15", cancellationToken: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<TemporalFactsResponse>().Subject;
        response.FactCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFactsValidAt_WithInvalidDate_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetFactsValidAt("not-a-date", cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetFactsValidAt_WithEmptyDate_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetFactsValidAt("", cancellationToken: CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region ArchiveAndUpdateFact Tests

    [Fact]
    public async Task ArchiveAndUpdateFact_WithValidRequest_ReturnsUpdatedEntry()
    {
        // Arrange
        var updatedEntry = new SemanticStoreEntry
        {
            Key = "name",
            Value = "New Value",
            Version = 2,
            SupersedesKey = "name_v1"
        };

        _mockArchiveStore.ArchiveAndUpdateAsync(
            "default", "name", "New Value", Arg.Any<CancellationToken>())
            .Returns(updatedEntry);

        var request = new ArchiveAndUpdateRequest
        {
            Key = "name",
            NewValue = "New Value"
        };

        // Act
        var result = await _controller.ArchiveAndUpdateFact(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ArchiveAndUpdateResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Version.Should().Be(2);
    }

    [Fact]
    public async Task ArchiveAndUpdateFact_WithNonExistentKey_ReturnsNotFound()
    {
        // Arrange
        _mockArchiveStore.ArchiveAndUpdateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SemanticStoreEntry?)null);

        var request = new ArchiveAndUpdateRequest
        {
            Key = "nonexistent",
            NewValue = "New Value"
        };

        // Act
        var result = await _controller.ArchiveAndUpdateFact(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ArchiveAndUpdateFact_WithEmptyKey_ReturnsBadRequest()
    {
        // Arrange
        var request = new ArchiveAndUpdateRequest
        {
            Key = "",
            NewValue = "New Value"
        };

        // Act
        var result = await _controller.ArchiveAndUpdateFact(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region GetCategoryRule Tests

    [Fact]
    public void GetCategoryRule_WithValidCategory_ReturnsRule()
    {
        // Arrange
        var rule = CategoryResolutionRule.Defaults[FactCategory.Identity];
        _mockValidator.GetCategoryRule(FactCategory.Identity).Returns(rule);

        // Act
        var result = _controller.GetCategoryRule("Identity");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CategoryRuleResponse>().Subject;
        response.Category.Should().Be("Identity");
        response.RequiresExplicitConfirmation.Should().BeTrue();
    }

    [Fact]
    public void GetCategoryRule_WithInvalidCategory_ReturnsBadRequest()
    {
        // Act
        var result = _controller.GetCategoryRule("InvalidCategory");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region GetAllCategoryRules Tests

    [Fact]
    public void GetAllCategoryRules_ReturnsAllRules()
    {
        // Act
        var result = _controller.GetAllCategoryRules();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AllCategoryRulesResponse>().Subject;
        response.RuleCount.Should().BeGreaterThan(0);
        response.Rules.Should().NotBeEmpty();
    }

    #endregion
}
