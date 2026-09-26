using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// <c>BindConfiguration</c> without its hard dependency on a registered <see cref="IConfiguration"/>. A host has one,
/// and gets the same binding and reload as <c>BindConfiguration</c>. A console app, tool or test that configures
/// Memory Indexer in code has none — <c>BindConfiguration</c> made resolving any options-dependent service throw
/// "No service for type IConfiguration" there; this skips the binding instead.
/// </summary>
internal static class OptionalConfigurationBinding
{
    public static OptionsBuilder<TOptions> BindConfigurationIfPresent<TOptions>(this OptionsBuilder<TOptions> builder, string sectionPath)
        where TOptions : class
    {
        builder.Configure<IServiceProvider>((options, sp) => sp.GetService<IConfiguration>()?.GetSection(sectionPath).Bind(options));
        builder.Services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(sp =>
            sp.GetService<IConfiguration>() is { } configuration
                ? new ConfigurationChangeTokenSource<TOptions>(builder.Name, configuration.GetSection(sectionPath))
                : new NoChangeTokenSource<TOptions>(builder.Name));
        return builder;
    }

    private sealed class NoChangeTokenSource<TOptions>(string? name) : IOptionsChangeTokenSource<TOptions>
    {
        public string? Name { get; } = name;

        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
