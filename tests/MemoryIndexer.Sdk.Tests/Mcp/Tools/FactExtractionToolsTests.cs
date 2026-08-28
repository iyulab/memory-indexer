using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Unit tests for FactExtractionTools MCP endpoints.
/// Phase v0.9.1: Intelligent Fact Extraction.
/// </summary>
public class FactExtractionToolsTests
{
    private readonly IFastTrackPromoter _mockPromoter;
    private readonly FactExtractionTools _tools;

    public FactExtractionToolsTests()
    {
        _mockPromoter = Substitute.For<IFastTrackPromoter>();
        _tools = new FactExtractionTools(_mockPromoter, Options.Create(new MemoryIndexerOptions()));
    }

    #region ExtractFacts Tests

    [Fact]
    public async Task ExtractFacts_WithDirectStatement_ShouldReturnExtractedFacts()
    {
        // Arrange
        var processResult = new FastTrackResult
        {
            Success = true,
            ContextType = StatementContextType.Direct,
            ExtractedFacts =
            [
                new UserFact
                {
                    Content = "User's name is John",
                    Category = FactCategory.Identity,
                    Confidence = 0.95f,
                    Importance = 0.9f,
                    PromotionPath = PromotionPath.FastTrack,
                    Subject = "user",
                    Predicate = "name",
                    Object = "John"
                }
            ],
            FastTrackedFacts =
            [
                new UserFact { Content = "User's name is John" }
            ],
            StandardPathFacts = [],
            SkippedFacts = []
        };

        _mockPromoter.ProcessAsync(
            Arg.Is<FactExtractionContext>(c => c.Content == "My name is John"),
            Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await _tools.ExtractFacts("My name is John", "user1");

        // Assert
        result.Success.Should().BeTrue();
        result.ContextType.Should().Be("Direct");
        result.TotalExtracted.Should().Be(1);
        result.FastTracked.Should().Be(1);
        result.StandardPath.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Facts.Should().HaveCount(1);
        result.Facts![0].Content.Should().Be("User's name is John");
        result.Facts[0].Category.Should().Be("Identity");
        result.Facts[0].PromotionPath.Should().Be("FastTrack");
    }

    [Fact]
    public async Task ExtractFacts_WithQuotedContent_ShouldReportSkipped()
    {
        // Arrange
        var processResult = new FastTrackResult
        {
            Success = true,
            ContextType = StatementContextType.Quoted,
            ExtractedFacts =
            [
                new UserFact
                {
                    Content = "Character's name is Lincoln",
                    Category = FactCategory.Identity,
                    Confidence = 0.3f,
                    PromotionPath = PromotionPath.SessionOnly
                }
            ],
            FastTrackedFacts = [],
            StandardPathFacts = [],
            SkippedFacts =
            [
                new FactSkipReason
                {
                    Fact = new UserFact { Content = "Character's name is Lincoln" },
                    Reason = SkipReasonType.ContextNotDirect,
                    Details = "Quoted content detected"
                }
            ]
        };

        _mockPromoter.ProcessAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await _tools.ExtractFacts("In the novel, 'My name is Lincoln'");

        // Assert
        result.Success.Should().BeTrue();
        result.ContextType.Should().Be("Quoted");
        result.Skipped.Should().Be(1);
        result.FastTracked.Should().Be(0);
        result.SkippedReasons.Should().HaveCount(1);
        result.SkippedReasons![0].Reason.Should().Be("ContextNotDirect");
    }

    [Fact]
    public async Task ExtractFacts_WithNoFacts_ShouldReturnEmptyResult()
    {
        // Arrange
        var processResult = new FastTrackResult
        {
            Success = true,
            ContextType = StatementContextType.Question,
            ExtractedFacts = [],
            FastTrackedFacts = [],
            StandardPathFacts = [],
            SkippedFacts = []
        };

        _mockPromoter.ProcessAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await _tools.ExtractFacts("How are you?");

        // Assert
        result.Success.Should().BeTrue();
        result.TotalExtracted.Should().Be(0);
        result.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFacts_WithSessionAndRole_ShouldPassContext()
    {
        // Arrange
        var processResult = FastTrackResult.Empty;

        _mockPromoter.ProcessAsync(
            Arg.Is<FactExtractionContext>(c =>
                c.UserId == "custom-user" &&
                c.SessionId == "session-123" &&
                c.Role == "user"),
            Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        await _tools.ExtractFacts(
            "My name is John",
            userId: "custom-user",
            sessionId: "session-123",
            role: "user");

        // Assert
        await _mockPromoter.Received(1).ProcessAsync(
            Arg.Is<FactExtractionContext>(c =>
                c.UserId == "custom-user" &&
                c.SessionId == "session-123" &&
                c.Role == "user"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetUserProfile Tests

    [Fact]
    public async Task GetUserProfile_WithExistingProfile_ShouldReturnFacts()
    {
        // Arrange
        var profile = new UserProfile
        {
            UserId = "user1",
            Facts =
            [
                new UserProfileFact
                {
                    Key = "identity:user:name",
                    Content = "User's name is John",
                    Category = FactCategory.Identity,
                    Confidence = 0.95f,
                    ConfirmationCount = 2,
                    IsConfirmed = false,
                    CreatedAt = DateTime.UtcNow.AddDays(-7),
                    UpdatedAt = DateTime.UtcNow
                },
                new UserProfileFact
                {
                    Key = "preference:user:likes",
                    Content = "User likes pizza",
                    Category = FactCategory.Preference,
                    Confidence = 0.8f,
                    ConfirmationCount = 1,
                    IsConfirmed = false,
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow
                }
            ],
            FactsByCategory = new Dictionary<FactCategory, IReadOnlyList<UserProfileFact>>
            {
                [FactCategory.Identity] = [new UserProfileFact { Key = "test", Content = "test", Category = FactCategory.Identity }],
                [FactCategory.Preference] = [new UserProfileFact { Key = "test2", Content = "test2", Category = FactCategory.Preference }]
            },
            LastUpdatedAt = DateTime.UtcNow
        };

        _mockPromoter.GetUserProfileAsync("user1", null, Arg.Any<CancellationToken>())
            .Returns(profile);

        // Act
        var result = await _tools.GetUserProfile("user1");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.TotalFacts.Should().Be(2);
        result.Facts.Should().HaveCount(2);
        result.FactsByCategory.Should().ContainKey("Identity");
        result.FactsByCategory.Should().ContainKey("Preference");
    }

    [Fact]
    public async Task GetUserProfile_WithCategoryFilter_ShouldFilterResults()
    {
        // Arrange
        var profile = new UserProfile
        {
            UserId = "user1",
            Facts =
            [
                new UserProfileFact
                {
                    Key = "preference:user:likes",
                    Content = "User likes pizza",
                    Category = FactCategory.Preference,
                    Confidence = 0.8f
                }
            ],
            FactsByCategory = new Dictionary<FactCategory, IReadOnlyList<UserProfileFact>>
            {
                [FactCategory.Preference] = [new UserProfileFact { Key = "test", Content = "test", Category = FactCategory.Preference }]
            }
        };

        _mockPromoter.GetUserProfileAsync("user1", FactCategory.Preference, Arg.Any<CancellationToken>())
            .Returns(profile);

        // Act
        var result = await _tools.GetUserProfile("user1", "Preference");

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFacts.Should().Be(1);
        result.Facts![0].Category.Should().Be("Preference");
    }

    [Fact]
    public async Task GetUserProfile_WithNoFacts_ShouldReturnEmptyProfile()
    {
        // Arrange
        var profile = UserProfile.Empty("user1");

        _mockPromoter.GetUserProfileAsync("user1", null, Arg.Any<CancellationToken>())
            .Returns(profile);

        // Act
        var result = await _tools.GetUserProfile("user1");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.TotalFacts.Should().Be(0);
        result.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserProfile_WithDefaultUserId_ShouldUseDefault()
    {
        // Arrange
        var profile = UserProfile.Empty("default");

        _mockPromoter.GetUserProfileAsync("default", null, Arg.Any<CancellationToken>())
            .Returns(profile);

        // Act
        await _tools.GetUserProfile();

        // Assert
        await _mockPromoter.Received(1).GetUserProfileAsync("default", null, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetFactCategories Tests

    [Fact]
    public async Task GetFactCategories_ShouldReturnAllCategories()
    {
        // Act
        var result = await FactExtractionTools.GetFactCategories();

        // Assert
        result.Success.Should().BeTrue();
        result.Categories.Should().HaveCount(10);
        result.Categories!.Should().Contain(c => c.Name == "Identity");
        result.Categories.Should().Contain(c => c.Name == "Preference");
        result.Categories.Should().Contain(c => c.Name == "Relationship");
        result.Categories.Should().Contain(c => c.Name == "Skill");
        result.Categories.Should().Contain(c => c.Name == "Professional");
    }

    #endregion

    #region GetPromotionPaths Tests

    [Fact]
    public async Task GetPromotionPaths_ShouldReturnAllPaths()
    {
        // Act
        var result = await FactExtractionTools.GetPromotionPaths();

        // Assert
        result.Success.Should().BeTrue();
        result.Paths.Should().HaveCount(4);

        var fastTrack = result.Paths!.Single(p => p.Name == "FastTrack");
        fastTrack.ConfidenceThreshold.Should().Be(0.9f);
        fastTrack.Description.Should().Contain("Direct promotion to Archive");

        var standard = result.Paths.Single(p => p.Name == "Standard");
        standard.ConfidenceThreshold.Should().Be(0.5f);

        result.Paths.Should().Contain(p => p.Name == "SessionOnly");
        result.Paths.Should().Contain(p => p.Name == "Discard");
    }

    #endregion
}
