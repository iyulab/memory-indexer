using MemoryIndexer.Intelligence.Search;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public class BM25IndexTests
{
    [Fact]
    public void AddDocument_ShouldIndexContent()
    {
        // Arrange
        var index = new BM25Index();
        var id = Guid.NewGuid();

        // Act
        index.AddDocument(id, "The quick brown fox jumps over the lazy dog");

        // Assert
        var results = index.Search("fox", 10);
        Assert.Single(results);
        Assert.Equal(id, results[0].Id);
    }

    [Fact]
    public void Search_ShouldReturnRelevantDocuments()
    {
        // Arrange
        var index = new BM25Index();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        index.AddDocument(id1, "Machine learning is a subset of artificial intelligence");
        index.AddDocument(id2, "Deep learning uses neural networks");
        index.AddDocument(id3, "Natural language processing is important for AI");

        // Act
        var results = index.Search("machine learning neural", 10);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Id == id1 || r.Id == id2);
    }

    [Fact]
    public void Search_ShouldRankByRelevance()
    {
        // Arrange
        var index = new BM25Index();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        index.AddDocument(id1, "Python programming language for data science");
        index.AddDocument(id2, "Python Python Python is a programming language");

        // Act
        var results = index.Search("Python programming", 10);

        // Assert - id2 should rank higher due to more Python occurrences
        Assert.Equal(2, results.Count);
        Assert.Equal(id2, results[0].Id);
    }

    [Fact]
    public void RemoveDocument_ShouldRemoveFromIndex()
    {
        // Arrange
        var index = new BM25Index();
        var id = Guid.NewGuid();
        index.AddDocument(id, "Test document content");

        // Act
        index.RemoveDocument(id);
        var results = index.Search("test document", 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_WithNoMatchingTerms_ShouldReturnEmpty()
    {
        // Arrange
        var index = new BM25Index();
        index.AddDocument(Guid.NewGuid(), "The quick brown fox");

        // Act
        var results = index.Search("zebra elephant", 10);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Search_ShouldRespectLimit()
    {
        // Arrange
        var index = new BM25Index();
        for (var i = 0; i < 20; i++)
        {
            index.AddDocument(Guid.NewGuid(), $"Document {i} about programming and coding");
        }

        // Act
        var results = index.Search("programming coding", 5);

        // Assert
        Assert.Equal(5, results.Count);
    }
}
