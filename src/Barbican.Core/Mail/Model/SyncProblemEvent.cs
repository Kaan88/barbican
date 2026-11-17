// Mirrors: Fable review 1 F-15 (a push open that fails with an auth error was retried for ever and never reached the UI); Bulwark
// lib/mail-push-listener.ts (the listener reports a failed connection). Owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>Live updates cannot continue as they are: the server rejected the credentials, so the user must act.</summary>
/// <param name="Message">What to show.</param>
/// <param name="NeedsSignIn">True when the account must sign in again.</param>
public sealed record SyncProblemEvent(string Message, bool NeedsSignIn) : MailSyncEvent;
