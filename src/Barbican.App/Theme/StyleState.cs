#pragma warning disable CA1822 // x:Bind binds instance members; these are per-style values read from the views
// Mirrors: PORT-PLAN.md §3.7 and §4.7 (shape tokens per style); spec/layout/*.md (the Bento and Nordic columns). The Shape.* x:Double resources cannot be bound to
// Thickness, CornerRadius or GridLength in XAML (WinUI crashes on load: M1 F1 found it), so views bind typed values from here instead.

using Barbican.Presentation.Shell;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.App.Theme;

/// <summary>
/// The look of the shell as typed XAML values: corner radii, paddings, widths and which of two shapes a region takes. One singleton; it raises
/// "all properties changed" once when the style (or the window width) changes, so a style flip is one binding pass and nothing is rebuilt.
/// </summary>
public sealed class StyleState : ObservableObject
{
    private StyleMetrics _m = StyleMetrics.Bento;
    private double _windowWidth = 1400;

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

    public ShellStyle Style => _m.Nordic ? ShellStyle.Nordic : ShellStyle.Bento;

    /// <summary>The window width the layout was computed for.</summary>
    public double WindowWidth => _windowWidth;

    public ShellLayout Layout => ShellLayout.Compute(_windowWidth, _m.Nordic);

    /// <summary>Applies a style; true when it changed.</summary>
    public bool SetStyle(bool nordic)
    {
        if (nordic == _m.Nordic)
        {
            return false;
        }

        _m = StyleMetrics.For(nordic);
        OnPropertyChanged(string.Empty);
        return true;
    }

