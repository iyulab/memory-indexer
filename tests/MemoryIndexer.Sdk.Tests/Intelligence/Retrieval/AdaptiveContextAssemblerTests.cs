using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Retrieval;

public class AdaptiveContextAssemblerTests
{
    private readonly IAdaptiveContextAssembler _assembler;

    public AdaptiveContextAssemblerTests()
    {
        _assembler = new AdaptiveContextAssembler(
            NullLogger<AdaptiveContextAssembler>.Instance);
    }

    #region AssembleAsync Tests

    [Fact]
    public async Task AssembleAsync_WithEmptyResults_ReturnsEmptyContext()
    {
        // Arrange
        var result = CreateRetrievalResult([]);

        // Act
        var context = await _assembler.AssembleAsync(result);

        // Assert
        context.MemoryCount.Should().Be(0);
        context.TokenCount.Should().Be(0);
        context.WasTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task AssembleAsync_WithFullFidelityMemories_IncludesCompleteContent()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("This is the complete content.", MemoryTier.Working, ContextFidelity.Full)
        };
        var result = CreateRetrievalResult(memories);

        // Act
        var context = await _assembler.AssembleAsync(result);

        // Assert
        context.Content.Should().Contain("This is the complete content.");
        context.MemoryCount.Should().Be(1);
        context.FidelityBreakdown[ContextFidelity.Full].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AssembleAsync_WithCompressedFidelity_ReturnsCompressedContent()
    {
        // Arrange
        var longContent = "This is the first sentence. This is the second sentence. This is the third sentence.";
        var memories = new[]
        {
            CreateScoredMemory(longContent, MemoryTier.Session, ContextFidelity.Compressed)
        };
        var result = CreateRetrievalResult(memories);

        // Act
        var context = await _assembler.AssembleAsync(result);

        // Assert
        context.Content.Should().Contain("first sentence");
        context.Content.Should().Contain("more sentences");
        context.Statistics.CompressionCount.Should().Be(1);
    }

