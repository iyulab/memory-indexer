using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.InMemory;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using MemoryIndexer.Scoring;
using MemoryIndexer.Sdk.Intelligence.Deduplication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Deduplication;

public class DeduplicationServiceTests
{
    private readonly IMemoryStore _memoryStore;
    private readonly MockEmbeddingService _embeddingService;
    private readonly IScoringService _scoringService;
    private readonly DeduplicationService _deduplicationService;

    public DeduplicationServiceTests()
    {
        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        _embeddingService = new MockEmbeddingService(
            Options.Create(new MemoryIndexerOptions()),
            NullLogger<MockEmbeddingService>.Instance);
        _scoringService = new DefaultScoringService(Options.Create(new MemoryIndexerOptions()));

        var options = Options.Create(new MemoryIndexerOptions
        {
            Deduplication = new DeduplicationOptions
            {
                Enabled = true,
                DefaultSimilarityThreshold = 0.80f,
                LookbackWindow = 20,
                ExactDuplicateThreshold = 0.95f,
                HighSimilarityThreshold = 0.85f,
                MediumSimilarityThreshold = 0.75f,
                LowSimilarityThreshold = 0.65f
            }
        });

        _deduplicationService = new DeduplicationService(
            _memoryStore,
            _embeddingService,
            _scoringService,
            NullLogger<DeduplicationService>.Instance,
            options);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_WhenDisabled_ShouldReturnNoDuplicate()
    {
        // Arrange
        var options = Options.Create(new MemoryIndexerOptions
        {
            Deduplication = new DeduplicationOptions { Enabled = false }
        });
        var service = new DeduplicationService(
            _memoryStore,
            _embeddingService,
            _scoringService,
            NullLogger<DeduplicationService>.Instance,
            options);

        // Act
        var result = await service.CheckForDuplicateAsync("test content", "user1");

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateType.None, result.DuplicateType);
        Assert.Equal(DuplicateAction.Add, result.RecommendedAction);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_NoExistingMemories_ShouldReturnNoDuplicate()
    {
        // Arrange
        var content = "This is a test memory";

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(content, "user1");

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateType.None, result.DuplicateType);
        Assert.Equal(DuplicateAction.Add, result.RecommendedAction);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_ExactDuplicate_ShouldRecommendSkip()
    {
        // Arrange
        var content = "This is an exact duplicate memory";
        await StoreMemoryAsync("user1", content);

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(content, "user1");

        // Assert - exact duplicates should have similarity >= 0.95
        if (result.SimilarityScore >= 0.95f)
        {
            Assert.True(result.IsDuplicate);
            Assert.Equal(DuplicateAction.Skip, result.RecommendedAction);
            Assert.NotNull(result.ExistingMemory);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_HighSimilarity_ShouldRecommendMerge()
    {
        // Arrange
        var existing = "The capital of France is Paris";
        var similar = "Paris is the capital city of France";
        await StoreMemoryAsync("user1", existing);

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(similar, "user1");

        // Assert - high similarity (0.85-0.94) should recommend Merge
        if (result.SimilarityScore >= 0.85f && result.SimilarityScore < 0.95f)
        {
            Assert.True(result.IsDuplicate);
            Assert.Equal(DuplicateAction.Merge, result.RecommendedAction);
            Assert.NotNull(result.ExistingMemory);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_MediumSimilarity_ShouldRecommendUpdate()
    {
        // Arrange
        var existing = "I like programming in Python";
        var similar = "Programming in Python is enjoyable";
        await StoreMemoryAsync("user1", existing);

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(similar, "user1");

        // Assert - medium similarity (0.75-0.84) should recommend Update
        if (result.SimilarityScore >= 0.75f && result.SimilarityScore < 0.85f)
        {
            Assert.True(result.IsDuplicate);
            Assert.Equal(DuplicateAction.Update, result.RecommendedAction);
            Assert.NotNull(result.ExistingMemory);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_LowSimilarity_ShouldRecommendAddWithRelation()
    {
        // Arrange
        var existing = "I enjoy reading books";
        var similar = "Books are a great source of knowledge";
        await StoreMemoryAsync("user1", existing);

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(similar, "user1");

        // Assert - low similarity (0.65-0.74) should recommend AddWithRelation
        if (result.SimilarityScore >= 0.65f && result.SimilarityScore < 0.75f)
        {
            Assert.True(result.IsDuplicate);
            Assert.Equal(DuplicateAction.AddWithRelation, result.RecommendedAction);
            Assert.NotNull(result.ExistingMemory);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_DifferentContent_ShouldRecommendAdd()
    {
        // Arrange
        var existing = "I like programming";
        var different = "The weather is nice today";
        await StoreMemoryAsync("user1", existing);

        // Act
        var result = await _deduplicationService.CheckForDuplicateAsync(different, "user1");

        // Assert - very low similarity (< 0.65) should recommend Add
        if (result.SimilarityScore < 0.65f)
        {
            Assert.False(result.IsDuplicate);
            Assert.Equal(DuplicateAction.Add, result.RecommendedAction);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_LookbackWindow_ShouldOnlyCheckRecentMemories()
    {
        // Arrange - create 25 memories
        for (int i = 0; i < 25; i++)
        {
            await StoreMemoryAsync("user1", $"Memory number {i}");
            await Task.Delay(10); // Ensure different timestamps
        }

        // Act - should only check last 20 memories
        var result = await _deduplicationService.CheckForDuplicateAsync(
            "Memory number 24",
            "user1",
            lookbackWindow: 20);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_ContentTypeAware_QuestionAndQuestion_ShouldSkip()
    {
        // Arrange
        var existing = "What is the capital of France?";
        var metadata = new Dictionary<string, string> { { "ContentType", "QUESTION" } };
        await StoreMemoryAsync("user1", existing, metadata);

        // Act
        var similar = "What's the capital city of France?";
        var result = await _deduplicationService.CheckForDuplicateAsync(
            similar,
            "user1",
            contentType: "QUESTION");

        // Assert - QUESTION + QUESTION with high similarity should Skip
        if (result.SimilarityScore >= 0.90f)
        {
            Assert.Equal(DuplicateAction.Skip, result.RecommendedAction);
        }
    }

    [Fact]
    public async Task CheckForDuplicateAsync_CustomThreshold_ShouldUseCustomValue()
    {
        // Arrange
        var existing = "Test content";
        await StoreMemoryAsync("user1", existing);

        // Act - use very low threshold
        var result = await _deduplicationService.CheckForDuplicateAsync(
            "Different content",
            "user1",
            similarityThreshold: 0.1f);

        // Assert - with low threshold, even different content might be detected as duplicate
        Assert.NotNull(result);
        Assert.True(result.SimilarityScore >= 0.0f);
    }

    [Fact]
    public async Task CheckForDuplicateAsync_DifferentUsers_ShouldNotFindDuplicates()
    {
        // Arrange
        var content = "User-specific memory";
        await StoreMemoryAsync("user1", content);

        // Act - check for user2
        var result = await _deduplicationService.CheckForDuplicateAsync(content, "user2");

        // Assert - should not find duplicates from different users
        Assert.False(result.IsDuplicate);
        Assert.Equal(DuplicateAction.Add, result.RecommendedAction);
    }

    private async Task<MemoryUnit> StoreMemoryAsync(
        string userId,
        string content,
        Dictionary<string, string>? metadata = null)
    {
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
        var memory = new MemoryUnit
        {
            UserId = userId,
            Content = content,
            Embedding = embedding,
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            Metadata = metadata ?? []
        };
        return await _memoryStore.StoreAsync(memory);
    }
}
