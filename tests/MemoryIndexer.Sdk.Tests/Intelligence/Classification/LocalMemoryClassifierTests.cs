using FluentAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Classification;

/// <summary>
/// Tests for LocalMemoryClassifier with Phase 23.1 enhancements.
/// </summary>
public class LocalMemoryClassifierTests
{
    private readonly LocalMemoryClassifier _classifier;

    public LocalMemoryClassifierTests()
    {
        var options = Options.Create(new MemoryIndexerOptions());
        _classifier = new LocalMemoryClassifier(options, NullLogger<LocalMemoryClassifier>.Instance);
    }

    #region Procedural Classification Tests

    [Theory]
    [InlineData("I use pnpm for package management")]
    [InlineData("The project is built with React and TypeScript")]
    [InlineData("I always use Docker for deployment")]
    [InlineData("How to configure nginx for reverse proxy")]
    [InlineData("First, install the dependencies. Then, run the build script")]
    [InlineData("You need to set up the database before running the app")]
    public async Task ClassifyAsync_ProceduralContent_ReturnsProceduralType(string content)
    {
        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Procedural, $"content should be classified as Procedural: {content}");
        result.TypeConfidences.Should().ContainKey(MemoryType.Procedural);
        result.TypeConfidences[MemoryType.Procedural].Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public async Task ClassifyAsync_ToolKeywords_BoostsProceduralScore()
    {
        // Arrange
        var withTool = "The app uses React for the frontend";
        var withoutTool = "The app shows data in the interface";

        // Act
        var withToolResult = await _classifier.ClassifyAsync(withTool);
        var withoutToolResult = await _classifier.ClassifyAsync(withoutTool);

        // Assert
        withToolResult.TypeConfidences.Should().ContainKey(MemoryType.Procedural);
        withToolResult.TypeConfidences[MemoryType.Procedural].Should()
            .BeGreaterThan(withoutToolResult.TypeConfidences.GetValueOrDefault(MemoryType.Procedural));
    }

    #endregion

    #region Semantic Classification Tests

    [Theory]
    [InlineData("Docker is a containerization platform")]
    [InlineData("TypeScript is a typed superset of JavaScript")]
    [InlineData("React is a JavaScript library for building user interfaces")]
    [InlineData("A function is defined as a reusable block of code")]
    public async Task ClassifyAsync_SemanticContent_ReturnsSemanticType(string content)
    {
        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Semantic);
        result.TypeConfidences.Should().ContainKey(MemoryType.Semantic);
        result.TypeConfidences[MemoryType.Semantic].Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public async Task ClassifyAsync_DefinitionPattern_BoostsSemanticScore()
    {
        // Arrange
        var definition = "REST is a software architectural style";
        var nonDefinition = "REST provides good performance";

        // Act
        var definitionResult = await _classifier.ClassifyAsync(definition);
        var nonDefinitionResult = await _classifier.ClassifyAsync(nonDefinition);

        // Assert
        definitionResult.TypeConfidences[MemoryType.Semantic].Should()
            .BeGreaterThan(nonDefinitionResult.TypeConfidences[MemoryType.Semantic]);
    }

    #endregion

    #region Episodic Classification Tests

    [Theory]
    [InlineData("Yesterday I fixed the authentication bug")]
    [InlineData("Last week we discussed the new architecture")]
    [InlineData("I met with the team at the office")]
    [InlineData("Recently I updated the deployment pipeline")]
    public async Task ClassifyAsync_EpisodicContent_ReturnsEpisodicType(string content)
    {
        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Episodic);
        result.TypeConfidences.Should().ContainKey(MemoryType.Episodic);
        result.TypeConfidences[MemoryType.Episodic].Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public async Task ClassifyAsync_TimeLocationMarkers_BoostsEpisodicScore()
    {
        // Arrange
        var withMarkers = "Yesterday at the office I debugged the issue";
        var withoutMarkers = "I debugged the issue in the codebase";

        // Act
        var withMarkersResult = await _classifier.ClassifyAsync(withMarkers);
        var withoutMarkersResult = await _classifier.ClassifyAsync(withoutMarkers);

        // Assert
        withMarkersResult.TypeConfidences[MemoryType.Episodic].Should()
            .BeGreaterThan(withoutMarkersResult.TypeConfidences[MemoryType.Episodic]);
    }

