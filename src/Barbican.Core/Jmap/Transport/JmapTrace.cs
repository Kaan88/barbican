// Mirrors: Rust jmap.rs:82 trace (BARBICAN_JMAP_TRACE: each request's method names and the response's method names, plus error
// types; never arguments or data), :1130 the PUSH lines; PORT-PLAN.md §3.4 "Trace", §4.6 (trace-diff compares method sequences,
// never payloads). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Transport;

/// <summary>
/// The method-name trace. One line per event, no timestamps, ids, arguments, addresses or bodies, so two runs of a scenario compare
/// line for line (tools/trace-diff.py).
/// </summary>
public sealed class JmapTrace
{
    /// <summary>The environment variable that turns the trace on.</summary>
    public const string Variable = "BARBICAN_JMAP_TRACE";

    private static readonly Lock FileLock = new();

    private readonly Action<string>? _sink;

    private JmapTrace(Action<string>? sink) => _sink = sink;

    /// <summary>A trace that writes nothing.</summary>
    public static JmapTrace Off { get; } = new(null);

    /// <summary>True when lines are written.</summary>
    public bool IsOn => _sink is not null;

    /// <summary>A trace appending to <paramref name="path"/> (null or empty: off).</summary>
    public static JmapTrace ToFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Off;
        }

        return new JmapTrace(line =>
        {
            // a diagnostic must never fail the request it describes: a missing folder or a locked file drops the line
            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(path, line + "\n");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // the trace is best effort
            }
        });
    }

    /// <summary>A trace handing each line to <paramref name="sink"/> (tests, the scenario runner).</summary>
    public static JmapTrace To(Action<string> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        return new JmapTrace(sink);
    }

    /// <summary>
    /// One API request, as Rust jmap.rs:82 writes it without the time: <c>Email/query(limit=50),Email/get -> Email/query,Email/get</c>,
    /// then <c> errors="type",...</c> when a call failed; <c>-> (failed)</c> when no response came.
    /// </summary>
    public void Request(IEnumerable<JsonArray> calls, IReadOnlyList<JsonArray>? responses)
    {
        ArgumentNullException.ThrowIfNull(calls);
        if (_sink is null)
        {
            return;
        }

        var sent = Names(calls);
        if (responses is null)
        {
            _sink($"{sent} -> (failed)");
            return;
        }

        var errors = responses.Where(r => r.Count > 1 && string.Equals(r[0].AsString(), "error", StringComparison.Ordinal))
            .Select(r => "\"" + (r[1].Str("type") ?? "") + "\"").ToList();
        _sink($"{sent} -> {Names(responses)}{(errors.Count == 0 ? "" : " errors=" + string.Join(",", errors))}");
    }

    private static string Names(IEnumerable<JsonArray> calls) => string.Join(",", calls.Select(c =>
    {
        var n = c.Count > 0 ? c[0].AsString() ?? "?" : "?";
        return c.Count > 1 && c[1] is JsonObject o && o["limit"].AsNumber() is { } l ? $"{n}(limit={l})" : n;
    }));

    /// <summary>A non-API event: <c>SESSION</c>, <c>PUSH open</c>, <c>PUSH state Email,Mailbox</c>, <c>PUSH ping</c>, <c>PUSH closed</c>, ...</summary>
    public void Event(string kind, string what)
    {
        _sink?.Invoke(what.Length == 0 ? kind : $"{kind} {what}");
    }
}
