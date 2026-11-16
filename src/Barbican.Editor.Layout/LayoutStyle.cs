// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using SkiaSharp;

namespace Barbican.Editor.Layout;

/// <summary>Fonts, sizes and colours the layout uses. Colours come from the theme tokens in the App.</summary>
public sealed record LayoutStyle
{
    /// <summary>Body font family; Segoe UI on Windows (Rust BODY_FONT), the platform UI font on Linux.</summary>
    public string BodyFont { get; init; } = "Segoe UI";

    public string MonoFont { get; init; } = "Consolas";

    /// <summary>Body size in px. Rust's composer used 14; the S3 gate compares at 15.</summary>
    public float BaseSize { get; init; } = 15f;

    /// <summary>Line height as a multiple of the font size (Rust: 1.5 body, 1.3 headings, 1.45 code).</summary>
    public float LineHeight { get; init; } = 1.5f;

    public SKColor Text { get; init; } = new(0xFF1B1F24);

    public SKColor Link { get; init; } = new(0xFF2F6FDB);

    public SKColor Selection { get; init; } = new(0x663D8BFD);

    /// <summary>Glyph edging: Antialias (grayscale) matches WinUI TextBlock in a non-opaque surface.</summary>
    public SKFontEdging Edging { get; init; } = SKFontEdging.Antialias;

    public SKFontHinting Hinting { get; init; } = SKFontHinting.Normal;

    public bool SubpixelPositioning { get; init; } = true;
}
