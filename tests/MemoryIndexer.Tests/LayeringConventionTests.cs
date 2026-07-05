using System.Reflection;
using FluentAssertions;
using Xunit;

namespace MemoryIndexer.Tests;

/// <summary>
/// Regression teeth for the umbrella layering rule (docs/LAYERING.md): memory-indexer is a
/// leaf module — its shipped assemblies must not reference other iyulab package groups
/// (Flux.*, FluxIndex.*, FileFlux, IronHive.*). A Flux.Abstractions edge existed until
/// 0.16.0 (tier inversion, stale pin) and must not come back.
/// </summary>
public class LayeringConventionTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Flux.",
        "FluxIndex",
        "FileFlux",
        "WebFlux",
        "IronHive",
        "LMSupply",
        "TokenMeter",
        "IndexThinking",
    ];

    [Theory]
    [InlineData(typeof(MemoryIndexer.Interfaces.ITextCompletionService))] // MemoryIndexer assembly
    public void Shipped_Assemblies_Do_Not_Reference_Other_Iyulab_Groups(Type anchor)
    {
        var references = anchor.Assembly.GetReferencedAssemblies();

        foreach (var reference in references)
        {
            ForbiddenPrefixes.Should().NotContain(
                prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal),
                $"leaf module assembly '{anchor.Assembly.GetName().Name}' must not reference '{reference.Name}'");
        }
    }
}
