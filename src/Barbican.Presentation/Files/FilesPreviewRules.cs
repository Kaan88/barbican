// Mirrors: Bulwark lib/file-preview.ts (previewKind), components/files/file-preview-modal.tsx, image-preview-modal.tsx, eml-preview.tsx. G-013. Contract from the M5 plan section 4.11; owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>What the in-app preview can show.</summary>
public static class FilesPreviewRules
{
    /// <summary>The preview kind from the media type, falling back to the extension when the type is empty or application/octet-stream (.png Image, .md and .txt Text, .pdf Pdf, .eml Eml); None for anything else.</summary>
    public static FilesPreviewKind KindOf(string? mediaType, string name) 
    {
        var type = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        var semi = type.IndexOf(';', StringComparison.Ordinal);
        if (semi >= 0)
        {
            type = type[..semi].Trim();
        }

        if (type.Length > 0 && !string.Equals(type, "application/octet-stream", StringComparison.Ordinal))
        {
            if (type.StartsWith("image/", StringComparison.Ordinal))
            {
                return FilesPreviewKind.Image;
            }

            if (string.Equals(type, "application/pdf", StringComparison.Ordinal))
            {
                return FilesPreviewKind.Pdf;
            }

            if (string.Equals(type, "message/rfc822", StringComparison.Ordinal))
            {
                return FilesPreviewKind.Eml;
            }

            if (type.StartsWith("audio/", StringComparison.Ordinal))
            {
                return FilesPreviewKind.Audio;
            }

            if (type.StartsWith("video/", StringComparison.Ordinal))
            {
                return FilesPreviewKind.Video;
            }

            if (type.StartsWith("text/", StringComparison.Ordinal) || type is "application/json" or "application/xml")
            {
                return FilesPreviewKind.Text;
            }

            return FilesPreviewKind.None;
        }

        var dot = name.LastIndexOf('.');
        var ext = dot >= 0 ? name[(dot + 1)..].ToLowerInvariant() : string.Empty;
        return ext switch
        {
            "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp" or "svg" or "ico" => FilesPreviewKind.Image,
            "txt" or "md" or "log" or "json" or "csv" or "xml" or "yaml" or "yml" or "ini" or "html" or "css" or "js" => FilesPreviewKind.Text,
            "pdf" => FilesPreviewKind.Pdf,
            "eml" => FilesPreviewKind.Eml,
            "mp3" or "wav" or "ogg" or "m4a" or "flac" => FilesPreviewKind.Audio,
            "mp4" or "webm" or "mov" or "mkv" => FilesPreviewKind.Video,
            _ => FilesPreviewKind.None,
        };
    }
}
