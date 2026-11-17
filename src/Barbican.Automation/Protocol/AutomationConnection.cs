using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Barbican.Automation.Protocol;

/// <summary>
/// One automation connection (spec/automation-protocol.md, Fable review 1 F-08): the first line must be <c>auth &lt;token&gt;</c> with the
/// run's token from <c>BARBICAN_AUTOMATION_TOKEN</c> (made per run by tools/auto-run.ps1), answered <c>ok auth</c>; anything else, and any
/// later line that looks like HTTP (a web page can POST to a loopback port, and its request line and headers would arrive as lines), ends
/// the connection without running anything. Comparisons are constant-time.
/// </summary>
public static partial class AutomationConnection
{
    /// <summary>The environment variable that carries the run's token.</summary>
    public const string TokenVariable = "BARBICAN_AUTOMATION_TOKEN";

    /// <summary>Shorter tokens are refused (the server then does not start).</summary>
    public const int MinTokenLength = 16;

    /// <summary>The token of this run, or null when it is missing or too short.</summary>
    public static string? TokenFromEnvironment() => Usable(Environment.GetEnvironmentVariable(TokenVariable));

    /// <summary>The token when it is long enough, else null.</summary>
    public static string? Usable(string? token) =>
        token is { Length: >= MinTokenLength } t && !t.Any(char.IsWhiteSpace) ? t : null;

    /// <summary>True when <paramref name="line"/> is <c>auth &lt;token&gt;</c>.</summary>
    public static bool Authenticates(string? line, string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (line is null || !line.StartsWith("auth ", StringComparison.Ordinal))
        {
            return false;
        }

        var given = Encoding.UTF8.GetBytes(line.AsSpan(5).TrimEnd("\r").ToString());
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(token));
    }

    /// <summary>True for an HTTP request line or header line, which no automation command looks like.</summary>
    public static bool LooksLikeHttp(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return RequestLine().IsMatch(line) || HeaderLine().IsMatch(line);
    }

    /// <summary>
    /// Serves one connection: authenticates, then runs each line through <paramref name="run"/> and writes its one-line reply.
    /// </summary>
    public static async Task ServeAsync(Stream stream, string token, Func<string, CancellationToken, Task<string>> run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(run);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var first = await ReadBoundedAsync(reader, ct).ConfigureAwait(false);
        if (!Authenticates(first, token))
        {
            await writer.WriteLineAsync("error auth required").ConfigureAwait(false);
            return;
        }

        await writer.WriteLineAsync("ok auth").ConfigureAwait(false);
        while (!ct.IsCancellationRequested && await ReadBoundedAsync(reader, ct).ConfigureAwait(false) is { } line)
        {
            if (LooksLikeHttp(line))
            {
                return;
            }

            var reply = line.Length > CommandLine.MaxLength ? "error line too long" : await run(line, ct).ConfigureAwait(false);
            await writer.WriteLineAsync(CommandLine.OneLine(reply)).ConfigureAwait(false);
        }
    }

    // a line longer than the protocol allows is cut there (the rest of it is read and dropped), so one line never grows without bound
    private static async Task<string?> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) == 1)
        {
            if (buffer[0] == '\n')
            {
                return sb.ToString().TrimEnd('\r');
            }

            if (sb.Length <= CommandLine.MaxLength)
            {
                sb.Append(buffer[0]);
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    [GeneratedRegex(@"^[A-Z]{3,10} \S+ HTTP/\d", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RequestLine();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]*: ", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex HeaderLine();
}
