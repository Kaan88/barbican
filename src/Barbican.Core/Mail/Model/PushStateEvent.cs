// Mirrors: Bulwark lib/mail-push-listener.ts; Rust jmap.rs SseParser :1583; hook onPushConnectionChange. Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The push connection opened or dropped.</summary>
public sealed record PushStateEvent(bool Connected) : MailSyncEvent;
