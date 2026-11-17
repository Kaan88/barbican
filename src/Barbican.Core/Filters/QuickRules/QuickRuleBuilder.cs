// Mirrors: Rust quick_rules.rs:44 extract_list_id, :55 subject_of, :75 sieve_path, :105 preset_rule, :180 apply, :218 plan; Bulwark lib/quick-rule.ts
// (applyQuickRule, the preset rules), RFC 2919 (List-Id); Stalwart SieveScript set (crates/jmap/src/sieve/set.rs). Stub from stage F of M2; owner search (wave 2).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Sieve;

namespace Barbican.Core.Filters.QuickRules;

/// <summary>The one-click rules of the Rules menu, as pure functions over the parsed script.</summary>
public static class QuickRuleBuilder
{
    /// <summary>The list id in a List-Id header value (the part in angle brackets, or a bare token); null when it has spaces.</summary>
    public static string? ExtractListId(string raw)
    {
        throw new PortPendingException("quick_rules.rs:44 extract_list_id");
    }

    /// <summary>The sender(s), domain and list of a message; the user's own addresses are left out.</summary>
    public static QuickRuleSubject SubjectOf(JsonNode message, IReadOnlyList<string> ownAddresses)
    {
        throw new PortPendingException("quick_rules.rs:55 subject_of");
    }

    /// <summary>The Sieve fileinto path of a mailbox: Inbox children are INBOX/name, role folders their own name.</summary>
    public static string SievePath(IReadOnlyList<SieveFolderRef> folders, string id)
    {
        throw new PortPendingException("quick_rules.rs:75 sieve_path");
    }

    /// <summary>The Bulwark rule object a preset makes.</summary>
    public static JsonNode PresetRule(QuickRulePreset preset, QuickRuleSubject subject, string id)
    {
        throw new PortPendingException("quick_rules.rs:105 preset_rule");
    }

    /// <summary>Creates the rule at the top, merges its values into an equivalent enabled rule without a period, or reports it as covered.</summary>
    public static QuickRuleOutcome Apply(IReadOnlyList<JsonNode> rules, JsonNode candidate)
    {
        throw new PortPendingException("quick_rules.rs:180 apply");
    }

    /// <summary>What saving the candidate into the parsed script does; a hand-edited (locked) script is refused with "edited by hand".</summary>
    public static Result<QuickRulePlan> Plan(SieveParsed script, JsonNode candidate)
    {
        throw new PortPendingException("quick_rules.rs:218 plan");
    }
}