    #endregion

    #region Fact Classification Tests

    [Theory]
    [InlineData("My name is John Doe")]
    [InlineData("I prefer TypeScript over JavaScript")]
    [InlineData("My favorite framework is React")]
    [InlineData("I work as a software engineer")]
    public async Task ClassifyAsync_FactContent_ReturnsFactType(string content)
    {
        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Fact);
        result.TypeConfidences.Should().ContainKey(MemoryType.Fact);
        result.TypeConfidences[MemoryType.Fact].Should().BeGreaterThan(0.5f);
    }

    #endregion

    #region Multi-Label Classification Tests

    [Fact]
    public async Task ClassifyAsync_HybridContent_ReturnsMultipleTypes()
    {
        // Arrange - Content that has both Procedural and Fact characteristics
        var content = "I always use TypeScript for my projects";

        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Procedural, "primary type should be the highest scoring");
        result.SecondaryTypes.Should().Contain(MemoryType.Fact, "should detect Fact as secondary type");
        result.TypeConfidences.Should().HaveCount(4, "should have confidence scores for all types");
    }

    [Fact]
    public async Task ClassifyAsync_ProceduralWithSemanticExplanation_CapturesBothTypes()
    {
        // Arrange
        var content = "Docker is a containerization platform. I always use it for deployment because it provides isolation.";

        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.TypeConfidences[MemoryType.Semantic].Should().BeGreaterThan(0.3f, "should detect semantic definition");
        result.TypeConfidences[MemoryType.Procedural].Should().BeGreaterThan(0f, "should detect procedural usage");

        // Either Semantic or Procedural should be primary, both should be present
        var combinedPresence = result.Type == MemoryType.Semantic || result.Type == MemoryType.Procedural;
        combinedPresence.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_SecondaryTypeThreshold_OnlyIncludesSignificantTypes()
    {
        // Arrange - Pure procedural content
        var content = "How to install Docker: First, download the installer. Then, run the installation wizard.";

        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Procedural);
        // Secondary types should only include those with >= 0.3 confidence
        foreach (var secondaryType in result.SecondaryTypes)
        {
            result.TypeConfidences[secondaryType].Should().BeGreaterThanOrEqualTo(0.3f);
        }
    }

    #endregion

    #region Type Confidence Tests

    [Fact]
    public async Task ClassifyAsync_AllContent_ProvicesTypeConfidences()
    {
        // Arrange
        var content = "This is a test message";

        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.TypeConfidences.Should().NotBeNull();
        result.TypeConfidences.Should().ContainKey(MemoryType.Episodic);
        result.TypeConfidences.Should().ContainKey(MemoryType.Semantic);
        result.TypeConfidences.Should().ContainKey(MemoryType.Procedural);
        result.TypeConfidences.Should().ContainKey(MemoryType.Fact);

        // All confidence values should be between 0 and 1
        foreach (var (type, confidence) in result.TypeConfidences)
        {
            confidence.Should().BeInRange(0f, 1f, $"{type} confidence should be normalized");
        }
    }

    [Fact]
    public async Task ClassifyAsync_OverallConfidence_BasedOnMaxScore()
    {
        // Arrange
        var highConfidenceContent = "My name is Alice and I prefer React";
        var lowConfidenceContent = "Something happened";

        // Act
        var highResult = await _classifier.ClassifyAsync(highConfidenceContent);
        var lowResult = await _classifier.ClassifyAsync(lowConfidenceContent);

        // Assert
        highResult.Confidence.Should().BeGreaterThan(lowResult.Confidence);
        highResult.Confidence.Should().BeInRange(0.5f, 1.0f);
    }

    #endregion

    #region Tier Assignment Tests

    [Fact]
    public async Task ClassifyAsync_FactType_AssignsUserTier()
    {
        // Arrange
        var factContent = "My favorite color is blue";

        // Act
        var result = await _classifier.ClassifyAsync(factContent);

        // Assert
        result.Type.Should().Be(MemoryType.Fact);
        result.Tier.Should().Be(Tier.Archive);
    }

    [Fact]
    public async Task ClassifyAsync_LongSemanticContent_AssignsUserTier()
    {
        // Arrange
        var words = string.Join(" ", Enumerable.Repeat("definition concept principle", 20));
        var content = $"The theory is defined as {words}";

        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.Type.Should().Be(MemoryType.Semantic);
        result.Tier.Should().Be(Tier.Archive);
    }

    [Fact]
    public async Task ClassifyAsync_ProceduralContent_AssignsSessionOrUserTier()
    {
        // Arrange
        var shortProcedural = "Use Docker for deployment";
        var words = string.Join(" ", Enumerable.Repeat("step procedure configure", 40));
        var longProcedural = $"How to deploy: {words}";

        // Act
        var shortResult = await _classifier.ClassifyAsync(shortProcedural);
        var longResult = await _classifier.ClassifyAsync(longProcedural);

        // Assert
        shortResult.Type.Should().Be(MemoryType.Procedural);
        shortResult.Tier.Should().Be(Tier.Long);

        longResult.Type.Should().Be(MemoryType.Procedural);
        longResult.Tier.Should().Be(Tier.Archive);
    }

    [Fact]
    public async Task ClassifyAsync_ShortEpisodicContent_AssignsWorkingTier()
    {
        // Arrange
        var shortEpisodic = "I saw that earlier";

        // Act
        var result = await _classifier.ClassifyAsync(shortEpisodic);

        // Assert
        result.Tier.Should().Be(Tier.Short);
    }

    #endregion

    #region Transient Detection Tests

    [Theory]
    [InlineData("Hello")]
    [InlineData("Thanks")]
    [InlineData("Okay, got it")]
    [InlineData("Yes")]
    [InlineData("Sure thing")]
    public async Task ClassifyAsync_TransientContent_ReturnsTransient(string content)
    {
        // Act
        var result = await _classifier.ClassifyAsync(content);

        // Assert
        result.ShouldPersist.Should().BeFalse();
        result.Tier.Should().Be(Tier.Short);
    }

    #endregion

    #region Importance Calculation Tests

    [Fact]
    public async Task ClassifyAsync_FactType_HighImportance()
    {
        // Arrange
        var fact = "My email is john@example.com";
        var general = "The weather is nice";

        // Act
        var factResult = await _classifier.ClassifyAsync(fact);
        var generalResult = await _classifier.ClassifyAsync(general);

        // Assert
        factResult.Importance.Should().BeGreaterThan(generalResult.Importance);
        factResult.Importance.Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public async Task ClassifyAsync_LongerContent_HigherImportance()
    {
        // Arrange
        var shortContent = "Docker is useful";
        var longContent = "Docker is a containerization platform that provides isolation, portability, and consistency across environments. It packages applications with their dependencies.";

        // Act
        var shortResult = await _classifier.ClassifyAsync(shortContent);
        var longResult = await _classifier.ClassifyAsync(longContent);

        // Assert
        longResult.Importance.Should().BeGreaterThan(shortResult.Importance);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ClassifyAsync_EmptyContent_ReturnsTransient()
    {
        // Act
        var result = await _classifier.ClassifyAsync("");

        // Assert
        result.Should().NotBeNull();
        result.ShouldPersist.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_NullContent_ReturnsTransient()
    {
        // Act
        var result = await _classifier.ClassifyAsync(null!);

        // Assert
        result.Should().NotBeNull();
        result.ShouldPersist.Should().BeFalse();
    }

    #endregion

    #region Batch Classification Tests

    [Fact]
    public async Task ClassifyBatchAsync_MultipleContents_ClassifiesAll()
    {
        // Arrange
        var contents = new[]
        {
            "My name is Alice",                          // Fact
            "Docker is a container platform",            // Semantic
            "I use TypeScript for projects",             // Procedural
            "Yesterday I fixed a bug"                    // Episodic
        };

        // Act
        var results = await _classifier.ClassifyBatchAsync(contents);

        // Assert
        results.Should().HaveCount(4);
        results[0].Type.Should().Be(MemoryType.Fact);
        results[1].Type.Should().Be(MemoryType.Semantic);
        results[2].Type.Should().Be(MemoryType.Procedural);
        results[3].Type.Should().Be(MemoryType.Episodic);
    }

    #endregion
}
