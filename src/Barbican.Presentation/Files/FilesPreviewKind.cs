// Mirrors: Bulwark lib/file-preview.ts (the kinds the preview modal can show). G-013. Contract from the M5 plan section 4.11; owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>How the in-app preview shows a file.</summary>
public enum FilesPreviewKind
{
    None,
    Image,
    Text,
    Pdf,
    Eml,
    Audio,
    Video,
}
