// Mirrors: Rust jmap.rs PushEvent :1570 and the list delta; M1-plan §4.1 (closed hierarchy, one record per file). Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>An event of the sync engine (closed hierarchy: the sealed records in this folder).</summary>
public abstract record MailSyncEvent
{
    private protected MailSyncEvent()
    {
    }
}
