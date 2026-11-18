// Mirrors: Bulwark lib/plugin-hook-catalog.ts:154 onAttachmentPreview (AttachmentPreview, AttachmentInfo): a plugin may take the preview over. Owner files-ui (I-228).

namespace Barbican.Core.Mail.Reader.Hooks;

/// <summary>What the onAttachmentPreview transform passes along: the file and whether a plugin showed it itself (then the app's own preview stays closed).</summary>
public sealed record AttachmentPreview(AttachmentInfo Attachment, bool Handled);
