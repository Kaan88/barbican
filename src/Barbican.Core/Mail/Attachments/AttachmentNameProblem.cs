// Mirrors: Bulwark lib/file-name-rules.ts:87-93 plainFileNameProblem (what makes a typed name unusable with no server rules involved);
// components/email/rename-attachment-dialog.tsx:32-36 maps tooLong and chars to their messages. Owner reader.

namespace Barbican.Core.Mail.Attachments;

/// <summary>Why a name the user typed cannot be used, as <c>plainFileNameProblem</c> reports it; <see cref="None"/> when it can.</summary>
public enum AttachmentNameProblem
{
    /// <summary>The name is usable.</summary>
    None,

    /// <summary>More than 255 characters (<c>plainFileNameProblem</c> lengths in characters, not bytes).</summary>
    TooLong,

    /// <summary>A path separator or a control character (Bulwark's "chars" and "control" share one message).</summary>
    InvalidCharacters,
}
