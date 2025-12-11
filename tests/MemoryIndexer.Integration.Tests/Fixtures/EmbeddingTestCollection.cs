using Xunit;

namespace MemoryIndexer.Integration.Tests.Fixtures;

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
public class EmbeddingTestCollection : ICollectionFixture<SharedEmbeddingFixture>
{
    /// <summary>
    /// The name of the test collection.
    /// </summary>
    public const string Name = "Embedding Tests";
}
