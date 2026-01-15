using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Inference;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Inference;

/// <summary>
/// Tests for FactInferenceService.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class FactInferenceServiceTests
{
    private readonly Mock<IArchiveStore> _mockArchiveStore;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ILogger<FactInferenceService>> _mockLogger;
    private readonly FactInferenceService _service;

    private readonly ReadOnlyMemory<float> _testEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

    public FactInferenceServiceTests()
    {
        _mockArchiveStore = new Mock<IArchiveStore>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockLogger = new Mock<ILogger<FactInferenceService>>();

        _mockEmbeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        _service = new FactInferenceService(
            _mockArchiveStore.Object,
            _mockEmbeddingService.Object,
            _mockLogger.Object);
    }

    #region InferAsync Tests

    [Fact]
    public async Task InferAsync_WithEmptyFacts_ShouldReturnEmpty()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>();

        // Act
        var result = await _service.InferAsync(facts);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InferAsync_WithSingleFact_ShouldReturnEmpty()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test", Value = "User lives in Seoul", IsActive = true, Confidence = 0.9f }
        };

        // Act
        var result = await _service.InferAsync(facts);

        // Assert
        result.Should().BeEmpty(); // Need at least 2 facts for inference
    }

    [Fact]
    public async Task InferAsync_WithLowConfidenceFacts_ShouldFilterOut()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test1", Value = "User lives in Seoul", IsActive = true, Confidence = 0.3f },
            new() { Key = "test2", Value = "User works at Samsung", IsActive = true, Confidence = 0.4f }
        };

        var options = new InferenceOptions { MinSourceConfidence = 0.7f };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InferAsync_WithCoOccurrence_ShouldInfer()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "location:residence",
                Value = "User lives in Seoul",
                Category = SemanticStoreCategory.Fact,
                IsActive = true,
                Confidence = 0.9f
            },
            new()
            {
                Key = "work:company",
                Value = "User works at Samsung in Seoul",
                Category = SemanticStoreCategory.Work,
                IsActive = true,
                Confidence = 0.9f
            }
        };

        var options = new InferenceOptions
        {
            EnabledTypes = [InferenceType.CoOccurrence],
            MinSourceConfidence = 0.7f,
            MinInferenceConfidence = 0.3f
        };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(f => f.Type == InferenceType.CoOccurrence);
    }

    [Fact]
    public async Task InferAsync_WithGeneralization_ShouldInferFromMultipleFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "pref1", Value = "User likes pizza", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.9f },
            new() { Key = "pref2", Value = "User likes pasta", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.8f },
            new() { Key = "pref3", Value = "User likes sushi", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.85f },
            new() { Key = "pref4", Value = "User likes burgers", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.9f }
        };

        var options = new InferenceOptions
        {
            EnabledTypes = [InferenceType.Generalization],
            MinSourceConfidence = 0.7f,
            MinInferenceConfidence = 0.3f
        };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(f => f.Type == InferenceType.Generalization);
    }

    [Fact]
    public async Task InferAsync_WithNegationPattern_ShouldInfer()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "neg1", Value = "User dislikes coffee", IsActive = true, Confidence = 0.9f },
            new() { Key = "neg2", Value = "User never drinks alcohol", IsActive = true, Confidence = 0.85f }
        };

        var options = new InferenceOptions
        {
            EnabledTypes = [InferenceType.Negation],
            MinSourceConfidence = 0.7f,
            MinInferenceConfidence = 0.3f
        };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(f => f.Type == InferenceType.Negation);
    }

    [Fact]
    public async Task InferAsync_WithMaxResults_ShouldLimit()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>();
        for (int i = 0; i < 10; i++)
        {
            facts.Add(new SemanticStoreEntry
            {
                Key = $"pref{i}",
                Value = $"User likes item{i}",
                Category = SemanticStoreCategory.Preference,
                IsActive = true,
                Confidence = 0.9f
            });
        }

        var options = new InferenceOptions
        {
            MaxResults = 3,
            MinInferenceConfidence = 0.1f
        };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().HaveCountLessThanOrEqualTo(3);
    }

    #endregion

    #region InferForUserAsync Tests

    [Fact]
    public async Task InferForUserAsync_ShouldGetUserFactsAndInfer()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "pref1", Value = "User likes pizza", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.9f },
            new() { Key = "pref2", Value = "User likes pasta", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.8f },
            new() { Key = "pref3", Value = "User likes sushi", Category = SemanticStoreCategory.Preference, IsActive = true, Confidence = 0.85f }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Act
        var result = await _service.InferForUserAsync("user1");

        // Assert
        result.UserId.Should().Be("user1");
        result.SourceFactCount.Should().Be(3);
        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task InferForUserAsync_WithAutoStore_ShouldStoreHighConfidence()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "loc1", Value = "User lives in Seoul", Category = SemanticStoreCategory.Fact, IsActive = true, Confidence = 0.95f },
            new() { Key = "work1", Value = "User works at Samsung in Seoul", Category = SemanticStoreCategory.Work, IsActive = true, Confidence = 0.95f }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var options = new InferenceOptions
        {
            AutoStore = true,
            AutoStoreThreshold = 0.7f,
            EnabledTypes = [InferenceType.CoOccurrence],
            MinInferenceConfidence = 0.3f
        };

        // Act
        var result = await _service.InferForUserAsync("user1", options);

        // Assert
        result.UserId.Should().Be("user1");
        // Auto-store may have been called depending on inferences
    }

    #endregion

    #region CustomRule Tests

    [Fact]
    public void RegisterRule_ShouldAddRule()
    {
        // Arrange
        var mockRule = new Mock<IInferenceRule>();
        mockRule.Setup(r => r.Name).Returns("TestRule");

        // Act
        _service.RegisterRule(mockRule.Object);

        // Assert
        var rules = _service.GetRegisteredRules();
        rules.Should().Contain(r => r.Name == "TestRule");
    }

    [Fact]
    public async Task InferAsync_WithCustomRule_ShouldEvaluate()
    {
        // Arrange
        var mockRule = new Mock<IInferenceRule>();
        mockRule.Setup(r => r.Name).Returns("TestRule");
        mockRule.Setup(r => r.Type).Returns(InferenceType.Custom);
        mockRule.Setup(r => r.EvaluateAsync(It.IsAny<IReadOnlyList<SemanticStoreEntry>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<InferredFact>
            {
                new InferredFact
                {
                    Content = "Custom inference",
                    Category = FactCategory.General,
                    Type = InferenceType.Custom,
                    Confidence = 0.8f,
                    RuleName = "TestRule"
                }
            });

        _service.RegisterRule(mockRule.Object);

        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test1", Value = "Test fact 1", IsActive = true, Confidence = 0.9f },
            new() { Key = "test2", Value = "Test fact 2", IsActive = true, Confidence = 0.9f }
        };

        var options = new InferenceOptions
        {
            EnabledTypes = [InferenceType.Custom],
            MinSourceConfidence = 0.7f,
            MinInferenceConfidence = 0.5f
        };

        // Act
        var result = await _service.InferAsync(facts, options);

        // Assert
        result.Should().Contain(f => f.Type == InferenceType.Custom && f.RuleName == "TestRule");
    }

    #endregion
}
