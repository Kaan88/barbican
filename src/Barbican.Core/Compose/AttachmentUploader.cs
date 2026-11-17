// Mirrors: see IAttachmentUploader. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IAttachmentUploader"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class AttachmentUploader : IAttachmentUploader
{
    public Task<Result<DraftAttachment>> UploadAsync(string accountId, AttachmentUpload upload, IReadOnlyList<DraftAttachment> existing, CancellationToken ct) => Task.FromException<Result<DraftAttachment>>(new PortPendingException("AttachmentUploader.UploadAsync"));
}
