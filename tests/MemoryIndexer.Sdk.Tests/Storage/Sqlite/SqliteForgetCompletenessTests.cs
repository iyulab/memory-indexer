using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.Sqlite;

/// <summary>
/// Forgetting a user removes the text from the database files, not only from query results: deleted pages are
/// overwritten, the full-text index forgets the tokens, and the write-ahead log is truncated.
/// </summary>
public class SqliteForgetCompletenessTests : IDisposable
{
    private const string Marker = "FORGETME7f3a9c2bQZ";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mi-forget-{Guid.NewGuid():N}");

    public SqliteForgetCompletenessTests() => Directory.CreateDirectory(_dir);

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

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HardDeleteOfAUser_LeavesNoTextInTheDatabaseFiles()
    {
        var path = Path.Combine(_dir, "secure.db");
        await StoreThenForgetAsync(path, secureDelete: true);

        Assert.False(FilesContainMarker(path), "the forgotten text is still readable in the database files");
    }

    [Fact]
    public async Task WithoutSecureDelete_TheTextStaysInFreePages_PositiveControl()
    {
        var path = Path.Combine(_dir, "plain.db");
        await StoreThenForgetAsync(path, secureDelete: false);

        Assert.True(FilesContainMarker(path), "control: without secure delete the freed pages should still hold the text");
    }

    [Fact]
    public async Task ConnectionOpened_RunsBeforeTheStoreUsesTheConnection()
    {
        var calls = 0;
        await using var store = new SqliteVecMemoryStore(
            Path.Combine(_dir, "hook.db"), vectorDimensions: 4,
            connectionOpened: connection =>
            {
                calls++;
                Assert.Equal(System.Data.ConnectionState.Open, connection.State);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 42;";
                command.ExecuteNonQuery();
            });

        await store.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);
        await store.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
    }

    private static async Task StoreThenForgetAsync(string path, bool secureDelete)
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var store = new SqliteVecMemoryStore(path, vectorDimensions: 4,
                         new SqliteOptions { SecureDelete = secureDelete, EnableAutoMaintenance = false }))
        {
            for (var i = 0; i < 20; i++)
            {
                await store.StoreAsync(new MemoryUnit
                {
                    UserId = "alice",
                    Content = $"Alice said {Marker} number {i}",
                    Embedding = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]),
                }, ct);
            }

            await store.StoreAsync(new MemoryUnit
            {
                UserId = "bob",
                Content = "Bob keeps his memory",
                Embedding = new ReadOnlyMemory<float>([0.4f, 0.3f, 0.2f, 0.1f]),
            }, ct);

            Assert.Equal(20, await store.DeleteByUserAsync("alice", hardDelete: true, ct));
        }

        SqliteConnection.ClearAllPools();
    }

    private static bool FilesContainMarker(string path)
    {
        var marker = Encoding.UTF8.GetBytes(Marker);
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file) && File.ReadAllBytes(file).AsSpan().IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
