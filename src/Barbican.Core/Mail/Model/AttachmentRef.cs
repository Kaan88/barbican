// Mirrors: M2 plan section 4.5 (AttachmentRef: one attachment of a message by blob id and index); Bulwark components/email/reader-attachments.tsx. Contract from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Model;

/// <summary>One attachment of a message.</summary>
/// <param name="Index">Its position among the message's attachments (menus pass it as AttachmentIndex).</param>
/// <param name="Attachment">The attachment's parts.</param>
public sealed record AttachmentRef(int Index, Attachment Attachment);
