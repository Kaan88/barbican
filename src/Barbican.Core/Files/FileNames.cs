// Mirrors: Rust files.rs:127 unique_name; Bulwark lib/files-upload-conflicts.ts (a name taken: "name (2).ext"), lib/download-filename.ts (sanitizePart :63, applyTransforms :71, attachmentDownloadFilename without an email :245: the download
// filename rules of settings filenameSpaceReplacement, filenameLowercase, filenameStripDiacritics, filenameCollapseSeparators), renamedAttachmentFilename (what a file system cannot take becomes "_"), lib/files-zip.ts (zip path unique names),
// stores/file-store.ts duplicateResource ("name (copy).ext"). G-019. Owner files-data.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Barbican.Core.Files;

/// <summary>Name arithmetic of the Files app.</summary>
public static partial class FileNames
{
    /// <summary>Longest stem of a generated download name (Bulwark FILENAME_MAX_LEN).</summary>
    public const int DownloadMaxLength = 200;

    private static readonly string[] DeviceNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>The name itself when free, else "stem (2).ext", "stem (3).ext", ... (a leading dot is not an extension: ".bashrc (2)").</summary>
    public static string UniqueName(string name, ISet<string> used)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(used);
        if (!used.Contains(name))
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
        for (var i = 2; ; i++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({i}){ext}");
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>"name (copy).ext" for a file, "name (copy)" for a folder or a name without a real extension (Bulwark duplicateResource, Rust files.rs:684).</summary>
    public static string CopyName(string name, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        return dot > 0 && !isDirectory ? $"{name[..dot]} (copy){name[dot..]}" : $"{name} (copy)";
    }

    /// <summary>The name a download gets from the filename rules of settings. <paramref name="spaceReplacement"/> is "keep", "underscore" or "dash".</summary>
    public static string DownloadName(string name, string spaceReplacement, bool lowercase, bool stripDiacritics, bool collapseSeparators)
    {
        ArgumentNullException.ThrowIfNull(name);
        var cleaned = SanitizePart(name.Trim(), DownloadMaxLength);
        if (cleaned.Length == 0)
        {
            cleaned = "attachment";
        }

        var transformed = Transform(cleaned, spaceReplacement, lowercase, stripDiacritics, collapseSeparators);
        return transformed.Length == 0 ? cleaned : transformed;
    }

    /// <summary>A name that is safe as one path segment on the local disk: control and bidi characters and <c>&lt;&gt;:"/\|?*</c> become "_", trailing dots and spaces go, a Windows device name gets a "_" prefix, at most 255 UTF-16 units. Never empty, never "." or "..".</summary>
    public static string LocalName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var bad = c <= '\u001f' || (c >= '\u007f' && c <= '\u009f') || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) || (c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩');
            _ = sb.Append(bad ? '_' : c);
        }

        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0)
        {
            return "file";
        }

        var stem = s.Split('.', 2)[0].Trim();
        if (DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            s = "_" + s;
        }

        if (s.Length > 255)
        {
            var dot = s.LastIndexOf('.');
            var ext = dot > 0 && s.Length - dot <= 32 ? s[dot..] : string.Empty;
            s = s[..(255 - ext.Length)] + ext;
        }

        return s;
    }

    /// <summary>One segment of a path inside a zip: separators and control characters become "_", and "." or ".." are replaced.</summary>
    public static string ZipSegment(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var s = new string([.. name.Select(c => c is '\\' or '/' || c <= '\u001f' || (c >= '\u007f' && c <= '\u009f') ? '_' : c)]);
        return s is "" or "." or ".." ? "_" : s;
    }

    private static string SanitizePart(string input, int maxLen)
    {
        var cleaned = Whitespace().Replace(Underscores().Replace(Unsafe().Replace(input, "_"), "_"), " ").Trim().Trim('.', '_', '-');
        return cleaned.Length <= maxLen ? cleaned : cleaned[..maxLen];
    }

    private static string Transform(string input, string spaceReplacement, bool lowercase, bool stripDiacritics, bool collapse)
    {
        var s = input;
        if (stripDiacritics)
        {
            s = Marks().Replace(s.Normalize(NormalizationForm.FormD), string.Empty).Normalize(NormalizationForm.FormC);
        }

        if (string.Equals(spaceReplacement, "underscore", StringComparison.Ordinal))
        {
            s = Spaces().Replace(s, "_");
        }
        else if (string.Equals(spaceReplacement, "dash", StringComparison.Ordinal))
        {
            s = Spaces().Replace(s, "-");
        }

        if (collapse)
        {
            s = Spaces().Replace(Dashes().Replace(Underscores().Replace(s, "_"), "-"), " ");
        }

        if (lowercase)
        {
            s = s.ToLowerInvariant();
        }

        return Edges().Replace(s, string.Empty);
    }

    [GeneratedRegex(@"[^\p{L}\p{N} _\-().,!@#&+=\[\]{}']", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Unsafe();

    [GeneratedRegex("_+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Underscores();

    [GeneratedRegex("-+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Dashes();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex(" +", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\p{M}+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Marks();

    [GeneratedRegex(@"^[._\- ]+|[._\- ]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Edges();
}
