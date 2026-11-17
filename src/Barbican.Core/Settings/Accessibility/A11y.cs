// Mirrors: Rust a11y.rs (A11y :84-:231, DEFAULTS :9, reset_keys :22, font_scale :30, cvd_color :233); Bulwark lib/accessibility-settings.ts,
// lib/mail-reading.ts, lib/cvd-palette.ts. Stub from F2; owner mailcore.

using Barbican.Core.Common;
using Barbican.Core.Storage;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

namespace Barbican.Core.Settings.Accessibility;

/// <summary>Settings > Accessibility applied natively: the app chrome through bindings, the message body through CSS (and a hash-allowed script).</summary>
public sealed record A11y
{
    public bool MoreContrast { get; init; }

    public bool ReduceMotion { get; init; }

    public bool LargeTargets { get; init; }

    public bool UnderlineLinks { get; init; }

    /// <summary>0 off, 1 normal, 2 thick, 3 extra.</summary>
    public int FocusRing { get; init; }

    public bool Cvd { get; init; }

    public bool SingleKeys { get; init; } = true;

    /// <summary>Seconds a toast stays; 0 until closed.</summary>
    public long ToastSecs { get; init; } = 5;

    public bool ConfirmDelete { get; init; }

    public bool ConfirmArchive { get; init; }

    public bool VisualAlerts { get; init; }

    /// <summary>count, dot or hidden.</summary>
    public string UnreadStyle { get; init; } = "count";

    public bool PlainView { get; init; }

    public bool ShowRowAction { get; init; }

    public double FontScaleFactor { get; init; } = 1;

    public string LineSpacing { get; init; } = "normal";

    public string LetterSpacing { get; init; } = "normal";

    public string WordSpacing { get; init; } = "normal";

    public string ParagraphSpacing { get; init; } = "normal";

    public bool ComfortableWidth { get; init; }

    public double TextScale { get; init; }

    public double MinFontPx { get; init; }

    public string OverrideStyles { get; init; } = "never";

    public bool FitWidth { get; init; }

    /// <summary>Bulwark ACCESSIBILITY_DEFAULTS (same keys and values as prefs.json stores them).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Defaults { get; } = [];

    /// <summary>Keys "Reset accessibility settings" puts back (do not disturb is shared with Notifications and stays).</summary>
    public static IReadOnlyList<string> ResetKeys() => throw new PortPendingException("A11y.ResetKeys: mailcore stream");

    /// <summary>Bulwark FONT_SIZE_SCALE.</summary>
    public static double FontScale(string size) => throw new PortPendingException("A11y.FontScale: mailcore stream");

    /// <summary>The settings as the prefs and the OS state give them.</summary>
    public static A11y From(PrefsSnapshot prefs, SystemAccessibility system) => throw new PortPendingException("A11y.From: mailcore stream");

    /// <summary>Bulwark mailOverrideActive: always, or with "more contrast" when contrast is raised.</summary>
    public bool OverrideActive() => throw new PortPendingException("A11y.OverrideActive: mailcore stream");

    /// <summary>Bulwark mailTextFactor: the message text size, or the app's when it follows it.</summary>
    public double TextFactor() => throw new PortPendingException("A11y.TextFactor: mailcore stream");

    /// <summary>Extra CSS for the reader's HTML (Bulwark mailFrameCss + mailOverrideCss).</summary>
    public string MailCss(bool dark) => throw new PortPendingException("A11y.MailCss: mailcore stream");

    /// <summary>Script that scales message text (Bulwark applyMailTextSize) and shrinks wide messages to the pane; null when nothing needs it.</summary>
    public string? MailScript() => throw new PortPendingException("A11y.MailScript: mailcore stream");

    /// <summary>Bulwark cvdHue + CVD_COLORS: the colour-blind-safe (Okabe-Ito) stand-in for a 0xRRGGBB colour.</summary>
    public static uint CvdColor(uint rgb) => throw new PortPendingException("A11y.CvdColor: mailcore stream");
}
