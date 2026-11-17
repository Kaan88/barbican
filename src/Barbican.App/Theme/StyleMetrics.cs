#pragma warning disable CA1822 // x:Bind binds instance members; these are per-style values read from the views
// Mirrors: spec/layout/shell.md (pane widths, rail, sidebar, common controls), spec/layout/mail-list.md, spec/layout/reader.md (the Bento and Nordic
// columns); Rust ui/theme.slint Theme (pad, gap, rail-w, r-*); design/README.md. UI-free numbers only (the XAML-typed values are StyleState).

namespace Barbican.App.Theme;

/// <summary>Every per-style number the views need, in effective pixels. One instance per style; nothing here knows WinUI.</summary>
/// <param name="Nordic">Nordic (one surface, pills and circles) instead of Bento (tiles).</param>
public sealed record StyleMetrics(bool Nordic)
{
    /// <summary>The Bento metrics.</summary>
    public static StyleMetrics Bento { get; } = new(false);

    /// <summary>The Nordic metrics.</summary>
    public static StyleMetrics NordicStyle { get; } = new(true);

    /// <summary>The metrics of a style.</summary>
    public static StyleMetrics For(bool nordic) => nordic ? NordicStyle : Bento;

    /// <summary>Window padding (Theme.pad).</summary>
    public double Pad => Nordic ? 0 : 14;

    /// <summary>Gap between tiles (Theme.gap).</summary>
    public double Gap => Nordic ? 0 : 12;

    public double RailWidth => Nordic ? 76 : 64;

    /// <summary>The rail button box.</summary>
    public double RailButton => Nordic ? 46 : 42;

    public double RailButtonRadius => Nordic ? 23 : 14;

    public double RailPadY => Nordic ? 26 : 16;

    public double RailSpacing => Nordic ? 12 : 8;

    public double MarkSize => Nordic ? 34 : 32;

    public double SidebarWidth => Nordic ? 228 : 204;

    /// <summary>The message list's width; narrow below 1150 (Bento) or 1180 (Nordic) px of window.</summary>
    public double ListWidth(double windowWidth) => windowWidth < (Nordic ? 1180 : 1150) ? (Nordic ? 330 : 320) : (Nordic ? 390 : 368);

    public double TileRadius => 24;

    public double PaneRadius => Nordic ? 28 : 26;

    public double SummaryRadius => 22;

    public double ButtonRadius => Nordic ? 19 : 12;

    public double FieldRadius => Nordic ? 20 : 14;

    public double RowRadius => Nordic ? 0 : 18;

    public double NavRadius => Nordic ? 18 : 14;

    public double PopupRadius => Nordic ? 16 : 14;

    public double ComposeHeight => Nordic ? 50 : 52;

    public double ComposeRadius => Nordic ? 25 : 20;

    public double FolderRowHeight => 36;

    public double FolderIndent => 14;

    public double MessageRowHeight => Nordic ? 88 : 82;

    public double GroupHeaderHeight => 30;

    public double AvatarSize => 38;

    /// <summary>Squircle (Bento) or circle (Nordic): Theme.round(size).</summary>
    public double Round(double size) => Nordic ? size / 2 : size * 0.37;

    public double ReaderPadTop => Nordic ? 30 : 22;

    public double ReaderPadBottom => Nordic ? 24 : 18;

    public double ReaderPadSide => Nordic ? 44 : 26;

    public double ListPadLeft => Nordic ? 28 : 20;

    public double ListPadRight => Nordic ? 24 : 16;

    public double ListPadTop => Nordic ? 28 : 20;

    public double ReaderSubjectSize => Nordic ? 34 : 27;

    public double ActionButtonHeight => Nordic ? 38 : 34;

    public double ReaderMinWidth => 240;

    /// <summary>A hairline between list and reader (Nordic) instead of a gap between tiles.</summary>
    public bool Hairlines => Nordic;
}
