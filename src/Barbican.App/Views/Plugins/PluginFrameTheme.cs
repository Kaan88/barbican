#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-theme.ts snapshotHostTheme (whether dark mode is on, the font stack and the resolved colour tokens), shared by every host that draws an add-on frame (the page, the dialog).
using System.Globalization;
using System.Text.Json;
using Barbican.App.Theme;

namespace Barbican.App.Views.Plugins;

/// <summary>The theme an add-on frame starts with.</summary>
internal static class PluginFrameTheme
{
    /// <summary>The snapshot for a frame drawn inside <paramref name="area"/>.</summary>
    internal static JsonElement Snapshot(FrameworkElement area)
    {
        ArgumentNullException.ThrowIfNull(area);
        static string Css(string key)
        {
            var c = AppResources.Brush(key).Color;
            return c.A == 255
                ? string.Create(CultureInfo.InvariantCulture, $"#{c.R:x2}{c.G:x2}{c.B:x2}")
                : string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");
        }

        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, key) in new[] { ("--color-background", "Bg"), ("--color-foreground", "Text"), ("--color-muted-foreground", "Faint"), ("--color-border", "Line"), ("--color-primary", "Accent") })
        {
            try { vars[token] = Css(key); }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidCastException or NullReferenceException) { }
        }

        return JsonSerializer.SerializeToElement(new
        {
            dark = area.ActualTheme == ElementTheme.Dark,
            fontFamily = "\"Segoe UI Variable\", \"Segoe UI\", sans-serif",
            vars,
        });
    }
}
#endif
