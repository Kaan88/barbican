// Mirrors: Rust schedule.rs:215 banner ("Scheduled to send at ..." for the open message of the Scheduled view), :240 action (reschedule, edit = cancel and edit, cancel); Bulwark components/email/message-actions.ts, lib/scheduled-send-label.ts (formatScheduledSendAt).
// I-241 (Reschedule), I-282. Owner send-pipeline.

using System.Runtime.CompilerServices;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Schedule;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The reader banner of a message that is waiting to be sent: its release time, Reschedule, Cancel and edit, Cancel send.</summary>
public sealed class ScheduledBannerSource(IScheduledSends scheduled, IActiveMail active, IPrefsStore prefs, TimeProvider time) : IReaderBannerSource
{
    public const string Id = "scheduled";
    public const string Reschedule = "scheduled.reschedule";
    public const string CancelAndEdit = "scheduled.edit";
    public const string CancelSend = "scheduled.cancel";

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (active.AccountKey is not { } account || snapshot.EmailId.Length == 0 || !snapshot.MailboxRoles.Contains("sent", StringComparer.Ordinal))
        {
            yield break;
        }

        var list = await scheduled.ListAsync(account, ct).ConfigureAwait(false);
        if (!list.IsOk || list.Value!.FirstOrDefault(m => string.Equals(m.EmailId, snapshot.EmailId, StringComparison.Ordinal)) is not { } message)
        {
            yield break;
        }

        var label = ScheduleRules.Label(message.At, string.Equals(prefs.Current.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal), time.GetUtcNow(), time.LocalTimeZone);
        yield return new ReaderBanner(Id, $"Scheduled to send at {label}", null, false, [(Reschedule, "Reschedule"), (CancelAndEdit, "Cancel and edit"), (CancelSend, "Cancel send")]);
    }
}
