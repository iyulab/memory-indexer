using System.Reflection;
using MemoryIndexer.Configuration;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Every MCP tool the package ships can be built from <c>AddMemoryIndexer()</c> alone, and
/// <c>WithAllMemoryIndexerTools()</c> registers all of them — a tool class whose dependencies nothing registers is a
/// tool a host lists and then fails to call.
/// </summary>
public class McpToolResolutionTests
{
    // A tool class is any type declaring an [McpServerTool] method; [McpServerToolType] is not required for that
    private static readonly Type[] s_toolTypes = typeof(MemoryTools).Assembly.GetTypes()
        .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    public static TheoryData<string> ToolTypeNames() => new(s_toolTypes.Select(t => t.Name));

    private static ServiceProvider Build(Action<IMcpServerBuilder> tools)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryIndexer(o =>
        {
            o.Embedding.Provider = EmbeddingProvider.Mock;
            o.Embedding.Dimensions = 64;
        });
        tools(services.AddMcpServer());
        return services.BuildServiceProvider();
    }

    // Positive control: the scan finds the tool classes at all.
    [Fact]
    public void The_scan_finds_the_shipped_tool_classes()
    {
        Assert.Contains(typeof(MemoryTools), s_toolTypes);
        Assert.True(s_toolTypes.Length >= 10, $"found {s_toolTypes.Length} tool classes");
    }

    // WithToolsFromAssembly finds tool classes by this attribute, so a class without it is invisible to that host
    [Theory]
    [MemberData(nameof(ToolTypeNames))]
    public void Each_tool_class_is_marked_as_a_tool_type(string typeName)
    {
        var type = s_toolTypes.Single(t => t.Name == typeName);

        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());
    }

    [Theory]
    [MemberData(nameof(ToolTypeNames))]
    public void Each_tool_class_resolves_from_AddMemoryIndexer(string typeName)
    {
        var type = s_toolTypes.Single(t => t.Name == typeName);
        using var provider = Build(_ => { });
        using var scope = provider.CreateScope();

        var instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, type);

        Assert.NotNull(instance);
    }

    [Fact]
    public void WithAllMemoryIndexerTools_registers_every_shipped_tool()
    {
        using var all = Build(b => b.WithAllMemoryIndexerTools());
        // Each shipped type registered on its own, through the same generic overload the extension uses
        var withTools = typeof(McpServerBuilderExtensions).GetMethods()
            .Single(m => m.Name == "WithTools" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
        using var each = Build(b =>
        {
            foreach (var type in s_toolTypes)
            {
                withTools.MakeGenericMethod(type).Invoke(null, [b, null]);
            }
        });

        static string[] Names(IServiceProvider sp) => sp.GetServices<McpServerTool>()
            .Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal).ToArray();

        var expected = Names(each);
        var actual = Names(all);
        Assert.NotEmpty(expected);
        Assert.Empty(expected.Except(actual));   // shipped tools WithAllMemoryIndexerTools leaves out
        Assert.Empty(actual.Except(expected));   // registered tools no shipped type declares
    }
}
