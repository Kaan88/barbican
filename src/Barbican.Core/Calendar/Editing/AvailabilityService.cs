// Mirrors: Rust cal_editor.rs:1180 principal_ids (Principal/query by email), :1213 availability (Principal/getAvailability), :1258 refresh_availability; JMAP Principals Availability (urn:ietf:params:jmap:principals:availability), Stalwart crates/jmap/src/principal/get_availability.rs.
// G-009: without the capability nothing is asked.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Periods = Barbican.Core.Common.Result<System.Collections.Generic.IReadOnlyList<(long Start, long End, bool Tentative)>>;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IAvailabilityService"/>.</summary>
public sealed class AvailabilityService(IAccountManager accounts) : IAvailabilityService
{
    public const string Capability = "urn:ietf:params:jmap:principals:availability";

    private const string PrincipalsCapability = "urn:ietf:params:jmap:principals";

    public bool IsSupported(string accountKey) =>
        accounts.SessionFor(accountKey)?.Client.Session is { } s && s.Has(Capability) && s.Has(CalendarJmap.Capability);

    public async Task<Result<IReadOnlyDictionary<string, Periods>>> BusyAsync(string accountKey, IReadOnlyList<string> emails, long fromMs, long toMs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emails);
        if (!IsSupported(accountKey))
        {
            return Result<IReadOnlyDictionary<string, Periods>>.Fail(new ValidationError("This server does not offer availability."));
        }

        var client = accounts.SessionFor(accountKey)!.Client;
        var account = client.Session.Has(PrincipalsCapability) ? client.Session.AccountFor(PrincipalsCapability) : client.Session.AccountFor(CalendarJmap.Capability);
        var from = DateTimeOffset.FromUnixTimeMilliseconds(fromMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var to = DateTimeOffset.FromUnixTimeMilliseconds(toMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var result = new Dictionary<string, Periods>(StringComparer.Ordinal);
        foreach (var email in emails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal))
        {
            result[email] = await OneAsync(client, account, email, from, to, ct).ConfigureAwait(false);
        }

        return Result<IReadOnlyDictionary<string, Periods>>.Ok(result);
    }

    private static async Task<Periods> OneAsync(IJmapClient client, string account, string email, string from, string to, CancellationToken ct)
    {
        var query = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Principal/query", new JsonObject { ["accountId"] = account, ["filter"] = new JsonObject { ["email"] = email } }, "q")), ct).ConfigureAwait(false);
        if (!query.IsOk)
        {
            return Periods.Fail(query.Error!);
        }

        var q = query.Value!.Args("q", "Principal/query");
        if (!q.IsOk)
        {
            return Periods.Fail(q.Error!);
        }

        if ((q.Value!["ids"] as JsonArray)?.FirstOrDefault()?.GetValue<string>() is not { } id)
        {
            return Periods.Fail(new ValidationError("notFound"));
        }

        var call = new JsonObject { ["accountId"] = account, ["id"] = id, ["utcStart"] = from, ["utcEnd"] = to };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Principal/getAvailability", call, "a")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Periods.Fail(r.Error!);
        }

        var a = r.Value!.Args("a", "Principal/getAvailability");
        if (!a.IsOk)
        {
            return Periods.Fail(a.Error!);
        }

        var list = new List<(long Start, long End, bool Tentative)>();
        foreach (var p in a.Value!["list"] as JsonArray ?? [])
        {
            if (Ms((string?)p?["utcStart"]) is { } s && Ms((string?)p?["utcEnd"]) is { } e)
            {
                list.Add((s, e, string.Equals((string?)p?["busyStatus"], "tentative", StringComparison.Ordinal)));
            }
        }

        return Periods.Ok(list);
    }

    private static long? Ms(string? utc) =>
        DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : null;
}
