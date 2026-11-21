// Mirrors: Bulwark components/email/email-composer.tsx attachment limits (maxSizeUpload of the account, maxSizeAttachmentsPerEmail of the mail capability) as
// Barbican's AttachmentUploader applies them (Core/Compose/AttachmentUploader.cs:24-37); RFC 8620 section 2 (maxSizeUpload), RFC 8621 section 1.3.1.
using System.Text.Json;
using Barbican.Core.Jmap;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>The same size gates the composer's own uploader applies, for a blob a plugin says it already uploaded. The size is the plugin's claim; the server checks the real one at send.</summary>
public static class PluginAttachGate
{
    public const int MaxAttachments = 200;

    public static bool Allows(JmapSession session, long existingBytes, int existingCount, long size)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (size < 0 || size > session.Limits.MaxSizeUpload || existingCount >= MaxAttachments) return false;
        return session.CapabilityObject(session.MailAccountId, JmapCapabilities.Mail) is not { } mail ||
            !mail.TryGetProperty("maxSizeAttachmentsPerEmail", out var cap) || cap.ValueKind != JsonValueKind.Number ||
            !cap.TryGetInt64(out var limit) || limit <= 0 || existingBytes + size <= limit;
    }
}
