// Mirrors: M2 plan section 4.5 (IAttachmentSaver: download, save as, .eml, zip, Save to Files); Bulwark components/email/reader-attachments.tsx, lib/files-save.ts, lib/files-zip.ts;
// Rust reader.rs:803 save_eml, :824 download_all, :919 save_to_files. Contract from stage F of M2; owner reader.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Attachments;

/// <summary>Saves attachments to disk or to Files.</summary>
public interface IAttachmentSaver
{
    Task<Result<Unit>> SaveAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct);

    Task<Result<byte[]?>> ReadForPreviewAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct);

    Task<Result<Unit>> SaveEmlAsync(MailTarget message, CancellationToken ct);

    Task<Result<(byte[] Bytes, string Name)>> RawAsync(MailTarget message, CancellationToken ct);

    Task<Result<Unit>> SaveAllAsZipAsync(MailTarget message, CancellationToken ct);
}
