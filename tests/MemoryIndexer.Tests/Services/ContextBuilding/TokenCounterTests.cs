using FluentAssertions;
using MemoryIndexer.Services.TokenCounting;
using Xunit;

namespace MemoryIndexer.Tests.Services.ContextBuilding;

public class TokenCounterTests
{
    private readonly ApproximateTokenCounter _counter = new();

    // ApproximateTokenCounter uses Math.Ceiling(text.Length / 4.0)
    [Theory]
    [InlineData("", 0)]
    [InlineData("Hi", 1)]        // 2 / 4 = 0.5 → ceiling = 1
    [InlineData("Hello", 2)]     // 5 / 4 = 1.25 → ceiling = 2
    [InlineData("Hello World", 3)] // 11 / 4 = 2.75 → ceiling = 3
    [InlineData("This is a test sentence with multiple words.", 11)] // 44 / 4 = 11.0 → ceiling = 11
    public void Count_ShouldReturnApproximateTokens(string text, int expectedTokens)
    {
        // Act
        var result = _counter.Count(text);

        // Assert
        result.Should().Be(expectedTokens);
    }

    [Fact]
    public void Count_WithNull_ShouldReturnZero()
    {
        // Act
        string? nullText = null;
        var result = _counter.Count(nullText!);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void Count_Enumerable_ShouldSumTokens()
    {
        // Arrange
        var texts = new[] { "Hello World", "This is a test" };

        // Act
        var result = _counter.Count(texts);

        // Assert
        // "Hello World" = 3 tokens (11/4 ceiling), "This is a test" = 4 tokens (14/4 ceiling) = 7 total
        result.Should().Be(7);
    }

    [Theory]
    [InlineData("Hello World", 1, "Hell...")]  // 1 token = 4 chars, "Hell" + "..."
    [InlineData("Hello World", 2, "Hello Wo...")]  // 2 tokens = 8 chars, no word boundary (space at 62.5% < 80%)
    [InlineData("Hello World", 10, "Hello World")] // More tokens than needed, no truncation
    public void Truncate_ShouldLimitToMaxTokens(string text, int maxTokens, string expected)
    {
        // Act
        var result = _counter.Truncate(text, maxTokens);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Truncate_WithZeroTokens_ShouldReturnEmpty()
    {
        // Act
        var result = _counter.Truncate("Hello World", 0);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Truncate_WithNullText_ShouldReturnEmpty()
    {
        // Act
        var result = _counter.Truncate(null!, 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithCustomCharsPerToken_ShouldUseValue()
    {
        // Arrange
        var counter = new ApproximateTokenCounter(charsPerToken: 2.0);

        // Act - "Hello" = 5 chars / 2 = 2.5 → ceiling = 3
        var result = counter.Count("Hello");

        // Assert
        result.Should().Be(3);
    }

    [Fact]
    public void Constructor_WithInvalidCharsPerToken_ShouldThrow()
    {
        // Act & Assert
        var act = () => new ApproximateTokenCounter(charsPerToken: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("claude-3.5-sonnet")]
    [InlineData("")]
    public void SupportsModel_ShouldReturnTrue_ForAnyModel(string modelId)
    {
        // Approximate counter is model-agnostic, supports all models
        _counter.SupportsModel(modelId).Should().BeTrue();
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("claude-3.5-sonnet")]
    [InlineData("")]
    public void IsApproximate_ShouldReturnTrue_ForAnyModel(string modelId)
    {
        // Character-based heuristic is always approximate
        _counter.IsApproximate(modelId).Should().BeTrue();
    }

    [Fact]
    public void ImplementsITokenCounterInterface()
    {
        // Verify that ApproximateTokenCounter implements MemoryIndexer.Interfaces.ITokenCounter
        _counter.Should().BeAssignableTo<MemoryIndexer.Interfaces.ITokenCounter>();

        // Core counting contract works through the interface
        ((MemoryIndexer.Interfaces.ITokenCounter)_counter).Count("Hello").Should().Be(2);
        ((MemoryIndexer.Interfaces.ITokenCounter)_counter).SupportsModel("gpt-4o").Should().BeTrue();
    }
}
