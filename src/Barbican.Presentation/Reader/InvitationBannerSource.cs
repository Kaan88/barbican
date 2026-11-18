// Mirrors: Bulwark components/email/calendar-invitation-banner.tsx (title, when, where, organizer, trust note; RSVP buttons); Rust reader.rs:412 banners
// (invite), invite.rs:191 (CalendarEvent/parse of the attached .ics; the METHOD read from the raw text), :250 trust. J-072 partial: display and trust
// only; RSVP needs Calendar (M4), so the banner says so instead of offering buttons. Owner reader.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Invitations;
using Barbican.Presentation.Compose;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>The calendar invitation banner.</summary>
public sealed class InvitationBannerSource(IActiveMail active, IAccountManager accounts, IInvitationService? invitations = null) : IReaderBannerSource, IBannerActionHandler
{
    public const string Id = "invitation";
    public const string Accept = "invite-accept";
    public const string Maybe = "invite-maybe";
    public const string Decline = "invite-decline";
    public const string AddToCalendar = "invite-add";
    public const string ApplyCounter = "invite-apply-counter";
    public const string RemoveCancelled = "invite-remove";
    public const string Calendars = "urn:ietf:params:jmap:calendars";
    public const string RsvpNote = "Replies arrive with Calendar (M4)";
    private const long MaxIcsBytes = 1024 * 1024;

