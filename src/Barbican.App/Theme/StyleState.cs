#pragma warning disable CA1822 // x:Bind binds instance members; these are per-style values read from the views
// Mirrors: PORT-PLAN.md §3.7 and §4.7 (shape tokens per style); the WinUI prototype the user approved (prototype/winui, values listed in
// docs/milestones/M1-look-gap.md); spec/layout/*.md (the Bento and Nordic columns). The Shape.* x:Double resources cannot be bound to
// Thickness, CornerRadius or GridLength in XAML (WinUI crashes on load: M1 F1 found it), so views bind typed values from here instead.

using Barbican.Presentation.Shell;
using Barbican.Core.Settings.Accessibility;
using Barbican.Presentation.Compose;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Theme;

/// <summary>
/// The look of the shell as typed XAML values: corner radii, paddings, widths and which of two shapes a region takes. One singleton; it raises
/// "all properties changed" once when the style (or the window width) changes, so a style flip is one binding pass and nothing is rebuilt.
/// </summary>
public sealed class StyleState : ObservableObject
{
    private StyleMetrics _m = StyleMetrics.Bento;
    private double _windowWidth = 1400;
    private A11y _accessibility = new();
    private bool _showToolbarLabels = true;
    private string _composePlacement = "floating";
    private bool _sidebarAccountSwitcher = true;

