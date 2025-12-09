using MemoryIndexer.Core.Models;
using MemoryIndexer.Intelligence.Scoring;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class ImportanceAnalyzerTests
{
    private readonly ImportanceAnalyzer _analyzer;

    public ImportanceAnalyzerTests()
    {
        _analyzer = new ImportanceAnalyzer(NullLogger<ImportanceAnalyzer>.Instance);
    }

    [Fact]
    public void AnalyzeImportance_HighImportanceKeywords_ShouldReturnHighScore()
    {
        // Arrange
        var content = "This is a critical and important decision that we must remember";

        // Act
        var score = _analyzer.AnalyzeImportance(content);

        // Assert
        Assert.True(score >= 0.7f, $"Score {score} should be >= 0.7 for high importance keywords");
    }

    [Fact]
    public void AnalyzeImportance_LowImportanceContent_ShouldReturnLowScore()
    {
        // Arrange
        var content = "Hello, okay, thanks";

        // Act
        var score = _analyzer.AnalyzeImportance(content);

        // Assert
        Assert.True(score <= 0.5f, $"Score {score} should be <= 0.5 for low importance content");
    }

    [Fact]
    public void AnalyzeImportance_ContentWithDates_ShouldIncreaseScore()
    {
        // Arrange
        var contentWithDate = "Meeting scheduled for 2024-12-15";
        var contentWithoutDate = "Meeting scheduled for sometime";

        // Act
        var scoreWithDate = _analyzer.AnalyzeImportance(contentWithDate);
        var scoreWithoutDate = _analyzer.AnalyzeImportance(contentWithoutDate);

        // Assert
        Assert.True(scoreWithDate > scoreWithoutDate,
            $"Score with date ({scoreWithDate}) should be higher than without ({scoreWithoutDate})");
    }

    [Fact]
    public void AnalyzeImportance_ContentWithEmail_ShouldIncreaseScore()
    {
        // Arrange
        var contentWithEmail = "Contact me at user@example.com";
        var contentWithoutEmail = "Contact me later";

        // Act
        var scoreWithEmail = _analyzer.AnalyzeImportance(contentWithEmail);
        var scoreWithoutEmail = _analyzer.AnalyzeImportance(contentWithoutEmail);

        // Assert
        Assert.True(scoreWithEmail > scoreWithoutEmail,
            $"Score with email ({scoreWithEmail}) should be higher than without ({scoreWithoutEmail})");
    }

    [Fact]
    public void AnalyzeImportance_ProceduralType_ShouldHaveBonus()
    {
        // Arrange
        var content = "How to configure the system settings";

        // Act
        var proceduralScore = _analyzer.AnalyzeImportance(content, MemoryType.Procedural);
        var episodicScore = _analyzer.AnalyzeImportance(content, MemoryType.Episodic);

        // Assert
        Assert.True(proceduralScore > episodicScore,
            $"Procedural score ({proceduralScore}) should be higher than episodic ({episodicScore})");
    }

    [Fact]
    public void AnalyzeImportance_ContentWithPassword_ShouldReturnHighScore()
    {
        // Arrange
        var content = "The password for the server is stored securely";

        // Act
        var score = _analyzer.AnalyzeImportance(content);

        // Assert
        Assert.True(score >= 0.6f, $"Score {score} should be >= 0.6 for security-related content");
    }

    [Fact]
    public void AnalyzeImportance_StructuredContent_ShouldIncreaseScore()
    {
        // Arrange
        var bulletContent = @"Key points:
- First item
- Second item
- Third item";
        var plainContent = "First item. Second item. Third item.";

        // Act
        var bulletScore = _analyzer.AnalyzeImportance(bulletContent);
        var plainScore = _analyzer.AnalyzeImportance(plainContent);

        // Assert
        Assert.True(bulletScore > plainScore,
            $"Bullet score ({bulletScore}) should be higher than plain ({plainScore})");
    }

    [Fact]
    public void AnalyzeImportance_EmptyContent_ShouldReturnMinimumScore()
    {
        // Arrange
        var content = "";

        // Act
        var score = _analyzer.AnalyzeImportance(content);

        // Assert
        Assert.Equal(0.1f, score);
    }

    [Fact]
    public void AnalyzeBatch_ShouldProcessMultipleContents()
    {
        // Arrange
        var contents = new[]
        {
            "Critical important deadline",
            "Hello thanks",
            "Meeting at 2024-01-15 at 14:00"
        };

        // Act
        var scores = _analyzer.AnalyzeBatch(contents);

        // Assert
        Assert.Equal(3, scores.Count);
        Assert.True(scores[0] > scores[1]); // Critical > casual
        Assert.True(scores[2] > scores[1]); // Date content > casual
    }
}
