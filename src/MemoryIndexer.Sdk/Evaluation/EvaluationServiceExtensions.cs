using Microsoft.Extensions.DependencyInjection;

namespace MemoryIndexer.Sdk.Evaluation;

/// <summary>
/// Extension methods for registering evaluation services.
/// </summary>
public static class EvaluationServiceExtensions
{
    /// <summary>
    /// Adds evaluation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMemoryEvaluation(this IServiceCollection services)
    {
        services.AddSingleton<IEvaluationService, EvaluationService>();
        services.AddTransient<NiahTestRunner>();
        services.AddTransient<CognitiveScenarioTests>();

        return services;
    }
}
