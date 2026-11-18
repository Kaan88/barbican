// Mirrors: Rust cal_extra.rs:611 subscriptions, :650 sub_begin, :666 refresh_feed, :766 sub_save, :817 sub_remove, :840 sub_refresh; Bulwark lib/calendar-subscription-sync.ts, calendar-feed-diff.ts, components/calendar/ical-subscription-modal.tsx; B-CAL-041..044, 061; I-307; G-006; hook onICalSubscriptionChange.
// Stalwart keeps no feed address on a calendar, so the subscriptions live in the profile's calendar-subscriptions.json (D-M4-E02), with a hash of every stored feed event so a refresh that changed nothing writes nothing.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>The default <see cref="ICalendarSubscriptions"/>.</summary>
public sealed class CalendarSubscriptions(IAccountManager accounts, ICalendarAdmin admin, ICalendarStore store, IGuardedHttp http, IHookBus hooks, IProfilePaths paths) : ICalendarSubscriptions, IDisposable
{
    public const string FileName = "calendar-subscriptions.json";

    private const string DefaultColor = "#3b82f6";

    private static readonly string[] Clearable = ["description", "locations", "recurrenceRules", "recurrenceOverrides", "participants", "organizerCalendarAddress", "status", "freeBusyStatus", "privacy", "keywords", "showWithoutTime", "timeZone"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<CalendarSubscription>> ListAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return Subscriptions(Read(), accountKey);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<Result<string?>> SaveAsync(string accountKey, CalendarSubscription subscription, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(name);
        var check = FeedRules.Check(subscription.Url);
        if (!check.IsAllowed)
        {
            return Result<string?>.Fail(new ValidationError(check.Blocked ?? "This address cannot be used."));
        }

        var calendarId = subscription.CalendarId;
        var isNew = calendarId.Length == 0;
        if (isNew)
        {
            var created = await admin.CreateCalendarAsync(accountKey, name.Trim().Length > 0 ? name.Trim() : check.Url!, DefaultColor, ct).ConfigureAwait(false);
            if (!created.IsOk)
            {
                return Result<string?>.Fail(created.Error!);
            }

            calendarId = created.Value!;
        }

        var saved = subscription with { CalendarId = calendarId, Url = check.Url!, IntervalMinutes = Math.Max(FeedRules.IntervalsMinutes[0], subscription.IntervalMinutes) };
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = Read();
            var list = Subscriptions(state, accountKey).Where(s => !string.Equals(s.CalendarId, calendarId, StringComparison.Ordinal)).Append(saved).ToList();
            Write(state, accountKey, list);
        }
        finally
        {
            _ = _gate.Release();
        }

        hooks.Fire(HookNames.OnICalSubscriptionChange, () => new SubscriptionChangePayload(calendarId, isNew ? "added" : "updated"));
        var refreshed = await RefreshAsync(accountKey, calendarId, ct).ConfigureAwait(false);
        return refreshed.IsOk ? Result<string?>.Ok(check.Warning) : Result<string?>.Fail(refreshed.Error!);
    }

    public async Task<Result<int>> RefreshAsync(string accountKey, string calendarId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<int>.Fail(client.Error!);
        }

        var sub = (await ListAsync(accountKey, ct).ConfigureAwait(false)).FirstOrDefault(s => string.Equals(s.CalendarId, calendarId, StringComparison.Ordinal));
        if (sub is null)
        {
            return Result<int>.Fail(new ValidationError("This calendar is not a subscription."));
        }

        var fetched = await FetchAsync(sub.Url, ct).ConfigureAwait(false);
        if (!fetched.IsOk)
        {
            return Result<int>.Fail(fetched.Error!);
        }

        var feed = IcsReader.Parse(fetched.Value!).Where(e => !e.IsTask()).ToList();
        FeedRules.FillUids(sub.Url, feed);
        var loaded = store.Snapshot(accountKey) is { } snapshot ? Result<CalendarSnapshot>.Ok(snapshot) : await store.LoadAsync(accountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Result<int>.Fail(loaded.Error!);
        }

