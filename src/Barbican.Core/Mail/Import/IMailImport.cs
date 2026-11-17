// Mirrors: M2 plan section 4.6 (IMailImport); Rust mailimport.rs:281, Bulwark lib/mail-import/engine.ts. Contract from stage F of M2; owner unified.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Import;

/// <summary>Imports .eml and .mbox files into a mailbox.</summary>
public interface IMailImport
{
    Task<Result<ImportOutcome>> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, IProgress<int> progress, CancellationToken ct);
}
