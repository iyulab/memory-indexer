namespace MemoryIndexer.Sdk.Tests.Integration;

/// <summary>
/// Test category constants for filtering tests in CI/CD pipelines.
/// </summary>
public static class TestCategories
{
    /// <summary>
    /// General integration tests - can run in CI/CD
    /// </summary>
    public const string Integration = "Integration";

    /// <summary>
    /// Tests requiring GPU or heavy computation - skip in CI/CD
    /// </summary>
    public const string Heavy = "Heavy";

    /// <summary>
    /// Tests requiring GPUStack server - skip in CI/CD
    /// </summary>
    public const string GpuStack = "GpuStack";

    /// <summary>
    /// Tests requiring local embedding model loading - may be slow
    /// </summary>
    public const string LocalModel = "LocalModel";

    /// <summary>
    /// Simulation tests - comprehensive but time-consuming
    /// </summary>
    public const string Simulation = "Simulation";

    /// <summary>
    /// Quality improvement validation tests
    /// </summary>
    public const string QualityImprovement = "QualityImprovement";

    /// <summary>
    /// End-to-end tests with real LLM providers - requires API keys
    /// </summary>
    public const string LlmProvider = "LlmProvider";

    /// <summary>
    /// Intelligence feature integration tests
    /// </summary>
    public const string Intelligence = "Intelligence";
}
