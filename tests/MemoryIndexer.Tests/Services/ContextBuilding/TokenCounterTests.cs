using AwesomeAssertions;
using MemoryIndexer.Services.TokenCounting;
using Xunit;

namespace MemoryIndexer.Tests.Services.ContextBuilding;

public class TokenCounterTests
{
    private readonly ApproximateTokenCounter _counter = new();

    // Each kind of character has its own ratio (fitted to o200k_base): Hangul 1.35 characters per token, Han 1.2, Kana 1.5,
    // Latin letters 7, a whitespace run 2.75. Four characters per token for everything would let a Korean budget hold about
    // two and a half times what it says.
    [Theory]
    [InlineData("회의록", 3)]       // 3 / 1.35 = 2.22
    [InlineData("会议记录", 4)]     // 4 / 1.2 = 3.33
    [InlineData("ミーティング", 4)] // 6 / 1.5 = 4
    [InlineData("회의 notes", 3)]   // 2 / 1.35 + 1 / 2.75 + 5 / 7 = 2.56
    public void Count_CountsEachKindOfCharacterAtItsOwnRatio(string text, int expectedTokens)
    {
        _counter.Count(text).Should().Be(expectedTokens);
    }

    [Fact]
    public void Truncate_CutsKoreanTextAtTheSameTokenBudget()
    {
        // 5 Hangul = 3.70 tokens, 6 = 4.44
        _counter.Truncate("가나다라마바사아자차", 4).Should().Be("가나다라마...");
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hi", 1)]          // 2 / 7 = 0.29
    [InlineData("Hello", 1)]       // 5 / 7 = 0.71
    [InlineData("Hello World", 2)] // 10 / 7 + 1 / 2.75 = 1.79
    [InlineData("This is a test sentence with multiple words.", 9)] // 36 / 7 + 7 / 2.75 + 1 / 1.25 = 8.49
    [InlineData("{\"a\": 1}", 6)]  // 5 / 1.25 + 1 / 7 + 1 / 2.75 + 1 / 1.3 = 5.28 — braces and quotes are about a token each
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
        // "Hello World" = 2 tokens, "This is a test" = 11 / 7 + 3 / 2.75 = 2.66 → 3
        result.Should().Be(5);
    }

    [Theory]
    [InlineData("Hello World", 1, "Hello...")]  // "Hello" = 0.71, the space would make it 1.08
    [InlineData("Hello wonderful World", 2, "Hello wonder...")]  // 1.08 + 6 letters = 1.94; the space at 5 of 12 is too far back for a word cut
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
        ((MemoryIndexer.Interfaces.ITokenCounter)_counter).Count("Hello").Should().Be(1);
        ((MemoryIndexer.Interfaces.ITokenCounter)_counter).SupportsModel("gpt-4o").Should().BeTrue();
    }
}
