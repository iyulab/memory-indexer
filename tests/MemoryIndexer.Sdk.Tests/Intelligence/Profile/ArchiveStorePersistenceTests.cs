using MemoryIndexer.InMemory;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Profile;
using MemoryIndexer.Sdk.Storage.Sqlite;
using MemoryIndexer.Sdk.Tests.Storage.VectorData;
using MemoryIndexer.VectorData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// The profile is kept in the memory store: every property of an entry survives the store (SQLite and a vector store have
/// no column for most of them), and a second service over the same database file — a restart — reads what the first one wrote.
/// </summary>
public sealed class ArchiveStorePersistenceTests : IDisposable
{
    private const int Dimensions = 4;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mi-archive-{Guid.NewGuid():N}");
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    public ArchiveStorePersistenceTests()
    {
        Directory.CreateDirectory(_dir);
        _embedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]));
        _embedding.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A pooled handle may outlive the test on some platforms
        }
    }

    public static TheoryData<string> Stores => ["memory", "sqlite", "vectorstore"];

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task EveryPropertyOfAnEntry_SurvivesTheStore(string kind)
    {
        await using var holder = new StoreHolder(kind, Path.Combine(_dir, "roundtrip.db"));
        var archive = CreateService(holder.Store);
        var created = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc).AddTicks(9);
        var entry = new SemanticStoreEntry
        {
            Key = "preference:user:drink",
            Value = "User prefers green tea",
            Category = SemanticStoreCategory.Preference,
            Confidence = 0.8765f,
            ConfirmationCount = 4,
            SourceSessions = ["s-1", "s-2"],
            CreatedAt = created,
            UpdatedAt = created.AddDays(1),
            LastAccessedAt = created.AddDays(2),
            Embedding = new ReadOnlyMemory<float>([0.4f, 0.3f, 0.2f, 0.1f]),
            Metadata = new Dictionary<string, string> { ["subject"] = "user", ["archive.key"] = "not the key" },
            ValidFrom = created.AddHours(-5),
            ValidTo = created.AddDays(30),
            SupersedesKey = "preference:user:drink:v2",
            Version = 3,
            IsActive = false,
        };

        Assert.False(await archive.SetAsync("u", entry, Ct));
        var read = (await archive.GetAllAsync("u", Ct)).Single();

        Assert.Equal(entry.Key, read.Key);
        Assert.Equal(entry.Value, read.Value);
        Assert.Equal(entry.Category, read.Category);
        Assert.Equal(entry.Confidence, read.Confidence);
        Assert.Equal(entry.ConfirmationCount, read.ConfirmationCount);
        Assert.Equal(entry.SourceSessions, read.SourceSessions);
        Assert.Equal(entry.CreatedAt, read.CreatedAt);
        Assert.Equal(entry.UpdatedAt, read.UpdatedAt);
        Assert.Equal(entry.LastAccessedAt, read.LastAccessedAt);
        Assert.Equal(entry.Embedding!.Value.ToArray(), read.Embedding!.Value.ToArray());
        Assert.Equal(entry.Metadata, read.Metadata);
        Assert.Equal(entry.ValidFrom, read.ValidFrom);
        Assert.Equal(entry.ValidTo, read.ValidTo);
        Assert.Equal(entry.SupersedesKey, read.SupersedesKey);
        Assert.Equal(entry.Version, read.Version);
        Assert.Equal(entry.IsActive, read.IsActive);
        Assert.Equal(DateTimeKind.Utc, read.CreatedAt.Kind);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task AnEntryWithoutOptionalValues_RoundTripsThemAsAbsent(string kind)
    {
        await using var holder = new StoreHolder(kind, Path.Combine(_dir, "optional.db"));
        var archive = new ArchiveStoreService(
            holder.Store, _embedding, Options.Create(new SemanticStoreOptions { EnableSemanticSearch = false }),
            NullLogger<ArchiveStoreService>.Instance);

        await archive.SetAsync("u", new SemanticStoreEntry { Key = "k", Value = "v" }, Ct);
        var read = await archive.GetAsync("u", "k", Ct);

        Assert.NotNull(read);
        Assert.Null(read.Embedding);
        Assert.Null(read.ValidFrom);
        Assert.Null(read.ValidTo);
        Assert.Null(read.SupersedesKey);
        Assert.Empty(read.SourceSessions);
        Assert.Empty(read.Metadata);
        Assert.True(read.IsActive);
    }

    [Fact]
    public async Task TheProfile_IsKeptUnderTheArchiveKey_WithAnIdDerivedFromUserAndKey()
    {
        var store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        var archive = CreateService(store);

        await archive.SetAsync("u", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is John" }, Ct);
        await archive.SetAsync("w", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is Ann" }, Ct);

        Assert.Empty(await store.GetAllAsync("u", cancellationToken: Ct));
        var unit = Assert.Single(await store.GetAllAsync(ArchiveUserKey.For("u"), cancellationToken: Ct));
        var other = Assert.Single(await store.GetAllAsync(ArchiveUserKey.For("w"), cancellationToken: Ct));
        Assert.Equal("User's name is John", unit.Content);
        Assert.NotEqual(unit.Id, other.Id);
        Assert.Equal(5, unit.Id.Version);
        Assert.Equal(MemoryType.Fact, unit.Type);
        Assert.Equal(Scope.User, unit.Scope);

        // A second service finds it by the same id
        var again = CreateService(store);
        Assert.Equal("User's name is John", (await again.GetAsync("u", "identity:user:name", Ct))?.Value);
        Assert.True(await again.SetAsync("u", new SemanticStoreEntry { Key = "identity:user:name", Value = "User's name is Jo" }, Ct));
        Assert.Single(await store.GetAllAsync(ArchiveUserKey.For("u"), cancellationToken: Ct));
    }

    [Fact]
    public async Task AReadIsKeptAsTheLastAccess_WhenTheStoredOneIsAnHourOld_AndNotWrittenOtherwise()
    {
        var store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
        var archive = CreateService(store);
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);
        await archive.SetAsync("u", new SemanticStoreEntry { Key = "old", Value = "read long ago", LastAccessedAt = twoDaysAgo }, Ct);
        await archive.SetAsync("u", new SemanticStoreEntry { Key = "recent", Value = "read just now", LastAccessedAt = tenMinutesAgo }, Ct);

        await archive.GetAsync("u", "old", Ct);
        await archive.GetAsync("u", "recent", Ct);

        // A second service reads what the store holds (GetAllAsync does not record an access)
        var stored = (await CreateService(store).GetAllAsync("u", Ct)).ToDictionary(e => e.Key);
        Assert.True(stored["old"].LastAccessedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(tenMinutesAgo, stored["recent"].LastAccessedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AProfileWrittenBeforeARestart_IsReadAfterIt_WithItsHistory()
    {
        var path = Path.Combine(_dir, "restart.db");

        await using (var first = new SqliteVecMemoryStore(path, vectorDimensions: Dimensions))
        {
            var archive = CreateService(first);
            await archive.SetAsync("u", new SemanticStoreEntry
            {
                Key = "identity:user:name",
                Value = "User's name is John",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.7f,
            }, Ct);
            await archive.ConfirmAsync("u", "identity:user:name", "s-1", Ct);
            await archive.ArchiveAndUpdateAsync("u", "identity:user:name", "User's name is Mike", Ct);
            await archive.SetAsync("u", new SemanticStoreEntry
            {
                Key = "preference:user:drink",
                Value = "User prefers tea",
                Category = SemanticStoreCategory.Preference,
                Confidence = 0.9f,
            }, Ct);
        }

        SqliteConnection.ClearAllPools();

        await using var second = new SqliteVecMemoryStore(path, vectorDimensions: Dimensions);
        var restarted = CreateService(second);

        var current = await restarted.GetAsync("u", "identity:user:name", Ct);
        Assert.NotNull(current);
        Assert.Equal("User's name is Mike", current.Value);
        Assert.Equal(2, current.Version);
        Assert.Equal(0.8f, current.Confidence, 3);

        var history = await restarted.GetHistoryAsync("u", "identity:user:name", Ct);
        Assert.Equal(["User's name is Mike", "User's name is John"], history.Select(e => e.Value));
        Assert.False(history[1].IsActive);
        Assert.NotNull(history[1].ValidTo);
        Assert.Equal(2, history[1].ConfirmationCount);
        Assert.Equal("s-1", history[1].Metadata["evidence_2"]);

        var drink = await restarted.GetByCategoryAsync("u", SemanticStoreCategory.Preference, Ct);
        Assert.Equal("User prefers tea", Assert.Single(drink).Value);
        Assert.True(await restarted.HasProfileAsync("u", Ct));
        Assert.Equal(3, (await restarted.GetStatsAsync("u", Ct)).TotalEntries);
        Assert.NotEmpty(await restarted.SearchAsync("u", "tea", cancellationToken: Ct));

        Assert.Equal(3, await restarted.RemoveAllAsync("u", Ct));
        Assert.False(await restarted.HasProfileAsync("u", Ct));
    }

    private ArchiveStoreService CreateService(IMemoryStore store) => new(
        store, _embedding, Options.Create(new SemanticStoreOptions()), NullLogger<ArchiveStoreService>.Instance);

    private sealed class StoreHolder : IAsyncDisposable
    {
        private readonly TranslatingInMemoryVectorStore? _vectorStore;

        public StoreHolder(string kind, string path)
        {
            switch (kind)
            {
                case "sqlite":
                    Store = new SqliteVecMemoryStore(path, vectorDimensions: Dimensions);
                    break;
                case "vectorstore":
                    _vectorStore = new TranslatingInMemoryVectorStore();
                    Store = new VectorStoreMemoryStore(
                        _vectorStore,
                        new VectorStoreMemoryStoreOptions { Dimensions = Dimensions },
                        NullLogger<VectorStoreMemoryStore>.Instance);
                    break;
                default:
                    Store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
                    break;
            }
        }

        public IMemoryStore Store { get; }

        public async ValueTask DisposeAsync()
        {
            if (Store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            _vectorStore?.Dispose();
        }
    }
}
