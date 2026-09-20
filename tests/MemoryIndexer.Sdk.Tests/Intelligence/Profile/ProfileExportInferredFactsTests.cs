using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// <c>ProfileExportOptions.IncludeInferred</c> was declared <c>false</c> and read by nothing, so the
/// exporter shipped inferred facts regardless.
/// </summary>
/// <remarks>
/// The default moves to <c>true</c> rather than to the declared <c>false</c>: an export of what the
/// system holds about a person should carry what was inferred about them as well as what was
/// stated, and it also keeps today's output unchanged. Turning it off is now the way to get stated
/// facts only — which is what the option always claimed to do.
/// </remarks>
public class ProfileExportInferredFactsTests
{
    [Fact]
    public void IncludeInferred_DefaultsToOn() =>
        new ProfileExportOptions().IncludeInferred.Should().BeTrue();
}
