// Mirrors: Rust filters_x.rs :451 apply_with, :475 current, :477 applied_text, :446 Applied; RFC 8620 section 5.3 (ifInState, stateMismatch), RFC 8621 section 4.6 (Email/set updates); Bulwark lib/filters/retroactive.ts (the apply loop). T-098, T-099.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Filters.Rules;

public static partial class FilterRules
{
    /// <summary>Applies the steps in Email/set calls of at most <paramref name="max"/> updates, each with ifInState chained from the previous newState; stops on stateMismatch (Stale) or an error (Failed) with the count so far (T-099). <paramref name="send"/> posts one method call and returns its response.</summary>
    public static async Task<FilterApplied> ApplyAsync(Func<JsonArray, Task<Result<JsonArray>>> send, string accountId, string state, IReadOnlyList<FilterPlanStep> steps, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(steps);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var step in steps)
        {
            var patch = StepPatch(step.Kind, step.Arg);
            foreach (var chunk in step.Ids.Chunk(Math.Max(1, max)))
            {
                ct.ThrowIfCancellationRequested();
                var update = new JsonObject();
                foreach (var id in chunk)
                {
                    update[id] = patch.DeepClone();
                }

                var args = new JsonObject { ["accountId"] = accountId, ["update"] = update };
                if (state.Length > 0)
                {
                    args["ifInState"] = state;
                }

                var sent = await send(new JsonArray("Email/set", args, "a")).ConfigureAwait(false);
                if (!sent.IsOk)
                {
                    return new FilterApplied(FilterAppliedKind.Failed, done.Count, sent.Error!.Message);
                }

                var r = sent.Value!;
                var body = r.Count > 1 ? r[1] : null;
                if (string.Equals(Text(r[0]), "error", StringComparison.Ordinal))
                {
                    return string.Equals(Str(body, "type"), "stateMismatch", StringComparison.Ordinal)
                        ? new FilterApplied(FilterAppliedKind.Stale, done.Count, null)
                        : new FilterApplied(FilterAppliedKind.Failed, done.Count, MethodErrors.Message(Str(body, "type") ?? "error", Str(body, "description")));
                }

                if (body?["updated"] is JsonObject updated)
                {
                    done.UnionWith(updated.Select(kv => kv.Key));
                }

                if (body?["notUpdated"] is JsonObject notUpdated)
                {
                    bad.AddRange(notUpdated.Select(kv => $"{kv.Key}: {Str(kv.Value, "type") ?? "error"}"));
                }

                state = Str(body, "newState") ?? string.Empty;
            }
        }

        return bad.Count == 0 ? new FilterApplied(FilterAppliedKind.Done, done.Count, null) : new FilterApplied(FilterAppliedKind.Failed, done.Count, string.Join("; ", bad));
    }

    /// <summary>Whether a scan's result still belongs to the open dialog: it is open and no later run started (T-098).</summary>
    public static bool Current(long now, long generation, bool open) => open && now == generation;

    /// <summary>"Rule applied to 3 messages", "Applied to 2 of 3 messages; your mail changed meanwhile, run the rule again for the rest.", "Applied to 0 of 3 messages: network down" (T-099).</summary>
    public static string AppliedText(FilterApplied result, int total)
    {
        ArgumentNullException.ThrowIfNull(result);
        static string S(int n) => n == 1 ? string.Empty : "s";
        return result.Kind switch
        {
            FilterAppliedKind.Done => $"Rule applied to {result.Count} message{S(result.Count)}",
            FilterAppliedKind.Stale when result.Count == 0 => "Your mail changed since the check; nothing was applied. Run the rule again.",
            FilterAppliedKind.Stale => $"Applied to {result.Count} of {total} message{S(total)}; your mail changed meanwhile, run the rule again for the rest.",
            _ => $"Applied to {result.Count} of {total} message{S(total)}: {result.Error}",
        };
    }
}