    /// <summary>The calendar part of a message: text/calendar, application/ics or a .ics name, with a blob.</summary>
    public static Attachment? CalendarPart(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Attachments.FirstOrDefault(a => a.BlobId is not null
            && ((a.Type ?? string.Empty).StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Type, "application/ics", StringComparison.OrdinalIgnoreCase)
                || (a.Name ?? string.Empty).EndsWith(".ics", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The banner for one parsed event (pure; the source fetches what it needs).</summary>
    public static ReaderBanner Banner(JsonNode ev, string? rawIcs, string? from, IReadOnlyList<Core.Mail.AuthResults.AuthResult> serverResults, IReadOnlyList<string> mine, TimeZoneInfo zone) =>
        Banner(ev, rawIcs, from, serverResults, mine, zone, withActions: false);

    /// <summary>As above; with <paramref name="withActions"/> the banner has its buttons (I-212): Accept, Maybe and Decline for a request that names the user, Add to calendar, Apply the proposal (the organiser's), Remove from calendar (a cancellation).</summary>
    public static ReaderBanner Banner(JsonNode ev, string? rawIcs, string? from, IReadOnlyList<Core.Mail.AuthResults.AuthResult> serverResults, IReadOnlyList<string> mine, TimeZoneInfo zone, bool withActions)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(mine);
        var method = InvitationModel.Method(ev, rawIcs);
        var title = ev.Str("title") is { Length: > 0 } t ? t : "(no title)";
        var lead = method switch
        {
            InviteMethod.Cancel => "Cancelled",
            InviteMethod.Reply => "Reply",
            InviteMethod.Counter => "New time proposed",
            InviteMethod.Publish => "Event",
            _ => "Invitation",
        };
        var parts = new List<string> { InvitationModel.When(ev, zone.Id, zone) };
        if (InvitationModel.Location(ev) is { Length: > 0 } where)
        {
            parts.Add(where);
        }

        if (InvitationModel.Organizer(ev) is { } organizer)
        {
            parts.Add("Organizer: " + (organizer.Name is { Length: > 0 } n ? n : organizer.Email));
        }

        var trust = InvitationModel.Trust(ev, method, from, serverResults, serverResults.Count > 0, mine);
        var detail = string.Join(" · ", parts.Where(p => p.Length > 0));
        if (trust is not null)
        {
            detail += "\n" + trust.Text;
        }

        var actions = withActions ? ActionsFor(ev, method, mine) : [];
        return new ReaderBanner(Id, $"{lead}: {title}", detail, trust?.Warning ?? false, actions) { Note = !withActions && method is InviteMethod.Request or InviteMethod.Add ? RsvpNote : null };
    }

    private static List<(string ActionId, string Label)> ActionsFor(JsonNode ev, InviteMethod method, IReadOnlyList<string> mine)
    {
        var actions = new List<(string, string)>();
        switch (method)
        {
            case InviteMethod.Request or InviteMethod.Add:
                if (InvitationModel.MyParticipant(ev, mine) is not null)
                {
                    actions.AddRange([(Accept, "Accept"), (Maybe, "Maybe"), (Decline, "Decline")]);
                }

                actions.Add((AddToCalendar, "Add to calendar"));
                break;
            case InviteMethod.Publish:
                actions.Add((AddToCalendar, "Add to calendar"));
                break;
            case InviteMethod.Cancel:
                actions.Add((RemoveCancelled, "Remove from calendar"));
                break;
            case InviteMethod.Counter when InvitationModel.IOrganize(ev, mine):
                actions.Add((ApplyCounter, "Apply the proposal"));
                break;
            default:
                break;
        }

        return actions;
    }

    public bool Owns(string actionId) => actionId is Accept or Maybe or Decline or AddToCalendar or ApplyCounter or RemoveCancelled;

    public async Task<string> RunAsync(ReaderSnapshot snapshot, string actionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (invitations is null)
        {
            return "Calendar is not available.";
        }

        var row = UnifiedRows.ParseRow(snapshot.EmailId);
        if (snapshot.Body is not { } body || CalendarPart(body) is not { } part || (row?.Login ?? active.AccountKey) is not { } key)
        {
            return "There is no invitation in this message.";
        }

        var parsed = await invitations.ParseAsync(key, part.BlobId!, ct).ConfigureAwait(false);
        if (!parsed.IsOk)
        {
            return parsed.Error!.Message;
        }

        var ev = parsed.Value!.Event;
        var mine = accounts.SessionFor(key) is { } session && await session.Mail.GetIdentitiesAsync(ct).ConfigureAwait(false) is { IsOk: true } ids
            ? (IReadOnlyList<string>)[.. ids.Value!.Select(i => i.Email)]
            : [];
        var uid = ev.Str("uid") ?? "";
        switch (actionId)
        {
            case Accept or Maybe or Decline:
                var status = actionId switch { Accept => "accepted", Maybe => "tentative", _ => "declined" };
                var answered = await invitations.AnswerAsync(key, ev, mine, status, ct).ConfigureAwait(false);
                return answered.IsOk ? status switch { "accepted" => "Accepted. The organiser is told.", "tentative" => "Answered maybe. The organiser is told.", _ => "Declined. The organiser is told." } : answered.Error!.Message;
            case AddToCalendar:
                var added = await invitations.AddToCalendarAsync(key, ev, ct).ConfigureAwait(false);
                return added.IsOk ? "Added to your calendar." : added.Error!.Message;
            case ApplyCounter:
                var applied = await invitations.ApplyCounterAsync(key, ev, ct).ConfigureAwait(false);
                return applied.IsOk ? "The event has the proposed time; the guests are told." : applied.Error!.Message;
            default:
                var removed = await invitations.RemoveCancelledAsync(key, uid, ct).ConfigureAwait(false);
                return removed.IsOk ? "Removed from your calendar." : removed.Error!.Message;
        }
    }

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // a unified row's invitation is read through the login that holds it, from its own (or shared) mail account (Fable review 3 R3-01)
        var row = UnifiedRows.ParseRow(snapshot.EmailId);
        if (snapshot.Body is not { } body || CalendarPart(body) is not { } part
            || (row?.Login ?? active.AccountKey) is not { } key || accounts.SessionFor(key) is not { } session)
        {
            yield break;
        }

        var client = session.Client;
        var account = client.Session.AccountFor(Calendars);
        if (account.Length == 0)
        {
            yield break;
        }

        var parsed = await ReaderCalls.CallAsync(client, "CalendarEvent/parse", new JsonObject { ["accountId"] = account, ["blobIds"] = new JsonArray(part.BlobId) }, ct).ConfigureAwait(false);
        if (!parsed.IsOk || parsed.Value!["parsed"]?[part.BlobId!]?[0] is not { } ev)
        {
            yield break;
        }

        var raw = part.Size is > 0 and <= MaxIcsBytes ? await RawAsync(client, row?.SharedAccount ?? client.Session.MailAccountId, part, ct).ConfigureAwait(false) : null;
        var mine = (row is null ? active.Mail : session.Mail) is { } mail && await mail.GetIdentitiesAsync(ct).ConfigureAwait(false) is { IsOk: true } ids
            ? (IReadOnlyList<string>)[.. ids.Value!.Select(i => i.Email)]
            : [];
        var from = body.From.Count > 0 ? body.From[0].Email : null;
        yield return Banner(ev, raw, from, snapshot.ServerResults, mine, TimeZoneInfo.Local, invitations is not null);
    }

    private static async Task<string?> RawAsync(IJmapClient client, string account, Attachment part, CancellationToken ct)
    {
        var r = await client.DownloadAsync(account, part.BlobId!, part.Name ?? "invite.ics", "text/calendar", ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return null;
        }

        var stream = r.Value!;
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }
}
