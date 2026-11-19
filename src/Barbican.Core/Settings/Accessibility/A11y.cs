// Mirrors: Rust a11y.rs (A11y :84-:231, DEFAULTS :9, reset_keys :22, font_scale :30, cvd_color :233); Bulwark lib/accessibility-settings.ts,
// lib/mail-reading.ts (mailFrameCss, mailOverrideCss, applyMailTextSize), lib/cvd-palette.ts. Owner mailcore.

using System.Globalization;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Accessibility;

/// <summary>Settings > Accessibility applied natively: the app chrome through bindings, the message body through CSS (and a hash-allowed script).</summary>
public sealed record A11y
{
    private const string SizingScript =
        "addEventListener('DOMContentLoaded',function(){var f=@F@,m=@M@,fit=@FIT@;var b=document.body,v=window;if(!b)return;"
        + "if(f!=1||m>0){var skip={IMG:1,BR:1,HR:1,WBR:1,PICTURE:1,SOURCE:1,VIDEO:1,AUDIO:1,CANVAS:1,IFRAME:1,OBJECT:1,EMBED:1,STYLE:1,SCRIPT:1,TEMPLATE:1};"
        + "var els=[b];b.querySelectorAll('*').forEach(function(e){if(skip[e.tagName]||e.closest('svg'))return;els.push(e)});var any=false,sz=[];"
        + "els.forEach(function(e){var s=v.getComputedStyle(e);var p=parseFloat(s.fontSize);if(!(p>0))return;var lh=s.lineHeight.slice(-2)=='px'?parseFloat(s.lineHeight):null;"
        + "var n=Math.max(p*f,m||0);if(Math.abs(n-p)>0.01)any=true;sz.push([e,n,lh===null?null:lh*n/p])});"
        + "if(any)sz.forEach(function(x){x[0].style.setProperty('font-size',Math.round(x[1]*100)/100+'px','important');"
        + "if(x[2]!==null)x[0].style.setProperty('line-height',Math.round(x[2]*100)/100+'px','important')})}"
        + "if(fit){var d=document.documentElement;var w=d.scrollWidth,c=d.clientWidth;if(w>c+2)b.style.zoom=(c/w).toFixed(4)}});";

    public string AppFont { get; init; } = "default";

    public bool ShowAccessibilityButton { get; init; }

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
    public static IReadOnlyList<KeyValuePair<string, string>> Defaults { get; } =
    [
        new("contrast", "system"), new("reduceTransparency", "system"), new("underlineLinks", "false"), new("showColorNames", "false"),
        new("colorBlindPalette", "false"), new("appFont", "default"), new("lineSpacing", "normal"), new("letterSpacing", "normal"),
        new("wordSpacing", "normal"), new("paragraphSpacing", "normal"), new("readingWidth", "full"), new("readingMask", "false"),
        new("reduceMotion", "system"), new("pauseAnimatedImages", "system"), new("focusRing", "off"), new("focusAlwaysVisible", "false"),
        new("singleKeyShortcuts", "true"), new("largeTargets", "system"), new("largeCursor", "false"), new("openOnHover", "true"),
        new("showRowActionButton", "false"), new("nativeScrollbars", "false"), new("mailViewMode", "html"), new("mailTextScale", "0"),
        new("mailMinFontSize", "0"), new("mailOverrideStyles", "never"), new("mailFitWidth", "false"), new("readAloudRate", "1"),
        new("readAloudVoice", string.Empty), new("composeFontFamily", string.Empty), new("composeFontSize", string.Empty), new("toastDuration", "5"),
        new("confirmDelete", "false"), new("confirmArchive", "false"), new("visualAlerts", "false"), new("unreadCountStyle", "count"),
        new("doNotDisturb", "false"), new("announceNewMail", "true"), new("announceRowPreview", "false"), new("simpleMode", "false"),
        new("simpleModePagedList", "false"), new("showAccessibilityButton", "false"),
    ];

    /// <summary>Keys "Reset accessibility settings" puts back (do not disturb is shared with Notifications and stays).</summary>
    public static IReadOnlyList<string> ResetKeys() =>
        [.. Defaults.Select(d => d.Key).Where(k => !string.Equals(k, "doNotDisturb", StringComparison.Ordinal)), "fontSize", "plainTextFont"];

    /// <summary>Bulwark FONT_SIZE_SCALE.</summary>
    public static double FontScale(string size)
    {
        ArgumentNullException.ThrowIfNull(size);
        return size switch
        {
            "small" => 0.875,
            "large" => 1.125,
            "x-large" or "extra-large" => 1.25,
            "xx-large" => 1.5,
            "xxx-large" => 1.75,
            "huge" => 2.0,
            _ => 1.0,
        };
    }

