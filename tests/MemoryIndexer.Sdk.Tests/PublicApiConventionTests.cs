using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Services;
using McpServer.Controllers;
using Iyu.Conventions.Testing;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// The public surface follows the two API rules of the ecosystem: every public async method takes a
/// <see cref="CancellationToken"/>, and failure is reported by an exception rather than by a returned object carrying a
/// success flag and an error. The scans are <c>Iyu.Conventions.Testing</c>'s, over the same assemblies as the
/// operational-language scan.
/// </summary>
/// <remarks>
/// The rosters are the methods that break a rule today. Shrink them; never grow them silently. A change to a listed
/// method's parameters changes its entry, which is a roster change on purpose.
/// </remarks>
public class PublicApiConventionTests
{
    private static readonly string[] KnownUncancellable =
    [
        // The HealthCheckOptions.ResponseWriter delegate shape (HttpContext, HealthReport) is ASP.NET Core's, not ours.
        "MemoryIndexer.Sdk.Health.HealthCheckResponseWriter.WriteResponse(HttpContext, HealthReport)",
    ];

    private static readonly string[] KnownResultReturns =
    [
        // A metric, not a failure channel: Success is hit@k for one benchmark query (the method throws when it cannot
        // run); Error is filled only by the suite runner for a query that threw, so the suite reports every query.
        "MemoryIndexer.Sdk.Intelligence.Evaluation.ILoCoMoEvaluator.EvaluateQueryAsync(IMemoryStore, LoCoMoTestQuery, String, CancellationToken)",
        // MCP tool: the return value is what the model reads, so a failed policy run is reported to it as data.
        // The service underneath throws; the tool lets the caller's cancellation through.
        "MemoryIndexer.Sdk.Mcp.Tools.RetentionPolicyTools.ApplyRetentionPolicy(String, Boolean, CancellationToken)",
    ];

    [Fact]
    public void PublicAsyncMethods_TakeACancellationToken() =>
        AsyncCancellation.Scan([typeof(SimpleMemoryService).Assembly, typeof(MemoryPromotionBackgroundService).Assembly, typeof(MemoryController).Assembly]).ShouldMatchRoster(KnownUncancellable);

    [Fact]
    public void PublicMethods_DoNotReturnResultObjects() =>
        ResultReturns.Scan([typeof(SimpleMemoryService).Assembly, typeof(MemoryPromotionBackgroundService).Assembly, typeof(MemoryController).Assembly]).ShouldMatchRoster(KnownResultReturns);

    // Positive control: an empty roster would also pass if the scan saw no public method at all.
    [Fact]
    public void Scan_SeesThePublicSurface() =>
        Assert.True(ResultReturns.Scan([typeof(SimpleMemoryService).Assembly, typeof(MemoryPromotionBackgroundService).Assembly, typeof(MemoryController).Assembly]).MembersRead > 0, "the scan read too few public methods");
}
