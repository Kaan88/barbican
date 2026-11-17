// Mirrors: Bulwark components/email/calendar-invitation-banner.tsx (title, when, where, organizer, trust note; RSVP buttons); Rust reader.rs:412 banners
// (invite), invite.rs:191 (CalendarEvent/parse of the attached .ics; the METHOD read from the raw text), :250 trust. J-072 partial: display and trust
// only; RSVP needs Calendar (M4), so the banner says so instead of offering buttons. Owner reader.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>The calendar invitation banner.</summary>
public sealed class InvitationBannerSource(IActiveMail active, IAccountManager accounts) : IReaderBannerSource
{
    public const string Id = "invitation";
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
    public static ReaderBanner Banner(JsonNode ev, string? rawIcs, string? from, IReadOnlyList<Core.Mail.AuthResults.AuthResult> serverResults, IReadOnlyList<string> mine, TimeZoneInfo zone)
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

        return new ReaderBanner(Id, $"{lead}: {title}", detail, trust?.Warning ?? false, []) { Note = method is InviteMethod.Request or InviteMethod.Add ? RsvpNote : null };
    }

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Body is not { } body || CalendarPart(body) is not { } part
            || active.AccountKey is not { } key || accounts.SessionFor(key) is not { } session)
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

        var raw = part.Size is > 0 and <= MaxIcsBytes ? await RawAsync(client, client.Session.MailAccountId, part, ct).ConfigureAwait(false) : null;
        var mine = active.Mail is { } mail && await mail.GetIdentitiesAsync(ct).ConfigureAwait(false) is { IsOk: true } ids
            ? (IReadOnlyList<string>)[.. ids.Value!.Select(i => i.Email)]
            : [];
        var from = body.From.Count > 0 ? body.From[0].Email : null;
        yield return Banner(ev, raw, from, snapshot.ServerResults, mine, TimeZoneInfo.Local);
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
