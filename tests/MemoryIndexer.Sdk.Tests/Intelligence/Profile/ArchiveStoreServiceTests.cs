using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

public class ArchiveStoreServiceTests
{
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly SemanticStoreOptions _options;
    private readonly ArchiveStoreService _profileService;

    public ArchiveStoreServiceTests()
    {
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[768].AsMemory());

        _options = new SemanticStoreOptions
        {
            MinConfirmationCount = 3,
            MinConfidenceThreshold = 0.8f,
            ConfidenceBoostPerConfirmation = 0.1f,
            MaxEntriesPerUser = 500,
            EnableSemanticSearch = true
        };

        _profileService = new ArchiveStoreService(
            _embeddingServiceMock,
            Options.Create(_options),
            NullLogger<ArchiveStoreService>.Instance);
    }

    #region SetAsync Tests

    [Fact]
    public async Task SetAsync_NewEntry_CreatesEntry()
    {
        // Arrange
        const string userId = "user-1";
        var entry = CreateTestEntry("name", "John Doe");

        // Act
        var isUpdate = await _profileService.SetAsync(userId, entry, TestContext.Current.CancellationToken);

        // Assert
        isUpdate.Should().BeFalse();
        var retrieved = await _profileService.GetAsync(userId, "name", TestContext.Current.CancellationToken);
        retrieved.Should().NotBeNull();
        retrieved!.Value.Should().Be("John Doe");
    }

    [Fact]
    public async Task SetAsync_ExistingEntry_Updates()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("name", "John Doe"), TestContext.Current.CancellationToken);

        var updatedEntry = CreateTestEntry("name", "Jane Doe");

        // Act
        var isUpdate = await _profileService.SetAsync(userId, updatedEntry, TestContext.Current.CancellationToken);

        // Assert
        isUpdate.Should().BeTrue();
        var retrieved = await _profileService.GetAsync(userId, "name", TestContext.Current.CancellationToken);
        retrieved!.Value.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task SetAsync_GeneratesEmbedding()
    {
        // Arrange
        const string userId = "user-1";
        var entry = CreateTestEntry("skill", "Programming");

        // Act
        await _profileService.SetAsync(userId, entry, TestContext.Current.CancellationToken);

        // Assert
        await _embeddingServiceMock.Received(1).GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_NullUserId_ThrowsArgumentNullException()
    {
        // Arrange
        var entry = CreateTestEntry("key", "value");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _profileService.SetAsync(null!, entry, TestContext.Current.CancellationToken));
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ExistingEntry_ReturnsEntry()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("location", "New York"), TestContext.Current.CancellationToken);

        // Act
        var entry = await _profileService.GetAsync(userId, "location", TestContext.Current.CancellationToken);

        // Assert
        entry.Should().NotBeNull();
        entry!.Value.Should().Be("New York");
    }

    [Fact]
    public async Task GetAsync_NonexistentEntry_ReturnsNull()
    {
        // Act
        var entry = await _profileService.GetAsync("user-1", "nonexistent", TestContext.Current.CancellationToken);

        // Assert
        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_UpdatesLastAccessedAt()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("key", "value"), TestContext.Current.CancellationToken);
        var initialEntry = await _profileService.GetAsync(userId, "key", TestContext.Current.CancellationToken);
        var initialAccess = initialEntry!.LastAccessedAt;

        await Task.Delay(10, TestContext.Current.CancellationToken); // Small delay to ensure time difference

        // Act
        var entry = await _profileService.GetAsync(userId, "key", TestContext.Current.CancellationToken);

        // Assert
        entry!.LastAccessedAt.Should().BeOnOrAfter(initialAccess);
    }

    #endregion

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_MultipleEntries_ReturnsAllOrderedByConfidence()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("low", "value", 0.3f), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("high", "value", 0.9f), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("medium", "value", 0.6f), TestContext.Current.CancellationToken);

        // Act
        var entries = await _profileService.GetAllAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        entries.Should().HaveCount(3);
        entries[0].Key.Should().Be("high");
        entries[1].Key.Should().Be("medium");
        entries[2].Key.Should().Be("low");
    }

    [Fact]
    public async Task GetAllAsync_NoEntries_ReturnsEmpty()
    {
        // Act
        var entries = await _profileService.GetAllAsync("user-1", TestContext.Current.CancellationToken);

        // Assert
        entries.Should().BeEmpty();
    }

    #endregion

    #region GetByCategoryAsync Tests

    [Fact]
    public async Task GetByCategoryAsync_ReturnsOnlyMatchingCategory()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("skill1", "Python", category: SemanticStoreCategory.Skill), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("skill2", "C#", category: SemanticStoreCategory.Skill), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("interest", "Music", category: SemanticStoreCategory.Interest), TestContext.Current.CancellationToken);

        // Act
        var skills = await _profileService.GetByCategoryAsync(userId, SemanticStoreCategory.Skill, TestContext.Current.CancellationToken);

        // Assert
        skills.Should().HaveCount(2);
        skills.Should().OnlyContain(e => e.Category == SemanticStoreCategory.Skill);
    }

    #endregion

    #region ConfirmAsync Tests

    [Fact]
    public async Task ConfirmAsync_IncrementsConfirmationCount()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("fact", "value"), TestContext.Current.CancellationToken);

        // Act
        var entry = await _profileService.ConfirmAsync(userId, "fact", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        entry.Should().NotBeNull();
        entry!.ConfirmationCount.Should().Be(2);
    }

    [Fact]
    public async Task ConfirmAsync_BoostsConfidence()
    {
        // Arrange
        const string userId = "user-1";
        var initialConfidence = 0.5f;
        await _profileService.SetAsync(userId, CreateTestEntry("fact", "value", initialConfidence), TestContext.Current.CancellationToken);

        // Act
        var entry = await _profileService.ConfirmAsync(userId, "fact", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        entry!.Confidence.Should().Be(initialConfidence + _options.ConfidenceBoostPerConfirmation);
    }

    [Fact]
    public async Task ConfirmAsync_WithEvidence_AddsToMetadata()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("fact", "value"), TestContext.Current.CancellationToken);

        // Act
        var entry = await _profileService.ConfirmAsync(userId, "fact", "Session confirmed this", TestContext.Current.CancellationToken);

        // Assert
        entry!.Metadata.Should().ContainKey("evidence_2");
    }

    [Fact]
    public async Task ConfirmAsync_ThreeConfirmations_BecomesConfirmed()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("fact", "value", 0.7f), TestContext.Current.CancellationToken);

        // Act - confirm twice more (initial count is 1)
        await _profileService.ConfirmAsync(userId, "fact", cancellationToken: TestContext.Current.CancellationToken);
        var entry = await _profileService.ConfirmAsync(userId, "fact", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        entry!.ConfirmationCount.Should().Be(3);
        entry.Confidence.Should().BeApproximately(0.9f, 0.01f); // 0.7 + 0.1 + 0.1 (with float tolerance)
        entry.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmAsync_NonexistentEntry_ReturnsNull()
    {
        // Act
        var entry = await _profileService.ConfirmAsync("user-1", "nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        entry.Should().BeNull();
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_ExistingEntry_RemovesAndReturnsTrue()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("key", "value"), TestContext.Current.CancellationToken);

        // Act
        var removed = await _profileService.RemoveAsync(userId, "key", TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeTrue();
        var entry = await _profileService.GetAsync(userId, "key", TestContext.Current.CancellationToken);
        entry.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonexistentEntry_ReturnsFalse()
    {
        // Act
        var removed = await _profileService.RemoveAsync("user-1", "nonexistent", TestContext.Current.CancellationToken);

        // Assert
        removed.Should().BeFalse();
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_FindsMatchingEntries()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("programming_skill", "Python expert"), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("hobby", "Reading books"), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("location", "New York"), TestContext.Current.CancellationToken);

        // Act
        var results = await _profileService.SearchAsync(userId, "Python", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        // Arrange
        const string userId = "user-1";
        for (int i = 0; i < 10; i++)
        {
            await _profileService.SetAsync(userId, CreateTestEntry($"skill_{i}", "Programming"), TestContext.Current.CancellationToken);
        }

        // Act
        var results = await _profileService.SearchAsync(userId, "Programming", limit: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCountLessThanOrEqualTo(5);
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public async Task GetStats_ReturnsCorrectStatistics()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("skill1", "Python", 0.9f, SemanticStoreCategory.Skill), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("skill2", "C#", 0.8f, SemanticStoreCategory.Skill), TestContext.Current.CancellationToken);
        await _profileService.SetAsync(userId, CreateTestEntry("interest", "Music", 0.7f, SemanticStoreCategory.Interest), TestContext.Current.CancellationToken);

        // Confirm one entry to make it confirmed
        await _profileService.ConfirmAsync(userId, "skill1", cancellationToken: TestContext.Current.CancellationToken);
        await _profileService.ConfirmAsync(userId, "skill1", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var stats = _profileService.GetStats(userId);

        // Assert
        stats.TotalEntries.Should().Be(3);
        stats.EntriesByCategory[SemanticStoreCategory.Skill].Should().Be(2);
        stats.EntriesByCategory[SemanticStoreCategory.Interest].Should().Be(1);
        stats.AverageConfidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetStats_NoProfile_ReturnsEmptyStats()
    {
        // Act
        var stats = _profileService.GetStats("nonexistent-user");

        // Assert
        stats.TotalEntries.Should().Be(0);
        stats.ConfirmedEntries.Should().Be(0);
    }

    #endregion

    #region HasProfile Tests

    [Fact]
    public async Task HasProfile_WithEntries_ReturnsTrue()
    {
        // Arrange
        const string userId = "user-1";
        await _profileService.SetAsync(userId, CreateTestEntry("key", "value"), TestContext.Current.CancellationToken);

        // Act & Assert
        _profileService.HasProfile(userId).Should().BeTrue();
    }

    [Fact]
    public void HasProfile_NoEntries_ReturnsFalse()
    {
        // Act & Assert
        _profileService.HasProfile("nonexistent-user").Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static SemanticStoreEntry CreateTestEntry(
        string key,
        string value,
        float confidence = 0.5f,
        SemanticStoreCategory category = SemanticStoreCategory.Fact)
    {
        return new SemanticStoreEntry
        {
            Key = key,
            Value = value,
            Confidence = confidence,
            Category = category
        };
    }

    #endregion
}
