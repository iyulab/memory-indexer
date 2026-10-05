using System.Reflection;
using MemoryIndexer.Sdk.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp;

/// <summary>
/// Every asynchronous MCP tool takes a <see cref="CancellationToken"/> so a client that cancels a request stops the work behind it.
/// The SDK binds that parameter to the request's token; it must never appear in a tool's input schema, where a model
/// would see it as an argument to fill.
/// </summary>
public class McpToolSchemaTests
{
    private static IEnumerable<(MethodInfo Method, McpServerTool Tool)> Tools() =>
        typeof(SecurityTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => (m, m.IsStatic
                ? McpServerTool.Create(m, target: null)
                : McpServerTool.Create(m, (Func<RequestContext<CallToolRequestParams>, object>)(_ => throw new InvalidOperationException("schema only")))));

    [Fact]
    public void A_tool_s_input_schema_never_lists_the_cancellation_token()
    {
        var tools = Tools().ToList();

        // Positive control: the scan found the tools, and a schema lists a real parameter.
        Assert.True(tools.Count > 30, $"only {tools.Count} MCP tools found");
        var detectPii = tools.Single(t => t.Method.Name == nameof(SecurityTools.DetectPii));
        Assert.Contains("\"text\"", detectPii.Tool.ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);

        Assert.All(tools, t =>
        {
            if (typeof(Task).IsAssignableFrom(t.Method.ReturnType) || t.Method.ReturnType.Name.StartsWith("ValueTask", StringComparison.Ordinal))
                Assert.Contains(t.Method.GetParameters(), p => p.ParameterType == typeof(CancellationToken));
            Assert.DoesNotContain("cancellationToken", t.Tool.ProtocolTool.InputSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        });
    }
}
