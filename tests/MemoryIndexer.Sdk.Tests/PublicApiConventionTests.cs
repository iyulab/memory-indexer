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
        "MemoryIndexer.Interfaces.IFastTrackPromoter.ProcessAsync(FactExtractionContext, CancellationToken)",
        "MemoryIndexer.Interfaces.IFastTrackPromoter.ProcessBatchAsync(IReadOnlyList<SensoryMemory>, CancellationToken)",
        "MemoryIndexer.Interfaces.ILongTermPromoter.PromoteMemoryAsync(MemoryUnit, CancellationToken)",
        "MemoryIndexer.Interfaces.ILongTermPromoter.PromoteToArchiveAsync(String, CancellationToken)",
        "MemoryIndexer.Interfaces.IMemoryExporter.ImportAsync(MemoryExportPackage, ImportOptions, CancellationToken)",
        "MemoryIndexer.Interfaces.IMemoryExporter.ImportFromStreamAsync(Stream, ImportOptions, CancellationToken)",
        "MemoryIndexer.Interfaces.IMemoryPrimitives.ConfirmAsync(ConfirmRequest, CancellationToken)",
        "MemoryIndexer.Interfaces.IProfileExporter.ExportAsync(String, ProfileExportOptions, CancellationToken)",
        "MemoryIndexer.Interfaces.IRetentionPolicyService.ApplyAsync(String, Boolean, CancellationToken)",
        "MemoryIndexer.Interfaces.IRetentionPolicyService.ApplyToAllAsync(Boolean, CancellationToken)",
        "MemoryIndexer.Interfaces.ISensoryPromoter.PromoteAsync(String, PromotionTriggerType, CancellationToken)",
        "MemoryIndexer.Interfaces.ISensoryPromoter.PromoteItemsAsync(IReadOnlyList<SensoryMemory>, CancellationToken)",
        "MemoryIndexer.Interfaces.IShortTermMemoryOrchestrator.ArchiveToSessionAsync(String, WorkingPromotionTrigger, Boolean, CancellationToken)",
        "MemoryIndexer.Interfaces.ITierManager.DemoteAsync(MemoryUnit, Tier, PromotionReason, CancellationToken)",
        "MemoryIndexer.Interfaces.ITierManager.PromoteAsync(MemoryUnit, Tier, PromotionReason, CancellationToken)",
        "MemoryIndexer.Sdk.Evaluation.CognitiveScenarioTests.RunCrossSessionRetentionTestAsync(CrossSessionTestConfig, CancellationToken)",
        "MemoryIndexer.Sdk.Evaluation.CognitiveScenarioTests.RunFalseMemoryTestAsync(FalseMemoryTestConfig, CancellationToken)",
        "MemoryIndexer.Sdk.Evaluation.NiahTestRunner.RunMultiNeedleTestAsync(MultiNeedleTestConfig, CancellationToken)",
        "MemoryIndexer.Sdk.Evaluation.NiahTestRunner.RunTestAsync(NiahTestConfig, CancellationToken)",
        "MemoryIndexer.Sdk.Intelligence.Consolidation.IMemoryConsolidator.ConsolidateAsync(ConsolidationOptions, CancellationToken)",
        "MemoryIndexer.Sdk.Intelligence.Evaluation.ILoCoMoEvaluator.EvaluateQueryAsync(IMemoryStore, LoCoMoTestQuery, String, CancellationToken)",
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
