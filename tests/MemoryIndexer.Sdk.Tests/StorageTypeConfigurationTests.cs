using MemoryIndexer.Configuration;
using MemoryIndexer.InMemory;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Storage.Sqlite;
using MemoryIndexer.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// <c>MemoryIndexer:Storage:Type</c> chooses the store <c>AddMemoryIndexer</c> registers; a value that is not a
/// built-in store stops the host at startup instead of failing the first request.
/// </summary>
public class StorageTypeConfigurationTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"mi-storage-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _db, _db + "-wal", _db + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // The store may still hold the file on some platforms; the temp directory is cleaned eventually
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NoStorageType_RegistersTheInMemoryStore()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<InMemoryMemoryStore>(provider.GetRequiredService<IMemoryStore>());
    }

    [Fact]
    public void StorageTypeSqliteVec_RegistersTheSqliteStore_AtTheConfiguredPath()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["MemoryIndexer:Storage:Type"] = "SqliteVec",
            ["MemoryIndexer:Storage:ConnectionString"] = _db,
            ["MemoryIndexer:Storage:VectorDimensions"] = "8",
        });

        var store = provider.GetRequiredService<IMemoryStore>();

        Assert.IsType<SqliteVecMemoryStore>(store);
        (store as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task UnknownStorageType_FailsTheHostAtStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MemoryIndexer:Storage:Type"] = "Qdrant",
        });
        builder.Services.AddMemoryIndexer();
        using var host = builder.Build();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Fingerprint_IsStableAndDoesNotCarryTheValue()
    {
        var a = LogRedaction.Fingerprint("personal:alex:lives_in");

        Assert.Equal(a, LogRedaction.Fingerprint("personal:alex:lives_in"));
        Assert.NotEqual(a, LogRedaction.Fingerprint("personal:sam:lives_in"));
        Assert.Equal(12, a.Length);
        Assert.DoesNotContain("alex", a, StringComparison.Ordinal);
        Assert.Equal(a, new FingerprintedValue("personal:alex:lives_in").ToString());
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddMemoryIndexer();
        return services.BuildServiceProvider();
    }
}
