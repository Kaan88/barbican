// Mirrors: Bulwark lib/file-name-rules.ts (fileNameRulesFrom: forbiddenNameChars, forbiddenNodeNames, maxSizeFileNodeName of the filenode capability, Stalwart 0.16.6+; Stalwart 1.0 validate_name also refuses control characters and a reserved stem). Contract from the M5 plan section 4.6; owner files-data.

namespace Barbican.Core.Files;

/// <summary>The naming rules a server publishes in its filenode capability.</summary>
public sealed record FileNameRules(string ForbiddenChars, IReadOnlyList<string> ForbiddenNames, int MaxNameBytes)
{
    /// <summary>Every Stalwart release refuses names over 255 bytes.</summary>
    public const int DefaultMaxNameBytes = 255;
}
