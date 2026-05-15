using Flux.Abstractions;
using MemoryIndexer.Interfaces;
using ITextCompletionService = Flux.Abstractions.ITextCompletionService;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Conflict;

/// <summary>
/// Tests for RecencyWeightedResolver.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
public sealed class RecencyWeightedResolverTests
{
    private readonly ITextCompletionService _mockCompletion;
    private readonly LlmConflictDetector _detector;
    private readonly RecencyWeightedResolver _resolver;

    public RecencyWeightedResolverTests()
    {
        _mockCompletion = Substitute.For<ITextCompletionService>();
        _detector = new LlmConflictDetector(
            _mockCompletion,
            NullLogger<LlmConflictDetector>.Instance);

        _resolver = new RecencyWeightedResolver(
            _detector,
            NullLogger<RecencyWeightedResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_NoSimilarMemories_ReturnsAdd()
    {
        // Arrange
        var newMemory = CreateMemory("New content", DateTime.UtcNow);
        var similarMemories = Array.Empty<MemoryUnit>();

        // Act
        var result = await _resolver.ResolveAsync(newMemory, similarMemories);

        // Assert
        Assert.Equal(MemoryAction.Add, result.Action);
        Assert.Equal(ConflictType.None, result.ConflictType);
        Assert.Equal(1.0f, result.Confidence);
        Assert.Contains("No similar memories", result.Reasoning);
    }

    [Fact]
    public async Task ResolveAsync_DuplicateType_UsesLlmRecommendation()
    {
        // Arrange
        var newMemory = CreateMemory("likes pizza", DateTime.UtcNow);
        var existingMemory = CreateMemory("enjoys pizza", DateTime.UtcNow.AddDays(-1));

        var llmResponse = """
            {
              "conflictType": "DUPLICATE",
              "confidence": 0.95,
              "reasoning": "Paraphrase of existing memory",
              "recommendedAction": "NO_OP"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        Assert.Equal(MemoryAction.NoOp, result.Action);
        Assert.Equal(ConflictType.Duplicate, result.ConflictType);
        Assert.Equal(0.95f, result.Confidence);
        Assert.Null(result.TargetMemoryId);
    }

    [Fact]
    public async Task ResolveAsync_UpdateType_AlwaysReplace()
    {
        // Arrange
        var newMemory = CreateMemory("age 26", DateTime.UtcNow);
        var existingMemory = CreateMemory("age 25", DateTime.UtcNow.AddDays(-30));

        var llmResponse = """
            {
              "conflictType": "UPDATE",
              "confidence": 0.9,
              "reasoning": "Age updated from 25 to 26",
              "recommendedAction": "REPLACE"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        Assert.Equal(MemoryAction.Replace, result.Action);
        Assert.Equal(ConflictType.Update, result.ConflictType);
        Assert.Equal(existingMemory.Id.ToString(), result.TargetMemoryId);
    }

    [Fact]
    public async Task ResolveAsync_TemporalType_ArchiveOld()
    {
        // Arrange
        var newMemory = CreateMemory("quit smoking in 2023", DateTime.UtcNow);
        var existingMemory = CreateMemory("used to smoke", DateTime.UtcNow.AddYears(-2));

        var llmResponse = """
            {
              "conflictType": "TEMPORAL",
              "confidence": 0.85,
              "reasoning": "Temporal evolution detected",
              "recommendedAction": "ARCHIVE"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        Assert.Equal(MemoryAction.Archive, result.Action);
        Assert.Equal(ConflictType.Temporal, result.ConflictType);
        Assert.Equal(existingMemory.Id.ToString(), result.TargetMemoryId);
        Assert.Contains("preserving historical context", result.Reasoning);
    }

    [Fact]
    public async Task ResolveAsync_Contradiction_NewSignificantlyStronger_Replace()
    {
        // Arrange
        // New: recent (today), high confidence (0.9)
        var newMemory = CreateMemory("dislikes apples", DateTime.UtcNow, confidence: 0.9f);

        // Existing: old (30 days ago), low confidence (0.5)
        var existingMemory = CreateMemory("likes apples", DateTime.UtcNow.AddDays(-30), confidence: 0.5f);

        var llmResponse = """
            {
              "conflictType": "CONTRADICTION",
              "confidence": 0.8,
              "reasoning": "Contradicting preference detected",
              "recommendedAction": "MARK_CONFLICT"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        // New score: 1.0 (recent) * 0.9 = 0.9
        // Existing score: ~0.05 (30 days old) * 0.5 = ~0.025
        // Ratio: 0.9 / 0.025 = 36x > 1.2 threshold
        Assert.Equal(MemoryAction.Replace, result.Action);
        Assert.Equal(ConflictType.Contradiction, result.ConflictType);
        Assert.Equal(existingMemory.Id.ToString(), result.TargetMemoryId);
        Assert.Contains("score advantage", result.Reasoning);
    }

    [Fact]
    public async Task ResolveAsync_Contradiction_ExistingSignificantlyStronger_NoOp()
    {
        // Arrange
        // New: old (30 days ago), low confidence (0.5)
        var newMemory = CreateMemory("dislikes apples", DateTime.UtcNow.AddDays(-30), confidence: 0.5f);

        // Existing: recent (today), high confidence (0.9)
        var existingMemory = CreateMemory("likes apples", DateTime.UtcNow, confidence: 0.9f);

        var llmResponse = """
            {
              "conflictType": "CONTRADICTION",
              "confidence": 0.8,
              "reasoning": "Contradicting preference detected",
              "recommendedAction": "MARK_CONFLICT"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        // New score: ~0.05 * 0.5 = ~0.025
        // Existing score: 1.0 * 0.9 = 0.9
        // Ratio: 0.9 / 0.025 = 36x > 1.2 threshold
        Assert.Equal(MemoryAction.NoOp, result.Action);
        Assert.Equal(ConflictType.Contradiction, result.ConflictType);
        Assert.Contains("score advantage", result.Reasoning);
    }

    [Fact]
    public async Task ResolveAsync_Contradiction_ScoresTooClose_MarkConflict()
    {
        // Arrange
        // Both recent, similar confidence
        var newMemory = CreateMemory("dislikes apples", DateTime.UtcNow, confidence: 0.75f);
        var existingMemory = CreateMemory("likes apples", DateTime.UtcNow.AddHours(-1), confidence: 0.7f);

        var llmResponse = """
            {
              "conflictType": "CONTRADICTION",
              "confidence": 0.8,
              "reasoning": "Contradicting preference detected",
              "recommendedAction": "MARK_CONFLICT"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        // Scores very close (both ~0.7-0.75), ratio < 1.2
        Assert.Equal(MemoryAction.MarkConflict, result.Action);
        Assert.Equal(ConflictType.Contradiction, result.ConflictType);
        Assert.Equal(0.5f, result.Confidence); // Low confidence when unclear
        Assert.Equal(existingMemory.Id.ToString(), result.TargetMemoryId);
        Assert.Contains("too close", result.Reasoning);
    }

    [Fact]
    public async Task ResolveAsync_RefinementType_UsesLlmRecommendation()
    {
        // Arrange
        var newMemory = CreateMemory("loves margherita pizza", DateTime.UtcNow);
        var existingMemory = CreateMemory("likes pizza", DateTime.UtcNow.AddDays(-7));

        var llmResponse = """
            {
              "conflictType": "REFINEMENT",
              "confidence": 0.9,
              "reasoning": "New memory adds detail to existing",
              "recommendedAction": "MERGE"
            }
            """;

        _mockCompletion.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var result = await _resolver.ResolveAsync(newMemory, new[] { existingMemory });

        // Assert
        Assert.Equal(MemoryAction.Merge, result.Action);
        Assert.Equal(ConflictType.Refinement, result.ConflictType);
        Assert.Equal(existingMemory.Id.ToString(), result.TargetMemoryId);
    }

    private static MemoryUnit CreateMemory(
        string content,
        DateTime timestamp,
        float? confidence = null)
    {
        return new MemoryUnit
        {
            Content = content,
            UserId = "test-user",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            ConfidenceScore = confidence ?? 0.7f,
            Embedding = new ReadOnlyMemory<float>(new float[1024])
        };
    }
}
