// Mirrors: Rust quick_rules.rs:229 save (the scripts, the active one's text, the server's sieveExtensions); Bulwark lib/jmap/client.ts:7533-7708 (the Sieve methods).

namespace Barbican.Core.Sieve;

/// <summary>The account's scripts and the text of the active one.</summary>
/// <param name="Scripts">Every script.</param>
/// <param name="Text">The active script's text (the first script when none is active); empty without scripts.</param>
/// <param name="Extensions">The Sieve extensions the server advertises (spamtestplus, mailboxid, ...).</param>
public sealed record SieveScriptState(IReadOnlyList<SieveScriptInfo> Scripts, string Text, IReadOnlyList<string> Extensions)
{
    /// <summary>The server's SieveScript/get state, required for a guarded save.</summary>
    public string? State { get; init; }

    /// <summary>The script a save replaces: the active one, else the first; null without scripts.</summary>
    public SieveScriptInfo? Target => Scripts.FirstOrDefault(s => s.IsActive) ?? (Scripts.Count > 0 ? Scripts[0] : null);
}
