#pragma warning disable CA1822 // members mirror the other StyleState partials
// Mirrors: Bulwark components/settings/settings-app.tsx:1213-1360 (rail w-14, nav column 256 px with bg-secondary and border-e, rows py-2 ps-3 text-sm gap-2.5 with 16 px icons, group heading
// text-[0.6875rem] font-semibold, page max-w-3xl px-6 py-6), settings-section.tsx:222-342 (section: h3 text-lg font-medium, description text-sm muted mt-1, space-y-4; row: py-3 border-b, label
// text-sm font-medium, description text-xs muted mt-1), :463-481 and :432-460 (radio group px-3 py-1.5 text-xs rounded-md; select), ui/button.tsx:15-23 (sm: h-9 px-3 text-sm font-medium).
// The Bento and Nordic values stay the ones the settings views used before. WP-11, ST-01, ST-02, ST-04.

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The nav column: Bulwark 256 (the stored default), the older styles 272.</summary>
    public double SetNavWidth => IsBulwark ? 256 : 272;

    /// <summary>The page's widest line: Bulwark max-w-3xl (768), the older styles 760.</summary>
    public double SetPageMaxWidth => IsBulwark ? 768 : 760;

    /// <summary>The page padding: Bulwark px-6 py-6 (the page is centred), Bento 28 / 20 / 28 / 28.</summary>
    public Thickness SetPagePadding => IsBulwark ? new Thickness(24) : new Thickness(28, 20, 28, 28);

    /// <summary>Bulwark names the page only in the nav (the h1 is for screen readers); Bento and Nordic keep the title.</summary>
    public Visibility SetPageTitleVisibility => IsBulwark ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The page body is centred in Bulwark (mx-auto) and left-aligned in the older styles.</summary>
    public HorizontalAlignment SetPageAlignment => IsBulwark ? HorizontalAlignment.Center : HorizontalAlignment.Left;

    /// <summary>Section title: Bulwark text-lg (18) font-medium, Bento 15 bold.</summary>
    public double SetSectionSize => IsBulwark ? 18 : 15;

    public FontWeight SetSectionWeight => IsBulwark ? FontWeights.Medium : FontWeights.Bold;

    /// <summary>The space above a section that follows another (space-y-8 is 32).</summary>
    public double SetSectionGap => IsBulwark ? 32 : 18;

    /// <summary>A row's padding: Bulwark py-3.</summary>
    public Thickness SetRowPadding => IsBulwark ? new Thickness(0, 12, 0, 12) : new Thickness(0, 8, 0, 8);

    /// <summary>A row's label: Bulwark text-sm font-medium in the foreground colour.</summary>
    public double SetLabelSize => IsBulwark ? 14 : 13;

    public FontWeight SetLabelWeight => IsBulwark ? FontWeights.Medium : FontWeights.Normal;

    public string SetLabelBrush => IsBulwark ? "Text" : "Body";

    /// <summary>A row's description: Bulwark text-xs (12) muted with mt-1.</summary>
    public double SetDescSize => 12;

    public double SetDescGap => IsBulwark ? 4 : 2;

    /// <summary>The text of a button in a row: Bulwark h-9 px-3 text-sm font-medium, radius from the shape; the older styles keep the pill.</summary>
    public double SetButtonHeight => IsBulwark ? 36 : 32;

    public double SetButtonSize => IsBulwark ? 14 : 13;

    public FontWeight SetButtonWeight => IsBulwark ? FontWeights.Medium : FontWeights.SemiBold;

    public CornerRadius SetButtonRadius => IsBulwark ? BulwarkButtonRadius : new CornerRadius(16);

    public Thickness SetButtonPadding => IsBulwark ? new Thickness(12, 0, 12, 0) : new Thickness(14, 0, 16, 0);

    public double SetButtonMinWidth => IsBulwark ? 0 : 80;

    /// <summary>An option of a choice group: Bulwark px-3 py-1.5 text-xs rounded-md, the older styles the 30 px pill.</summary>
    public double SetChoiceHeight => IsBulwark ? 28 : 30;

    public double SetChoiceSize => IsBulwark ? 12 : 12.5;

    public CornerRadius SetChoiceRadius => IsBulwark ? BulwarkButtonRadius : new CornerRadius(15);

    /// <summary>A card of the add-ons list: Bulwark rounded-lg border p-3.</summary>
    public CornerRadius SetCardRadius => IsBulwark ? DialogRadius : new CornerRadius(10);

    private CornerRadius BulwarkButtonRadius => ButtonRadius;
}
