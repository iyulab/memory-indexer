using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Intelligence.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests;

public sealed class HydeQueryExpanderTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly HydeQueryExpander _expander;

    public HydeQueryExpanderTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) =>
            {
                // Return a mock embedding (normalized)
                var hash = text.GetHashCode();
                var embedding = new float[384];
                for (var i = 0; i < embedding.Length; i++)
                {
                    embedding[i] = ((hash + i) % 100) / 100f;
                }
                // Normalize
                var norm = MathF.Sqrt(embedding.Sum(x => x * x));
                if (norm > 0)
                {
                    for (var i = 0; i < embedding.Length; i++)
                    {
                        embedding[i] /= norm;
                    }
                }
                return new ReadOnlyMemory<float>(embedding);
            });

        _mockEmbeddingService
            .Setup(x => x.GenerateBatchEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
            {
                var results = new List<ReadOnlyMemory<float>>();
                foreach (var text in texts)
                {
                    var hash = text.GetHashCode();
                    var embedding = new float[384];
                    for (var i = 0; i < embedding.Length; i++)
                    {
                        embedding[i] = ((hash + i) % 100) / 100f;
                    }
                    var norm = MathF.Sqrt(embedding.Sum(x => x * x));
                    if (norm > 0)
                    {
                        for (var i = 0; i < embedding.Length; i++)
                        {
                            embedding[i] /= norm;
                        }
                    }
                    results.Add(new ReadOnlyMemory<float>(embedding));
                }
                return results;
            });

        _expander = new HydeQueryExpander(
            _mockEmbeddingService.Object,
            NullLogger<HydeQueryExpander>.Instance);
    }

    [Theory]
    [InlineData("Who is the project manager?", "person")]
    [InlineData("What is the API endpoint?", "API endpoint")]
    [InlineData("When is the deadline?", "happened")]
    [InlineData("Where is the configuration file?", "located")]
    [InlineData("Why does the test fail?", "reason")]
    [InlineData("How do I deploy the application?", "need to")]
    public void GenerateHypotheticalDocument_ShouldMatchQueryPattern(string query, string expectedContent)
    {
        // Act
        var hypothetical = _expander.GenerateHypotheticalDocument(query);

        // Assert
        Assert.NotEmpty(hypothetical);
        Assert.NotEqual(query, hypothetical); // Should transform the query
        Assert.Contains(expectedContent, hypothetical, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateHypotheticalDocument_WithEmptyQuery_ReturnsEmpty()
    {
        // Act
        var result = _expander.GenerateHypotheticalDocument("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GenerateHypotheticalDocument_WithWhitespace_ReturnsWhitespace()
    {
        // Act
        var result = _expander.GenerateHypotheticalDocument("   ");

        // Assert
        Assert.Equal("   ", result);
    }

    [Theory]
    [InlineData("What does the user prefer?", "prefer")]
    [InlineData("What is your favorite color?", "favorite")] // "favorite" is preserved in output
    [InlineData("Do you like Python or JavaScript?", "prefers")] // Transformed to preference statement
    public void GenerateHypotheticalDocument_WithPreferenceQuery_ContainsPreference(string query, string expectedPattern)
    {
        // Act
        var hypothetical = _expander.GenerateHypotheticalDocument(query);

        // Assert
        Assert.Contains(expectedPattern, hypothetical, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateMultipleHypotheticalDocuments_ReturnsRequestedCount()
    {
        // Arrange
        var query = "What is the best approach for API design?";

        // Act
        var documents = _expander.GenerateMultipleHypotheticalDocuments(query, 3);

        // Assert
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d => Assert.NotEmpty(d));
        Assert.All(documents, d => Assert.NotEqual(query, d));
    }

    [Fact]
    public void GenerateMultipleHypotheticalDocuments_WithCountOne_ReturnsSingleDocument()
    {
        // Arrange
        var query = "How do I configure the database?";

        // Act
        var documents = _expander.GenerateMultipleHypotheticalDocuments(query, 1);

        // Assert
        Assert.Single(documents);
    }

    [Fact]
    public void GenerateMultipleHypotheticalDocuments_ProducesVariety()
    {
        // Arrange
        var query = "What is the project architecture?";

        // Act
        var documents = _expander.GenerateMultipleHypotheticalDocuments(query, 3);

        // Assert - Documents should not all be identical
        var uniqueDocuments = documents.Distinct().Count();
        Assert.True(uniqueDocuments >= 2, "Expected at least 2 unique hypothetical documents");
    }

    [Fact]
    public async Task GenerateHypotheticalEmbeddingAsync_CallsEmbeddingService()
    {
        // Arrange
        var query = "Who manages the deployment pipeline?";

        // Act
        var embedding = await _expander.GenerateHypotheticalEmbeddingAsync(query);

        // Assert
        Assert.Equal(384, embedding.Length);
        _mockEmbeddingService.Verify(
            x => x.GenerateEmbeddingAsync(It.Is<string>(s => s != query), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateMultipleHypotheticalEmbeddingsAsync_ReturnsMultipleEmbeddings()
    {
        // Arrange
        var query = "What features are planned for next release?";

        // Act
        var embeddings = await _expander.GenerateMultipleHypotheticalEmbeddingsAsync(query, 3);

        // Assert
        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(384, e.Length));
    }

    [Theory]
    [InlineData("Is this feature complete?")]
    [InlineData("Are there any pending issues?")]
    [InlineData("Do you support OAuth?")]
    [InlineData("Can the system scale?")]
    public void GenerateHypotheticalDocument_WithYesNoQuestion_ConvertsToStatement(string query)
    {
        // Act
        var hypothetical = _expander.GenerateHypotheticalDocument(query);

        // Assert
        Assert.NotEmpty(hypothetical);
        Assert.DoesNotContain("?", hypothetical); // Should be a statement, not a question
    }

    [Fact]
    public void GenerateHypotheticalDocument_ExtractsProperNouns()
    {
        // Arrange
        var query = "Who is John Smith on the team?";

        // Act
        var hypothetical = _expander.GenerateHypotheticalDocument(query);

        // Assert
        Assert.Contains("John", hypothetical);
    }

    [Theory]
    [InlineData("Tell me about the API")] // Not a question word
    [InlineData("Explain the architecture")] // Imperative
    [InlineData("The system configuration")] // Noun phrase
    public void GenerateHypotheticalDocument_WithNonQuestionQuery_StillGenerates(string query)
    {
        // Act
        var hypothetical = _expander.GenerateHypotheticalDocument(query);

        // Assert
        Assert.NotEmpty(hypothetical);
    }
}
