using System.Text.Json.Serialization;

namespace Barbican.Spike.BodyShell;

/// <summary>A command sent to the shell as a web message (shell.js "message" listener).</summary>
internal sealed record ShellCommand(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("html")] string Html,
    [property: JsonPropertyName("bg")] string Background,
    [property: JsonPropertyName("sandbox")] string Sandbox);
