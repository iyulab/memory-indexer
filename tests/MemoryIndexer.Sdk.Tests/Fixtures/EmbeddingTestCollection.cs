using Xunit;

namespace MemoryIndexer.Sdk.Tests.Integration.Fixtures;

/// <summary>
/// Test collection definition for tests that require the shared embedding service.
/// All tests in this collection share a single embedding model instance, significantly
/// reducing test execution time and resource usage.
/// </summary>
/// <remarks>
/// Usage: Add [Collection(EmbeddingTestCollection.Name)] to test classes that need embeddings.
/// The embedding model (~90MB) is loaded once and shared across all tests in the collection.
/// </remarks>
[CollectionDefinition(Name)]
#pragma warning disable CA1711 // Collection — xUnit CollectionDefinition convention
public class EmbeddingTestCollection : ICollectionFixture<SharedEmbeddingFixture>
#pragma warning restore CA1711
{
    /// <summary>
    /// The name of the test collection.
    /// </summary>
    public const string Name = "Embedding Tests";
}
