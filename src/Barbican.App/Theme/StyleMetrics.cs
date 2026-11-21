#pragma warning disable CA1822 // x:Bind binds instance members; these are per-style values read from the views
// Mirrors: the WinUI prototype the user approved (prototype/winui/MainWindow.xaml, MainWindow.xaml.cs ApplyStyle/BuildRail, App.xaml item styles);
// where it and the mockups or spec/layout/*.md disagree, the prototype wins (docs/milestones/M1-look-gap.md lists each choice). Rust ui/theme.slint
// (pad, gap, r-*) for the rest; design/README.md. UI-free numbers only (the XAML-typed values are StyleState).

using Barbican.Presentation.Shell;
using Barbican.Presentation.Theme;

namespace Barbican.App.Theme;

/// <summary>Every per-style number the views need, in effective pixels. One instance per style; nothing here knows WinUI.</summary>
/// <param name="Nordic">Nordic (one surface, pills and circles) instead of Bento (tiles).</param>
/// <param name="Bulwark">Use Bulwark shell dimensions instead of the legacy style.</param>
public sealed record StyleMetrics(bool Nordic, bool Bulwark = false)
{
    /// <summary>The Bento metrics.</summary>
    public static StyleMetrics Bento { get; } = new(false);

    /// <summary>The Nordic metrics.</summary>
    public static StyleMetrics NordicStyle { get; } = new(true);

    public static StyleMetrics BulwarkStyle { get; } = new(false, true);

    /// <summary>The metrics of a style.</summary>
    public static StyleMetrics For(bool nordic) => nordic ? NordicStyle : Bento;

    public static StyleMetrics For(ShellStyle style) => style switch
    {
        ShellStyle.Bulwark => BulwarkStyle,
        ShellStyle.Nordic => NordicStyle,
        _ => Bento,
    };

    /// <summary>The title-bar band the content is extended into (prototype: 38 px of ground with the window title and the caption buttons).</summary>
    public double TitleBarHeight => 38;

    /// <summary>Mail shell panes touch the window and one another.</summary>
    public double PadLeft => Bulwark || Nordic ? 0 : 14;

    /// <summary>Window padding right of and below the content.</summary>
    public double Pad => Bulwark ? 0 : 14;

    /// <summary>Gap between shell panes.</summary>
    public double Gap => Bulwark || Nordic ? 0 : 12;

    /// <summary>The rail column. The reference shell is approximately 56 px wide.</summary>
    public double RailWidth => Bulwark ? 56 : Nordic ? 76 : 66;

    /// <summary>The rail button box.</summary>
    public double RailButton => Bulwark ? 40 : Nordic ? 46 : 44;

    public double RailButtonRadius => Bulwark ? 6 : Nordic ? 23 : 14;

    public double RailPadTop => Bulwark ? 12 : 14;

    public double RailPadBottom => Nordic ? 22 : 14;

    public double RailSpacing => Bulwark ? 4 : Nordic ? 12 : 8;

    /// <summary>The mark: the "B" squircle (Bento, 40) or the three bars (Nordic, 34).</summary>
    public double MarkSize => Nordic ? 34 : 40;

    public double MarkGap => Nordic ? 14 : 10;

    public double SidebarWidth => Bulwark ? 256 : Nordic ? 228 : 236;

    /// <summary>The default message list width (Bulwark stores/ui-store.ts EMAIL_LIST_DEFAULT).</summary>
    public double ListWidth(double windowWidth) => Bulwark ? 384 : windowWidth < (Nordic ? 1180 : 1150) ? (Nordic ? 330 : 320) : (Nordic ? 390 : 370);

    /// <summary>The sender panel beside the reader (prototype ContextTile: 272 Bento, 250 Nordic).</summary>
    public double ContextWidth => Nordic ? 250 : 272;

    /// <summary>The narrowest reader the sender panel leaves; below it the panel hides (the prototype squeezed the reader instead).</summary>
    public double ContextMinReader => 420;

    public double TileRadius => Nordic ? 24 : 26;

    public double PaneRadius => Nordic ? 28 : 26;

    public double SummaryRadius => Bulwark ? Shape("RSummary") : 22;

    /// <summary>Bulwark rounded-md (6) comes from the generated Shape.Bulwark tokens; Bento and Nordic keep their values.</summary>
    public double ButtonRadius => Bulwark ? Shape("RButton") : Nordic ? 19 : 12;

    public double FieldRadius => Bulwark ? Shape("RField") : Nordic ? 20 : 14;

    public double RowRadius => Bulwark ? Shape("RRow") : Nordic ? 0 : 18;

    public double NavRadius => Bulwark ? Shape("RNav") : Nordic ? 22 : 14;

    public double PopupRadius => Bulwark ? Shape("RPopup") : Nordic ? 16 : 14;

    /// <summary>Dialog cards (Bulwark rounded-lg 8), context-menu panels (6), their rows (square) and toasts (6): the generated Shape tokens of the style.</summary>
    public double DialogRadius => Shape("RDialog");

    public double MenuRadius => Shape("RMenu");

    public double MenuItemRadius => Shape("RMenuItem");

    public double ToastRadius => Shape("RToast");

    public double ComposeHeight => Nordic ? 50 : 46;

    public double ComposeRadius => Nordic ? 25 : 16;

    public double FolderRowHeight => Bulwark ? 30 : Nordic ? 44 : 38;

    public double FolderIndent => 14;

    public double MessageRowHeight => Nordic ? 90 : 76;

    public double GroupHeaderHeight => 30;

    public double AvatarSize => 38;

    /// <summary>Squircle (Bento) or circle (Nordic) of an avatar or icon button (prototype: 38 → 13 or 19).</summary>
    public double Round(double size) => Nordic || Bulwark ? size / 2 : Math.Round(size * 0.34);

    public double ReaderPadTop => Bulwark ? 0 : Nordic ? 30 : 22;

    public double ReaderPadBottom => Nordic ? 24 : 10;

    public double ReaderPadSide => Bulwark ? 24 : Nordic ? 44 : 26;

    public double ListPadLeft => Nordic ? 28 : 20;

    public double ListPadRight => Nordic ? 28 : 20;

    public double ListPadTop => Nordic ? 28 : 18;

    public double ListPadBottom => Nordic ? 16 : 10;

    public double ListTitleSize => Nordic ? 28 : 24;

    public double ReaderSubjectSize => Bulwark ? 20 : Nordic ? 34 : 22;

    public double ActionButtonHeight => Nordic ? 38 : 36;

    public double ReaderMinWidth => 240;

    private double Shape(string key) => Tokens.Shape(new ThemeVariant(Dark: true, MoreContrast: false, Nordic: Nordic, Bulwark: Bulwark))[key];

    /// <summary>A hairline between list and reader (Nordic) instead of a gap between tiles.</summary>
    public bool Hairlines => Nordic;
}
