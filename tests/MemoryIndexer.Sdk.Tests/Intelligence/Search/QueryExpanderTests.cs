using AwesomeAssertions;
using MemoryIndexer.Sdk.Intelligence.Search;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Search;

/// <summary>
/// Unit tests for <see cref="QueryExpander"/>. No embedding model or storage dependency — these
/// exercise the string-level expansion/variant logic directly.
/// </summary>
public class QueryExpanderTests
{
    private readonly QueryExpander _sut = new();

    // ISSUE-memory-indexer-20260824-010000: GenerateQueryVariants does a literal single-word
    // substring replace, so any synonym for "code" corrupted the compound noun "code review(s)"
    // (e.g. "code reviews" -> "program reviews") — confirmed deterministic, not a model-tuning
    // issue. Fixed by removing the "code" entry from SynonymMap rather than teaching the
    // substitution logic about compound nouns in general (out of scope for this fix).
    [Fact]
    public void GenerateQueryVariants_QueryContainingCode_DoesNotSubstituteCode()
    {
        var variants = _sut.GenerateQueryVariants("When are code reviews?", maxVariants: 3);

        variants.Should().NotContain(v => v.Contains("program", StringComparison.OrdinalIgnoreCase)
            || v.Contains("script", StringComparison.OrdinalIgnoreCase)
            || v.Contains("implementation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetSynonyms_Code_ReturnsEmpty()
    {
        _sut.GetSynonyms("code").Should().BeEmpty();
    }

    [Fact]
    public void ExpandQuery_QueryContainingCode_DoesNotAddCodeSynonyms()
    {
        var expanded = _sut.ExpandQuery("When are code reviews?");

        expanded.Should().NotContain("program")
            .And.NotContain("script")
            .And.NotContain("implementation");
    }

    [Fact]
    public void GenerateQueryVariants_AlwaysIncludesOriginalQuery()
    {
        var query = "What is my favorite color?";

        var variants = _sut.GenerateQueryVariants(query, maxVariants: 3);

        variants.Should().Contain(query);
    }

    [Fact]
    public void ExpandQuery_UnrecognizedSynonym_StillReturnsOriginalTerm()
    {
        // "bug" keeps its own SynonymMap entry (unaffected by this fix) — the additive path
        // should still surface a synonym for words other than "code".
        var expanded = _sut.ExpandQuery("What was the bug?");

        expanded.Should().Contain("issue");
    }
}
