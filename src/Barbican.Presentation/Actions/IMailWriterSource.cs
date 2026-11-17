// Mirrors: M2 plan 4.3 (IMailWriterFactory.ForSession: one writer per account session); Bulwark lib/jmap/client.ts (every write takes the account the rows belong to).

using Barbican.Core.Mail.Write;

namespace Barbican.Presentation.Actions;

/// <summary>The writer of an account, by its key; actions never see sessions.</summary>
public interface IMailWriterSource
{
    /// <summary>The writer of the account, or null when it has no session (offline at start, signed out).</summary>
    IMailWriter? WriterOf(string accountKey);

    /// <summary>False for a read-only account and for one without a session: menus show the write items disabled.</summary>
    bool CanWrite(string accountKey);
}
