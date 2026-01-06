using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using MemoryIndexer.Scoring;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Intelligence.Deduplication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Services;

/// <summary>
/// Tests for Phase 22.1: Memory Growth Rate Control.
/// </summary>
public sealed class MemoryServicePhase221Tests
{
    private readonly MemoryService _memoryService;
    private readonly InMemoryGrowthMonitor _growthMonitor;
    private readonly IMemoryStore _memoryStore;
    private readonly MemoryIndexerOptions _options;

    public MemoryServicePhase221Tests()
    {
        _options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Mock,
                Dimensions = 1024
            },
            MemoryGrowth = new MemoryGrowthOptions
            {
                MaxGrowthRatePerRound = 4.0f,
                MinImportanceForStorage = 0.3f,
                TopicBasedDedup = true,
                DynamicThresholds = true,
                LowPressureThresholdMultiplier = 0.8f,
                HighPressureThresholdMultiplier = 1.5f
            }
        };

        var optionsWrapper = Options.Create(_options);

        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        var embeddingService = new MockEmbeddingService(
            optionsWrapper,
            NullLogger<MockEmbeddingService>.Instance);
        var scoringService = new DefaultScoringService(optionsWrapper, null);
        var deduplicationService = new DeduplicationService(
            _memoryStore,
            embeddingService,
            scoringService,
            NullLogger<DeduplicationService>.Instance,
            optionsWrapper);
        var pressureMonitor = new MemoryPressureMonitorService();
        _growthMonitor = new InMemoryGrowthMonitor(optionsWrapper);

        _memoryService = new MemoryService(
            _memoryStore,
            embeddingService,
            scoringService,
            deduplicationService,
            pressureMonitor,
            _growthMonitor,
            optionsWrapper);
    }

    [Fact]
    public async Task StoreAsync_WithLowImportance_ShouldFilterMemory()
    {
        // Arrange
        var userId = "user1";
        var content = "This is a low importance memory";

        // Act
        var result = await _memoryService.StoreAsync(
            userId,
            content,
            importance: 0.2f); // Below 0.3 threshold

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Metadata.ContainsKey("Filtered"));
        Assert.Equal("true", result.Metadata["Filtered"]);
        Assert.Contains("ImportanceScore", result.Metadata["Reason"]);

        // Verify memory was not actually stored
        var allMemories = await _memoryStore.GetAllAsync(userId);
        Assert.Empty(allMemories);

        // Verify growth monitor tracked the filtering
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(0, metrics.MemoriesStoredThisRound);
        Assert.Equal(1, metrics.MemoriesFilteredThisRound);
    }

    [Fact]
    public async Task StoreAsync_WithHighImportance_ShouldStoreMemory()
    {
        // Arrange
        var userId = "user1";
        var content = "This is a high importance memory";

        // Act
        var result = await _memoryService.StoreAsync(
            userId,
            content,
            importance: 0.8f); // Above 0.3 threshold

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Metadata.ContainsKey("Filtered"));

        // Verify memory was stored
        var allMemories = await _memoryStore.GetAllAsync(userId);
        Assert.Single(allMemories);

        // Verify growth monitor tracked the storage
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(1, metrics.MemoriesStoredThisRound);
        Assert.Equal(0, metrics.MemoriesFilteredThisRound);
    }

    [Fact]
    public async Task StoreAsync_WithDuplicateTopic_ShouldFilterMemory()
    {
        // Arrange
        var userId = "user1";
        var topic = "Machine learning basics.";
        var content1 = $"{topic} First explanation";
        var content2 = $"{topic} Second explanation";

        // Act - Store first memory
        var result1 = await _memoryService.StoreAsync(
            userId,
            content1,
            importance: 0.8f);

        // Store second memory with same topic
        var result2 = await _memoryService.StoreAsync(
            userId,
            content2,
            importance: 0.8f);

        // Assert
        Assert.NotNull(result1);
        Assert.False(result1.Metadata.ContainsKey("Filtered"));

        Assert.NotNull(result2);
        Assert.True(result2.Metadata.ContainsKey("Filtered"));
        Assert.Contains("Duplicate topic", result2.Metadata["Reason"]);

        // Verify only first memory was stored
        var allMemories = await _memoryStore.GetAllAsync(userId);
        Assert.Single(allMemories);

        // Verify growth monitor tracked correctly
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(1, metrics.MemoriesStoredThisRound);
        Assert.Equal(1, metrics.MemoriesFilteredThisRound);
        // Check that some filter reason containing "Duplicate topic" exists
        Assert.Contains(metrics.FilterReasons.Keys, k => k.Contains("Duplicate topic"));
    }

    [Fact]
    public async Task StoreAsync_WithDifferentTopics_ShouldStoreBoth()
    {
        // Arrange
        var userId = "user1";
        var content1 = "Machine learning is about teaching computers.";
        var content2 = "Python is a programming language.";

        // Act
        var result1 = await _memoryService.StoreAsync(userId, content1, importance: 0.8f);
        var result2 = await _memoryService.StoreAsync(userId, content2, importance: 0.8f);

        // Assert
        Assert.False(result1.Metadata.ContainsKey("Filtered"));
        Assert.False(result2.Metadata.ContainsKey("Filtered"));

        // Verify both memories were stored
        var allMemories = await _memoryStore.GetAllAsync(userId);
        Assert.Equal(2, allMemories.Count);

        // Verify growth monitor tracked correctly
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(2, metrics.MemoriesStoredThisRound);
        Assert.Equal(0, metrics.MemoriesFilteredThisRound);
    }

    [Fact]
    public async Task StoreAsync_ShouldExtractAndStoreTopic()
    {
        // Arrange
        var userId = "user1";
        var content = "Machine learning is about teaching computers. It involves algorithms.";

        // Act
        var result = await _memoryService.StoreAsync(userId, content, importance: 0.8f);

        // Assert - Topic should be extracted (first sentence)
        Assert.True(result.Metadata.ContainsKey("Topic"));
        Assert.Equal("Machine learning is about teaching computers", result.Metadata["Topic"]);
    }

    [Fact]
    public async Task StoreAsync_WithDefaultImportance_ShouldUse05()
    {
        // Arrange
        var userId = "user1";
        var content = "This is a memory with default importance";

        // Act
        var result = await _memoryService.StoreAsync(userId, content);

        // Assert - Default importance is 0.5, which is > 0.3 threshold
        Assert.Equal(0.5f, result.ImportanceScore);
        Assert.False(result.Metadata.ContainsKey("Filtered"));

        // Verify memory was stored
        var allMemories = await _memoryStore.GetAllAsync(userId);
        Assert.Single(allMemories);
    }

    [Fact]
    public async Task GrowthRateControl_ShouldTrackMultipleMemories()
    {
        // Arrange
        var userId = "user1";

        // Act - Store 3 memories, filter 2
        await _memoryService.StoreAsync(userId, "Memory 1", importance: 0.8f);
        await _memoryService.StoreAsync(userId, "Memory 2", importance: 0.2f); // Filtered
        await _memoryService.StoreAsync(userId, "Memory 3", importance: 0.8f);
        await _memoryService.StoreAsync(userId, "Memory 4", importance: 0.1f); // Filtered
        await _memoryService.StoreAsync(userId, "Memory 5", importance: 0.9f);

        // Assert
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(3, metrics.MemoriesStoredThisRound);
        Assert.Equal(2, metrics.MemoriesFilteredThisRound);
        Assert.False(metrics.ExceedsThreshold); // 3 < 4.0
    }

    [Fact]
    public async Task GrowthRateControl_ShouldDetectExceedingThreshold()
    {
        // Arrange
        var userId = "user1";

        // Act - Store 5 memories (exceeds 4.0 threshold)
        for (int i = 0; i < 5; i++)
        {
            await _memoryService.StoreAsync(userId, $"Memory {i}", importance: 0.8f);
        }

        // Assert
        var metrics = await _growthMonitor.GetGrowthMetricsAsync(userId);
        Assert.Equal(5, metrics.MemoriesStoredThisRound);
        Assert.True(metrics.ExceedsThreshold); // 5 > 4.0
    }

    [Fact]
    public async Task MultipleUsers_ShouldTrackIndependently()
    {
        // Arrange
        var user1 = "user1";
        var user2 = "user2";

        // Act
        await _memoryService.StoreAsync(user1, "User1 memory 1", importance: 0.8f);
        await _memoryService.StoreAsync(user1, "User1 memory 2", importance: 0.2f); // Filtered

        await _memoryService.StoreAsync(user2, "User2 memory 1", importance: 0.8f);
        await _memoryService.StoreAsync(user2, "User2 memory 2", importance: 0.8f);
        await _memoryService.StoreAsync(user2, "User2 memory 3", importance: 0.8f);

        // Assert
        var metrics1 = await _growthMonitor.GetGrowthMetricsAsync(user1);
        var metrics2 = await _growthMonitor.GetGrowthMetricsAsync(user2);

        Assert.Equal(1, metrics1.MemoriesStoredThisRound);
        Assert.Equal(1, metrics1.MemoriesFilteredThisRound);

        Assert.Equal(3, metrics2.MemoriesStoredThisRound);
        Assert.Equal(0, metrics2.MemoriesFilteredThisRound);

        // Verify actual storage
        var user1Memories = await _memoryStore.GetAllAsync(user1);
        var user2Memories = await _memoryStore.GetAllAsync(user2);

        Assert.Single(user1Memories);
        Assert.Equal(3, user2Memories.Count);
    }
}
