using MemoryIndexer.InMemory;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryIndexer.Sdk.Tests.Storage.InMemory;

public sealed class InMemoryMemoryStoreTests : MemoryStoreContractTests
{
    protected override IMemoryStore CreateStore() => new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
}
