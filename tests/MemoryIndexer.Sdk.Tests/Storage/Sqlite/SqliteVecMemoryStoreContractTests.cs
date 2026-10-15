using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryIndexer.Sdk.Tests.Storage.Sqlite;

/// <summary>The store contract, run against <see cref="SqliteVecMemoryStore"/> on a file of its own.</summary>
public sealed class SqliteVecMemoryStoreContractTests : MemoryStoreContractTests, IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mi-contract-{Guid.NewGuid():N}.db");
    private SqliteVecMemoryStore? _sqlite;

    protected override IMemoryStore CreateStore()
        => _sqlite = new SqliteVecMemoryStore(
            _path,
            vectorDimensions: 768,
            options: new SqliteOptions { UseWalMode = true, FtsTokenizer = "trigram", EnableFullTextSearch = true },
            logger: NullLogger<SqliteVecMemoryStore>.Instance);

    public async ValueTask DisposeAsync()
    {
        if (_sqlite is not null)
            await _sqlite.DisposeAsync();

        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try { File.Delete(file); }
            catch (IOException) { /* the temp directory is cleaned eventually */ }
        }
    }
}
