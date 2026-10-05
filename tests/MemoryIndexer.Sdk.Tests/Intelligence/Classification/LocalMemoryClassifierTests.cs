using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Classification;
using MemoryIndexer.Services.TokenCounting;
using Microsoft.Extensions.Logging.Abstractions;
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
        _classifier = new LocalMemoryClassifier(NullLogger<LocalMemoryClassifier>.Instance, new ApproximateTokenCounter());
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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Type.Should().Be(MemoryType.Procedural, $"content should be classified as Procedural: {content}");
        result.TypeConfidences.Should().ContainKey(MemoryType.Procedural);
        result.TypeConfidences[MemoryType.Procedural].Should().BeGreaterThan(0.3f);
    }

    // Indicators match whole words only: "use" inside "User:" (a role prefix a caller stores with the utterance) or
    // "effect" inside "effective" said nothing about the content, and the first sent every stored user turn to Procedural,
    // which no recall path reads.
    [Theory]
    [InlineData("User: 오늘 회의 결과를 정리해 주세요")]
    [InlineData("User: 지난주 보고서 초안을 다시 보여 주세요")]
    public async Task ClassifyAsync_ARolePrefix_IsNotAProceduralIndicator(string content)
    {
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        result.Type.Should().NotBe(MemoryType.Procedural);
        result.TypeConfidences[MemoryType.Procedural].Should().BeLessThan(result.TypeConfidences[MemoryType.Episodic]);
    }

    [Fact]
    public async Task ClassifyAsync_AnIndicatorInsideALongerWord_DoesNotCount()
    {
        var inside = await _classifier.ClassifyAsync("회의록 effective 2026", cancellationToken: TestContext.Current.CancellationToken);
        var whole = await _classifier.ClassifyAsync("회의록 effect 2026", cancellationToken: TestContext.Current.CancellationToken);

        inside.TypeConfidences[MemoryType.Semantic].Should().BeLessThan(whole.TypeConfidences[MemoryType.Semantic],
            "'effect' is a semantic indicator as a word, not as part of 'effective'");
    }

    [Fact]
    public async Task ClassifyAsync_ToolKeywords_BoostsProceduralScore()
    {
        // Arrange
        var withTool = "The app uses React for the frontend";
        var withoutTool = "The app shows data in the interface";

        // Act
        var withToolResult = await _classifier.ClassifyAsync(withTool, cancellationToken: TestContext.Current.CancellationToken);
        var withoutToolResult = await _classifier.ClassifyAsync(withoutTool, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var definitionResult = await _classifier.ClassifyAsync(definition, cancellationToken: TestContext.Current.CancellationToken);
        var nonDefinitionResult = await _classifier.ClassifyAsync(nonDefinition, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var withMarkersResult = await _classifier.ClassifyAsync(withMarkers, cancellationToken: TestContext.Current.CancellationToken);
        var withoutMarkersResult = await _classifier.ClassifyAsync(withoutMarkers, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var highResult = await _classifier.ClassifyAsync(highConfidenceContent, cancellationToken: TestContext.Current.CancellationToken);
        var lowResult = await _classifier.ClassifyAsync(lowConfidenceContent, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(factContent, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

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
        var shortResult = await _classifier.ClassifyAsync(shortProcedural, cancellationToken: TestContext.Current.CancellationToken);
        var longResult = await _classifier.ClassifyAsync(longProcedural, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(shortEpisodic, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.ShouldPersist.Should().BeFalse();
        result.Tier.Should().Be(Tier.Short);
    }

    [Theory]
    [InlineData("User: thanks!")]
    [InlineData("ok thanks")]
    [InlineData("Thank you so much!")]
    [InlineData("Good morning!")]
    [InlineData("👍")]
    public async Task ClassifyAsync_SmallTalkOnly_IsDropped(string content)
    {
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        result.ShouldPersist.Should().BeFalse();
    }

    // A short turn that carries information is kept in every language. Length was a whitespace word count, and anything
    // untyped under 20 words was dropped: a Korean turn has about half the space-separated units of its English
    // equivalent, and Chinese and Japanese have none, so nearly every turn in those languages was lost (and short
    // English facts with them).
    [Theory]
    [InlineData("User: 제 프로젝트 코드명은 '청록고래-1005'입니다. 앞으로 이걸 기억해 주세요.")]
    [InlineData("User: 내 차 번호는 12가 3456이야.")]
    [InlineData("我的项目代号是青鲸一〇〇五，请记住。")]
    [InlineData("私のプロジェクト名は青鯨1005です。")]
    [InlineData("User: my project codename is Teal Whale 1005")]
    [InlineData("ok, my plate is 12-3456")]
    [InlineData("his name is Kim")]
    public async Task ClassifyAsync_AShortTurnThatCarriesInformation_IsKept(string content)
    {
        var result = await _classifier.ClassifyAsync(content, cancellationToken: TestContext.Current.CancellationToken);

        result.ShouldPersist.Should().BeTrue();
    }

    // The tier says where a memory lives, not whether it is kept: a short episodic turn is working memory.
    [Fact]
    public async Task ClassifyAsync_AShortEpisodicTurn_IsWorkingMemoryAndKept()
    {
        var result = await _classifier.ClassifyAsync("User: 내 차 번호는 12가 3456이야.", cancellationToken: TestContext.Current.CancellationToken);

        result.Tier.Should().Be(Tier.Short);
        result.ShouldPersist.Should().BeTrue();
    }

    // Length is measured in tokens: the same sentence without spaces is as long as with them.
    [Fact]
    public async Task ClassifyAsync_LengthDoesNotDependOnSpaces()
    {
        var spaced = string.Join(" ", Enumerable.Repeat("지난주 회의에서 결정한 배포 일정", 6));
        var unspaced = spaced.Replace(" ", string.Empty, StringComparison.Ordinal);

        var a = await _classifier.ClassifyAsync(spaced, cancellationToken: TestContext.Current.CancellationToken);
        var b = await _classifier.ClassifyAsync(unspaced, cancellationToken: TestContext.Current.CancellationToken);

        a.Tier.Should().Be(Tier.Long);
        b.Tier.Should().Be(a.Tier);
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
        var factResult = await _classifier.ClassifyAsync(fact, cancellationToken: TestContext.Current.CancellationToken);
        var generalResult = await _classifier.ClassifyAsync(general, cancellationToken: TestContext.Current.CancellationToken);

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
        var shortResult = await _classifier.ClassifyAsync(shortContent, cancellationToken: TestContext.Current.CancellationToken);
        var longResult = await _classifier.ClassifyAsync(longContent, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        longResult.Importance.Should().BeGreaterThan(shortResult.Importance);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ClassifyAsync_EmptyContent_ReturnsTransient()
    {
        // Act
        var result = await _classifier.ClassifyAsync("", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.ShouldPersist.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_NullContent_ReturnsTransient()
    {
        // Act
        var result = await _classifier.ClassifyAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

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
        var results = await _classifier.ClassifyBatchAsync(contents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(4);
        results[0].Type.Should().Be(MemoryType.Fact);
        results[1].Type.Should().Be(MemoryType.Semantic);
        results[2].Type.Should().Be(MemoryType.Procedural);
        results[3].Type.Should().Be(MemoryType.Episodic);
    }

    #endregion
}
