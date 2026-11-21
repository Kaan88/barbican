#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/layout/navigation-rail.tsx:606-608,640-668 (18 px icons in a 40 px box, active = bg-primary/10 text-primary, red-600 unread pill or
// 10 px red-500 dot), :164-221 (storage ring: stroke-success under 70 %, stroke-warning over it, stroke-destructive over 90 %, lib/quota.ts:72-77),
// :712-715,735 (footer gap-2, hairline divider rgba(128,128,128,.3)), :801-826 (account circles: ring-2 ring-primary on the active one, opacity-70 on the others).
// Only the Bulwark style takes these values; Bento and Nordic keep the prototype's rail (UI-GUIDELINES.md).

using Windows.UI;

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    private static readonly SolidColorBrush RedBadge = new(Color.FromArgb(255, 0xDC, 0x26, 0x26)); // tailwind red-600 (navigation-rail.tsx:663)
    private static readonly SolidColorBrush RedDot = new(Color.FromArgb(255, 0xEF, 0x44, 0x44)); // tailwind red-500 (navigation-rail.tsx:655)
    private static readonly SolidColorBrush RailRule = new(Color.FromArgb(0x4D, 0x80, 0x80, 0x80)); // rgba(128, 128, 128, 0.3) (navigation-rail.tsx:735)
    private static readonly SolidColorBrush AmberStar = new(Color.FromArgb(255, 0xF5, 0x9E, 0x0B)); // tailwind amber-500 (account-switcher.tsx:482)
    private static readonly SolidColorBrush Clear = new(Color.FromArgb(0, 0, 0, 0));

    /// <summary>The icon of a rail button: Bulwark 18 px (w-[18px]), the prototype's 20 px otherwise.</summary>
    public double RailIcon => IsBulwark ? 18 : 20;

    /// <summary>The gap inside the footer group (Bulwark gap-2, 8; the prototype 2).</summary>
    public double RailFooterSpacing => IsBulwark ? 8 : 2;

    /// <summary>No extra gap under the footer group (Bulwark pb-3 is the rail padding, StyleMetrics.RailPadBottom).</summary>
    public Thickness RailFooterMargin => default;

    /// <summary>Space above the account part: none in Bulwark (it is one more gap-2 item), the prototype's 10 px otherwise.</summary>
    public Thickness RailAccountMargin => new(0, IsBulwark ? 0 : 10, 0, 0);

    /// <summary>The hairline between Settings and the shortcuts button (Bulwark only).</summary>
    public Brush RailDivider => RailRule;

    /// <summary>The unread pill (Bulwark red-600 with white text; the accent elsewhere).</summary>
    public Brush RailBadgeBrush => IsBulwark ? RedBadge : AppResources.Brush("Accent");

    public Brush RailBadgeForeground => IsBulwark ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) : AppResources.Brush("OnAccent");

    /// <summary>The dot of the "dot" unread style (Bulwark red-500).</summary>
    public Brush RailDotBrush => IsBulwark ? RedDot : AppResources.Brush("Accent");

    /// <summary>Bulwark -top-0.5 -end-0.5 (collapsed rail); the prototype hung the pill 4 px out.</summary>
    public Thickness RailBadgeMargin => IsBulwark ? new(0, -2, -2, 0) : new(0, -2, -4, 0);

    /// <summary>Bulwark top-1 end-1 for the dot.</summary>
    public Thickness RailDotMargin => new(0, 4, 4, 0);

    /// <summary>The ring around the storage button: stroke-muted track, level colour on the arc.</summary>
    public static Brush RingBrush(double fraction) => AppResources.Brush(fraction > 0.9 ? "Destructive" : fraction > 0.7 ? "Warning" : "Success");

    // ---- the account popover (components/layout/account-switcher.tsx:353-682): w-72, rounded-lg, rows px-3 py-2.5 / px-3 py-2, border-t between sections ----

    public double PopoverWidth => IsBulwark ? 288 : 260;

    public Thickness PopoverPadding => new(IsBulwark ? 0 : 6);

    public double PopoverSpacing => IsBulwark ? 0 : 2;

    public Thickness PopoverRuleMargin => new(0, IsBulwark ? 0 : 4, 0, IsBulwark ? 0 : 4);

    public Thickness PopoverRowPadding => IsBulwark ? new(12, 10, 12, 10) : new(8, 6, 8, 6);

    public Thickness PopoverActionPadding => IsBulwark ? new(12, 8, 12, 8) : new(8, 8, 8, 8);

    /// <summary>The Accounts caption exists only outside Bulwark (the Bulwark list has none).</summary>
    public Visibility PopoverHeaderVisibility => IsBulwark ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A popover row is square in Bulwark (the hover fills the card's width), rounded in Bento and Nordic.</summary>
    public CornerRadius PopoverRowRadius => IsBulwark ? new CornerRadius(0) : new CornerRadius(9);

    /// <summary>The avatar of a popover row (Bulwark w-9 h-9 rounded-full, 36; the prototype 28).</summary>
    public double PopoverAvatar => IsBulwark ? 36 : 28;

    public CornerRadius PopoverAvatarRadius => new(_m.Round(PopoverAvatar));

    public double PopoverTextSize => IsBulwark ? 14 : 13;

    /// <summary>The default account's star (Bulwark text-amber-500).</summary>
    public Brush PopoverStarBrush => IsBulwark ? AmberStar : AppResources.Brush("Accent");

    /// <summary>The unread count: Bulwark plain primary text (text-xs font-medium text-primary); the prototype a pill.</summary>
    public Brush PopoverCountFill => IsBulwark ? Clear : AppResources.Brush("Accent");

    public Brush PopoverCountText => IsBulwark ? AppResources.Brush("Accent") : AppResources.Brush("Ground");

    public Thickness PopoverCountPadding => IsBulwark ? new Thickness(0) : new Thickness(6, 0, 6, 0);

    /// <summary>The row of the active account: Bulwark bg-accent/50 (the shared selected fill).</summary>
    public static Brush AccountRowFill(bool active) => active ? AppResources.Brush("FeedbackSelected") : Clear;

    /// <summary>The "dot" unread style is the single bullet the view model hands over.</summary>
    public static Visibility RailPill(string badge) => !string.IsNullOrEmpty(badge) && !IsDot(badge) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility RailDot(string badge) => IsDot(badge) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>An account circle that is not the active one is dimmed (opacity-70, navigation-rail.tsx:809).</summary>
    public static double AccountDim(bool active) => active ? 1 : 0.7;

    /// <summary>The ring of the active account circle (ring-2 ring-primary).</summary>
    public static Thickness AccountRing(bool active) => new(active ? 2 : 0);

    /// <summary>The used amount of the storage text ("1.2 GB of 15 GB" or "3.4 GB used"), for the popover's Used row (navigation-rail.tsx:196-198).</summary>
    public static string QuotaUsed(string text)
    {
        var at = text.IndexOf(" of ", StringComparison.Ordinal);
        return at >= 0 ? text[..at] : text.Replace(" used", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>The total of the storage text (Total row, navigation-rail.tsx:204).</summary>
    public static string QuotaTotal(string text)
    {
        var at = text.IndexOf(" of ", StringComparison.Ordinal);
        return at >= 0 ? text[(at + 4)..] : string.Empty;
    }

    /// <summary>The free amount (Free row, navigation-rail.tsx:200-202): the total less the used fraction, written the way the meter writes sizes.</summary>
    public static string QuotaFree(string text, double fraction)
    {
        var total = QuotaTotal(text).Split(' ');
        if (fraction < 0 || total.Length != 2 || !double.TryParse(total[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            return string.Empty;
        }

        var bytes = amount * (string.Equals(total[1], "MB", StringComparison.Ordinal) ? 1e6 : 1e9);
        return Barbican.Core.Mail.Glance.GlanceRules.Gigabytes((long)(bytes * (1 - Math.Clamp(fraction, 0, 1))));
    }

    /// <summary>"42%" for the ring's label and the popover's caption.</summary>
    public static string Percent(double fraction) => fraction < 0 ? string.Empty : Math.Round(Math.Clamp(fraction, 0, 1) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>Only Bulwark shows the storage button in the footer; Bento and Nordic wrap the account button in the ring.</summary>
    public static Visibility StorageButton(bool bulwark, double fraction) => bulwark && fraction >= 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StorageAroundAccount(bool bulwark) => bulwark ? Visibility.Collapsed : Visibility.Visible;

    private static bool IsDot(string badge) => string.Equals(badge, "●", StringComparison.Ordinal);
}
