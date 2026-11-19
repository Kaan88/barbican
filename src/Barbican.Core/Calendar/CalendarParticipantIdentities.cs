// Mirrors Bulwark lib/jmap/client.ts:8155-8183; Stalwart crates/jmap/src/participant_identity/{get,set}.rs and crates/jmap-proto/src/object/participant_identity.rs.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar;

/// <summary>Reads and chooses the server's default calendar participant identity.</summary>
public sealed class CalendarParticipantIdentities(IAccountManager accounts) : ICalendarParticipantIdentities
{
    public async Task<Result<IReadOnlyList<CalendarParticipantIdentity>>> ListAsync(string accountKey, CancellationToken ct)
    {
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<IReadOnlyList<CalendarParticipantIdentity>>.Fail(client.Error!);
        }

        var r = await CallAsync(client.Value!, "ParticipantIdentity/get", new JsonObject { ["accountId"] = client.Value!.Session.AccountFor(CalendarJmap.Capability) }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<CalendarParticipantIdentity>>.Fail(r.Error!);
        }

        var list = (r.Value?["list"] as JsonArray ?? []).OfType<JsonObject>().Select(i => new CalendarParticipantIdentity((string?)i["id"] ?? string.Empty, (string?)i["name"] ?? string.Empty, (string?)i["calendarAddress"] ?? string.Empty, (bool?)i["isDefault"] ?? false)).Where(i => i.Id.Length > 0 && i.CalendarAddress.Length > 0).ToArray();
        return Result<IReadOnlyList<CalendarParticipantIdentity>>.Ok(list);
    }

    public async Task<Result<Unit>> SetDefaultAsync(string accountKey, string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var r = await CallAsync(client.Value!, "ParticipantIdentity/set", new JsonObject { ["accountId"] = client.Value!.Session.AccountFor(CalendarJmap.Capability), ["onSuccessSetIsDefault"] = id }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        // Stalwart silently ignores an unknown id. Verify the effective default after the set.
        var list = await ListAsync(accountKey, ct).ConfigureAwait(false);
        if (!list.IsOk)
        {
            return Result<Unit>.Fail(list.Error!);
        }

        return list.Value!.Any(i => i.IsDefault && string.Equals(i.Id, id, StringComparison.Ordinal))
            ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("The server did not change the calendar address."));
    }

    private static async Task<Result<JsonObject>> CallAsync(IJmapClient client, string method, JsonObject args, CancellationToken ct)
    {
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call(method, args, "p")), ct).ConfigureAwait(false);
        return r.IsOk ? r.Value!.Args("p", method) : Result<JsonObject>.Fail(r.Error!);
    }
}

