// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.calendar.ts:42-116 doCalendarRespond (c.ts:24,85: the high-risk calendar:respond permission, c.types.ts:14-36 InvitationAnswer / InvitationResponseView) and the reader's
// banner steps it shares (lib/calendar-rsvp.ts answerInvitation; Barbican: IInvitationService.AnswerAsync, the same call the reader's Accept / Maybe / Decline buttons make: the stored copy is answered, else the invitation
// is added with the answer in it, and the server sends the reply to the organiser, JMAP Calendars sendSchedulingMessages / RFC 5546 REPLY). Refused, as in Bulwark: reading invitations switched off in Settings, a message of
// another login, a message that is no invitation that asks for an answer (a reply, a cancellation), an event the user organises, an invitation whose sender fails the trust check (a warning, InvitationModel.Trust; the account
// server's own Authentication-Results only, R2-07). Barbican refuses up front when the user is not among the participants instead of adding the event and then failing (c.calendar.ts:105). Resolves
// { status, title, start, organizer }, or null when another add-on's onBeforeEventRsvp stopped the answer.
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarRespond(IPluginCalendarHost host) : PluginCalendarMethod("calendar.respond", "calendar:respond", host, true, TimeSpan.FromSeconds(90))
{
    private static readonly string[] Statuses = ["accepted", "tentative", "declined"];

    /// <summary>InvitationService.AnswerAsync's text when an onBeforeEventRsvp handler said no.</summary>
    private const string StoppedText = "A plugin stopped the answer.";

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.At(arguments, 1) is { Kind: PluginValueKind.String } status && Statuses.Contains(status.StringValue, StringComparer.Ordinal)
            ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var (emailId, status) = (arguments.Item(0).StringValue, arguments.Item(1).StringValue);
        if (!Host.InvitationParsingEnabled || await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false) is null) return Fail(PluginErrorCode.Unavailable);
        var message = await call.Session.Mail.GetBodyAsync(emailId, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!message.IsOk) return Fail(message.Error is MethodError { Type: "notFound" } ? PluginErrorCode.Unavailable : PluginErrorCode.Failed);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var body = message.Value!;
        if (CalendarPart(body) is not { } part) return Fail(PluginErrorCode.Unavailable);
        var parsed = await Host.Invitations.ParseAsync(call.AccountKey, part.BlobId!, ct).ConfigureAwait(false);
        if (!parsed.IsOk) return Fail(PluginErrorCode.Failed);
        var (invitation, method) = (parsed.Value!.Event, parsed.Value.Method);
        if (method != InviteMethod.Request) return Fail(PluginErrorCode.Failed);
        var own = await PluginCalendarData.OwnAsync(call.Session, ct).ConfigureAwait(false);
        var server = AuthServTrust.ServerHostOf(call.AccountKey);
        var results = AuthTrace.ServerResults(body.Headers, null, id => AuthServTrust.Trusts(server, id), out _);
        var from = body.From.Count > 0 ? body.From[0].Email : null;
        if (InvitationModel.Trust(invitation, method, from, results, results.Count > 0, own.Addresses) is { Warning: true }) return Fail(PluginErrorCode.Failed);
        var stored = await Host.Invitations.StoredAsync(call.AccountKey, invitation.Str("uid") ?? "", ct).ConfigureAwait(false);
        var known = stored.IsOk ? stored.Value : null;
        if (InvitationModel.IOrganize(known ?? invitation, own.Addresses) || InvitationModel.MyParticipant(known ?? invitation, own.Addresses) is null) return Fail(PluginErrorCode.Failed);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var answered = await Host.Invitations.AnswerAsync(call.AccountKey, invitation, [], status, ct).ConfigureAwait(false);
        if (!answered.IsOk) return string.Equals(answered.Error?.Message, StoppedText, StringComparison.Ordinal) ? Null() : Fail(PluginErrorCode.Failed);
        var organizer = invitation.Str("organizerCalendarAddress") ?? invitation.Child("replyTo").Str("imip");
        var b = new PluginValueBuilder();
        return Success(b, b.Object(
            ("status", b.String(status)), ("title", b.String(invitation.Str("title") ?? "")), ("start", invitation.Str("start") is { } start ? b.String(start) : b.Null()),
            ("organizer", organizer is null ? b.Null() : b.String(organizer.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? organizer[7..] : organizer))));
    }

    /// <summary>The message's calendar part: text/calendar, application/ics or a .ics name, with a blob (the reader's InvitationBannerSource.CalendarPart).</summary>
    private static Attachment? CalendarPart(EmailBody body) => body.Attachments.FirstOrDefault(a => a.BlobId is not null
        && ((a.Type ?? "").StartsWith("text/calendar", StringComparison.OrdinalIgnoreCase) || string.Equals(a.Type, "application/ics", StringComparison.OrdinalIgnoreCase)
            || (a.Name ?? "").EndsWith(".ics", StringComparison.OrdinalIgnoreCase)));
}
