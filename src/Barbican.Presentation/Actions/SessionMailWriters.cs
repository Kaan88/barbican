// Mirrors: M2 plan 4.3 (IMailWriterFactory.ForSession); Rust jmap.rs read-only client (account_cap :306). Owner actions.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Write;

namespace Barbican.Presentation.Actions;

/// <summary>The writers of the signed-in accounts, built from their sessions.</summary>
public sealed class SessionMailWriters(IAccountManager accounts, IMailWriterFactory factory) : IMailWriterSource
{
    /// <inheritdoc />
    public IMailWriter? WriterOf(string accountKey) => accounts.SessionFor(accountKey) is { } session ? factory.ForSession(session) : null;

    /// <inheritdoc />
    public bool CanWrite(string accountKey) => accounts.SessionFor(accountKey) is { Client.IsReadOnly: false };
}
