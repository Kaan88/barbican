#pragma warning disable CA1822 // x:Bind binds instance members; these are per-style values read from the views
// Mirrors: the WinUI prototype the user approved (prototype/winui/MainWindow.xaml, MainWindow.xaml.cs ApplyStyle/BuildRail, App.xaml item styles);
// where it and the mockups or spec/layout/*.md disagree, the prototype wins (docs/milestones/M1-look-gap.md lists each choice). Rust ui/theme.slint
// (pad, gap, r-*) for the rest; design/README.md. UI-free numbers only (the XAML-typed values are StyleState).

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

    /// <summary>The title-bar band the content is extended into (prototype: 38 px of ground with the window title and the caption buttons).</summary>
    public double TitleBarHeight => 38;

    /// <summary>Window padding left of the rail (Bento: 14; Nordic: the rail sits on the window edge).</summary>
    public double PadLeft => Nordic ? 0 : 14;

    /// <summary>Window padding right of and below the content (both styles 14: the Nordic surface keeps 14 px from the window edge).</summary>
    public double Pad => 14;

    /// <summary>Gap between tiles (Theme.gap): Bento 12, Nordic none (rail, folders and the surface touch).</summary>
    public double Gap => Nordic ? 0 : 12;

    /// <summary>The rail column (prototype RailTile: 66 with its border in Bento, 76 in Nordic).</summary>
    public double RailWidth => Nordic ? 76 : 66;

    /// <summary>The rail button box.</summary>
    public double RailButton => Nordic ? 46 : 44;

    public double RailButtonRadius => Nordic ? 23 : 14;

    public double RailPadTop => 14;

    public double RailPadBottom => Nordic ? 22 : 14;

    public double RailSpacing => Nordic ? 12 : 8;

    /// <summary>The mark: the "B" squircle (Bento, 40) or the three bars (Nordic, 34).</summary>
    public double MarkSize => Nordic ? 34 : 40;

    public double MarkGap => Nordic ? 14 : 10;

    public double SidebarWidth => Nordic ? 228 : 236;

    /// <summary>The message list's width; narrow below 1150 (Bento) or 1180 (Nordic) px of window.</summary>
    public double ListWidth(double windowWidth) => windowWidth < (Nordic ? 1180 : 1150) ? (Nordic ? 330 : 320) : (Nordic ? 390 : 370);

    /// <summary>The sender panel beside the reader (prototype ContextTile: 272 Bento, 250 Nordic).</summary>
    public double ContextWidth => Nordic ? 250 : 272;

    /// <summary>The narrowest reader the sender panel leaves; below it the panel hides (the prototype squeezed the reader instead).</summary>
    public double ContextMinReader => 420;

    public double TileRadius => Nordic ? 24 : 26;

    public double PaneRadius => Nordic ? 28 : 26;

    public double SummaryRadius => 22;

    public double ButtonRadius => Nordic ? 19 : 12;

    public double FieldRadius => Nordic ? 20 : 14;

    public double RowRadius => Nordic ? 0 : 18;

    public double NavRadius => Nordic ? 22 : 14;

    public double PopupRadius => Nordic ? 16 : 14;

    public double ComposeHeight => Nordic ? 50 : 46;

    public double ComposeRadius => Nordic ? 25 : 16;

    public double FolderRowHeight => Nordic ? 44 : 38;

    public double FolderIndent => 14;

    public double MessageRowHeight => Nordic ? 90 : 76;

    public double GroupHeaderHeight => 30;

    public double AvatarSize => 38;

    /// <summary>Squircle (Bento) or circle (Nordic) of an avatar or icon button (prototype: 38 → 13 or 19).</summary>
    public double Round(double size) => Nordic ? size / 2 : Math.Round(size * 0.34);

    public double ReaderPadTop => Nordic ? 30 : 22;

    public double ReaderPadBottom => Nordic ? 24 : 10;

    public double ReaderPadSide => Nordic ? 44 : 26;

    public double ListPadLeft => Nordic ? 28 : 20;

    public double ListPadRight => Nordic ? 28 : 20;

    public double ListPadTop => Nordic ? 28 : 18;

    public double ListPadBottom => Nordic ? 16 : 10;

    public double ListTitleSize => Nordic ? 28 : 24;

    public double ReaderSubjectSize => Nordic ? 34 : 22;

    public double ActionButtonHeight => Nordic ? 38 : 36;

    public double ReaderMinWidth => 240;

    /// <summary>A hairline between list and reader (Nordic) instead of a gap between tiles.</summary>
    public bool Hairlines => Nordic;
}
