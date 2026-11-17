// Mirrors: Rust jmap.rs (SSE parser, list delta :1522-:1570), main.rs push wiring; Bulwark lib/mail-push-listener.ts;
// M1-plan §4.1. Contract stub from F2; owner jmap.

using System.Threading.Channels;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail;

/// <summary>The push and delta engine for one account.</summary>
public interface IMailSync
{
    /// <summary>Events: MailboxesChanged, list deltas and resets, EmailsChanged, PushState.</summary>
    ChannelReader<MailSyncEvent> Events { get; }

    /// <summary>The open list the engine keeps in sync.</summary>
    void Watch(ListQuery query);

    /// <summary>
    /// The open list, read as <paramref name="loaded"/>: its query, email and thread states let the next push bring it up to date with
    /// a delta instead of a reset (Rust mail.rs:34 states_of).
    /// </summary>
    void Watch(ListQuery query, ListPage loaded) => Watch(query);

    /// <summary>Runs push and the delta loop until cancelled.</summary>
    Task RunAsync(CancellationToken ct);
}