    public StyleState()
    {
        Current = this;
        TileBrush = AppResources.Brush("Tile");
        Transparent = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    /// <summary>The singleton, for x:Bind paths inside DataTemplates (a template cannot reach its page).</summary>
    public static StyleState Current { get; private set; } = null!;

    public StyleMetrics Metrics => _m;

    public bool IsNordic => _m.Nordic;

    public bool IsBento => !_m.Nordic;

    public bool IsBulwark => _m.Bulwark;

    public ShellStyle Style => IsBulwark ? ShellStyle.Bulwark : _m.Nordic ? ShellStyle.Nordic : ShellStyle.Bento;

    /// <summary>The window width the layout was computed for.</summary>
    public double WindowWidth => _windowWidth;

    public ShellLayout Layout => ShellLayout.Compute(_windowWidth, Style);

    /// <summary>Applies a style; true when it changed.</summary>
    public bool SetStyle(ShellStyle style)
    {
        if (style == Style)
        {
            return false;
        }

        _m = StyleMetrics.For(style);
        OnPropertyChanged(string.Empty);
        return true;
    }

    /// <summary>The window got another width: the list narrows, the sender panel and the summary tiles hide at the breakpoints.</summary>
    public void SetWindowWidth(double width)
    {
        if (Math.Abs(width - _windowWidth) < 0.5)
        {
            return;
        }

        var before = Layout;
        _windowWidth = width;
        var after = Layout;
        if (before != after)
        {
            OnPropertyChanged(nameof(Layout));
            OnPropertyChanged(nameof(ListWidth));
            OnPropertyChanged(nameof(SummaryTilesVisibility));
            OnPropertyChanged(nameof(ContextVisibility));
            OnPropertyChanged(nameof(FloatingComposeVisibility));
            OnPropertyChanged(nameof(SidebarComposeVisibility));
            OnPropertyChanged(nameof(ListViewportMargin));
        }
    }

    /// <summary>Updates typed dimensions when the shared accessibility settings change.</summary>
    public void SetAccessibility(A11y accessibility, bool showToolbarLabels, string? composePlacement, bool sidebarAccountSwitcher = true)
    {
        ArgumentNullException.ThrowIfNull(accessibility);
        var placement = ComposePlacementRules.Normalize(composePlacement);
        if (_accessibility == accessibility && _showToolbarLabels == showToolbarLabels && _sidebarAccountSwitcher == sidebarAccountSwitcher && string.Equals(_composePlacement, placement, StringComparison.Ordinal)) return;
        _sidebarAccountSwitcher = sidebarAccountSwitcher;
        _accessibility = accessibility;
        _showToolbarLabels = showToolbarLabels;
        _composePlacement = placement;
        OnPropertyChanged(string.Empty);
    }

    public string ComposePlacement => _composePlacement;
    public Visibility FloatingComposeVisibility => string.Equals(_composePlacement, "floating", StringComparison.Ordinal) || (string.Equals(_composePlacement, "sidebar", StringComparison.Ordinal) && !Layout.SidebarShown) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SidebarComposeVisibility => string.Equals(_composePlacement, "sidebar", StringComparison.Ordinal) && Layout.SidebarShown ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RailComposeVisibility => string.Equals(_composePlacement, "rail", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public bool ShowToolbarLabels => _showToolbarLabels;
    public Visibility ToolbarLabelVisibility => _showToolbarLabels ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ToolbarIconVisibility => IsBento || !_showToolbarLabels ? Visibility.Visible : Visibility.Collapsed;
    public double TextScale => _accessibility.FontScaleFactor;
    public double BodyTextSize => 13 * TextScale;
    public double MetaTextSize => 12 * TextScale;
    public double MinimumTarget => _accessibility.LargeTargets ? 44 : 0;
    public double QuickTarget => Math.Max(36 * TextScale, MinimumTarget);
    public Thickness FocusThickness => new(Math.Max(1, _accessibility.FocusRing));
    public FontFamily AppBoldFont => string.Equals(_accessibility.AppFont, "opendyslexic", StringComparison.Ordinal)
        ? new FontFamily("ms-appx:///Assets/Fonts/OpenDyslexic-Bold.otf#OpenDyslexic")
        : AppFont;
    public FontFamily AppFont => new(_accessibility.AppFont switch
    {
        "atkinson" => "ms-appx:///Assets/Fonts/AtkinsonHyperlegibleNext.ttf#Atkinson Hyperlegible Next",
        "lexend" => "ms-appx:///Assets/Fonts/Lexend.ttf#Lexend",
        "opendyslexic" => "ms-appx:///Assets/Fonts/OpenDyslexic-Regular.otf#OpenDyslexic",
        _ => "Segoe UI",
    });

    // brushes (the live palette instances; a region is a tile in Bento and bare in Nordic)
    public Brush TileBrush { get; }

    public Brush Transparent { get; }

    public Brush PaneBrush => IsBento ? TileBrush : Transparent;

    // Bulwark uses sidebar and background tokens, not Bento's raised tile.
    public Brush RailBrush => IsBulwark ? AppResources.Brush("Rail") : PaneBrush;

    public Brush ListBrush => IsBulwark ? AppResources.Brush("Bg") : PaneBrush;

    // window and regions
    public double TitleBarHeight => _m.TitleBarHeight;

    /// <summary>Below the title band: Bento keeps 14 px on three sides, Nordic only right and bottom (rail and folders sit on the window edge).</summary>
    public Thickness WindowPadding => new(_m.PadLeft, 0, _m.Pad, _m.Pad);

    public double Gap => _m.Gap;

    public double RailWidth => _m.RailWidth;

    public double SidebarWidth => _m.SidebarWidth;

    public double ListWidth => Layout.ListWidth;

    public double ContextWidth => _m.ContextWidth;

    public Visibility SummaryTilesVisibility => Layout.SummaryTilesShown ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContextVisibility => Layout.ContextShown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nordic: list, reader and sender panel are one rounded tile split by hairlines (NordMail; prototype Surface).</summary>
    public Brush SurfaceBrush => IsNordic ? TileBrush : Transparent;

    public Thickness SurfaceBorder => IsNordic ? new Thickness(1) : new Thickness(0);

    public CornerRadius SurfaceRadius => IsNordic ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0);

    public Thickness PaneBorder => IsBento && !IsBulwark ? new Thickness(1) : new Thickness(0);

    public CornerRadius PaneRadius => IsBento && !IsBulwark ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0);

    /// <summary>Nordic: the list's corners follow the surface's inner curve on the left, the sender panel's on the right.</summary>
    public CornerRadius ListRadius => IsBulwark ? new CornerRadius(0) : IsBento ? new CornerRadius(_m.PaneRadius) : new CornerRadius(_m.PaneRadius - 1, 0, 0, _m.PaneRadius - 1);

    public Thickness ListBorder => IsBulwark ? new Thickness(0, 0, 1, 0) : PaneBorder;

