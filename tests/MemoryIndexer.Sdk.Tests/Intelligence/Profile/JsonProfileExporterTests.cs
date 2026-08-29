using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text.Json;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// Tests for JsonProfileExporter.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class JsonProfileExporterTests
{
    private readonly IArchiveStore _mockArchiveStore;
    private readonly ILogger<JsonProfileExporter> _mockLogger;
    private readonly JsonProfileExporter _exporter;

    public JsonProfileExporterTests()
    {
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _mockLogger = Substitute.For<ILogger<JsonProfileExporter>>();

        _exporter = new JsonProfileExporter(
            _mockArchiveStore,
            _mockLogger);
    }

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_ShouldExportUserProfile()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "name",
                Value = "John Doe",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.9f,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Key = "preference:color",
                Value = "Blue is favorite color",
                Category = SemanticStoreCategory.Preference,
                Confidence = 0.8f,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                UpdatedAt = DateTime.UtcNow
            }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _exporter.ExportAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNullOrEmpty();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.UserId.Should().Be("user1");
        result.Metadata.FactCount.Should().Be(2);
        result.Metadata.Format.Should().Be("json");
    }

    [Fact]
    public async Task ExportAsync_ShouldIncludeChecksum()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test", Value = "Test value", IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _exporter.ExportAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Metadata!.Checksum.Should().NotBeNullOrEmpty();
        result.Metadata.Checksum.Should().HaveLength(64); // SHA256 hex string
    }

    [Fact]
    public async Task ExportAsync_WithCategoryFilter_ShouldFilterFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Fact value", Category = SemanticStoreCategory.Fact, IsActive = true },
            new() { Key = "pref1", Value = "Preference value", Category = SemanticStoreCategory.Preference, IsActive = true },
            new() { Key = "skill1", Value = "Skill value", Category = SemanticStoreCategory.Skill, IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions
        {
            IncludeCategories = [SemanticStoreCategory.Fact, SemanticStoreCategory.Preference]
        };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.FactCount.Should().Be(2); // Only Fact and Preference
    }

    [Fact]
    public async Task ExportAsync_WithExcludeCategories_ShouldExcludeFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Fact value", Category = SemanticStoreCategory.Fact, IsActive = true },
            new() { Key = "pref1", Value = "Preference value", Category = SemanticStoreCategory.Preference, IsActive = true },
            new() { Key = "skill1", Value = "Skill value", Category = SemanticStoreCategory.Skill, IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions
        {
            ExcludeCategories = [SemanticStoreCategory.Skill]
        };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.FactCount.Should().Be(2); // Fact and Preference only
    }

    [Fact]
    public async Task ExportAsync_WithDateRange_ShouldFilterByDate()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "old", Value = "Old fact", UpdatedAt = now.AddDays(-30), IsActive = true },
            new() { Key = "recent", Value = "Recent fact", UpdatedAt = now.AddDays(-5), IsActive = true },
            new() { Key = "new", Value = "New fact", UpdatedAt = now, IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions
        {
            Since = now.AddDays(-10),
            Until = now.AddDays(-1)
        };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.FactCount.Should().Be(1); // Only "recent" fact
    }

    [Fact]
    public async Task ExportAsync_WithIncludeArchived_ShouldIncludeInactiveFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "active", Value = "Active fact", IsActive = true },
            new() { Key = "archived", Value = "Archived fact", IsActive = false }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions { IncludeArchived = true };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.ActiveFactCount.Should().Be(1);
        result.Metadata.ArchivedFactCount.Should().Be(1);
        result.Metadata.FactCount.Should().Be(2);
    }

    [Fact]
    public async Task ExportAsync_WithoutIncludeArchived_ShouldExcludeInactiveFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "active", Value = "Active fact", IsActive = true },
            new() { Key = "archived", Value = "Archived fact", IsActive = false }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions { IncludeArchived = false };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Metadata!.ActiveFactCount.Should().Be(1);
        result.Metadata.ArchivedFactCount.Should().Be(0);
    }

    [Fact]
    public async Task ExportAsync_WithRedaction_ShouldRedactSensitiveData()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "email", Value = "User email is john@example.com", IsActive = true },
            new() { Key = "phone", Value = "Phone number: 123-456-7890", IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions
        {
            RedactSensitive = true,
            RedactionPatterns = [@"[\w\.-]+@[\w\.-]+\.\w+", @"\d{3}-\d{3}-\d{4}"]
        };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Contain("[REDACTED]");
        result.Data.Should().NotContain("john@example.com");
        result.Data.Should().NotContain("123-456-7890");
    }

    [Fact]
    public async Task ExportAsync_ShouldReturnValidJson()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test", Value = "Test value", IsActive = true, Confidence = 0.9f }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _exporter.ExportAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();

        // Verify valid JSON
        var parseAction = () => JsonDocument.Parse(result.Data!);
        parseAction.Should().NotThrow();
    }

    [Fact]
    public async Task ExportAsync_WithMetadata_ShouldIncludeFactMetadata()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "test",
                Value = "Test value",
                IsActive = true,
                Metadata = new Dictionary<string, string> { { "source", "manual" }, { "verified", "true" } }
            }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        var options = new ProfileExportOptions { IncludeMetadata = true };

        // Act
        var result = await _exporter.ExportAsync("user1", options, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Contain("source");
        result.Data.Should().Contain("manual");
    }

    [Fact]
    public async Task ExportAsync_WithBiTemporalData_ShouldIncludeVersionInfo()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "fact",
                Value = "Updated value",
                IsActive = true,
                Version = 2,
                ValidFrom = DateTime.UtcNow.AddDays(-5),
                ValidTo = null,
                SupersedesKey = "fact_v1"
            }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _exporter.ExportAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Contain("version");
        result.Data.Should().Contain("validFrom");
        result.Data.Should().Contain("supersedesKey");
    }

    #endregion

    #region ExportToStreamAsync Tests

    [Fact]
    public async Task ExportToStreamAsync_ShouldWriteToStream()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test", Value = "Test value", IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        using var stream = new MemoryStream();

        // Act
        var metadata = await _exporter.ExportToStreamAsync("user1", stream, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        metadata.Should().NotBeNull();
        stream.Length.Should().BeGreaterThan(0);

        // Verify stream contains valid JSON
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        var parseAction = () => JsonDocument.Parse(json);
        parseAction.Should().NotThrow();
    }

    [Fact]
    public async Task ExportToStreamAsync_WithEmptyProfile_ShouldThrow()
    {
        // Arrange
        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("User not found"));

        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _exporter.ExportToStreamAsync("user1", stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region Format Tests

    [Fact]
    public void Format_ShouldReturnJson()
    {
        // Assert
        _exporter.Format.Should().Be("json");
    }

    #endregion

    #region FactsByCategory Tests

    [Fact]
    public async Task ExportAsync_ShouldIncludeFactsByCategory()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Category = SemanticStoreCategory.Fact, Value = "V1", IsActive = true },
            new() { Key = "fact2", Category = SemanticStoreCategory.Fact, Value = "V2", IsActive = true },
            new() { Key = "pref1", Category = SemanticStoreCategory.Preference, Value = "V3", IsActive = true },
            new() { Key = "skill1", Category = SemanticStoreCategory.Skill, Value = "V4", IsActive = true }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(facts);

        // Act
        var result = await _exporter.ExportAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Metadata!.FactsByCategory.Should().ContainKey("Fact");
        result.Metadata.FactsByCategory["Fact"].Should().Be(2);
        result.Metadata.FactsByCategory.Should().ContainKey("Preference");
        result.Metadata.FactsByCategory["Preference"].Should().Be(1);
        result.Metadata.FactsByCategory.Should().ContainKey("Skill");
        result.Metadata.FactsByCategory["Skill"].Should().Be(1);
    }

    #endregion
}