    /// <summary>The window got another width: the list narrows and the summary tiles hide at the breakpoints.</summary>
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
        }
    }

    // brushes (the live palette instances; a region is a tile in Bento and bare in Nordic)
    public Brush TileBrush { get; }

    public Brush Transparent { get; }

    public Brush PaneBrush => IsBento ? TileBrush : Transparent;

    // window and regions
    public Thickness WindowPadding => new(_m.Pad);

    public double Gap => _m.Gap;

    public double RailWidth => _m.RailWidth;

    public double SidebarWidth => _m.SidebarWidth;

    public double ListWidth => Layout.ListWidth;

    public Visibility SummaryTilesVisibility => Layout.SummaryTilesShown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nordic's one surface keeps a 14 px margin from the window on three sides (NordMail.dc.html).</summary>
    public Thickness SurfaceMargin => IsNordic ? new Thickness(0, 14, 14, 14) : new Thickness(0);

    public Brush SurfaceBrush => IsNordic ? TileBrush : Transparent;

    public Thickness SurfaceBorder => IsNordic ? new Thickness(1) : new Thickness(0);

    public CornerRadius SurfaceRadius => IsNordic ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0);

    public Thickness PaneBorder => IsBento ? new Thickness(1) : new Thickness(0);

    public CornerRadius PaneRadius => IsBento ? new CornerRadius(_m.PaneRadius) : new CornerRadius(0);

    /// <summary>Nordic splits list from reader with a hairline.</summary>
    public Thickness ListHairline => IsNordic ? new Thickness(0, 0, 1, 0) : new Thickness(0);

    public Thickness ReaderPadding => new(_m.ReaderPadSide, _m.ReaderPadTop, _m.ReaderPadSide, _m.ReaderPadBottom);

    public Thickness ListHeaderPadding => new(_m.ListPadLeft, _m.ListPadTop, _m.ListPadRight, 10);

    public double ReaderSubjectSize => _m.ReaderSubjectSize;

    // rail
    public Thickness RailPadding => new(0, _m.RailPadY, 0, _m.RailPadY);

    public double RailSpacing => _m.RailSpacing;

    public double RailButton => _m.RailButton;

    public CornerRadius RailButtonRadius => new(_m.RailButtonRadius);

    public double MarkSize => _m.MarkSize;

    public CornerRadius RailRadius => IsBento ? new CornerRadius(_m.TileRadius) : new CornerRadius(0);

    public Thickness RailBorder => IsBento ? new Thickness(1) : new Thickness(0);

    public CornerRadius AccountRadius => new(_m.Round(IsBento ? 38 : 40));

    public double AccountSize => IsBento ? 38 : 40;

    // sidebar
    public Thickness SidebarPadding => IsNordic ? new Thickness(14, 30, 14, 22) : new Thickness(0);

    public double SidebarSpacing => IsNordic ? 4 : 12;

    public double ComposeHeight => _m.ComposeHeight;

    public CornerRadius ComposeRadius => new(_m.ComposeRadius);

    public string ComposeLabel => IsNordic ? "New message" : "Compose";

    public Thickness ComposeMargin => IsNordic ? new Thickness(0, 0, 0, 18) : new Thickness(0);

    /// <summary>Bento: a tile per section; Nordic: sections on the ground.</summary>
    public Thickness SectionPadding => IsBento ? new Thickness(10) : new Thickness(0);

    public Brush SectionBrush => IsBento ? TileBrush : Transparent;

    public Thickness SectionBorder => IsBento ? new Thickness(1) : new Thickness(0);

    public CornerRadius SectionRadius => IsBento ? new CornerRadius(_m.TileRadius) : new CornerRadius(0);

    public CornerRadius StorageRadius => IsBento ? new CornerRadius(20) : new CornerRadius(0);

    public Thickness StoragePadding => IsBento ? new Thickness(14, 12, 14, 12) : new Thickness(16, 0, 16, 0);

    public Thickness SectionHeaderPadding => IsBento ? new Thickness(6, 2, 2, 4) : new Thickness(16, 20, 12, 6);

    public Visibility BentoOnly => IsBento ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NordicOnly => IsNordic ? Visibility.Visible : Visibility.Collapsed;

    // list rows
    public double FolderRowHeight => IsBento ? _m.FolderRowHeight : 40;

    public Thickness FolderRowPadding => IsBento ? new Thickness(11, 0, 10, 0) : new Thickness(16, 0, 12, 0);

    public Thickness MessageRowPadding => IsBento ? new Thickness(12, 10, 12, 10) : new Thickness(28, 14, 24, 14);

    public double MessageRowSpacing => IsBento ? 11 : 8;

    public Visibility ShowAvatar => IsBento ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ShowUnreadDot => IsNordic ? Visibility.Visible : Visibility.Collapsed;

    public CornerRadius AvatarRadius => new(_m.Round(_m.AvatarSize));

    public Thickness MessageTextMargin => IsNordic ? new Thickness(15, 0, 0, 0) : new Thickness(0);

    public Thickness GroupHeaderPadding => new(_m.ListPadLeft, 8, 12, 2);

    // controls
    public CornerRadius ButtonRadius => new(_m.ButtonRadius);

    public CornerRadius FieldRadius => new(_m.FieldRadius);

    public CornerRadius SummaryRadius => new(_m.SummaryRadius);

    public CornerRadius PopupRadius => new(_m.PopupRadius);

    public double ActionButtonHeight => _m.ActionButtonHeight;

    public CornerRadius ChipRadius => new(14);

    public CornerRadius CardRadius => IsBento ? new CornerRadius(16) : new CornerRadius(18);

    public Thickness CardBorder => IsBento ? new Thickness(1) : new Thickness(0);

    public Brush CardBrush => AppResources.Brush("Tile2");

    public CornerRadius PillRadius => new(11);

    /// <summary>The two item-container styles of a list: Bento or Nordic variant (Styles/Controls.xaml).</summary>
    public Style MessageItemStyle => (Style)Application.Current.Resources[IsNordic ? "ListItem.Message.Nordic" : "ListItem.Message.Bento"];

    public Style FolderItemStyle => (Style)Application.Current.Resources[IsNordic ? "ListItem.Folder.Nordic" : "ListItem.Folder.Bento"];
}
