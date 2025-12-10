using MemoryIndexer.Intelligence.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests.Security;

public class RegexPiiDetectorTests
{
    private readonly RegexPiiDetector _detector;

    public RegexPiiDetectorTests()
    {
        var logger = new Mock<ILogger<RegexPiiDetector>>();
        _detector = new RegexPiiDetector(logger.Object);
    }

    #region Email Detection

    [Fact]
    public async Task DetectAsync_Email_DetectsValidEmails()
    {
        // Arrange
        var text = "Contact me at john.doe@example.com or support@company.org";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.True(result.Count >= 2);
        Assert.All(result.Where(e => e.Type == PiiType.Email), e => Assert.True(e.Confidence >= 0.9f));
    }

    [Fact]
    public async Task DetectAsync_Email_HighConfidence()
    {
        // Arrange
        var text = "Email: test@domain.com";

        // Act
        var result = await _detector.DetectAsync(text, 0.9f);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.Email && e.Text == "test@domain.com");
    }

    #endregion

    #region SSN Detection

    [Fact]
    public async Task DetectAsync_Ssn_DetectsValidFormats()
    {
        // Arrange
        var text = "SSN: 123-45-6789 or 987654321";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.Ssn);
    }

    [Fact]
    public async Task DetectAsync_Ssn_RejectsInvalidSsn()
    {
        // Arrange - 000 prefix is invalid
        var text = "Invalid: 000-12-3456";

        // Act
        var result = await _detector.DetectAsync(text, 0.8f);

        // Assert - should have lower confidence for invalid SSN
        var ssn = result.FirstOrDefault(e => e.Type == PiiType.Ssn && e.Text.Contains("000"));
        if (ssn != null)
        {
            Assert.True(ssn.Confidence < 0.7f); // Lower confidence for invalid
        }
    }

    #endregion

    #region Credit Card Detection

    [Fact]
    public async Task DetectAsync_CreditCard_DetectsVisaFormat()
    {
        // Arrange - Valid Visa test number
        var text = "Card: 4111111111111111";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.CreditCard);
    }

    [Fact]
    public async Task DetectAsync_CreditCard_ValidatesLuhn()
    {
        // Arrange - Number that passes Luhn
        var text = "Card: 4532015112830366";

        // Act
        var result = await _detector.DetectAsync(text, 0.8f);

        // Assert
        var card = result.FirstOrDefault(e => e.Type == PiiType.CreditCard);
        Assert.NotNull(card);
    }

    #endregion

    #region Phone Number Detection

    [Fact]
    public async Task DetectAsync_PhoneNumber_DetectsUsFormats()
    {
        // Arrange
        var text = "Call (555) 123-4567 or 800-555-1234";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.PhoneNumber);
    }

    [Fact]
    public async Task DetectAsync_PhoneNumber_DetectsInternational()
    {
        // Arrange
        var text = "International: +1-555-123-4567";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.PhoneNumber);
    }

    #endregion

    #region IP Address Detection

    [Fact]
    public async Task DetectAsync_IpAddress_DetectsIPv4()
    {
        // Arrange
        var text = "Server IP: 192.168.1.100";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.Contains(result, e => e.Type == PiiType.IpAddress);
    }

    [Fact]
    public async Task DetectAsync_IpAddress_RejectsInvalidIp()
    {
        // Arrange - Invalid octets
        var text = "Invalid: 999.999.999.999";

        // Act
        var result = await _detector.DetectAsync(text, 0.8f);

        // Assert - Should not detect as valid IP with high confidence
        var ip = result.FirstOrDefault(e => e.Type == PiiType.IpAddress && e.Text == "999.999.999.999");
        Assert.True(ip == null || ip.Confidence < 0.8f);
    }

    #endregion

    #region Redaction

    [Fact]
    public async Task RedactAsync_ReplacesWithTypeLabels()
    {
        // Arrange
        var text = "Email: test@example.com, Phone: 555-123-4567";

        // Act
        var result = await _detector.RedactAsync(text);

        // Assert
        Assert.True(result.WasRedacted);
        Assert.Contains("[EMAIL]", result.RedactedText);
        Assert.DoesNotContain("test@example.com", result.RedactedText);
    }

    [Fact]
    public async Task RedactAsync_FullMask_MasksWithCharacters()
    {
        // Arrange
        var text = "Email: test@test.com";
        var options = new PiiRedactionOptions
        {
            Mode = RedactionMode.FullMask,
            MaskCharacter = '*'
        };

        // Act
        var result = await _detector.RedactAsync(text, options);

        // Assert
        Assert.True(result.WasRedacted);
        Assert.Contains("*", result.RedactedText);
    }

    [Fact]
    public async Task RedactAsync_Hash_CreatesHashReplacement()
    {
        // Arrange
        var text = "Email: secret@test.com";
        var options = new PiiRedactionOptions
        {
            Mode = RedactionMode.Hash
        };

        // Act
        var result = await _detector.RedactAsync(text, options);

        // Assert
        Assert.True(result.WasRedacted);
        Assert.Contains("[HASH:", result.RedactedText);
    }

    [Fact]
    public async Task RedactAsync_Remove_RemovesEntirely()
    {
        // Arrange
        var text = "Email: test@test.com here";
        var options = new PiiRedactionOptions
        {
            Mode = RedactionMode.Remove
        };

        // Act
        var result = await _detector.RedactAsync(text, options);

        // Assert
        Assert.True(result.WasRedacted);
        Assert.DoesNotContain("test@test.com", result.RedactedText);
        Assert.Contains("Email:", result.RedactedText);
    }

    #endregion

    #region ContainsPii

    [Fact]
    public async Task ContainsPiiAsync_ReturnsTrue_WhenPiiPresent()
    {
        // Arrange
        var text = "My email is user@example.com";

        // Act
        var result = await _detector.ContainsPiiAsync(text);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ContainsPiiAsync_ReturnsFalse_WhenNoPii()
    {
        // Arrange
        var text = "This is a normal text without any personal information.";

        // Act
        var result = await _detector.ContainsPiiAsync(text, 0.8f);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DetectAsync_EmptyText_ReturnsEmpty()
    {
        // Act
        var result = await _detector.DetectAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectAsync_NullText_ReturnsEmpty()
    {
        // Act
        var result = await _detector.DetectAsync(null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DetectAsync_MultiplePiiTypes_DetectsAll()
    {
        // Arrange
        var text = "Contact John Smith at john@example.com, SSN: 123-45-6789, Card: 4111111111111111";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert
        Assert.True(result.Count >= 3);
        Assert.Contains(result, e => e.Type == PiiType.Email);
        Assert.Contains(result, e => e.Type == PiiType.Ssn);
        Assert.Contains(result, e => e.Type == PiiType.CreditCard);
    }

    [Fact]
    public async Task DetectAsync_OverlappingPatterns_RemovesDuplicates()
    {
        // Arrange
        var text = "Email test@test.com";

        // Act
        var result = await _detector.DetectAsync(text);

        // Assert - Should not have overlapping entities for same text
        var emails = result.Where(e => e.Type == PiiType.Email).ToList();
        if (emails.Count > 1)
        {
            // Check no overlaps
            for (var i = 0; i < emails.Count - 1; i++)
            {
                Assert.True(emails[i].EndIndex <= emails[i + 1].StartIndex);
            }
        }
    }

    #endregion
}
