using CommunityToolkit.VectorData.Qdrant;
using MemoryIndexer.Interfaces;
using MemoryIndexer.VectorData;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Testcontainers.Qdrant;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.VectorData;

/// <summary>
/// One Qdrant container and one collection for the class; each test starts from an emptied collection (creating a
/// collection with its payload indexes costs seconds, emptying one milliseconds).
/// </summary>
public sealed class QdrantContainerFixture : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.15.1").Build();
    private QdrantClient? _client;

    /// <summary>The collection every test of the class uses.</summary>
    public string CollectionName { get; } = $"memories_{Guid.NewGuid():N}";

    /// <summary>Why the container did not start, or null when it runs.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>The connector's store over the running container.</summary>
    public QdrantVectorStore VectorStore { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable = $"Docker is not available: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        _client = new QdrantClient(_container.Hostname, _container.GetMappedPublicPort(6334));
        VectorStore = new QdrantVectorStore(_client, ownsClient: false);
    }

    /// <summary>Removes every point, leaving the collection (and its indexes) in place.</summary>
    public async Task EmptyAsync(CancellationToken cancellationToken)
    {
        if (_client is not null && await _client.CollectionExistsAsync(CollectionName, cancellationToken))
            await _client.DeleteAsync(CollectionName, new Filter(), wait: true, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        VectorStore?.Dispose();
        _client?.Dispose();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// <see cref="VectorStoreMemoryStore"/> passes the store contract on a real Qdrant through the .NET Community Toolkit's
/// Qdrant connector — the translation the guard fake in <see cref="VectorStoreMemoryStoreTests"/> only approximates
/// (filters, the Guid key, list properties, paging, scores, a missing collection, a point without a vector). Needs
/// Docker; without it every test is skipped with the reason, so a run that must exercise Qdrant should treat that
/// skip as a failure.
/// </summary>
[Trait("Category", "Integration")]
public sealed class QdrantVectorStoreMemoryStoreContractTests(QdrantContainerFixture qdrant)
    : MemoryStoreContractTests, IClassFixture<QdrantContainerFixture>, IAsyncLifetime
{
    private const int Dimensions = 768;

    public async ValueTask InitializeAsync()
    {
        if (qdrant.Unavailable is null)
            await qdrant.EmptyAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected override IMemoryStore CreateStore()
    {
        if (qdrant.Unavailable is { } reason)
            Assert.Skip(reason);

        return new VectorStoreMemoryStore(
            qdrant.VectorStore,
            new VectorStoreMemoryStoreOptions { CollectionName = qdrant.CollectionName, Dimensions = Dimensions, PageSize = 2 },
            NullLogger<VectorStoreMemoryStore>.Instance);
    }
}