    /// <summary>The settings as the prefs and the OS state give them.</summary>
    public static A11y From(PrefsSnapshot prefs, SystemAccessibility system)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new A11y
        {
            AppFont = prefs.Str("appFont", "default"),
            ShowAccessibilityButton = prefs.Bool("showAccessibilityButton", false),
            MoreContrast = prefs.Str("contrast", "system") switch { "more" => true, "normal" => false, _ => system.HighContrast },
            ReduceMotion = Toggle(prefs, "reduceMotion", system.ReduceMotion),
            LargeTargets = Toggle(prefs, "largeTargets", system.Touch),
            UnderlineLinks = prefs.Bool("underlineLinks", false),
            FocusRing = prefs.Str("focusRing", "off") switch { "normal" => 1, "thick" => 2, "extra" => 3, _ => 0 },
            Cvd = prefs.Bool("colorBlindPalette", false),
            SingleKeys = prefs.Bool("singleKeyShortcuts", true),
            ToastSecs = (long)prefs.Number("toastDuration", 5) switch { var n and (0 or 5 or 10 or 20 or 60) => n, _ => 5 },
            ConfirmDelete = prefs.Bool("confirmDelete", false),
            ConfirmArchive = prefs.Bool("confirmArchive", false),
            VisualAlerts = prefs.Bool("visualAlerts", false),
            UnreadStyle = prefs.Str("unreadCountStyle", "count") switch { "dot" => "dot", "hidden" => "hidden", _ => "count" },
            PlainView = string.Equals(prefs.Str("mailViewMode", "html"), "plain", StringComparison.Ordinal),
            ShowRowAction = prefs.Bool("showRowActionButton", false),
            FontScaleFactor = FontScale(prefs.Str("fontSize", "medium")),
            LineSpacing = prefs.Str("lineSpacing", "normal"),
            LetterSpacing = prefs.Str("letterSpacing", "normal"),
            WordSpacing = prefs.Str("wordSpacing", "normal"),
            ParagraphSpacing = prefs.Str("paragraphSpacing", "normal"),
            ComfortableWidth = string.Equals(prefs.Str("readingWidth", "full"), "comfortable", StringComparison.Ordinal),
            TextScale = prefs.Number("mailTextScale", 0),
            MinFontPx = prefs.Number("mailMinFontSize", 0),
            OverrideStyles = prefs.Str("mailOverrideStyles", "never"),
            FitWidth = prefs.Bool("mailFitWidth", false),
        };
    }

    /// <summary>Bulwark colour-blind-safe stand-in (cvdHue + CVD_COLORS, Okabe-Ito) for a 0xRRGGBB colour.</summary>
    public static uint CvdColor(uint rgb)
    {
        var r = ((rgb >> 16) & 0xff) / 255f;
        var g = ((rgb >> 8) & 0xff) / 255f;
        var b = (rgb & 0xff) / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var l = (max + min) / 2f;
        var sat = delta == 0f ? 0f : delta / (1f - Math.Abs((2f * l) - 1f));
        if (sat < 0.15f)
        {
            return 0x999999;
        }

        float hue;
        if (max == r)
        {
            hue = ((g - b) / delta) % 6f;
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2f;
        }
        else
        {
            hue = ((r - g) / delta) + 4f;
        }

        hue *= 60f;
        if (hue < 0f)
        {
            hue += 360f;
        }

        return hue switch
        {
            < 18f or >= 345f => 0xD55E00,
            < 44f => 0xE69F00,
            < 70f => 0xF0E442,
            < 165f => 0x009E73,
            < 200f => 0x56B4E9,
            < 255f => 0x0072B2,
            _ => 0xCC79A7,
        };
    }

    /// <summary>Bulwark mailOverrideActive: always, or with "more contrast" when contrast is raised.</summary>
    public bool OverrideActive() =>
        string.Equals(OverrideStyles, "always", StringComparison.Ordinal)
        || (string.Equals(OverrideStyles, "high-contrast", StringComparison.Ordinal) && MoreContrast);

    /// <summary>Bulwark mailTextFactor: the message text size, or the app's when it follows it.</summary>
    public double TextFactor() => TextScale > 0 ? TextScale : FontScaleFactor;

    /// <summary>Extra CSS for the reader's HTML (Bulwark mailFrameCss + mailOverrideCss).</summary>
    public string MailCss(bool dark)
    {
        var parts = new List<string>();
        if (ReduceMotion)
        {
            parts.Add("*,*::before,*::after{animation-duration:0.01ms!important;animation-iteration-count:1!important;animation-delay:0s!important;transition-duration:0.01ms!important;transition-delay:0s!important;scroll-behavior:auto!important}");
        }

        if (UnderlineLinks || MoreContrast)
        {
            parts.Add("a[href]{text-decoration-line:underline!important}");
        }

        if (ComfortableWidth)
        {
            parts.Add("body{max-width:80ch!important;margin-inline:auto!important}");
        }

        if (OverrideActive())
        {
            parts.AddRange(OverrideCss(dark));
        }
        else
        {
            // the desktop app chrome cannot take letter/line spacing, so they apply to the message text
            const string text = "body,body *:not(svg,svg *,img,picture,video,canvas)";
            switch (LineSpacing)
            {
                case "relaxed":
                    parts.Add($"{text}{{line-height:1.6!important}}");
                    break;
                case "loose":
                    parts.Add($"{text}{{line-height:1.9!important}}");
                    break;
                default:
                    break;
            }

            parts.AddRange(SpacingCss(text));
        }

        return string.Join('\n', parts);
    }

    /// <summary>
    /// What the body shell does to message text (Bulwark applyMailTextSize, fit to the pane); null when nothing needs it. Fable F-03: the
    /// shell applies it to the frame document, no script runs in the frame.
    /// </summary>
    public MailSizing? Sizing()
    {
        var (factor, min) = OverrideActive() ? (1.0, 0.0) : (TextFactor(), MinFontPx);
        var sizing = Math.Abs(factor - 1.0) > 0.001 || min > 0;
        return sizing || FitWidth ? new MailSizing(factor, min, FitWidth) : null;
    }

    /// <summary>
    /// The Rust in-frame script that scales message text and shrinks wide messages (Rust a11y.rs mail_script; kept for its ported tests and
    /// as the reference of shell.js's size()); null when nothing needs it. The app no longer puts it in a frame (Fable F-03).
    /// </summary>
    public string? MailScript()
    {
        var (factor, min) = OverrideActive() ? (1.0, 0.0) : (TextFactor(), MinFontPx);
        var sizing = Math.Abs(factor - 1.0) > 0.001 || min > 0;
        if (!sizing && !FitWidth)
        {
            return null;
        }

        return SizingScript
            .Replace("@F@", factor.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@M@", min.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@FIT@", FitWidth ? "true" : "false", StringComparison.Ordinal);
    }

    private List<string> OverrideCss(bool dark)
    {
        var parts = new List<string>();
        var (fg, bg, link) = (dark, MoreContrast) switch
        {
            (true, true) => ("#ffffff", "#0a0a0a", "#93c5fd"),
            (true, false) => ("#fafafa", "#0a0a0a", "#f0577a"),
            (false, true) => ("#000000", "#ffffff", "#1d4ed8"),
            (false, false) => ("#0a0a0a", "#ffffff", "#c8234a"),
        };
        var size = string.Create(CultureInfo.InvariantCulture, $"calc(0.875rem * {TextFactor()})");
        var bodySize = MinFontPx > 0 ? string.Create(CultureInfo.InvariantCulture, $"max({size}, {MinFontPx}px)") : size;
        var lh = string.Equals(LineSpacing, "loose", StringComparison.Ordinal) ? "1.9" : "1.6";
        const string text = "body *:not(svg,svg *,img,picture,video,canvas)";
        parts.Add($"html{{background:{bg}!important}}body{{background:transparent!important}}html,body{{color:{fg}!important}}");
        parts.Add($"body{{font-family:'Segoe UI',sans-serif!important;font-size:{bodySize}!important;line-height:{lh}!important}}");
        parts.Add($"{text}{{font-family:inherit!important;font-size:1em!important;line-height:inherit!important;color:{fg}!important;background-color:transparent!important;background-image:none!important;text-shadow:none!important}}");
        parts.Add("h1{font-size:1.6em!important}h2{font-size:1.4em!important}h3{font-size:1.2em!important}h4,h5,h6{font-size:1.05em!important}");
        parts.Add(MinFontPx > 0
            ? string.Create(CultureInfo.InvariantCulture, $"small,sub,sup{{font-size:max(0.85em,{MinFontPx}px)!important}}")
            : "small,sub,sup{font-size:0.85em!important}");
        parts.Add($"a[href],a[href] *{{color:{link}!important}}a[href]{{text-decoration-line:underline!important}}");
        parts.Add("[align=\"justify\"],[style*=\"justify\"]{text-align:start!important}");
        parts.AddRange(SpacingCss(text));
        return parts;
    }

    private static bool Toggle(PrefsSnapshot prefs, string key, bool system) =>
        prefs.Str(key, "system") switch { "on" => true, "off" => false, _ => system };

    private List<string> SpacingCss(string text)
    {
        var v = new List<string>();
        switch (LetterSpacing)
        {
            case "wide":
                v.Add($"{text}{{letter-spacing:0.05em!important}}");
                break;
            case "wider":
                v.Add($"{text}{{letter-spacing:0.12em!important}}");
                break;
            default:
                break;
        }

        if (string.Equals(WordSpacing, "wide", StringComparison.Ordinal))
        {
            v.Add($"{text}{{word-spacing:0.16em!important}}");
        }

        if (string.Equals(ParagraphSpacing, "wide", StringComparison.Ordinal))
        {
            v.Add("body p{margin-block-end:1.5em!important}");
        }

        return v;
    }
}
