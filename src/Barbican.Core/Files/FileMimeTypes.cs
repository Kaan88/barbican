// Mirrors: Rust compose.rs mime_for (the media type of an uploaded file by its extension); Bulwark lib/mime.ts. Owner files-data.

namespace Barbican.Core.Files;

/// <summary>The media type of a file name, for an upload.</summary>
public static class FileMimeTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".csv"] = "text/csv", [".html"] = "text/html", [".htm"] = "text/html", [".css"] = "text/css", [".js"] = "text/javascript",
        [".json"] = "application/json", [".xml"] = "application/xml", [".pdf"] = "application/pdf", [".zip"] = "application/zip", [".gz"] = "application/gzip", [".tar"] = "application/x-tar",
        [".7z"] = "application/x-7z-compressed", [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation", [".odt"] = "application/vnd.oasis.opendocument.text",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mov"] = "video/quicktime", [".eml"] = "message/rfc822", [".ics"] = "text/calendar", [".vcf"] = "text/vcard",
    };

    /// <summary>The media type for the extension of <paramref name="name"/>; "application/octet-stream" when unknown.</summary>
    public static string For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByExtension.TryGetValue(Path.GetExtension(name), out var t) ? t : "application/octet-stream";
    }
}
