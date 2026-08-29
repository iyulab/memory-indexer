using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Unit tests for ContextTools MCP endpoints.
/// Phase v0.9.0: Context Budget API.
/// </summary>
public class ContextToolsTests
{
    private readonly IContextBuilder _mockContextBuilder;
    private readonly ContextTools _tools;

    public ContextToolsTests()
    {
        _mockContextBuilder = Substitute.For<IContextBuilder>();
        _tools = new ContextTools(_mockContextBuilder, Options.Create(new MemoryIndexerOptions()));
    }

    #region ContextBuild Tests

    [Fact]
    public async Task ContextBuild_WithDefaultStrategy_ReturnsContextBundle()
    {
        // Arrange
        var bundle = new ContextBundle(
            Content: "## Recent Conversation\nTest content",
            TotalTokens: 150,
            Breakdown: new ContextBreakdown(60, 50, 30, 10),
            Items: new List<ContextItem>
            {
                new("Test content", 50, ContextItemSource.Recent, null, DateTime.UtcNow, "user"),
                new("Semantic memory", 50, ContextItemSource.Semantic, 0.85f, DateTime.UtcNow.AddDays(-1), null),
                new("Session info", 50, ContextItemSource.Episodic, null, DateTime.UtcNow.AddHours(-2), null)
            }
        );

        _mockContextBuilder.BuildAsync(
            Arg.Any<ContextRequest>(),
            "Balanced",
            Arg.Any<CancellationToken>())
            .Returns(bundle);

        // Act
        var result = await _tools.ContextBuild("test query", 2000, "Balanced", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalTokens.Should().Be(150);
        result.ItemCount.Should().Be(3);
        result.Strategy.Should().Be("Balanced");
        result.Breakdown.Should().NotBeNull();
        result.Breakdown!.RecentTokens.Should().Be(60);
        result.Breakdown.SemanticTokens.Should().Be(50);
        result.Breakdown.EpisodicTokens.Should().Be(30);
        result.Breakdown.FactTokens.Should().Be(10);
    }

    [Fact]
    public async Task ContextBuild_WithRecentHeavyStrategy_PassesCorrectStrategy()
    {
        // Arrange
        var bundle = new ContextBundle(
            Content: "Recent heavy content",
            TotalTokens: 200,
            Breakdown: new ContextBreakdown(90, 20, 70, 20),
            Items: []
        );

        _mockContextBuilder.BuildAsync(
            Arg.Any<ContextRequest>(),
            "RecentHeavy",
            Arg.Any<CancellationToken>())
            .Returns(bundle);

        // Act
        var result = await _tools.ContextBuild("test query", 2000, "RecentHeavy", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Strategy.Should().Be("RecentHeavy");
        await _mockContextBuilder.Received(1).BuildAsync(
            Arg.Any<ContextRequest>(),
            "RecentHeavy",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContextBuild_ClampsTokensToValidRange()
    {
        // Arrange
        var bundle = new ContextBundle("", 0, new ContextBreakdown(0, 0, 0, 0), []);
        _mockContextBuilder.BuildAsync(
            Arg.Is<ContextRequest>(r => r.Budget.TotalTokens >= 100 && r.Budget.TotalTokens <= 16000),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(bundle);

        // Act - request way too many tokens
        var result = await _tools.ContextBuild("test", 999999, "Balanced", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockContextBuilder.Received(1).BuildAsync(
            Arg.Is<ContextRequest>(r => r.Budget.TotalTokens == 16000),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetRecentConversation Tests

    [Fact]
    public async Task GetRecentConversation_ReturnsChronologicalTurns()
    {
        // Arrange
        var items = new List<ContextItem>
        {
            new("User: Hello", 10, ContextItemSource.Recent, null, DateTime.UtcNow.AddMinutes(-5), "user"),
            new("Assistant: Hi there!", 15, ContextItemSource.Recent, null, DateTime.UtcNow.AddMinutes(-4), "assistant"),
            new("User: How are you?", 12, ContextItemSource.Recent, null, DateTime.UtcNow.AddMinutes(-3), "user")
        };

        _mockContextBuilder.GetRecentTurnsAsync(
            "default", "session1", 1000, Arg.Any<CancellationToken>())
            .Returns(items);

        // Act
        var result = await _tools.GetRecentConversation(1000, "session1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.TurnCount.Should().Be(3);
        result.TotalTokens.Should().Be(37);
        result.Turns.Should().HaveCount(3);
        result.Turns![0].Role.Should().Be("user");
        result.Turns[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task GetRecentConversation_ClampsMaxTokens()
    {
        // Arrange
        _mockContextBuilder.GetRecentTurnsAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<int>(t => t >= 50 && t <= 8000),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContextItem>());

        // Act
        var result = await _tools.GetRecentConversation(50000, "session1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockContextBuilder.Received(1).GetRecentTurnsAsync(
            "default", "session1", 8000, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetSessionContext Tests

    [Fact]
    public async Task GetSessionContext_ReturnsEpisodicMemories()
    {
        // Arrange
        var items = new List<ContextItem>
        {
            new("Game started", 20, ContextItemSource.Episodic, null, DateTime.UtcNow.AddHours(-1), null, MemoryType.Episodic),
            new("Question 1 asked", 25, ContextItemSource.Episodic, null, DateTime.UtcNow.AddMinutes(-30), null, MemoryType.Episodic)
        };

        _mockContextBuilder.GetSessionContextAsync(
            "default", "game-session-1", 1000, Arg.Any<CancellationToken>())
            .Returns(items);

        // Act
        var result = await _tools.GetSessionContext("game-session-1", 1000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.SessionId.Should().Be("game-session-1");
        result.ItemCount.Should().Be(2);
        result.TotalTokens.Should().Be(45);
        result.Items.Should().HaveCount(2);
        result.Items![0].MemoryType.Should().Be("Episodic");
    }

    #endregion

    #region GetUserFacts Tests

    [Fact]
    public async Task GetUserFacts_ReturnsPersistentFacts()
    {
        // Arrange
        var items = new List<ContextItem>
        {
            new("User's name is John", 10, ContextItemSource.Fact, 0.95f, DateTime.UtcNow.AddDays(-30), null, MemoryType.Fact),
            new("User likes pizza", 8, ContextItemSource.Fact, 0.80f, DateTime.UtcNow.AddDays(-7), null, MemoryType.Semantic)
        };

        _mockContextBuilder.GetUserFactsAsync(
            "default", 500, Arg.Any<CancellationToken>())
            .Returns(items);

        // Act
        var result = await _tools.GetUserFacts(500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.FactCount.Should().Be(2);
        result.TotalTokens.Should().Be(18);
        result.Facts.Should().HaveCount(2);
        result.Facts![0].Importance.Should().Be(0.95f);
        result.Facts[1].MemoryType.Should().Be("Semantic");
    }

    [Fact]
    public async Task GetUserFacts_ClampsMaxTokens()
    {
        // Arrange
        _mockContextBuilder.GetUserFactsAsync(
            Arg.Any<string>(),
            Arg.Is<int>(t => t >= 50 && t <= 4000),
            Arg.Any<CancellationToken>())
            .Returns(new List<ContextItem>());

        // Act
        var result = await _tools.GetUserFacts(10000, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockContextBuilder.Received(1).GetUserFactsAsync(
            "default", 4000, Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetContextStrategies Tests

    [Fact]
    public async Task GetContextStrategies_ReturnsAllStrategies()
    {
        // Act
        var result = await ContextTools.GetContextStrategies();

        // Assert
        result.Success.Should().BeTrue();
        result.Strategies.Should().HaveCount(3);

        var balanced = result.Strategies!.Single(s => s.Name == "Balanced");
        balanced.RecentPercent.Should().Be(0.30);
        balanced.SemanticPercent.Should().Be(0.25);
        balanced.EpisodicPercent.Should().Be(0.25);
        balanced.FactPercent.Should().Be(0.20);

        var recentHeavy = result.Strategies.Single(s => s.Name == "RecentHeavy");
        recentHeavy.RecentPercent.Should().Be(0.45);
        recentHeavy.EpisodicPercent.Should().Be(0.35);

        var semanticHeavy = result.Strategies.Single(s => s.Name == "SemanticHeavy");
        semanticHeavy.SemanticPercent.Should().Be(0.45);
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public async Task ContextBuild_WithSessionId_IncludesSessionInRequest()
    {
        // Arrange
        var bundle = new ContextBundle("", 0, new ContextBreakdown(0, 0, 0, 0), []);
        _mockContextBuilder.BuildAsync(
            Arg.Is<ContextRequest>(r => r.SessionId == "my-session"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(bundle);

        // Act
        var result = await _tools.ContextBuild("query", 1000, "Balanced", "my-session", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockContextBuilder.Received(1).BuildAsync(
            Arg.Is<ContextRequest>(r => r.SessionId == "my-session"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContextBuild_WithCustomUserId_PassesUserIdInRequest()
    {
        // Arrange
        var bundle = new ContextBundle("", 0, new ContextBreakdown(0, 0, 0, 0), []);
        _mockContextBuilder.BuildAsync(
            Arg.Is<ContextRequest>(r => r.UserId == "custom-user"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(bundle);

        // Act
        var result = await _tools.ContextBuild("query", 1000, "Balanced", null, "custom-user", TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockContextBuilder.Received(1).BuildAsync(
            Arg.Is<ContextRequest>(r => r.UserId == "custom-user"),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