    [Fact]
    public async Task AssembleAsync_WithPlaceholderFidelity_ReturnsMinimalReference()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("This is a detailed memory content.", MemoryTier.User, ContextFidelity.Placeholder)
        };
        var result = CreateRetrievalResult(memories);

        // Act
        var context = await _assembler.AssembleAsync(result);

        // Assert
        context.Content.Should().Contain("[");
        context.Content.Should().Contain("]");
        context.FidelityBreakdown[ContextFidelity.Placeholder].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AssembleAsync_WithMixedFidelities_AssemblesAllLevels()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("Full content here.", MemoryTier.Working, ContextFidelity.Full),
            CreateScoredMemory("Compressed content here. More sentences follow.", MemoryTier.Session, ContextFidelity.Compressed),
            CreateScoredMemory("Placeholder content here.", MemoryTier.User, ContextFidelity.Placeholder)
        };
        var result = CreateRetrievalResult(memories);

        // Act
        var context = await _assembler.AssembleAsync(result);

        // Assert
        context.MemoryCount.Should().Be(3);
        context.FidelityBreakdown[ContextFidelity.Full].Should().BeGreaterThan(0);
        context.FidelityBreakdown[ContextFidelity.Compressed].Should().BeGreaterThan(0);
        context.FidelityBreakdown[ContextFidelity.Placeholder].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AssembleAsync_WithTierHeaders_IncludesTierInformation()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("Content", MemoryTier.Working, ContextFidelity.Full)
        };
        var result = CreateRetrievalResult(memories);
        var options = new ContextAssemblyOptions { IncludeTierHeaders = true };

        // Act
        var context = await _assembler.AssembleAsync(result, options);

        // Assert
        context.Content.Should().Contain("Working");
    }

    [Fact]
    public async Task AssembleAsync_WithGraphContext_IncludesGraphInformation()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("Content", MemoryTier.Session, ContextFidelity.Full)
        };
        var result = CreateRetrievalResultWithGraph(
            memories,
            "## Knowledge Graph\n- Entity1 relates_to Entity2");
        var options = new ContextAssemblyOptions { IncludeGraphContext = true };

        // Act
        var context = await _assembler.AssembleAsync(result, options);

        // Assert
        context.Content.Should().Contain("Knowledge Graph");
        context.Content.Should().Contain("Entity1");
    }

    [Fact]
    public async Task AssembleAsync_WithCustomHeaderFooter_IncludesThem()
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("Content", MemoryTier.Working, ContextFidelity.Full)
        };
        var result = CreateRetrievalResult(memories);
        var options = new ContextAssemblyOptions
        {
            CustomHeader = "=== Memory Context ===",
            CustomFooter = "=== End of Context ==="
        };

        // Act
        var context = await _assembler.AssembleAsync(result, options);

        // Assert
        context.Content.Should().StartWith("=== Memory Context ===");
        context.Content.Should().EndWith("=== End of Context ===" + Environment.NewLine);
    }

    [Fact]
    public async Task AssembleAsync_ExceedingBudget_TruncatesAndReportsExclusion()
    {
        // Arrange
        var largeContent = new string('A', 1000);
        var memories = Enumerable.Range(0, 20)
            .Select(i => CreateScoredMemory(largeContent, MemoryTier.Session, ContextFidelity.Full))
            .ToArray();
        var result = CreateRetrievalResult(memories);
        var options = new ContextAssemblyOptions { MaxTokens = 500 };

        // Act
        var context = await _assembler.AssembleAsync(result, options);

        // Assert
        context.WasTruncated.Should().BeTrue();
        context.ExcludedCount.Should().BeGreaterThan(0);
        context.TokenCount.Should().BeLessThanOrEqualTo(500);
    }

    #endregion

    #region CompressAsync Tests

    [Fact]
    public async Task CompressAsync_FullFidelity_ReturnsOriginalContent()
    {
        // Arrange
        var memory = CreateMemory("Complete original content here.");

        // Act
        var compressed = await _assembler.CompressAsync(memory, ContextFidelity.Full);

        // Assert
        compressed.Should().Be("Complete original content here.");
    }

    [Fact]
    public async Task CompressAsync_CompressedFidelity_TruncatesLongContent()
    {
        // Arrange
        var memory = CreateMemory("First sentence here. Second sentence here. Third sentence here.");

        // Act
        var compressed = await _assembler.CompressAsync(memory, ContextFidelity.Compressed);

        // Assert
        compressed.Should().Contain("First sentence");
        compressed.Should().Contain("more sentences");
        compressed.Length.Should().BeLessThan(memory.Content.Length);
    }

    [Fact]
    public async Task CompressAsync_PlaceholderFidelity_ReturnsMinimalFormat()
    {
        // Arrange
        var memory = CreateMemory("This is a long piece of content that should be reduced to a placeholder.");

        // Act
        var compressed = await _assembler.CompressAsync(memory, ContextFidelity.Placeholder);

        // Assert
        compressed.Should().StartWith("[");
        compressed.Should().EndWith("]");
        // Should contain temporal indicator (today, ago, etc.)
        compressed.Should().MatchRegex(@"(today|ago)");
    }

    #endregion

    #region EstimateTokens Tests

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        // Act
        var tokens = _assembler.EstimateTokens("");

        // Assert
        tokens.Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_NullString_ReturnsZero()
    {
        // Act
        var tokens = _assembler.EstimateTokens(null!);

        // Assert
        tokens.Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_ShortString_ReturnsReasonableEstimate()
    {
        // Arrange
        var content = "Hello world"; // 11 characters

        // Act
        var tokens = _assembler.EstimateTokens(content);

        // Assert
        tokens.Should().BeInRange(2, 4); // ~4 chars per token
    }

    [Fact]
    public void EstimateTokens_LongString_ScalesAppropriately()
    {
        // Arrange
        var content = new string('A', 400); // 400 characters

        // Act
        var tokens = _assembler.EstimateTokens(content);

        // Assert
        tokens.Should().BeInRange(90, 110); // ~100 tokens for 400 chars
    }

    #endregion

    #region Format Tests

    [Theory]
    [InlineData(ContextFormat.Markdown)]
    [InlineData(ContextFormat.PlainText)]
    [InlineData(ContextFormat.Xml)]
    [InlineData(ContextFormat.Json)]
    public async Task AssembleAsync_DifferentFormats_ProducesValidOutput(ContextFormat format)
    {
        // Arrange
        var memories = new[]
        {
            CreateScoredMemory("Test content", MemoryTier.Session, ContextFidelity.Full)
        };
        var result = CreateRetrievalResult(memories);
        var options = new ContextAssemblyOptions { Format = format };

        // Act
        var context = await _assembler.AssembleAsync(result, options);

        // Assert
        context.Content.Should().NotBeNullOrEmpty();
        context.MemoryCount.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static TieredRetrievalResult CreateRetrievalResult(ScoredMemory[] memories)
    {
        return new TieredRetrievalResult
        {
            Query = "test query",
            Intent = new QueryIntentResult
            {
                Intent = QueryIntent.General,
                Confidence = 0.5f,
                Specificity = 0.5f
            },
            MergedResults = memories
        };
    }

    private static TieredRetrievalResult CreateRetrievalResultWithGraph(
        ScoredMemory[] memories,
        string graphContext)
    {
        return new TieredRetrievalResult
        {
            Query = "test query",
            Intent = new QueryIntentResult
            {
                Intent = QueryIntent.General,
                Confidence = 0.5f,
                Specificity = 0.5f
            },
            MergedResults = memories,
            GraphContext = new GraphRetrievalContext
            {
                FormattedContext = graphContext
            }
        };
    }

    private static ScoredMemory CreateScoredMemory(
        string content,
        MemoryTier tier,
        ContextFidelity fidelity)
    {
        return new ScoredMemory
        {
            Memory = CreateMemory(content),
            SimilarityScore = 0.8f,
            RelevanceScore = 0.75f,
            SourceTier = tier,
            EstimatedTokens = content.Length / 4,
            Fidelity = fidelity
        };
    }

    private static MemoryUnit CreateMemory(string content)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "test-user",
            Content = content,
            Type = MemoryType.Fact,
            Tier = MemoryTier.Session,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
    }

    #endregion
}
