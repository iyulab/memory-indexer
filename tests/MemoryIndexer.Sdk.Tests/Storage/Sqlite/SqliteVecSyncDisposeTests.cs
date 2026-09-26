using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.Sqlite;

/// <summary>
/// A container disposed with <c>Dispose()</c> throws on a singleton that is only <see cref="IAsyncDisposable"/>. The
/// SQLite store implements both, so a console app or test that builds its provider with <c>using</c> exits cleanly.
/// </summary>
public sealed class SqliteVecSyncDisposeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mi-syncdispose-{Guid.NewGuid():N}.db");

    [Fact]
    public void Container_WithSqliteStoreResolved_DisposesSynchronously()
    {
        var services = new ServiceCollection();
        services.AddMemoryIndexer(_ => { }).WithSqliteVec(_dbPath);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IMemoryStore>();

        var dispose = Record.Exception(provider.Dispose);

        Assert.Null(dispose);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
