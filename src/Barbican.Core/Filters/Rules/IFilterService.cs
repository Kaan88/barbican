// Mirrors: Rust filters_x.rs save_rules :542, run_rule :566; server_settings.rs:72 save_script (upload, SieveScript/validate, SieveScript/set with onSuccessActivateScript; a failed save restores the list, B-SIEVE-023); Core/Sieve/SieveScriptStore.cs (M2); hooks onFiltersLoaded, onBeforeFiltersSave (intercept), onFiltersSave, onFilterRuleChange, onSieveScriptChange, onSieveScriptGenerate (transform).
// THREADING (D-M6-00): every call completes off the UI thread; Changed is raised on the thread that finished a save or saw the change, a pool thread. Contract from the M6 plan section 4.6; owner srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Filters.Rules;

/// <summary>The filter rules of an account over the Sieve script, and run-on-existing-mail.</summary>
public interface IFilterService
{
    Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct);

    /// <summary>Generates the script with the Bulwark-compatible generator (spam guard on move and copy rules when the server supports it, hand-written blocks kept), validates it, saves and activates it only if the loaded state still matches. Refused for a locked script or absent state. Returns the state as saved.</summary>
    Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct);

    /// <summary>Scans the Inbox for the rule (Email/query narrowed by <see cref="FilterRules.QueryFilter"/>, Email/get in chunks) and builds the plan; <paramref name="generation"/> is the caller's run counter.</summary>
    Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct);

    /// <summary>Applies a scan's plan with ifInState; the outcome says how much was done.</summary>
    Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct);

    event EventHandler<FilterChangedEventArgs>? Changed;
}
