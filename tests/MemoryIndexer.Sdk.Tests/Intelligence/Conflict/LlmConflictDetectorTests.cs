using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Conflict;

/// <summary>
/// Tests for LlmConflictDetector.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
public sealed class LlmConflictDetectorTests
{
    private readonly Mock<ITextCompletionService> _mockCompletion;
    private readonly LlmConflictDetector _detector;

    public LlmConflictDetectorTests()
    {
        _mockCompletion = new Mock<ITextCompletionService>();
        _detector = new LlmConflictDetector(
            _mockCompletion.Object,
            NullLogger<LlmConflictDetector>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_ValidJsonResponse_ReturnsParsedAnalysis()
    {
        // Arrange
        var newMemory = CreateMemory("dislikes apples");
        var existingMemory = CreateMemory("likes apples");

        var llmResponse = """
            {
              "conflictType": "CONTRADICTION",
              "confidence": 0.9,
              "reasoning": "Direct contradiction in preferences",
              "recommendedAction": "MARK_CONFLICT"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Contradiction, result.ConflictType);
        Assert.Equal(0.9f, result.Confidence);
        Assert.Equal(MemoryAction.MarkConflict, result.RecommendedAction);
        Assert.Contains("contradiction", result.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_DuplicateType_ReturnsNoOp()
    {
        // Arrange
        var newMemory = CreateMemory("enjoys pizza");
        var existingMemory = CreateMemory("likes pizza");

        var llmResponse = """
            {
              "conflictType": "DUPLICATE",
              "confidence": 0.95,
              "reasoning": "Paraphrase of existing memory",
              "recommendedAction": "NO_OP"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Duplicate, result.ConflictType);
        Assert.Equal(MemoryAction.NoOp, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_RefinementType_ReturnsMerge()
    {
        // Arrange
        var newMemory = CreateMemory("loves margherita pizza");
        var existingMemory = CreateMemory("likes pizza");

        var llmResponse = """
            {
              "conflictType": "REFINEMENT",
              "confidence": 0.85,
              "reasoning": "New memory adds specific detail",
              "recommendedAction": "MERGE"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Refinement, result.ConflictType);
        Assert.Equal(MemoryAction.Merge, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_UpdateType_ReturnsReplace()
    {
        // Arrange
        var newMemory = CreateMemory("age 26");
        var existingMemory = CreateMemory("age 25");

        var llmResponse = """
            {
              "conflictType": "UPDATE",
              "confidence": 0.9,
              "reasoning": "Age value updated",
              "recommendedAction": "REPLACE"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Update, result.ConflictType);
        Assert.Equal(MemoryAction.Replace, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_TemporalType_ReturnsArchive()
    {
        // Arrange
        var newMemory = CreateMemory("quit smoking in 2023");
        var existingMemory = CreateMemory("used to smoke");

        var llmResponse = """
            {
              "conflictType": "TEMPORAL",
              "confidence": 0.85,
              "reasoning": "Time-based evolution of habit",
              "recommendedAction": "ARCHIVE"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Temporal, result.ConflictType);
        Assert.Equal(MemoryAction.Archive, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_NoneType_ReturnsAdd()
    {
        // Arrange
        var newMemory = CreateMemory("age 25");
        var existingMemory = CreateMemory("likes pizza");

        var llmResponse = """
            {
              "conflictType": "NONE",
              "confidence": 0.8,
              "reasoning": "Unrelated topics",
              "recommendedAction": "ADD"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.None, result.ConflictType);
        Assert.Equal(MemoryAction.Add, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_JsonWithMarkdownCodeBlock_ParsesCorrectly()
    {
        // Arrange
        var newMemory = CreateMemory("test content");
        var existingMemory = CreateMemory("existing content");

        var llmResponse = """
            Here's my analysis:
            ```json
            {
              "conflictType": "DUPLICATE",
              "confidence": 0.9,
              "reasoning": "Same meaning",
              "recommendedAction": "NO_OP"
            }
            ```
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.Duplicate, result.ConflictType);
        Assert.Equal(0.9f, result.Confidence);
        Assert.Equal(MemoryAction.NoOp, result.RecommendedAction);
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidJson_ReturnsFallbackAnalysis()
    {
        // Arrange
        var newMemory = CreateMemory("test content");
        var existingMemory = CreateMemory("existing content");

        var llmResponse = "This is not valid JSON at all!";

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.None, result.ConflictType);
        Assert.Equal(0.5f, result.Confidence); // Fallback confidence
        Assert.Equal(MemoryAction.Add, result.RecommendedAction);
        Assert.Contains("Unable to analyze", result.Reasoning);
    }

    [Fact]
    public async Task AnalyzeAsync_CompletionFailure_ReturnsFallbackAnalysis()
    {
        // Arrange
        var newMemory = CreateMemory("test content");
        var existingMemory = CreateMemory("existing content");

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API error"));

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(ConflictType.None, result.ConflictType);
        Assert.Equal(0.5f, result.Confidence);
        Assert.Equal(MemoryAction.Add, result.RecommendedAction);
        Assert.Contains("failed", result.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("NO-OP", MemoryAction.NoOp)]
    [InlineData("NO_OP", MemoryAction.NoOp)]
    [InlineData("NOOP", MemoryAction.NoOp)]
    [InlineData("MERGE", MemoryAction.Merge)]
    [InlineData("REPLACE", MemoryAction.Replace)]
    [InlineData("ARCHIVE", MemoryAction.Archive)]
    [InlineData("MARK-CONFLICT", MemoryAction.MarkConflict)]
    [InlineData("MARK_CONFLICT", MemoryAction.MarkConflict)]
    [InlineData("MARKCONFLICT", MemoryAction.MarkConflict)]
    [InlineData("ADD", MemoryAction.Add)]
    public async Task AnalyzeAsync_MemoryActionVariations_ParsesCorrectly(
        string actionString,
        MemoryAction expectedAction)
    {
        // Arrange
        var newMemory = CreateMemory("test");
        var existingMemory = CreateMemory("test2");

        var llmResponse = $$"""
            {
              "conflictType": "DUPLICATE",
              "confidence": 0.8,
              "reasoning": "test",
              "recommendedAction": "{{actionString}}"
            }
            """;

        _mockCompletion
            .Setup(c => c.CompleteAsync(
                It.IsAny<string>(),
                It.IsAny<TextCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _detector.AnalyzeAsync(newMemory, existingMemory);

        // Assert
        Assert.Equal(expectedAction, result.RecommendedAction);
    }

    private static MemoryUnit CreateMemory(string content)
    {
        return new MemoryUnit
        {
            Content = content,
            UserId = "test-user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConfidenceScore = 0.7f,
            Embedding = new ReadOnlyMemory<float>(new float[1024])
        };
    }
}
