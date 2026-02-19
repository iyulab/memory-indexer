using Xunit;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Services.Export;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryIndexer.InMemory;

namespace MemoryIndexer.Sdk.Tests.Services.Export;

/// <summary>
/// Unit tests for JsonMemoryExporter.
/// Phase v0.6.0-β: Memory Export/Import (Backup/Restore).
/// </summary>
public class JsonMemoryExporterTests : IAsyncLifetime
{
    private static readonly System.Text.Json.JsonSerializerOptions s_camelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private InMemoryMemoryStore _memoryStore = null!;
    private JsonMemoryExporter _exporter = null!;

    public Task InitializeAsync()
    {
        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        _exporter = new JsonMemoryExporter(
            _memoryStore,
            NullLogger<JsonMemoryExporter>.Instance);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExportAsync_EmptyStore_ReturnsEmptyPackage()
    {
        // Arrange
        var options = new ExportOptions { UserId = "test-user" };

        // Act
        var package = await _exporter.ExportAsync(options);

        // Assert
        Assert.NotNull(package);
        Assert.Equal(0, package.Statistics.TotalMemories);
        Assert.Empty(package.Memories);
        Assert.NotNull(package.Checksum);
    }

    [Fact]
    public async Task ExportAsync_WithMemories_ReturnsCorrectCount()
    {
        // Arrange
        const string userId = "test-user";
        await _memoryStore.StoreAsync(CreateMemory(userId, "Memory 1"));
        await _memoryStore.StoreAsync(CreateMemory(userId, "Memory 2"));
        await _memoryStore.StoreAsync(CreateMemory(userId, "Memory 3"));

        var options = new ExportOptions { UserId = userId };

        // Act
        var package = await _exporter.ExportAsync(options);

        // Assert
        Assert.Equal(3, package.Statistics.TotalMemories);
        Assert.Equal(3, package.Memories.Count);
    }

    [Fact]
    public async Task ExportAsync_WithSinceFilter_ReturnsFilteredMemories()
    {
        // Arrange
        const string userId = "test-user";
        var oldTime = DateTime.UtcNow.AddDays(-2);
        var newTime = DateTime.UtcNow;
        
        var oldMemory = CreateMemory(userId, "Old memory");
        oldMemory.CreatedAt = oldTime;
        oldMemory.UpdatedAt = oldTime;
        await _memoryStore.StoreAsync(oldMemory);
        
        var newMemory = CreateMemory(userId, "New memory");
        newMemory.CreatedAt = newTime;
        newMemory.UpdatedAt = newTime;
        await _memoryStore.StoreAsync(newMemory);

        var options = new ExportOptions
        {
            UserId = userId,
            Since = DateTimeOffset.UtcNow.AddDays(-1) // Only memories from last day
        };

        // Act
        var package = await _exporter.ExportAsync(options);

        // Assert
        Assert.Single(package.Memories);
        Assert.Equal("New memory", package.Memories[0].Content);
    }

    [Fact]
    public async Task ExportAsync_WithoutEmbeddings_ExcludesEmbeddings()
    {
        // Arrange
        const string userId = "test-user";
        var memory = CreateMemory(userId, "Test content");
        memory.Embedding = new float[] { 0.1f, 0.2f, 0.3f };
        await _memoryStore.StoreAsync(memory);

        var options = new ExportOptions
        {
            UserId = userId,
            IncludeEmbeddings = false
        };

        // Act
        var package = await _exporter.ExportAsync(options);

        // Assert
        Assert.Single(package.Memories);
        Assert.True(package.Memories[0].Embedding is null or { Length: 0 });
    }

    [Fact]
    public async Task ImportAsync_ValidPackage_ImportsMemories()
    {
        // Arrange
        var package = new MemoryExportPackage
        {
            Memories = [
                CreateMemory("test-user", "Imported memory 1"),
                CreateMemory("test-user", "Imported memory 2")
            ]
        };

        var options = new ImportOptions { PreserveIds = true };

        // Act
        var result = await _exporter.ImportAsync(package, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task ImportAsync_ConflictSkip_SkipsExisting()
    {
        // Arrange
        const string userId = "test-user";
        var existingMemory = CreateMemory(userId, "Existing content");
        await _memoryStore.StoreAsync(existingMemory);

        var package = new MemoryExportPackage
        {
            Memories = [
                new MemoryUnit
                {
                    Id = existingMemory.Id, // Same ID
                    UserId = userId,
                    Content = "New content"
                }
            ]
        };

        var options = new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.Skip,
            PreserveIds = true
        };

        // Act
        var result = await _exporter.ImportAsync(package, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public async Task ImportAsync_ConflictReplace_ReplacesExisting()
    {
        // Arrange
        const string userId = "test-user";
        var existingMemory = CreateMemory(userId, "Existing content");
        await _memoryStore.StoreAsync(existingMemory);

        var package = new MemoryExportPackage
        {
            Memories = [
                new MemoryUnit
                {
                    Id = existingMemory.Id,
                    UserId = userId,
                    Content = "New content"
                }
            ]
        };

        var options = new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.Replace,
            PreserveIds = true
        };

        // Act
        var result = await _exporter.ImportAsync(package, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.ReplacedCount);

        // Verify content was replaced
        var updated = await _memoryStore.GetByIdAsync(existingMemory.Id);
        Assert.Equal("New content", updated?.Content);
    }

    [Fact]
    public async Task ImportAsync_NewIds_GeneratesNewIds()
    {
        // Arrange
        var originalId = Guid.NewGuid();
        var package = new MemoryExportPackage
        {
            Memories = [
                new MemoryUnit
                {
                    Id = originalId,
                    UserId = "test-user",
                    Content = "Test content"
                }
            ]
        };

        var options = new ImportOptions { PreserveIds = false };

        // Act
        var result = await _exporter.ImportAsync(package, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.IdMapping);
        Assert.Contains(originalId, result.IdMapping!.Keys);
        Assert.NotEqual(originalId, result.IdMapping[originalId]);
    }

    [Fact]
    public async Task ExportToStreamAsync_WritesValidJson()
    {
        // Arrange
        const string userId = "test-user";
        await _memoryStore.StoreAsync(CreateMemory(userId, "Stream test"));

        var options = new ExportOptions { UserId = userId };
        using var stream = new MemoryStream();

        // Act
        var stats = await _exporter.ExportToStreamAsync(stream, options);

        // Assert
        Assert.Equal(1, stats.TotalMemories);
        Assert.True(stats.SizeBytes > 0);
    }

    [Fact]
    public async Task ImportFromStreamAsync_ReadsValidJson()
    {
        // Arrange
        var memory = CreateMemory("test-user", "Stream import test");
        var package = new MemoryExportPackage
        {
            FormatVersion = "1.0",
            ExportedAt = DateTimeOffset.UtcNow,
            Memories = [memory],
            Statistics = new ExportStatistics { TotalMemories = 1 }
        };

        using var stream = new MemoryStream();
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, package, s_camelCaseJsonOptions);
        stream.Position = 0;

        var options = new ImportOptions { PreserveIds = false };

        // Act
        var result = await _exporter.ImportFromStreamAsync(stream, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ImportedCount);
    }

    [Fact]
    public async Task ExportStatistics_CalculatesCorrectly()
    {
        // Arrange
        const string userId = "test-user";
        await _memoryStore.StoreAsync(CreateMemory(userId, "Episodic", type: MemoryType.Episodic, tier: Tier.Short));
        await _memoryStore.StoreAsync(CreateMemory(userId, "Semantic", type: MemoryType.Semantic, tier: Tier.Long));
        await _memoryStore.StoreAsync(CreateMemory(userId, "Fact", type: MemoryType.Fact, tier: Tier.Long));

        var options = new ExportOptions { UserId = userId };

        // Act
        var package = await _exporter.ExportAsync(options);

        // Assert
        Assert.Equal(3, package.Statistics.TotalMemories);
        Assert.Equal(1, package.Statistics.UniqueUsers);
        Assert.Equal(2, package.Statistics.ByTier.Count);
        Assert.Equal(1, package.Statistics.ByTier[Tier.Short]);
        Assert.Equal(2, package.Statistics.ByTier[Tier.Long]);
        Assert.Equal(3, package.Statistics.ByType.Count);
    }

    private static MemoryUnit CreateMemory(
        string userId,
        string content,
        MemoryType type = MemoryType.Episodic,
        Tier tier = Tier.Long)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            Type = type,
            Tier = tier,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
