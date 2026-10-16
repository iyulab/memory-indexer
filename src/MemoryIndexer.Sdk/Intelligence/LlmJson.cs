using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Intelligence;

/// <summary>
/// One JSON object out of a model's answer. Small models wrap it in a code fence or a sentence, leave a trailing comma,
/// quote a number, or stop before the object closes; the reader takes the first complete top-level object that reads as
/// the expected type, tolerates the first three, and names the failure otherwise so the caller can ask once more.
/// </summary>
internal static class LlmJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Asks for <typeparamref name="T"/> as JSON and reads it. An answer that does not read is asked for once more, with the
    /// reason; transport failures are not retried here.
    /// </summary>
    /// <returns>The value, or <c>null</c> with the reason the second answer did not read either.</returns>
    public static async Task<(T? Value, string? Error)> CompleteAsync<T>(
        ITextCompletionService completion,
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken)
        where T : class
    {
        var response = await completion.CompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
        if (TryRead<T>(response, out var value, out var error))
        {
            return (value, null);
        }

        var retry = prompt + string.Create(CultureInfo.InvariantCulture,
            $"\n\nYour previous reply could not be read as one JSON object ({error}). Reply with the JSON object only: no explanation, no code fence.\n");
        response = await completion.CompleteAsync(retry, options, cancellationToken).ConfigureAwait(false);
        return TryRead(response, out value, out error) ? (value, null) : (null, error);
    }

    /// <summary>Reads the first complete top-level JSON object in <paramref name="response"/> that deserializes to <typeparamref name="T"/>.</summary>
    public static bool TryRead<T>(string? response, [NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error)
        where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(response))
        {
            error = "the reply was empty";
            return false;
        }

        error = null;
        var start = response.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            error = "the reply has no JSON object";
            return false;
        }

        // Only top-level objects are candidates: an object nested in one that failed to read is a part of it (a fact
        // inside "facts"), and read as the whole it would pass as an empty result
        while (start >= 0)
        {
            var end = FindObjectEnd(response, start);
            if (end < 0)
            {
                error ??= "the JSON object is not closed (the reply was cut off?)";
                return false;
            }

            try
            {
                value = JsonSerializer.Deserialize<T>(response.AsSpan(start, end - start + 1), Options);
                if (value is not null)
                {
                    return true;
                }

                error ??= "the JSON object was null";
            }
            catch (JsonException ex)
            {
                error ??= ex.Message;
            }

            start = response.IndexOf('{', end + 1);
        }

        error ??= "the reply has no JSON object";
        return false;
    }

    // The index of the brace that closes the object opened at start, skipping braces inside strings; -1 if it never closes
    private static int FindObjectEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    if (--depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }
}
