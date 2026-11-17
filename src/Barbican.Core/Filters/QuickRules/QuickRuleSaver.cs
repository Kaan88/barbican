// Mirrors: Rust quick_rules.rs:229 save (read the active script, plan, generate with the server's extensions, validate and save); Bulwark lib/filters/quick-rule-flow.ts:345.
// A hand-edited script is never rewritten (plan refuses), the text is validated by the server before it replaces the script (SieveScriptStore.SaveAsync), and a rule the
// script already covers writes nothing.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Sieve;

namespace Barbican.Core.Filters.QuickRules;

/// <summary>Saves one quick rule into the account's Sieve script.</summary>
public sealed class QuickRuleSaver(SieveScriptStore store)
{
    /// <summary>Creates, merges or finds the rule covered; the result is the message to show ("Rule ... created", "Added to rule ...", "Already covered by ...").</summary>
    public async Task<Result<string>> SaveAsync(JsonNode candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var read = await store.ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<string>.Fail(read.Error!);
        }

        var state = read.Value!;
        var parsed = SieveScript.Parse(state.Text);
        var plan = QuickRuleBuilder.Plan(parsed, candidate);
        if (!plan.IsOk)
        {
            return Result<string>.Fail(plan.Error!);
        }

        if (!plan.Value!.NeedsSave)
        {
            return Result<string>.Ok(plan.Value.Toast);
        }

        var text = SieveScript.GenerateMeta(SieveScript.WithRules(parsed.Metadata, plan.Value.Rules!), parsed.External, state.Extensions);
        var saved = await store.SaveAsync(text, state, ct).ConfigureAwait(false);
        return saved.IsOk ? Result<string>.Ok(plan.Value.Toast) : Result<string>.Fail(saved.Error!);
    }
}
