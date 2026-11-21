// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:162-206 (EXT, KIND_EXTENSIONS, matchesKind, KINDS, readDate, modifiedAt) and d.types.ts:54-68 (FileFindQuery.kind).
using System.Globalization;
using Barbican.Core.Files;

namespace Barbican.Core.Plugins.Api.Files;

internal static class PluginFileKinds
{
    internal static readonly IReadOnlyList<string> Kinds = ["document", "image", "pdf", "spreadsheet", "presentation", "text", "folder"];

    private static readonly string[] DocumentKinds = ["pdf", "spreadsheet", "presentation", "text", "word"];

    private static readonly Dictionary<string, string[]> Extensions = new(StringComparer.Ordinal)
    {
        ["pdf"] = ["pdf"],
        ["spreadsheet"] = ["xlsx", "xlsm", "xls", "ods", "csv", "tsv", "numbers"],
        ["presentation"] = ["pptx", "pptm", "ppt", "odp", "key"],
        ["text"] = ["txt", "md", "markdown", "log", "rtf"],
        ["word"] = ["docx", "docm", "doc", "odt", "pages"],
        ["image"] = ["jpg", "jpeg", "png", "gif", "webp", "bmp", "heic", "heif", "avif", "tif", "tiff", "svg"],
    };

    internal static bool Matches(FileEntry node, string kind)
    {
        if (string.Equals(kind, "folder", StringComparison.Ordinal)) return node.IsDirectory;
        if (node.IsDirectory) return false;
        var ext = Extension(node.Name);
        var type = (node.MediaType ?? "").ToLowerInvariant();
        bool Is(string k) => Array.IndexOf(Extensions[k], ext) >= 0;
        bool TypeHas(params string[] words) => words.Any(w => type.Contains(w, StringComparison.Ordinal));
        return kind switch
        {
            "image" => type.StartsWith("image/", StringComparison.Ordinal) || Is("image"),
            "pdf" => string.Equals(type, "application/pdf", StringComparison.Ordinal) || Is("pdf"),
            "spreadsheet" => Is("spreadsheet") || TypeHas("spreadsheet", "excel", "text/csv"),
            "presentation" => Is("presentation") || TypeHas("presentation", "powerpoint"),
            "text" => Is("text") || type.StartsWith("text/", StringComparison.Ordinal),
            "document" => DocumentKinds.Any(Is) || TypeHas("pdf", "wordprocessing", "msword", "opendocument", "spreadsheet", "presentation") || type.StartsWith("text/", StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>The lower-case extension after the last dot, "" when there is none (a letter-and-digit extension only).</summary>
    internal static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return "";
        var ext = name[(dot + 1)..];
        return ext.All(char.IsAsciiLetterOrDigit) ? ext.ToLowerInvariant() : "";
    }

    /// <summary>d.ts:196-200 readDate (Date.parse): a date or a date and time; a date alone is midnight UTC.</summary>
    internal static bool TryDate(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out value);

    /// <summary>d.ts:202 modifiedAt: the modified time (the store has no separate created time).</summary>
    internal static DateTimeOffset ModifiedAt(FileEntry node) => node.Modified ?? DateTimeOffset.UnixEpoch;
}
