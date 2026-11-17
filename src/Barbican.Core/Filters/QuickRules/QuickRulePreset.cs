// Mirrors: Rust quick_rules.rs:10 Preset; Bulwark components/email/ (the Rules menu presets), lib/quick-rule.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Filters.QuickRules;

/// <summary>A preset of the Rules menu.</summary>
public abstract record QuickRulePreset
{
    /// <summary>Move mail from the sender into a folder.</summary>
    public sealed record MoveSender(QuickRuleFolder Folder) : QuickRulePreset;

    /// <summary>Move mail from the sender's domain into a folder.</summary>
    public sealed record MoveDomain(QuickRuleFolder Folder) : QuickRulePreset;

    /// <summary>Move mail from the list into a folder.</summary>
    public sealed record MoveList(QuickRuleFolder Folder) : QuickRulePreset;

    /// <summary>Mark mail from the sender read.</summary>
    public sealed record MarkRead : QuickRulePreset;

    /// <summary>Add a label to mail from the senders.</summary>
    public sealed record Tag(string Id, string Name) : QuickRulePreset;

    /// <summary>Move mail from the sender to Junk, including spam.</summary>
    public sealed record Block(QuickRuleFolder Folder) : QuickRulePreset;
}
