// Mirrors: Rust filters_x.rs: :17 FIELDS, :20 CMP_LABELS, :26 comparators, :40 ACTS, :92 conds_of, :99 acts_of, :116 build_rule, :198 summary, :220 moved, :249 retro_unsupported, :273 sieve_matches, :314 cond_matches, :329 rule_matches, :340 query_filter, :361 retro_properties, :378 retro_message, :404 plan, :428 plan_count, :437 step_patch, :451 apply_with, :475 current, :477 applied_text;
// Bulwark lib/filters/retroactive.ts (:82 retroactiveSupport, :108 sieveMatches, :188 ruleMatches, :204 retroQueryFilter, :225 retroProperties, :248 toRetroMessage, :309 planRetroactive), lib/sieve/generator.ts:29 isValidSieveHeaderName, :187 supportsSpamGuard. T-093..T-101, G-020. Contract from the M6 plan section 4.6; owner srv-data.
// Rules are JsonNode (Bulwark's FilterRule JSON; unknown properties survive a rebuild, D-M6-C01).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Filters.Rules;

/// <summary>The pure rules of the filter editor and of "apply to existing mail".</summary>
public static class FilterRules
{
    /// <summary>The nine condition fields with their labels (id, label).</summary>
    public static IReadOnlyList<(string Id, string Label)> Fields { get; } =
    [
        ("from", "From"), ("to", "To"), ("cc", "Cc"), ("subject", "Subject"), ("header", "Custom header"),
        ("size", "Size"), ("body", "Body"), ("attachment", "Attachment"), ("all", "All messages"),
    ];

    /// <summary>The comparators a field allows (Bulwark COMPARATORS_BY_FIELD): from, to, cc ten; subject and header eight; body contains, is, regex; size greater_than and less_than; attachment has_any and has_type; all none.</summary>
    public static IReadOnlyList<string> Comparators(string field) => throw new PortPendingException("FilterRules.Comparators");

    /// <summary>The action kinds the editor offers, with labels (id, label); <c>reject</c> only when <paramref name="serverExtensions"/> lists "reject" (G-020, B-SIEVE-030).</summary>
    public static IReadOnlyList<(string Id, string Label)> ActionKinds(IReadOnlyList<string> serverExtensions) => throw new PortPendingException("FilterRules.ActionKinds");

    public static IReadOnlyList<FilterCond> ConditionsOf(JsonNode rule) => throw new PortPendingException("FilterRules.ConditionsOf");

    public static IReadOnlyList<FilterAct> ActionsOf(JsonNode rule) => throw new PortPendingException("FilterRules.ActionsOf");

    /// <summary>The rule as Bulwark stores it, from the form, or what to fix (T-093, T-094, T-100, T-101): a condition and an action are required; a size is digits with one optional K, M or G (upper-cased); a regex must compile; a header name is letters, digits, "_" and "-" only; a move or copy needs a folder, a forward a valid address, reject a message; an until before the from is refused; the id is kept when editing, else a new "bw-" id; unknown properties of <paramref name="base"/> survive.</summary>
    public static FilterBuilt BuildRule(JsonNode? @base, string? id, string name, bool any, IReadOnlyList<FilterCond> conds, IReadOnlyList<FilterAct> acts, IReadOnlyList<FilterFolder> folders, bool stop, bool includeSpam, string from, string until) => throw new PortPendingException("FilterRules.BuildRule");

    /// <summary>"From contains x or Has an attachment → Star message, Move to folder Archive · then stop" (T-095).</summary>
    public static string Summary(JsonNode rule) => throw new PortPendingException("FilterRules.Summary");

    /// <summary>The rules with <paramref name="id"/> moved one place up (-1) or down (1); unchanged at the ends or for an unknown id (T-095).</summary>
    public static IReadOnlyList<JsonNode> Moved(IReadOnlyList<JsonNode> rules, string id, int direction) => throw new PortPendingException("FilterRules.Moved");

    /// <summary>Sieve :matches: "*" any run, "?" one character, backslash escapes; case-insensitive for ASCII (T-096).</summary>
    public static bool SieveMatches(string pattern, string text) => throw new PortPendingException("FilterRules.SieveMatches");

    /// <summary>Why the rule cannot run on existing mail (a body, size, regex or header-only condition; forward, discard or reject; an active period; a move without a mailbox id), or null when it can (T-097).</summary>
    public static string? RetroUnsupported(JsonNode rule) => throw new PortPendingException("FilterRules.RetroUnsupported");

    /// <summary>The Email/query filter that narrows the scan: the mailbox plus the address conditions as JMAP filters (T-097).</summary>
    public static JsonNode QueryFilter(JsonNode rule, string mailboxId) => throw new PortPendingException("FilterRules.QueryFilter");

    /// <summary>The Email/get properties the rule's conditions need (T-097, T-101).</summary>
    public static IReadOnlyList<string> RetroProperties(JsonNode rule) => throw new PortPendingException("FilterRules.RetroProperties");

    /// <summary>An Email/get record as the matcher reads it: id, lower-cased addresses, unfolded headers by lower-case name, keywords, mailboxIds (T-097, T-101).</summary>
    public static JsonNode RetroMessage(JsonNode record) => throw new PortPendingException("FilterRules.RetroMessage");

    public static bool RuleMatches(JsonNode rule, JsonNode message) => throw new PortPendingException("FilterRules.RuleMatches");

    /// <summary>The steps that apply the rule to the matching messages: keyword steps for mark_read, star and add_label (skipping messages that already have it), a move step (the first move) and copy steps, each only for messages not already in the folder (T-097).</summary>
    public static IReadOnlyList<FilterPlanStep> Plan(JsonNode rule, IReadOnlyList<JsonNode> messages) => throw new PortPendingException("FilterRules.Plan");

    /// <summary>How many distinct messages the plan touches (T-097).</summary>
    public static int PlanCount(IReadOnlyList<FilterPlanStep> steps) => throw new PortPendingException("FilterRules.PlanCount");

    /// <summary>The Email/set patch of one step with "~" and "/" escaped (<c>mailboxIds/a~1b~0c</c>); a move replaces the folders (T-099).</summary>
    public static JsonObject StepPatch(string kind, string arg) => throw new PortPendingException("FilterRules.StepPatch");

    /// <summary>Applies the steps in Email/set calls of at most <paramref name="max"/> updates, each with ifInState chained from the previous newState; stops on stateMismatch (Stale) or an error (Failed) with the count so far (T-099). <paramref name="send"/> posts one method call and returns its response.</summary>
    public static Task<FilterApplied> ApplyAsync(Func<JsonArray, Task<Result<JsonArray>>> send, string accountId, string state, IReadOnlyList<FilterPlanStep> steps, int max, CancellationToken ct) => throw new PortPendingException("FilterRules.ApplyAsync");

    /// <summary>Whether a scan's result still belongs to the open dialog: it is open and no later run started (T-098).</summary>
    public static bool Current(long now, long generation, bool open) => throw new PortPendingException("FilterRules.Current");

    /// <summary>"Rule applied to 3 messages", "Applied to 2 of 3 messages; your mail changed meanwhile, run the rule again for the rest.", "Applied to 0 of 3 messages: network down" (T-099).</summary>
    public static string AppliedText(FilterApplied result, int total) => throw new PortPendingException("FilterRules.AppliedText");
}
