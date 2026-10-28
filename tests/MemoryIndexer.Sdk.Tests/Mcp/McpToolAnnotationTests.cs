using System.Reflection;
using MemoryIndexer.Sdk.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp;

/// <summary>
/// Every MCP tool says what it does to the world: read-only, destructive, idempotent, open-world. A client's approval policy
/// reads these hints (<c>tools/list</c> annotations) instead of guessing from a tool's name - "reading never asks, deleting
/// always asks". A tool without the hints reads as "may write, may destroy, reaches outside" to a careful client, and as
/// "harmless" to a careless one.
/// The writers and the destructive ones are pinned by name: adding a tool, or changing what one does, means choosing its
/// classification here on purpose.
/// </summary>
public class McpToolAnnotationTests
{
    // Tools that change state - the store, or the process-held state later calls read (working memory, graph, caches).
    private static readonly string[] Writers =
    [
        "ApplyRetentionPolicy", "ArchiveAndUpdateFact", "ArchivalMemoryInsert", "AutoResolveContradiction", "BuildKnowledgeGraph",
        "ClearKnowledgeGraph", "ComputeImportance", "ConfirmMemory", "CreateProfileSnapshot", "DeleteMemory", "DetectCommunities",
        "FindAndMergeDuplicates", "ImportMemories", "ManageContextWindow", "MemoryCorrect", "MemoryReplace", "MemoryUpdateConfidence",
        "PerformReflection", "RebuildSearchIndex", "RefreshUsage", "ResolveContradiction", "StoreMemory", "UpdateMemory",
        "UpdateWorkingMemory",
    ];

    // The writers that overwrite, merge or remove what was there (the rest only add or recompute derived data).
    private static readonly string[] Destructive =
    [
        "ApplyRetentionPolicy", "AutoResolveContradiction", "ClearKnowledgeGraph", "DeleteMemory", "FindAndMergeDuplicates",
        "ImportMemories", "ManageContextWindow", "MemoryCorrect", "MemoryReplace", "MemoryUpdateConfidence", "ResolveContradiction",
        "UpdateMemory", "UpdateWorkingMemory",
    ];

    private static List<(string Method, ToolAnnotations? Annotations)> Tools() =>
        typeof(SecurityTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => (m.Name, (m.IsStatic
                    ? McpServerTool.Create(m, target: null)
                    : McpServerTool.Create(m, (Func<RequestContext<CallToolRequestParams>, object>)(_ => throw new InvalidOperationException("annotations only"))))
                .ProtocolTool.Annotations))
            .ToList();

    [Fact]
    public void Every_tool_declares_all_the_hints_it_can()
    {
        var tools = Tools();

        // Positive control: the scan found the tools.
        Assert.True(tools.Count > 90, $"only {tools.Count} MCP tools found");

        foreach (var (method, annotations) in tools)
        {
            Assert.True(annotations is not null, $"{method} declares no hints");
            Assert.True(annotations!.ReadOnlyHint is not null, $"{method}: ReadOnly is not declared");
            Assert.True(annotations.OpenWorldHint is not null, $"{method}: OpenWorld is not declared");
            if (annotations.ReadOnlyHint == false)
            {
                Assert.True(annotations.DestructiveHint is not null, $"{method} writes but does not declare Destructive");
                Assert.True(annotations.IdempotentHint is not null, $"{method} writes but does not declare Idempotent");
            }
        }
    }

    [Fact]
    public void The_tools_that_write_are_the_ones_on_the_roster()
    {
        var declared = Tools().Where(t => t.Annotations?.ReadOnlyHint == false).Select(t => t.Method).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Writers.Order(StringComparer.Ordinal), declared);
    }

    [Fact]
    public void The_tools_that_destroy_are_the_ones_on_the_roster()
    {
        var declared = Tools().Where(t => t.Annotations is { ReadOnlyHint: false, DestructiveHint: true }).Select(t => t.Method).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Destructive.Order(StringComparer.Ordinal), declared);
    }

    [Fact]
    public void No_tool_reaches_outside_the_memory_store()
    {
        // The tools work on the server's own store and process state; the embedding backend is the server's configuration, not a
        // world a call talks to. A tool that fetches from the network or calls out to a service must say OpenWorld = true.
        Assert.All(Tools(), t => Assert.False(t.Annotations?.OpenWorldHint, $"{t.Method} declares OpenWorld"));
    }
}
