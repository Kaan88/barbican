// Mirrors: Bulwark lib/file-name-rules.ts fileNameProblem, fileNameRulesFrom; Stalwart crates/jmap/src/file/ (validate_name). B-FILE-004; G-019. Contract from the M5 plan section 4.6; owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>Checks names against the rules of the server before anything is uploaded.</summary>
public static class FileNameChecks
{
    /// <summary>The rules of a filenode capability object; null when it publishes none.</summary>
    public static FileNameRules? RulesFrom(JsonNode? capability) => throw new PortPendingException("FileNameChecks.RulesFrom");

    /// <summary>The first problem of a name, or null: longer than the limit in UTF-8 bytes (tooLong), a forbidden character (chars), a control character U+0000-001F or U+007F-009F (control), a reserved name or reserved stem before the first dot compared ignoring ASCII case ("con.txt": reserved). Null rules: no problem.</summary>
    public static FileNameProblem? ProblemOf(string name, FileNameRules? rules) => throw new PortPendingException("FileNameChecks.ProblemOf");
}
