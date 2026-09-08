using AwesomeAssertions;
using McpServer;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// The server's HTTP mode is Streamable HTTP (MCP 2026-07-28). The old <c>--sse</c> flag named a
/// transport this server never served; it stays as a deprecated alias that says so.
/// </summary>
public sealed class McpServerTransportTests
{
    [Fact]
    public void Parse_NoFlag_IsStdio()
    {
        var r = McpServerTransport.Parse(["--port", "3001"]);

        r.Mode.Should().Be(McpServerTransport.Stdio);
        r.DeprecationWarning.Should().BeNull();
    }

    [Fact]
    public void Parse_Http_IsHttpWithoutWarning()
    {
        var r = McpServerTransport.Parse(["--http"]);

        r.Mode.Should().Be(McpServerTransport.Http);
        r.DeprecationWarning.Should().BeNull();
    }

    [Fact]
    public void Parse_Sse_IsHttpWithDeprecationWarningNamingTheReplacement()
    {
        var r = McpServerTransport.Parse(["--sse"]);

        r.Mode.Should().Be(McpServerTransport.Http, "the alias keeps existing launch configurations working");
        r.DeprecationWarning.Should().Contain("--sse").And.Contain("--http").And.Contain("Streamable HTTP");
    }

    [Fact]
    public void Parse_HttpAndSse_IsHttpWithWarning()
    {
        var r = McpServerTransport.Parse(["--sse", "--http"]);

        r.Mode.Should().Be(McpServerTransport.Http);
        r.DeprecationWarning.Should().NotBeNull();
    }

    [Fact]
    public void StreamableHttpLabel_NamesTheTransportActuallyServed()
        => McpServerTransport.StreamableHttpLabel.Should().Be("streamable-http");
}
