using System.Text.Json.Serialization;

namespace Barbican.Spike.BodyShell;

/// <summary>Source-generated JSON for the shell commands and the report (trim and AOT safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShellCommand))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(SpikeReport))]
internal sealed partial class ShellJson : JsonSerializerContext;
