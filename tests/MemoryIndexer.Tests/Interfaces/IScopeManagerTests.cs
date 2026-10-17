using AwesomeAssertions;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Tests.Interfaces;

/// <summary>
/// Tests for IScopeManager scope resolution in the 3-Axis Memory Model.
/// </summary>
public class IScopeManagerTests
{
    private static ScopeManager CreateScopeManager() => new(NullLogger<ScopeManager>.Instance);

    #region ResolveScopeAsync Tests (6 tests)

    [Fact]
    public async Task ResolveScopeAsync_HighImportanceFact_ShouldReturnUserScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("User's name is Alice", MemoryType.Fact, importance: 0.9f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        scope.Should().Be(Scope.User);
    }

    [Fact]
    public async Task ResolveScopeAsync_EpisodicMemory_ShouldReturnTopicScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("We discussed project requirements", MemoryType.Episodic, importance: 0.6f, cancellationToken: TestContext.Current.CancellationToken); // 0.6 < 0.7, so returns Topic

        // Assert
        scope.Should().Be(Scope.Topic);
    }

    [Fact]
    public async Task ResolveScopeAsync_LowImportanceContent_ShouldReturnTurnScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("yes", MemoryType.Episodic, importance: 0.1f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        scope.Should().Be(Scope.Turn);
    }

    [Fact]
    public async Task ResolveScopeAsync_SemanticMemory_ShouldReturnSessionScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("Machine learning concepts", MemoryType.Semantic, importance: 0.5f, cancellationToken: TestContext.Current.CancellationToken); // 0.5 >= 0.5, 0.5 < 0.8, so returns Session

        // Assert
        scope.Should().Be(Scope.Session);
    }

    [Fact]
    public async Task ResolveScopeAsync_ProceduralMemory_ShouldReturnAppropriateScope()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("How to deploy the application", MemoryType.Procedural, importance: 0.7f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        scope.Should().BeOneOf(Scope.Session, Scope.User);
    }

    [Fact]
    public async Task ResolveScopeAsync_MediumImportance_ShouldReturnSessionOrTopic()
    {
        // Arrange
        var scopeManager = CreateScopeManager();

        // Act
        var scope = await scopeManager.ResolveScopeAsync("Discussing the current task", MemoryType.Episodic, importance: 0.5f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        scope.Should().BeOneOf(Scope.Topic, Scope.Session);
    }

    #endregion
}
