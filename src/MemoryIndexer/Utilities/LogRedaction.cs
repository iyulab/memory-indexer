using System.Security.Cryptography;
using System.Text;

namespace MemoryIndexer.Utilities;

/// <summary>
/// Turns a value that carries memory content into something a log line may hold. A fact key such as
/// <c>personal:alex:lives_in</c> is built from extracted text — logging it records the fact without its value — so log
/// lines carry its fingerprint instead: the same key gives the same fingerprint, which keeps log lines correlatable.
/// </summary>
public static class LogRedaction
{
    /// <summary>
    /// The first 12 hex digits of the value's SHA-256, or <c>-</c> for null/empty.
    /// </summary>
    public static string Fingerprint(string? value)
        => string.IsNullOrEmpty(value)
            ? "-"
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}

/// <summary>
/// A value logged as its <see cref="LogRedaction.Fingerprint"/>. The hash is computed when the log line is written,
/// so a disabled log level costs nothing.
/// </summary>
public readonly struct FingerprintedValue(string? value)
{
    /// <inheritdoc />
    public override string ToString() => LogRedaction.Fingerprint(value);
}