    public CornerRadius ContextRadius => IsBento ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0, _m.PaneRadius - 1, _m.PaneRadius - 1, 0);

    /// <summary>Nordic splits list from reader with a hairline.</summary>
    public Thickness ListHairline => IsNordic ? new Thickness(0, 0, 1, 0) : new Thickness(0);

    /// <summary>Nordic splits reader from the sender panel with a hairline.</summary>
    public Thickness ContextBorder => IsNordic ? new Thickness(1, 0, 0, 0) : new Thickness(1);

    public Thickness ReaderPadding => new(_m.ReaderPadSide, _m.ReaderPadTop, _m.ReaderPadSide, _m.ReaderPadBottom);

    public Thickness ListHeaderPadding => IsBulwark ? new Thickness(12) : new Thickness(_m.ListPadLeft, _m.ListPadTop, _m.ListPadRight, _m.ListPadBottom);

    public double ListTitleSize => _m.ListTitleSize * TextScale;

    public FontWeight ListTitleWeight => IsNordic ? FontWeights.ExtraBold : FontWeights.Bold;

    public int ListTitleSpacing => IsNordic ? -20 : 0;

    public double ReaderSubjectSize => _m.ReaderSubjectSize * TextScale;

    public FontWeight ReaderSubjectWeight => IsNordic ? FontWeights.Bold : FontWeights.SemiBold;

    public int ReaderSubjectSpacing => IsNordic ? -25 : 0;

    /// <summary>0 lets the font decide (Bento); Nordic sets the prototype's 41 px lines for the 34 px subject.</summary>
    public double ReaderSubjectLineHeight => IsNordic ? 41 * TextScale : 0;

    public Thickness ReaderSubjectMargin => IsNordic ? new Thickness(0, 12, 0, 0) : new Thickness(0);

    /// <summary>The reader's rows: Bento subject, sender, actions (prototype ReaderHead); Nordic actions, subject, sender (NordHead).</summary>
    public int ReaderActionsRow => IsNordic ? 0 : 2;

    public int ReaderSubjectRow => IsNordic ? 1 : 0;

    public int ReaderSenderRow => IsNordic ? 2 : 1;

    public double ReaderSenderSize => (IsNordic ? 13.5 : 12.5) * TextScale;

    /// <summary>The space between the reader's parts (prototype ReaderTile: 14 Bento, 18 Nordic).</summary>
    public double ReaderSpacing => IsBulwark ? 0 : IsNordic ? 18 : 14;

    // email-viewer.tsx: top actions and message header are full-width ruled bands.
    public Thickness ReaderBandMargin => IsBulwark ? new(-_m.ReaderPadSide, 0, -_m.ReaderPadSide, 0) : default;
    public Thickness ReaderBandPadding => IsBulwark ? new(_m.ReaderPadSide, 12, _m.ReaderPadSide, 12) : default;
    public Thickness ReaderBandRule => IsBulwark ? new(0, 0, 0, 1) : default;
    public Style ReaderActionStyle => (Style)Application.Current.Resources[IsBulwark ? "Button.Icon" : "Button.Action"];
    public Thickness ReaderActionPadding => IsBulwark ? new(10, 0, 10, 0) : ActionPadding;
    public CornerRadius ReaderActionRadius => IsBulwark ? new(6) : ActionRadius;
    public double ReaderActionHeight => IsBulwark ? Math.Max(32 * TextScale, MinimumTarget) : ActionButtonHeight;

    /// <summary>Reply all, Delete and More are icon-only pills in Bento so the six M1 actions fit the prototype's three-pill width.</summary>
    public Visibility SecondaryLabelVisibility => ToolbarLabelVisibility;

    // rail
    public Thickness RailPadding => new(0, _m.RailPadTop, 0, _m.RailPadBottom);

    public double RailSpacing => _m.RailSpacing;

    public double RailButton => Math.Max(_m.RailButton, MinimumTarget);

    public CornerRadius RailButtonRadius => new(_m.RailButtonRadius);

    public double MarkSize => _m.MarkSize;

    public Thickness MarkGap => new(0, IsBulwark ? 0 : _m.MarkGap, 0, 0);

    public Visibility LegacyMarkVisibility => IsBulwark ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BulwarkOnly => IsBulwark ? Visibility.Visible : Visibility.Collapsed;

    public CornerRadius RailRadius => IsBento && !IsBulwark ? new CornerRadius(24) : new CornerRadius(0);

    public Thickness RailBorder => IsBulwark ? new Thickness(0, 0, 1, 0) : IsBento ? new Thickness(1) : new Thickness(0);

    public CornerRadius AccountRadius => new(_m.Round(AccountSize));

    public double AccountSize => IsBento ? 38 : 40;

    // sidebar: Bento is one tile (compose, folders, tags, storage); Nordic sits on the ground
    public Thickness SidebarPadding => IsBulwark ? new Thickness(0, 0, 0, 12) : IsNordic ? new Thickness(14, 18, 14, 22) : new Thickness(12, 14, 12, 14);
    public Visibility SidebarSwitcherVisibility => IsBulwark && _sidebarAccountSwitcher ? Visibility.Visible : Visibility.Collapsed;

    public Brush SidebarBrush => IsBulwark ? AppResources.Brush("Side") : PaneBrush;

    public Thickness SidebarBorder => IsBulwark ? new Thickness(0, 0, 1, 0) : PaneBorder;

    public CornerRadius SidebarRadius => IsBento && !IsBulwark ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0);

    public double SidebarSpacing => 0;

    public double ComposeHeight => Math.Max(_m.ComposeHeight * TextScale, MinimumTarget);

    public CornerRadius ComposeRadius => new(_m.ComposeRadius);

    public string ComposeLabel => "New message";

    public double ComposeFontSize => (IsNordic ? 14.5 : 14) * TextScale;

    public Thickness ComposeMargin => IsNordic ? new Thickness(0, 0, 0, 22) : new Thickness(0, 0, 0, 12);

    /// <summary>Sections inside the sidebar have no frame of their own in either style.</summary>
    public Thickness SectionPadding => new(0);

    public Brush SectionBrush => Transparent;

    public Thickness SectionBorder => new(0);

    public CornerRadius SectionRadius => new(0);

    public CornerRadius StorageRadius => new(0);

    public Thickness StoragePadding => IsBento ? new Thickness(11, 12, 11, 0) : new Thickness(16, 8, 16, 0);

    /// <summary>Section headings: Bento "LABELS" (11, bold, tracked, faint), Nordic "Labels" (12, bold, dim).</summary>
    public Thickness SectionHeaderPadding => IsBento ? new Thickness(11, 18, 0, 6) : new Thickness(16, 24, 0, 8);

    public Thickness FirstSectionHeaderPadding => IsBento ? new Thickness(11, 0, 0, 6) : new Thickness(16, 0, 0, 8);

    public double SectionHeaderSize => (IsBento ? 11 : 12) * TextScale;

    public int SectionHeaderSpacing => IsBento ? 80 : 0;

    public Brush SectionHeaderBrush => AppResources.Brush(IsBento ? "Faint" : "Dim");

    public bool SidebarUppercaseHeaders => IsBento && !IsBulwark;

    public double SidebarHeaderSize => IsBulwark ? 14 * TextScale : SectionHeaderSize;

    public FontWeight SidebarHeaderWeight => IsBulwark ? FontWeights.SemiBold : FontWeights.Bold;

    public int SidebarHeaderSpacing => IsBulwark ? 0 : SectionHeaderSpacing;

    public Brush SidebarHeaderBrush => IsBulwark ? AppResources.Brush("Text") : SectionHeaderBrush;

    public Visibility BentoOnly => IsBento ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NordicOnly => IsNordic ? Visibility.Visible : Visibility.Collapsed;

    // folder and tag rows
    public double FolderRowHeight => Math.Max(_m.FolderRowHeight * TextScale, MinimumTarget);

    public Thickness FolderRowPadding => IsBento ? new Thickness(11, 0, 11, 0) : new Thickness(16, 0, 16, 0);

    public double FolderNameSize => (IsNordic ? 14.5 : 13.5) * TextScale;

    public double FolderCountSize => (IsNordic ? 12.5 : 12) * TextScale;

    public FontWeight FolderCountWeight => IsNordic ? FontWeights.Normal : FontWeights.SemiBold;

    /// <summary>Nordic folders are names only (NordMail); Bento has the line icons.</summary>
    public Visibility FolderIconVisibility => BentoOnly;

    public Thickness TagRowPadding => IsBento ? new Thickness(11, 7, 11, 7) : new Thickness(16, 9, 16, 9);

    public double TagNameSize => (IsNordic ? 14 : 13.5) * TextScale;

    public double TagSpacing => IsNordic ? 12 : 10;

    /// <summary>The corner of a tag row: the folder rows' (Bento 14, Nordic round).</summary>
    public CornerRadius TagRowRadius => new(IsNordic ? 22 : 14);

    // message rows
    public Thickness MessageRowPadding => IsBulwark ? new Thickness(12) : IsBento ? new Thickness(12, 10, 12, 10) : new Thickness(28, 14, 28, 14);

    public double MessageRowSpacing => IsBento ? 11 : 8;

    public Visibility ShowAvatar => BentoOnly;

    public CornerRadius AvatarRadius => new(IsBulwark ? _m.AvatarSize / 2 : _m.Round(_m.AvatarSize));

    /// <summary>The sender line (both styles 14), subject (13.5 Bento, 14 Nordic) and preview (12.5, 13) sizes of the prototype's rows.</summary>
    public double SenderSize => 14 * TextScale;

    public double SubjectSize => (IsBulwark || IsNordic ? 14 : 13.5) * TextScale;

    public double PreviewSize => (IsBulwark ? 14 : IsNordic ? 13 : 12.5) * TextScale;

    public double ListToolbarButtonSize => Math.Max(IsBulwark ? 32 * TextScale : 36, MinimumTarget);

    public double ListSearchHeight => Math.Max(IsBulwark ? 32 * TextScale : 36, MinimumTarget);

    public Brush SubjectBrush => AppResources.Brush(IsNordic ? "Text" : "Body");

    public Brush PreviewBrush => AppResources.Brush(IsNordic ? "Dim" : "Faint");

    public Thickness SubjectMargin => IsNordic ? new Thickness(15, 4, 0, 2) : new Thickness(0);

    public Thickness PreviewMargin => IsNordic ? new Thickness(15, 0, 0, 0) : new Thickness(0);

    /// <summary>Kept for views that indent both lines alike.</summary>
    public Thickness MessageTextMargin => PreviewMargin;

    public Thickness GroupHeaderPadding => new(_m.ListPadLeft, 8, 12, 2);

    public Thickness ListPadding => IsBento ? new Thickness(0, 0, 0, 8) : new Thickness(0);

    // The list viewport ends above the floating action, keeping every message reachable and unobscured.
    public Thickness ListViewportMargin => FloatingComposeVisibility == Visibility.Visible ? new Thickness(0, 0, 0, QuickTarget + 40) : new Thickness(0);

    // sender panel
    public Thickness ContextPadding => IsBento ? new Thickness(22, 26, 22, 26) : new Thickness(22, 30, 22, 30);

    public double ContextSpacing => IsBento ? 10 : 16;

    public double ContextAvatarSize => IsBento ? 68 : 64;

    public CornerRadius ContextAvatarRadius => IsBento ? new CornerRadius(22) : new CornerRadius(32);

    public double ContextInitialsSize => IsBento ? 24 : 20;

    public double ContextNameSize => IsBento ? 18 : 17;

    public FontWeight ContextNameWeight => IsBento ? FontWeights.Bold : FontWeights.ExtraBold;

    // controls
    public CornerRadius ButtonRadius => new(_m.ButtonRadius);

    /// <summary>The reader's action pills (prototype: 36 high, radius 18 in Bento; 38 / 19 in Nordic).</summary>
    public CornerRadius ActionRadius => new(_m.ActionButtonHeight / 2);

    public Thickness ActionPadding => IsBento ? new Thickness(14, 0, 16, 0) : new Thickness(16, 0, 16, 0);

    public CornerRadius FieldRadius => new(_m.FieldRadius);

    public CornerRadius SummaryRadius => new(_m.SummaryRadius);

    public CornerRadius PopupRadius => new(_m.PopupRadius);

    public double ActionButtonHeight => Math.Max(_m.ActionButtonHeight * TextScale, MinimumTarget);

    public CornerRadius ChipRadius => new(14);

    public CornerRadius CardRadius => IsBento ? new CornerRadius(16) : new CornerRadius(18);

    public Thickness CardBorder => IsBento ? new Thickness(1) : new Thickness(0);

    public Brush CardBrush => AppResources.Brush("Tile2");

    /// <summary>The sender panel's recent cards: tile2 cards in Bento, bare rows in Nordic (prototype RECENT / Recent).</summary>
    public Brush RecentBrush => IsBento ? AppResources.Brush("Tile2") : Transparent;

    public CornerRadius PillRadius => new(11);

    /// <summary>The quick-reply field (prototype QuickReply: radius 26, a hairline, no fill).</summary>
    public CornerRadius ReplyRadius => new(26);

    /// <summary>The two item-container styles of a list: Bento or Nordic variant (Styles/Controls.xaml).</summary>
    public Style MessageItemStyle => (Style)Application.Current.Resources[IsBulwark ? "ListItem.Message.Bulwark" : IsNordic ? "ListItem.Message.Nordic" : "ListItem.Message.Bento"];

    public Style FolderItemStyle => (Style)Application.Current.Resources[IsNordic ? "ListItem.Folder.Nordic" : "ListItem.Folder.Bento"];
}
