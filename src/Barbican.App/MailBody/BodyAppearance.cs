// Mirrors: Rust main.rs:229 page() inputs and mailview.rs reader_bg :84 (tag rust-final): the theme the app is drawn in, the reader's colour
// (the palette token Reader), "always light", the accessibility settings, plainTextFont and messageSpacing. Owner body.

using Barbican.App.Theme;
using Barbican.Core.Mail.Body;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Theme;
using Windows.UI;

namespace Barbican.App.MailBody;

/// <summary>The page options of the moment, from the applied theme and the settings.</summary>
public sealed class BodyAppearance(ThemeController theme, IPrefsStore prefs, ISystemAccessibilitySource system)
{
    /// <summary>The variant the window is drawn in (dark Bento until the theme is first applied).</summary>
    public ThemeVariant Variant => theme.Applied ?? new ThemeVariant(true, false, false);

    /// <summary>The reader's colour (the tile the body sits on).</summary>
    public Color ReaderColor => AppResources.FromArgb(Tokens.Palette(Variant)["Reader"]);

    /// <summary>The reader's colour as CSS hex.</summary>
    public string ReaderHex => Hex(ReaderColor);

    /// <summary>The accessibility settings now.</summary>
    public A11y A11y => A11y.From(prefs.Current, system.Current);

    /// <summary>The options for a page; <paramref name="allowRemote"/> is the per-message "Load external content".</summary>
    public MailPageOptions Options(bool allowRemote) => new()
    {
        Dark = Variant.Dark,
        AlwaysLight = prefs.Current.Bool("emailAlwaysLightMode", false),
        A11y = A11y,
        PlainTextFont = prefs.Current.Str("plainTextFont", "sans"),
        MessageSpacing = prefs.Current.Str("messageSpacing", "auto"),
        Nordic = Variant.Nordic,
        AllowRemote = allowRemote,
        ReaderBackground = ReaderHex,
    };

    /// <summary>A CSS hex colour as a colour (opaque).</summary>
    public static Color Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var h = hex.TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        }

        return h.Length == 6 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : Color.FromArgb(255, 255, 255, 255);
    }

    private static string Hex(Color c) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{c.R:x2}{c.G:x2}{c.B:x2}");
}