        var (account, serverCalendar) = CalendarIds.SplitNs(calendarId);
        account ??= client.Value!.Session.AccountFor(CalendarJmap.Capability);
        Dictionary<string, string> hashes;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            hashes = Hashes(Read(), accountKey, calendarId);
        }
        finally
        {
            _ = _gate.Release();
        }

        var plan = Plan(feed, loaded.Value!, account, serverCalendar, hashes);
        var (changed, failed) = await ApplyAsync(client.Value!, accountKey, account, plan, ct).ConfigureAwait(false);
        if (failed.Count == 0)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var state = Read();
                SetHashes(state, accountKey, calendarId, plan.NewHashes);
                Write(state);
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        hooks.Fire(HookNames.OnICalSubscriptionChange, () => new SubscriptionChangePayload(calendarId, "refreshed"));
        return failed.Count == 0 ? Result<int>.Ok(changed) : Result<int>.Fail(new ValidationError(FeedRules.RefreshError(failed)));
    }

    /// <summary>What a refresh does: the events to create, the patches, the server ids to remove, and the hash of every feed event for the next comparison.</summary>
    private sealed record RefreshPlan(List<(string Key, JsonObject Data)> Creates, List<(string Id, JsonObject Patch)> Updates, List<string> Removed, Dictionary<string, string> NewHashes);

    private static RefreshPlan Plan(IReadOnlyList<JsonNode> feed, CalendarSnapshot snapshot, string account, string serverCalendar, Dictionary<string, string> hashes)
    {
        var existing = snapshot.Events
            .Where(e => e.Obj("calendarIds") is { } ids && ids.Any(kv => string.Equals(CalendarIds.SplitNs(kv.Key).Id, serverCalendar, StringComparison.Ordinal)) && CalendarIds.RecurrenceIdOf(e.Str("id") ?? "") is null)
            .GroupBy(e => e.Str("uid") ?? "", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var plan = new RefreshPlan([], [], [], new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var ev in feed)
        {
            var uid = ev.Str("uid")!;
            if (plan.NewHashes.ContainsKey(uid))
            {
                continue;
            }

            var clean = (JsonObject)FeedRules.SanitizeFeed(ev, serverCalendar);
            var hash = Hash(clean);
            plan.NewHashes[uid] = hash;
            if (!existing.TryGetValue(uid, out var stored))
            {
                plan.Creates.Add(("f" + plan.Creates.Count.ToString(CultureInfo.InvariantCulture), clean));
            }
            else if (!hashes.TryGetValue(uid, out var old) || !string.Equals(old, hash, StringComparison.Ordinal))
            {
                plan.Updates.Add((CalendarWriter.ServerRef(account, stored, stored.Str("id") ?? "").ServerId, PatchOf(clean, stored)));
            }
        }

        plan.Removed.AddRange(existing.Where(kv => !plan.NewHashes.ContainsKey(kv.Key)).Select(kv => CalendarWriter.ServerRef(account, kv.Value, kv.Value.Str("id") ?? "").ServerId));
        return plan;
    }

    private static JsonObject PatchOf(JsonObject clean, JsonNode stored)
    {
        var patch = new JsonObject();
        foreach (var (k, v) in clean.Where(kv => kv.Key is not "uid" and not "calendarIds" and not "@type"))
        {
            patch[k] = v?.DeepClone();
        }

        foreach (var key in Clearable.Where(k => clean[k] is null && stored[k] is not null))
        {
            patch[key] = null;
        }

        return patch;
    }

    private async Task<(int Changed, List<string> Failed)> ApplyAsync(IJmapClient client, string accountKey, string account, RefreshPlan plan, CancellationToken ct)
    {
        var failed = new List<string>();
        var changed = 0;
        if (plan.Creates.Count > 0)
        {
            var (made, bad, error) = await CalendarJmap.CreateManyAsync(client, account, plan.Creates, ct).ConfigureAwait(false);
            changed += made;
            failed.AddRange(bad);
            failed.AddRange(error is null ? [] : [error.Message]);
        }

        if (plan.Updates.Count > 0)
        {
            var (bad, error) = await CalendarJmap.UpdateManyAsync(client, account, plan.Updates, ct).ConfigureAwait(false);
            changed += plan.Updates.Count - bad.Count;
            failed.AddRange(bad);
            failed.AddRange(error is null ? [] : [error.Message]);
        }

        if (plan.Removed.Count > 0)
        {
            var (bad, error) = await CalendarJmap.DestroyManyAsync(client, account, plan.Removed, ct).ConfigureAwait(false);
            changed += plan.Removed.Count - bad.Count;
            failed.AddRange(bad);
            failed.AddRange(error is null ? [] : [error.Message]);
        }

        if (changed > 0)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return (changed, failed);
    }

    public async Task<Result<Unit>> RemoveAsync(string accountKey, string calendarId, bool deleteCalendar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = Read();
            Write(state, accountKey, Subscriptions(state, accountKey).Where(s => !string.Equals(s.CalendarId, calendarId, StringComparison.Ordinal)).ToList());
            SetHashes(state, accountKey, calendarId, null);
            Write(state);
        }
        finally
        {
            _ = _gate.Release();
        }

        if (deleteCalendar)
        {
            var deleted = await admin.DeleteCalendarAsync(accountKey, calendarId, ct).ConfigureAwait(false);
            if (!deleted.IsOk)
            {
                return deleted;
            }
        }

        hooks.Fire(HookNames.OnICalSubscriptionChange, () => new SubscriptionChangePayload(calendarId, "removed"));
        return Result<Unit>.Ok(default);
    }

    /// <summary>G-006: the address is checked before the request and the guarded client refuses private addresses at connect time (D-M4-E03: a redirect is not followed, the client gives no Location); the body is capped.</summary>
    private async Task<Result<string>> FetchAsync(string url, CancellationToken ct)
    {
        var check = FeedRules.Check(url);
        if (!check.IsAllowed)
        {
            return Result<string>.Fail(new ValidationError(check.Blocked ?? "This address cannot be used."));
        }

        var sent = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri(check.Url!), Accept: "text/calendar, text/plain, */*"), ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<string>.Fail(sent.Error!);
        }

        var response = sent.Value!;
        if (response.StatusCode is >= 300 and < 400)
        {
            return Result<string>.Fail(new ValidationError("The feed address redirects; enter the address it redirects to."));
        }

        if (response.StatusCode is < 200 or >= 300)
        {
            return Result<string>.Fail(new ValidationError(string.Create(CultureInfo.InvariantCulture, $"The feed answered {response.StatusCode}.")));
        }

        return response.Body.Length > FeedRules.MaxBytes
            ? Result<string>.Fail(new ValidationError("The feed is larger than 20 MB."))
            : Result<string>.Ok(Encoding.UTF8.GetString(response.Body.Span));
    }

    private static string Hash(JsonNode node) => System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));

    private string FilePath => Path.Combine(paths.ConfigDir, FileName);

    private JsonObject Read()
    {
        try
        {
            return AtomicFile.ReadAllTextOrNull(FilePath) is { Length: > 0 } text && JsonNode.Parse(text) is JsonObject o ? o : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(JsonObject state) => AtomicFile.WriteAllText(FilePath, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    private void Write(JsonObject state, string accountKey, List<CalendarSubscription> list)
    {
        var subs = state["subscriptions"] as JsonObject ?? [];
        subs[accountKey] = new JsonArray([.. list.Select(s => (JsonNode)new JsonObject { ["calendarId"] = s.CalendarId, ["url"] = s.Url, ["intervalMinutes"] = s.IntervalMinutes })]);
        state["subscriptions"] = subs;
        Write(state);
    }

    private static List<CalendarSubscription> Subscriptions(JsonObject state, string accountKey)
    {
        var list = new List<CalendarSubscription>();
        foreach (var s in (state["subscriptions"] as JsonObject)?[accountKey] as JsonArray ?? [])
        {
            if (s.Str("calendarId") is { Length: > 0 } id && s.Str("url") is { Length: > 0 } url)
            {
                list.Add(new CalendarSubscription(id, url, (int?)s?["intervalMinutes"] ?? 60));
            }
        }

        return list;
    }

    private static Dictionary<string, string> Hashes(JsonObject state, string accountKey, string calendarId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if ((((state["hashes"] as JsonObject)?[accountKey] as JsonObject)?[calendarId]) is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                if ((string?)v is { } h)
                {
                    map[k] = h;
                }
            }
        }

        return map;
    }

    private static void SetHashes(JsonObject state, string accountKey, string calendarId, Dictionary<string, string>? hashes)
    {
        var all = state["hashes"] as JsonObject ?? [];
        var account = all[accountKey] as JsonObject ?? [];
        if (hashes is null)
        {
            _ = account.Remove(calendarId);
        }
        else
        {
            account[calendarId] = new JsonObject(hashes.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));
        }

        all[accountKey] = account;
        state["hashes"] = all;
    }
}
