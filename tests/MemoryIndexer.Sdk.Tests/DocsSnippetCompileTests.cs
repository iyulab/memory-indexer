using System.Reflection;
using Iyu.Conventions.Testing.Snippets;
using Microsoft.CodeAnalysis;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Compiles the C# blocks of the guides under <c>docs/</c> against the current assemblies. A reader copies these; a block
/// that no longer matches the shipped API fails here instead of in their build. The READMEs are compiled by
/// <see cref="ReadmeSnippetCompileTests"/>.
/// </summary>
/// <remarks>
/// Each guide compiles as a whole (<see cref="SnippetCompiler.CompileDocument"/>): a type one block declares is visible to
/// the next, each block's statements run in a method of their own, and a block that restates a library type is checked
/// against the real type (<see cref="SnippetCompiler.CheckListings"/>). A guide's blocks show no usings, so every namespace
/// the MemoryIndexer packages export is in scope, with the stand-ins below for values the surrounding text gives the reader. A
/// section that is deliberately not a program is in <see cref="Fragments"/> with the reason. A document with a C# block is
/// in <see cref="Checked"/>, in <see cref="Historical"/> or in <see cref="NotYetChecked"/> with the reason — never silently
/// in none.
/// </remarks>
public class DocsSnippetCompileTests
{
    // Guides whose every C# block compiles (or is a listed fragment).
    private static readonly string[] Checked =
    [
        "docs/ARCHITECTURE.md",
        "docs/EVALUATION.md",
        "docs/GUIDES.md",
        "docs/HEALTH.md",
        "docs/INTEGRATIONS.md",
        "docs/INTELLIGENCE.md",
        "samples/TwentyQuestionsGame/README.md",
    ];

    // Documents that show an API as it was, on purpose — a migration guide's «before», a decision record. They are not
    // compiled; their «after» code is what the guides above show.
    private static readonly Dictionary<string, string> Historical = new(StringComparer.Ordinal)
    {
    };

    // Guides with C# blocks that are not compiled yet, with the reason. Shrink this.
    private static readonly Dictionary<string, string> NotYetChecked = new(StringComparer.Ordinal)
    {
    };

