using McpServer;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// The bundled MCP server answers <c>Embedding:Provider = Custom</c> (the library's default) with its own in-process
/// LMSupply embedder, so the server needs no external embedding service; Mock stays the library's. A catalog model whose
/// vector size differs from the configured one stops the server at start rather than at the first stored memory.
/// No model is downloaded here.
/// </summary>
public class ServerEmbedderTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServerEmbedder(configuration);
        services.AddMemoryIndexer();
        services.Configure<MemoryIndexerOptions>(configuration.GetSection(MemoryIndexerOptions.SectionName));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Custom")]
    [InlineData(null)]
    public void CustomOrUnset_IsTheServersLocalEmbedder(string? provider)
    {
        using var services = Build(new()
        {
            ["MemoryIndexer:Embedding:Provider"] = provider,
            ["MemoryIndexer:Embedding:Model"] = "default",
            ["MemoryIndexer:Embedding:Dimensions"] = "1024",
        });

        var embedder = services.GetRequiredService<IEmbeddingService>();

        Assert.IsType<LocalEmbeddingService>(embedder);
        Assert.Equal(1024, embedder.Dimensions);
    }

    [Fact]
    public void Mock_StaysTheLibrarysMock()
    {
        using var services = Build(new() { ["MemoryIndexer:Embedding:Provider"] = "Mock", ["MemoryIndexer:Embedding:Dimensions"] = "384" });

        Assert.IsNotType<LocalEmbeddingService>(services.GetRequiredService<IEmbeddingService>());
    }

    [Fact]
    public void ACatalogModelOfAnotherSize_StopsTheServerAtStart_NamingBothSizes()
    {
        using var services = Build(new()
        {
            ["MemoryIndexer:Embedding:Provider"] = "Custom",
            ["MemoryIndexer:Embedding:Model"] = "default",
            ["MemoryIndexer:Embedding:Dimensions"] = "768",
        });

        var error = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IEmbeddingService>());

        Assert.Contains("1024", error.Message, StringComparison.Ordinal);
        Assert.Contains("768", error.Message, StringComparison.Ordinal);
    }
}
