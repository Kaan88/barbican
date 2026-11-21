// Mirrors: Bulwark lib/attachment-name.ts:43-58 (getAttachmentDisplayName, splitAttachmentName), lib/file-name-rules.ts:87-93 (plainFileNameProblem:
// over 255 characters, a path separator or a control character), lib/download-filename.ts:227-231 (renamedAttachmentFilename: bidi marks stripped with
// lib/bidi.ts:10-12, control characters and <>:"/\|?* become "_", trimmed, "attachment" when nothing is left).
// The reader's rename keeps the typed name for this open message only: the message is not written back (email-viewer.tsx:3101-3123). Owner reader.

using System.Text;

namespace Barbican.Core.Mail.Attachments;

/// <summary>The name rules of an attachment the user renames for saving a copy.</summary>
public static class AttachmentNames
{
    /// <summary>Every file system Bulwark supports refuses a name over 255 characters.</summary>
    public const int MaxLength = 255;

    /// <summary>The name shown for a part that has none (the reader's own fallback, reader-attachments.tsx:152).</summary>
    public static string Display(string? name) => string.IsNullOrEmpty(name) ? "attachment" : name;

    /// <summary>
    /// A file name split before its extension: "report.pdf" gives ("report", ".pdf") and "archive.tar.gz" gives ("archive.tar", ".gz").
    /// A name with no extension ("README", ".bashrc", "name.") comes back whole, extension empty.
    /// </summary>
    public static (string Stem, string Extension) Split(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? (name[..dot], name[dot..]) : (name, string.Empty);
    }

    /// <summary>What keeps <paramref name="name"/> from being used as a file name anywhere, with no server rules involved.</summary>
    public static AttachmentNameProblem Problem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > MaxLength)
        {
            return AttachmentNameProblem.TooLong;
        }

        return name.Any(c => c is '/' or '\\' || char.IsControl(c)) ? AttachmentNameProblem.InvalidCharacters : AttachmentNameProblem.None;
    }

    /// <summary>
    /// The download name of a renamed attachment: the typed name and nothing else (no template, no transforms), with what a file system cannot
    /// take replaced by "_": directional controls are dropped, control characters and <c>&lt;&gt;:"/\|?*</c> become "_", and the name is trimmed.
    /// </summary>
    public static string DownloadName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (IsBidiControl(c))
            {
                continue;
            }

            sb.Append(c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c) ? '_' : c);
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? "attachment" : cleaned;
    }

    // lib/bidi.ts:6-8: the LRM/RLM marks, the embeddings and overrides, and the isolates.
    private static bool IsBidiControl(char c) => c is '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');
}
