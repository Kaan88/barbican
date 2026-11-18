// Mirrors: Bulwark lib/file-name-rules.ts FileNameProblem. Contract from the M5 plan section 4.6; owner files-data.

namespace Barbican.Core.Files;

/// <summary>Why a name is refused before any upload: "chars" (Detail lists them), "control", "tooLong" or "reserved".</summary>
public sealed record FileNameProblem(string Kind, string Detail);
