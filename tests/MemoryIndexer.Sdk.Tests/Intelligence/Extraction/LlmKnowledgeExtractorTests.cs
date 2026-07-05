
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Extraction;

/// <summary>
/// Tests for LlmKnowledgeExtractor (Phase 25).
/// LLM-based knowledge extraction from Q&A exchanges.
/// </summary>
public sealed class LlmKnowledgeExtractorTests
{
    private readonly ITextCompletionService _mockCompletionService;
    private readonly LlmKnowledgeExtractor _extractor;

    public LlmKnowledgeExtractorTests()
    {
        _mockCompletionService = Substitute.For<ITextCompletionService>();
        _extractor = new LlmKnowledgeExtractor(
            _mockCompletionService,
            NullLogger<LlmKnowledgeExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_ValidResponse_ShouldExtractFacts()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": [
                {
                  "content": "The ocean is blue",
                  "confidence": 0.85,
                  "importance": 0.7,
                  "source": "LLM extraction"
                }
              ]
            }
            """;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is blue", facts[0].Content);
        Assert.Equal(0.85f, facts[0].Confidence);
        Assert.Equal(0.7f, facts[0].Importance);
        Assert.Equal("LLM extraction", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_MultipleFacts_ShouldExtractAll()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it a liquid?",
            Answer = "yes",
            Subject = "water",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": [
                {
                  "content": "Water is a liquid",
                  "confidence": 0.9,
                  "importance": 0.8,
                  "source": "category assertion"
                },
                {
                  "content": "Water flows freely",
                  "confidence": 0.85,
                  "importance": 0.6,
                  "source": "property inference"
                }
              ]
            }
            """;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Equal(2, facts.Count);
        Assert.Equal("Water is a liquid", facts[0].Content);
        Assert.Equal("Water flows freely", facts[1].Content);
    }

    [Fact]
    public async Task ExtractAsync_ResponseWithMarkdown_ShouldExtractJSON()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Can it move?",
            Answer = "yes",
            Subject = "clouds",
            UserId = "test-user"
        };

        var llmResponse = """
            Here is the extracted knowledge:

            ```json
            {
              "facts": [
                {
                  "content": "Clouds can move",
                  "confidence": 0.8,
                  "importance": 0.6,
                  "source": "capability assertion"
                }
              ]
            }
            ```
            """;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("Clouds can move", facts[0].Content);
    }

    [Fact]
    public async Task ExtractAsync_NoJSON_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "What is it?",
            Answer = "I don't know",
            Subject = "mystery object",
            UserId = "test-user"
        };

        var llmResponse = "I cannot extract facts from this uncertain response.";

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_InvalidJSON_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "sky",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": [
                "invalid format"
              ]
            }
            """;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_EmptyFactsArray_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it unclear?",
            Answer = "maybe",
            Subject = "something",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": []
            }
            """;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(llmResponse);

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_CompletionServiceThrows_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "ocean",
            UserId = "test-user"
        };

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("LLM service unavailable"));

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_ShouldUseCorrectOptions()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "ocean",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": [
                {
                  "content": "The ocean is blue",
                  "confidence": 0.85,
                  "importance": 0.7,
                  "source": "LLM"
                }
              ]
            }
            """;

        TextCompletionOptions? capturedOptions = null;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.ArgAt<TextCompletionOptions?>(1);
                return llmResponse;
            });

        // Act
        await _extractor.ExtractAsync(context);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(0.1f, capturedOptions.Temperature);  // Low temp for deterministic extraction
        Assert.Equal(500, capturedOptions.MaxTokens);
        Assert.Contains("###", capturedOptions.StopSequences ?? Array.Empty<string>());
    }

    [Fact]
    public async Task ExtractAsync_ShouldIncludeContextInPrompt()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Does it have waves?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        var llmResponse = """
            {
              "facts": [
                {
                  "content": "The ocean has waves",
                  "confidence": 0.9,
                  "importance": 0.7,
                  "source": "LLM"
                }
              ]
            }
            """;

        string? capturedPrompt = null;

        _mockCompletionService.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<TextCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedPrompt = callInfo.ArgAt<string>(0);
                return llmResponse;
            });

        // Act
        await _extractor.ExtractAsync(context);

        // Assert
        Assert.NotNull(capturedPrompt);
        Assert.Contains("Does it have waves?", capturedPrompt);
        Assert.Contains("yes", capturedPrompt);
        Assert.Contains("the ocean", capturedPrompt);
    }
}