    // A block that is deliberately not a program (a signature sketch, pseudocode), by the heading it sits under, with the
    // reason. Shrink this, never grow it silently.
    private static readonly SnippetRoster Fragments = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // GUIDES.md
        ["Helper Extensions"] = "a partial IMemoryStore implementation against the reader's own EF Core context; the members it does not show are elided",
        ["Hybrid Storage Pattern (PostgreSQL + Qdrant)"] = "a partial IMemoryStore on EF Core and Qdrant.Client, packages the library does not reference",
        ["Type Naming Considerations"] = "three alternatives side by side, with elided bodies («{ ... }») and a using alias that C# allows only at the top of a file",
        ["Load Testing"] = "an NBomber scenario, a package the library does not reference",
        // INTELLIGENCE.md
        ["Usage Examples"] = "MCP tool calls as a model sends them to the server (tool name and arguments), not C# a reader compiles",
        // INTEGRATIONS.md
        ["Basic Integration"] = "builds a Semantic Kernel (Microsoft.SemanticKernel), a package the library does not reference",
        ["Memory-Enhanced Chat Function"] = "a Semantic Kernel plugin (Microsoft.SemanticKernel), a package the library does not reference",
        ["Usage Example"] = "invokes the Semantic Kernel plugin above (Microsoft.SemanticKernel), a package the library does not reference",
        ["Memory-Backed Conversation Chain"] = "a LangChain chain (LangChain, LangChain.Providers.OpenAI), packages the library does not reference",
        ["Custom Memory Retriever"] = "a LangChain retriever (LangChain), a package the library does not reference",
        ["Memory-Enhanced Agent"] = "an AutoGen agent (AutoGen.Core, AutoGen.OpenAI), packages the library does not reference",
        ["Multi-Agent System with Shared Memory"] = "drives the AutoGen agent above (AutoGen.Core), a package the library does not reference",
    });

    private static readonly string[] AssembliesToLoad =
    [
        "MemoryIndexer", "MemoryIndexer.Sdk", "ModelContextProtocol", "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Hosting.Abstractions", "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.AI.Abstractions", "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Caching.Memory",
        // The ASP.NET Core host the health and middleware guides run in (the SDK's shared framework)
        "Microsoft.AspNetCore", "Microsoft.AspNetCore.Http.Abstractions", "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Diagnostics.HealthChecks", "Microsoft.Extensions.Diagnostics.HealthChecks",
        "Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions",
        // The OpenTelemetry packages the SDK's observability registration builds on
        "OpenTelemetry", "OpenTelemetry.Api", "OpenTelemetry.Api.ProviderBuilderExtensions", "OpenTelemetry.Extensions.Hosting",
        "OpenTelemetry.Exporter.OpenTelemetryProtocol",
        "Microsoft.Extensions.Logging",
        // The test frameworks the testing guide's examples are written in (this test project's own)
        "xunit.v3.core", "xunit.v3.assert", "NSubstitute",
    ];

    private static readonly string[] Usings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Text.Json",
        "System.Threading", "System.Threading.Tasks", "System.Runtime.CompilerServices", "System.Collections.Concurrent",
        "Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Diagnostics.HealthChecks", "Microsoft.AspNetCore.Builder",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.AI",
        .. AssembliesToLoad.Where(n => n.StartsWith("MemoryIndexer", StringComparison.Ordinal))
            .SelectMany(n => Assembly.Load(n).GetExportedTypes())
            .Select(t => t.Namespace)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    // Values the guides' running text gives the reader, typed as the API takes them.
    private static readonly Dictionary<string, string> StandIns = new(StringComparer.Ordinal)
    {
        ["services"] = "IServiceCollection services = new ServiceCollection();",
        ["serviceProvider"] = "IServiceProvider serviceProvider = null!;",
        ["cancellationToken"] = "CancellationToken cancellationToken = default;",
        // An ASP.NET Core host's arguments, builder and app, as the health and middleware guides have them.
        ["args"] = "string[] args = [];",
        ["builder"] = "Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = null!;",
        ["app"] = "Microsoft.AspNetCore.Builder.WebApplication app = null!;",
        // The caller's identifiers and texts.
        ["userId"] = "string userId = \"user-1\";",
        ["sessionId"] = "string sessionId = \"session-1\";",
        ["query"] = "string query = \"user preferences\";",
        ["content"] = "string content = \"User prefers dark mode\";",
        ["items"] = "string[] items = [\"User prefers dark mode\", \"User speaks Korean\"];",
        ["_logger"] = "ILogger _logger = NullLogger.Instance;",
        ["logger"] = "ILogger logger = NullLogger.Instance;",
        // The library's services, resolved from DI under the names the guides use.
        ["memory"] = "IMemoryService memory = null!;",
        ["primitives"] = "IMemoryPrimitives primitives = null!;",
        ["memoryPrimitives"] = "IMemoryPrimitives memoryPrimitives = null!;",
        ["store"] = "IMemoryStore store = null!;",
        ["archiveStore"] = "IArchiveStore archiveStore = null!;",
        ["contextBuilder"] = "IContextBuilder contextBuilder = null!;",
        ["runner"] = "NiahTestRunner runner = null!;",
        ["tests"] = "CognitiveScenarioTests tests = null!;",
        ["evaluator"] = "IEvaluationService evaluator = null!;",
        // The reader's own embedding service and store, which the registration guides register by type.
        ["MyOpenAIEmbeddingService"] = "public sealed class MyOpenAIEmbeddingService : IEmbeddingService { public int Dimensions => 1536; public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) => throw new NotImplementedException(); }",
        ["MyPostgresMemoryStore"] = "public sealed class MyPostgresMemoryStore : IMemoryStore { "
            + "public Task<MemoryUnit> StoreAsync(MemoryUnit memory, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyList<MemoryUnit>> StoreBatchAsync(IEnumerable<MemoryUnit> memories, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<MemoryUnit?> GetByIdAsync(string userId, Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyList<MemoryUnit>> GetByIdsAsync(string userId, IEnumerable<Guid> ids, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<bool> UpdateAsync(MemoryUnit memory, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<bool> DeleteAsync(string userId, Guid id, bool hardDelete = false, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<int> DeleteByUserAsync(string userId, bool hardDelete = false, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<int> DeleteBySessionAsync(string userId, string sessionId, bool hardDelete = false, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<int> DeleteByNamespaceAsync(string userId, string namespaceName, bool hardDelete = false, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, MemorySearchOptions options, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyList<MemoryUnit>> GetAllAsync(string userId, MemoryFilterOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<long> GetCountAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyList<string>> GetUserIdsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task<IReadOnlyDictionary<MemoryType, int>> GetTypeCountsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException(); "
            + "public Task DeleteCollectionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException(); }",
    };

    private static readonly IReadOnlyList<MetadataReference> References =
        SnippetCompiler.ReferencesFor([.. AssembliesToLoad.Select(Assembly.Load)]);

    private static readonly SnippetCompiler Compiler = new(References) { Usings = Usings, StandIns = StandIns, CheckListings = true };

    private static readonly IReadOnlyList<DocSnippet> Snippets =
        [.. Checked.SelectMany(d => DocSnippets.Read(Path.Combine(RepositoryRoot(), d), d))];

    private static readonly Dictionary<string, Lazy<DocumentCompilation>> Documents = Checked.ToDictionary(
        d => d,
        d => new Lazy<DocumentCompilation>(() => Compiler.CompileDocument(
            [.. Snippets.Where(s => s.File == d && !Fragments.IsFragment(s))])),
        StringComparer.Ordinal);

    public static TheoryData<string> Blocks() => new(Snippets.Select(s => s.Key));

    public static TheoryData<string> CheckedDocuments() => new(Checked);

    [Theory]
    [MemberData(nameof(Blocks))]
    public void DocsBlock_Compiles(string key)
    {
        var snippet = Snippets.Single(s => s.Key == key);
        if (!Fragments.IsFragment(snippet))
            Documents[snippet.File].Value.ShouldCompile(snippet);
    }

    /// <summary>A document's type stand-ins compile against the current API (they belong to no single example).</summary>
    [Theory]
    [MemberData(nameof(CheckedDocuments))]
    public void Document_HasNoErrorsOutsideItsExamples(string document) => Documents[document].Value.ShouldHaveNoSharedErrors();

    [Fact]
    public void EveryCheckedBlock_IsFoundAndFragmentsNameRealHeadings() => Fragments.ShouldMatch([.. Snippets], atLeast: 92);

    /// <summary>
    /// The guides' type listings (an interface shown with its members) are checked against the real types, not compiled as a
    /// second type of the same name. A floor, so a listing the kit stops recognising shows here instead of passing unchecked.
    /// </summary>
    [Fact]
    public void TypeListings_AreCheckedAgainstTheRealTypes()
    {
        var listed = Checked.Sum(d => Documents[d].Value.ListedTypes);
        Assert.True(listed >= 4, $"expected the guides' type listings to be checked, found {listed}");
    }

    /// <summary>
    /// Positive control: the compiler rejects the validation call the configuration guide showed before it was compiled — a
    /// <c>ValidateOnStart()</c> chained on the builder <c>AddMemoryIndexer</c> returns, which has no such method. A name check
    /// passed it, because a method of that name exists elsewhere (<c>OptionsBuilderExtensions.ValidateOnStart</c>).
    /// </summary>
    [Fact]
    public void Compile_RejectsTheValidationCallTheGuidesShowedBeforeTheyWereCompiled()
    {
        Assert.Empty(Compiler.Compile("""
            serviceProvider.GetRequiredService<IConfigurationValidator>()
                .ValidateAndThrow(serviceProvider.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value);
            """));
        Assert.NotEmpty(Compiler.Compile("""services.AddMemoryIndexer(options => { }).ValidateOnStart();"""));
    }

    /// <summary>
    /// A guide with a C# block is checked, historical or listed as not yet checked with the reason, and the lists name only
    /// documents that exist and have C# blocks — so a new guide cannot be skipped silently and a repaired one cannot stay
    /// on the waiting list. The READMEs are <see cref="ReadmeSnippetCompileTests"/>'.
    /// </summary>
    [Fact]
    public void EveryGuideWithCSharp_IsCheckedOrListed()
    {
        var withCSharp = GuidesWithCSharp().ToList();
        var listed = Checked.Concat(Historical.Keys).Concat(NotYetChecked.Keys).ToList();

        Assert.All(withCSharp, d => Assert.True(listed.Contains(d), $"{d} has C# blocks but is in no list"));
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.All(listed, d => Assert.Contains(d, withCSharp));
    }

    /// <summary>Every Markdown document of the repository with a C# block, except the READMEs compiled elsewhere, changelogs and the tests' own.</summary>
    private static IEnumerable<string> GuidesWithCSharp()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("tests/", StringComparison.Ordinal)
                        && !p.Contains("/bin/", StringComparison.Ordinal) && !p.Contains("/obj/", StringComparison.Ordinal)
                        && !p.StartsWith('.')
                        && !IsReadmeTestsDocument(p)
                        && !Path.GetFileName(p).Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase))
            .Where(p => DocSnippets.Read(Path.Combine(root, p), p).Count > 0)
            .Order(StringComparer.Ordinal);
    }

    // The repository README and the package READMEs (src/<package>/README.md) are ReadmeSnippetCompileTests'.
    private static bool IsReadmeTestsDocument(string path) =>
        path == "README.md" || (path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith("/README.md", StringComparison.Ordinal)
                                && path.Count(c => c == '/') == 2);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MemoryIndexer.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MemoryIndexer.slnx not found above the test output directory");
    }
}
