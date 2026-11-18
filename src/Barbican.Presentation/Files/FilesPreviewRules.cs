// Mirrors: Bulwark lib/file-preview.ts (previewKind), components/files/file-preview-modal.tsx, image-preview-modal.tsx, eml-preview.tsx. G-013. Contract from the M5 plan section 4.11; owner files-ui.

using Barbican.Core.Common;

namespace Barbican.Presentation.Files;

/// <summary>What the in-app preview can show.</summary>
public static class FilesPreviewRules
{
    /// <summary>The preview kind from the media type, falling back to the extension when the type is empty or application/octet-stream (.png Image, .md and .txt Text, .pdf Pdf, .eml Eml); None for anything else.</summary>
    public static FilesPreviewKind KindOf(string? mediaType, string name) => throw new PortPendingException("FilesPreviewRules.KindOf");
}
