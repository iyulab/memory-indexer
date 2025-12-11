using FluentAssertions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Storage.Tests.Sqlite;

/// <summary>
/// Tests for multi-tenant data isolation using CTE-based pre-filtering.
/// Ensures that users can only access their own data and cross-tenant
/// data leakage is prevented.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SqliteMultiTenantIsolationTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private SqliteVecMemoryStore _store = null!;

    // Test tenant IDs
    private const string Tenant1 = "tenant-1";
    private const string Tenant2 = "tenant-2";
    private const string Tenant3 = "tenant-3";

    public SqliteMultiTenantIsolationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_multitenant_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        var options = new SqliteOptions
        {
            UseWalMode = true,
            EnableFullTextSearch = true
        };

        _store = new SqliteVecMemoryStore(
            _testDbPath,
            vectorDimensions: 384, // Using smaller dimensions for tests
            options: options,
            logger: NullLogger<SqliteVecMemoryStore>.Instance);

        // Seed test data for all tenants
        await SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();

        TryDeleteFile(_testDbPath);
        TryDeleteFile($"{_testDbPath}-wal");
        TryDeleteFile($"{_testDbPath}-shm");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Ignore cleanup errors */ }
    }

    private async Task SeedTestDataAsync()
    {
        // Tenant 1: Technical documentation
        await _store.StoreAsync(CreateMemory(Tenant1, "Python is a programming language for web development.", MemoryType.Semantic));
        await _store.StoreAsync(CreateMemory(Tenant1, "Django is a Python web framework.", MemoryType.Semantic));
        await _store.StoreAsync(CreateMemory(Tenant1, "REST APIs use HTTP methods.", MemoryType.Procedural));

        // Tenant 2: Business information (should NOT be visible to Tenant 1)
        await _store.StoreAsync(CreateMemory(Tenant2, "Q4 revenue exceeded projections by 15%.", MemoryType.Episodic));
        await _store.StoreAsync(CreateMemory(Tenant2, "Confidential: Merger talks with CompanyX.", MemoryType.Episodic));
        await _store.StoreAsync(CreateMemory(Tenant2, "Employee salary data for 2024.", MemoryType.Semantic));

        // Tenant 3: Mixed content
        await _store.StoreAsync(CreateMemory(Tenant3, "Machine learning requires training data.", MemoryType.Semantic));
        await _store.StoreAsync(CreateMemory(Tenant3, "Customer PII: John Doe, SSN: 123-45-6789", MemoryType.Episodic));
    }

    private static MemoryUnit CreateMemory(string userId, string content, MemoryType type, string? sessionId = null)
    {
        var embedding = new float[384];
        new Random(content.GetHashCode()).NextBytes(new Span<byte>(new byte[embedding.Length * sizeof(float)]));

        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            Content = content,
            Type = type,
            ImportanceScore = 0.7f,
            Embedding = embedding
        };
    }

    #region Tenant Isolation Tests

    [Fact]
    public async Task SearchAsync_WithUserId_ShouldOnlyReturnTenantData()
    {
        // Arrange
        var queryEmbedding = new float[384];
        new Random(42).NextBytes(new Span<byte>(new byte[queryEmbedding.Length * sizeof(float)]));

        var options = new MemorySearchOptions
        {
            UserId = Tenant1,
            Limit = 100,
            MinScore = 0 // Accept all scores for this test
        };

        // Act
        var results = await _store.SearchAsync(queryEmbedding, options);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            r.Memory.UserId.Should().Be(Tenant1,
                $"Expected all results to belong to {Tenant1}"));

        // Verify no cross-tenant data
        results.Should().NotContain(r => r.Memory.Content.Contains("revenue"));
        results.Should().NotContain(r => r.Memory.Content.Contains("Confidential"));
        results.Should().NotContain(r => r.Memory.Content.Contains("SSN"));
    }

    [Fact]
    public async Task SearchAsync_DifferentTenants_ShouldReturnIsolatedData()
    {
        // Arrange
        var queryEmbedding = new float[384];
        new Random(42).NextBytes(new Span<byte>(new byte[queryEmbedding.Length * sizeof(float)]));

        var tenant1Options = new MemorySearchOptions { UserId = Tenant1, Limit = 100, MinScore = 0 };
        var tenant2Options = new MemorySearchOptions { UserId = Tenant2, Limit = 100, MinScore = 0 };

        // Act
        var tenant1Results = await _store.SearchAsync(queryEmbedding, tenant1Options);
        var tenant2Results = await _store.SearchAsync(queryEmbedding, tenant2Options);

        // Assert - Tenant 1 should only see technical content
        tenant1Results.Should().AllSatisfy(r => r.Memory.UserId.Should().Be(Tenant1));
        tenant1Results.Select(r => r.Memory.Content)
            .Should().Contain(c => c.Contains("Python"));

        // Assert - Tenant 2 should only see business content
        tenant2Results.Should().AllSatisfy(r => r.Memory.UserId.Should().Be(Tenant2));
        tenant2Results.Select(r => r.Memory.Content)
            .Should().Contain(c => c.Contains("revenue") || c.Contains("Confidential"));

        // Verify no overlap
        var tenant1Ids = tenant1Results.Select(r => r.Memory.Id).ToHashSet();
        var tenant2Ids = tenant2Results.Select(r => r.Memory.Id).ToHashSet();
        tenant1Ids.Intersect(tenant2Ids).Should().BeEmpty("tenants should not share memory IDs");
    }

    [Fact]
    public async Task GetAllAsync_WithUserId_ShouldOnlyReturnTenantData()
    {
        // Arrange & Act
        var tenant1Memories = await _store.GetAllAsync(Tenant1);
        var tenant2Memories = await _store.GetAllAsync(Tenant2);
        var tenant3Memories = await _store.GetAllAsync(Tenant3);

        // Assert
        tenant1Memories.Should().HaveCount(3);
        tenant1Memories.Should().AllSatisfy(m => m.UserId.Should().Be(Tenant1));

        tenant2Memories.Should().HaveCount(3);
        tenant2Memories.Should().AllSatisfy(m => m.UserId.Should().Be(Tenant2));

        tenant3Memories.Should().HaveCount(2);
        tenant3Memories.Should().AllSatisfy(m => m.UserId.Should().Be(Tenant3));
    }

    [Fact]
    public async Task GetCountAsync_ShouldReturnCorrectCountPerTenant()
    {
        // Act
        var tenant1Count = await _store.GetCountAsync(Tenant1);
        var tenant2Count = await _store.GetCountAsync(Tenant2);
        var tenant3Count = await _store.GetCountAsync(Tenant3);

        // Assert
        tenant1Count.Should().Be(3);
        tenant2Count.Should().Be(3);
        tenant3Count.Should().Be(2);
    }

    [Fact]
    public async Task GetCountAsync_NonexistentTenant_ShouldReturnZero()
    {
        // Act
        var count = await _store.GetCountAsync("nonexistent-tenant");

        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region Cross-Tenant Protection Tests

    [Fact]
    public async Task SearchAsync_SensitiveDataShouldNotLeakAcrossTenants()
    {
        // This test verifies that sensitive data (PII, confidential info)
        // doesn't leak across tenant boundaries
        var queryEmbedding = new float[384];

        // Tenant 1 searches for anything - should NOT see Tenant 2/3 sensitive data
        var tenant1Options = new MemorySearchOptions { UserId = Tenant1, Limit = 100, MinScore = 0 };
        var tenant1Results = await _store.SearchAsync(queryEmbedding, tenant1Options);

        // Assert no sensitive data leakage
        var tenant1Contents = string.Join(" ", tenant1Results.Select(r => r.Memory.Content));
        tenant1Contents.Should().NotContain("SSN");
        tenant1Contents.Should().NotContain("salary");
        tenant1Contents.Should().NotContain("Confidential");
        tenant1Contents.Should().NotContain("Merger");
    }

    [Fact]
    public async Task SearchAsync_WithSessionFilter_ShouldStillRespectTenantBoundary()
    {
        // Add session-specific data for both tenants
        var session1 = "session-shared-name";
        await _store.StoreAsync(CreateMemory(Tenant1, "Tenant1 session data", MemoryType.Episodic, session1));
        await _store.StoreAsync(CreateMemory(Tenant2, "Tenant2 session data", MemoryType.Episodic, session1));

        var queryEmbedding = new float[384];

        // Tenant 1 searches for shared session name
        var options = new MemorySearchOptions
        {
            UserId = Tenant1,
            SessionId = session1,
            Limit = 100,
            MinScore = 0
        };

        var results = await _store.SearchAsync(queryEmbedding, options);

        // Should only see Tenant 1's data despite same session name
        results.Should().AllSatisfy(r => r.Memory.UserId.Should().Be(Tenant1));
        results.Should().Contain(r => r.Memory.Content.Contains("Tenant1 session"));
        results.Should().NotContain(r => r.Memory.Content.Contains("Tenant2 session"));
    }

    [Fact]
    public async Task SearchAsync_WithTypeFilter_ShouldStillRespectTenantBoundary()
    {
        var queryEmbedding = new float[384];

        // Tenant 1 searches for Semantic type memories
        var options = new MemorySearchOptions
        {
            UserId = Tenant1,
            Types = [MemoryType.Semantic],
            Limit = 100,
            MinScore = 0
        };

        var results = await _store.SearchAsync(queryEmbedding, options);

        // Should only see Tenant 1's Semantic memories
        results.Should().AllSatisfy(r =>
        {
            r.Memory.UserId.Should().Be(Tenant1);
            r.Memory.Type.Should().Be(MemoryType.Semantic);
        });

        // Should NOT see Tenant 2's Semantic memories (salary data)
        results.Should().NotContain(r => r.Memory.Content.Contains("salary"));
    }

    #endregion

    #region CTE Query Verification Tests

    [Fact]
    public async Task SearchAsync_CteQueryShouldPreFilterByTenant()
    {
        // This test verifies that the CTE pre-filtering is working
        // by checking that results are properly scoped to tenant
        var queryEmbedding = new float[384];

        var options = new MemorySearchOptions
        {
            UserId = Tenant1,
            Limit = 10,
            MinScore = 0
        };

        var results = await _store.SearchAsync(queryEmbedding, options);

        // Verify count matches expected tenant data
        results.Count.Should().BeLessThanOrEqualTo(3, "Tenant1 only has 3 memories");
        results.Should().AllSatisfy(r => r.Memory.UserId.Should().Be(Tenant1));
    }

    [Fact]
    public async Task SearchAsync_WithDateFilters_ShouldRespectTenantScope()
    {
        // Add time-specific data
        var recentMemory = CreateMemory(Tenant1, "Recent tenant1 data", MemoryType.Episodic);
        await _store.StoreAsync(recentMemory);

        var queryEmbedding = new float[384];

        var options = new MemorySearchOptions
        {
            UserId = Tenant1,
            CreatedAfter = DateTime.UtcNow.AddMinutes(-1),
            Limit = 100,
            MinScore = 0
        };

        var results = await _store.SearchAsync(queryEmbedding, options);

        // Should only see recent Tenant 1 data
        results.Should().AllSatisfy(r => r.Memory.UserId.Should().Be(Tenant1));
        results.Should().Contain(r => r.Memory.Content.Contains("Recent tenant1"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SearchAsync_EmptyTenant_ShouldReturnEmpty()
    {
        var queryEmbedding = new float[384];

        var options = new MemorySearchOptions
        {
            UserId = "empty-tenant-no-data",
            Limit = 100,
            MinScore = 0
        };

        var results = await _store.SearchAsync(queryEmbedding, options);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_ShouldOnlyAffectOwnTenant()
    {
        // Store a memory for Tenant 1
        var memory = CreateMemory(Tenant1, "Memory to delete", MemoryType.Episodic);
        await _store.StoreAsync(memory);

        // Delete it
        var deleted = await _store.DeleteAsync(memory.Id, hardDelete: true);
        deleted.Should().BeTrue();

        // Verify Tenant 1's count decreased
        var tenant1Memories = await _store.GetAllAsync(Tenant1);
        tenant1Memories.Should().NotContain(m => m.Id == memory.Id);

        // Verify Tenant 2's data is unaffected
        var tenant2Count = await _store.GetCountAsync(Tenant2);
        tenant2Count.Should().Be(3, "Tenant 2's data should be unaffected by Tenant 1's delete");
    }

    [Fact]
    public async Task UpdateAsync_ShouldOnlyModifyOwnTenant()
    {
        // Get Tenant 1's first memory
        var tenant1Memories = await _store.GetAllAsync(Tenant1);
        var memoryToUpdate = tenant1Memories.First();

        // Update it
        memoryToUpdate.Content = "Updated content for Tenant 1";
        var updated = await _store.UpdateAsync(memoryToUpdate);
        updated.Should().BeTrue();

        // Verify update worked for Tenant 1
        var updatedMemory = await _store.GetByIdAsync(memoryToUpdate.Id);
        updatedMemory!.Content.Should().Be("Updated content for Tenant 1");
        updatedMemory.UserId.Should().Be(Tenant1);

        // Verify Tenant 2's data is completely unaffected
        var tenant2Memories = await _store.GetAllAsync(Tenant2);
        tenant2Memories.Should().AllSatisfy(m =>
            m.Content.Should().NotContain("Updated content"));
    }

    #endregion
}
