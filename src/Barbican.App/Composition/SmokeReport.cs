using System.Text;
using System.Text.Json;

namespace Barbican.App.Composition;

/// <summary>
/// What <c>Barbican --smoke REPORT.json</c> writes (docs/milestones/M1-plan.md §5 F1 step 3): the
/// cold-start timings from process start, measured in a window that was hidden from its first frame.
/// UI-free (written with Utf8JsonWriter, no reflection) so the tests and a trimmed publish both work.
/// </summary>
public sealed record SmokeReport(
    bool Ok,
    string? Error,
    string Configuration,
    string Head,
    double ProcessToLaunchedMs,
    double? ProcessToFirstFrameMs,
    double? ProcessToShellReadyMs,
    int ExitCode)
{
    public const int SchemaVersion = 1;

    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", SchemaVersion);
            w.WriteBoolean("ok", Ok);
            if (Error is null)
            {
                w.WriteNull("error");
            }
            else
            {
                w.WriteString("error", Error);
            }

            w.WriteString("configuration", Configuration);
            w.WriteString("head", Head);
            w.WriteNumber("processToLaunchedMs", Math.Round(ProcessToLaunchedMs, 1));
            Number(w, "processToFirstFrameMs", ProcessToFirstFrameMs);
            Number(w, "processToShellReadyMs", ProcessToShellReadyMs);
            w.WriteNumber("exitCode", ExitCode);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Number(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, Math.Round(v, 1));
        }
        else
        {
            w.WriteNull(name);
        }
    }
}
