//! Bulwark's Settings > Accessibility (lib/accessibility-settings.ts, lib/mail-reading.ts,
//! lib/cvd-palette.ts) applied natively: the app chrome through Slint globals, the message
//! body through CSS (and a hash-allowed script for text sizing) in the reader's HTML.
use crate::settings::Settings;

/// Bulwark ACCESSIBILITY_DEFAULTS (same keys and values as prefs.json stores them).
pub const DEFAULTS: &[(&str, &str)] = &[
    ("contrast", "system"), ("reduceTransparency", "system"), ("underlineLinks", "false"), ("showColorNames", "false"),
    ("colorBlindPalette", "false"), ("appFont", "default"), ("lineSpacing", "normal"), ("letterSpacing", "normal"),
    ("wordSpacing", "normal"), ("paragraphSpacing", "normal"), ("readingWidth", "full"), ("readingMask", "false"),
    ("reduceMotion", "system"), ("pauseAnimatedImages", "system"), ("focusRing", "off"), ("focusAlwaysVisible", "false"),
    ("singleKeyShortcuts", "true"), ("largeTargets", "system"), ("largeCursor", "false"), ("openOnHover", "true"),
    ("showRowActionButton", "false"), ("nativeScrollbars", "false"), ("mailViewMode", "html"), ("mailTextScale", "0"),
    ("mailMinFontSize", "0"), ("mailOverrideStyles", "never"), ("mailFitWidth", "false"), ("readAloudRate", "1"),
    ("readAloudVoice", ""), ("composeFontFamily", ""), ("composeFontSize", ""), ("toastDuration", "5"),
    ("confirmDelete", "false"), ("confirmArchive", "false"), ("visualAlerts", "false"), ("unreadCountStyle", "count"),
    ("doNotDisturb", "false"), ("announceNewMail", "true"), ("announceRowPreview", "false"), ("simpleMode", "false"),
    ("simpleModePagedList", "false"), ("showAccessibilityButton", "false"),
];

/// Keys "Reset accessibility settings" puts back: the page's own settings plus the text size
/// and plain-text font it shows. Do not disturb is shared with Notifications and stays.
pub fn reset_keys() -> Vec<&'static str> {
    let mut v: Vec<&str> = DEFAULTS.iter().map(|d| d.0).filter(|k| *k != "doNotDisturb").collect();
    v.extend(["fontSize", "plainTextFont"]);
    v
}

/// Bulwark FONT_SIZE_SCALE.
pub fn font_scale(size: &str) -> f32 {
    match size {
        "small" => 0.875, "large" => 1.125, "x-large" | "extra-large" => 1.25, "xx-large" => 1.5,
        "xxx-large" => 1.75, "huge" => 2.0, _ => 1.0,
    }
}

/// What Windows reports for the settings left on "System".
#[derive(Clone, Copy, Default, Debug)]
pub struct System { pub high_contrast: bool, pub reduce_motion: bool, pub touch: bool }

#[cfg(windows)]
mod win {
    #[repr(C)]
    struct HighContrast { size: u32, flags: u32, scheme: *mut u16 }
    #[link(name = "user32")]
    extern "system" {
        fn SystemParametersInfoW(action: u32, param: u32, pv: *mut core::ffi::c_void, ini: u32) -> i32;
        fn GetSystemMetrics(index: i32) -> i32;
    }
    pub fn high_contrast() -> bool {
        let mut hc = HighContrast { size: std::mem::size_of::<HighContrast>() as u32, flags: 0, scheme: std::ptr::null_mut() };
        // SPI_GETHIGHCONTRAST, HCF_HIGHCONTRASTON
        unsafe { SystemParametersInfoW(0x0042, hc.size, &mut hc as *mut _ as *mut _, 0) != 0 && hc.flags & 1 != 0 }
    }
    pub fn animations_off() -> bool {
        let mut on: i32 = 1;
        // SPI_GETCLIENTAREAANIMATION ("Animation effects" in Windows settings)
        unsafe { SystemParametersInfoW(0x1042, 0, &mut on as *mut _ as *mut _, 0) != 0 && on == 0 }
    }
    pub fn touch() -> bool {
        // SM_MAXIMUMTOUCHES
        unsafe { GetSystemMetrics(95) > 0 }
    }
}

impl System {
    #[cfg(windows)]
    pub fn detect() -> System { System { high_contrast: win::high_contrast(), reduce_motion: win::animations_off(), touch: win::touch() } }
    #[cfg(not(windows))]
    pub fn detect() -> System { System::default() }
}

#[derive(Clone, Debug, PartialEq)]
pub struct A11y {
    pub more_contrast: bool,
    pub reduce_motion: bool,
    pub large_targets: bool,
    pub underline_links: bool,
    /// 0 off, 1 normal, 2 thick, 3 extra
    pub focus_ring: i32,
    pub cvd: bool,
    pub single_keys: bool,
    /// seconds a toast stays; 0 = until closed
    pub toast_secs: u64,
    pub confirm_delete: bool,
    pub confirm_archive: bool,
    pub visual_alerts: bool,
    /// count | dot | hidden
    pub unread_style: String,
    pub plain_view: bool,
    pub show_row_action: bool,
    pub font_scale: f32,
    // message body
    pub line_spacing: String,
    pub letter_spacing: String,
    pub word_spacing: String,
    pub paragraph_spacing: String,
    pub comfortable_width: bool,
    pub text_scale: f32,
    pub min_font_px: f32,
    pub override_styles: String,
    pub fit_width: bool,
}

fn num(p: &Settings, key: &str, default: f32) -> f32 {
    match p.prefs.get(key) {
        Some(serde_json::Value::Number(n)) => n.as_f64().map(|f| f as f32).unwrap_or(default),
        Some(serde_json::Value::String(s)) => s.trim().parse().unwrap_or(default),
        _ => default,
    }
}

/// Bulwark SystemToggle: "on" | "off" | "system" (follows Windows).
fn toggle(p: &Settings, key: &str, system: bool) -> bool {
    match p.str(key, "system").as_str() { "on" => true, "off" => false, _ => system }
}

impl A11y {
    pub fn load() -> A11y { Self::from(&Settings::load(), System::detect()) }

    pub fn from(p: &Settings, sys: System) -> A11y {
        A11y {
            more_contrast: match p.str("contrast", "system").as_str() { "more" => true, "normal" => false, _ => sys.high_contrast },
            reduce_motion: toggle(p, "reduceMotion", sys.reduce_motion),
            large_targets: toggle(p, "largeTargets", sys.touch),
            underline_links: p.bool("underlineLinks", false),
            focus_ring: match p.str("focusRing", "off").as_str() { "normal" => 1, "thick" => 2, "extra" => 3, _ => 0 },
            cvd: p.bool("colorBlindPalette", false),
            single_keys: p.bool("singleKeyShortcuts", true),
            toast_secs: match num(p, "toastDuration", 5.0) as u64 { n @ (0 | 5 | 10 | 20 | 60) => n, _ => 5 },
            confirm_delete: p.bool("confirmDelete", false),
            confirm_archive: p.bool("confirmArchive", false),
            visual_alerts: p.bool("visualAlerts", false),
            unread_style: match p.str("unreadCountStyle", "count").as_str() { s @ ("dot" | "hidden") => s.into(), _ => "count".into() },
            plain_view: p.str("mailViewMode", "html") == "plain",
            show_row_action: p.bool("showRowActionButton", false),
            font_scale: font_scale(&p.str("fontSize", "medium")),
            line_spacing: p.str("lineSpacing", "normal"),
            letter_spacing: p.str("letterSpacing", "normal"),
            word_spacing: p.str("wordSpacing", "normal"),
            paragraph_spacing: p.str("paragraphSpacing", "normal"),
            comfortable_width: p.str("readingWidth", "full") == "comfortable",
            text_scale: num(p, "mailTextScale", 0.0),
            min_font_px: num(p, "mailMinFontSize", 0.0),
            override_styles: p.str("mailOverrideStyles", "never"),
            fit_width: p.bool("mailFitWidth", false),
        }
    }

    /// Bulwark mailOverrideActive: always, or with "more contrast" when contrast is raised.
    pub fn override_active(&self) -> bool {
        self.override_styles == "always" || (self.override_styles == "high-contrast" && self.more_contrast)
    }

    /// Bulwark mailTextFactor: the message text size, or the app's when it follows it.
    pub fn text_factor(&self) -> f32 { if self.text_scale > 0.0 { self.text_scale } else { self.font_scale } }

    /// Extra CSS for the reader's HTML (Bulwark mailFrameCss + mailOverrideCss).
    pub fn mail_css(&self, dark: bool) -> String {
        let mut parts: Vec<String> = vec![];
        if self.reduce_motion {
            parts.push("*,*::before,*::after{animation-duration:0.01ms!important;animation-iteration-count:1!important;animation-delay:0s!important;transition-duration:0.01ms!important;transition-delay:0s!important;scroll-behavior:auto!important}".into());
        }
        if self.underline_links || self.more_contrast { parts.push("a[href]{text-decoration-line:underline!important}".into()); }
        if self.comfortable_width { parts.push("body{max-width:80ch!important;margin-inline:auto!important}".into()); }
        if self.override_active() {
            let (fg, bg, link) = match (dark, self.more_contrast) {
                (true, true) => ("#ffffff", "#0a0a0a", "#93c5fd"),
                (true, false) => ("#fafafa", "#0a0a0a", "#f0577a"),
                (false, true) => ("#000000", "#ffffff", "#1d4ed8"),
                (false, false) => ("#0a0a0a", "#ffffff", "#c8234a"),
            };
            let size = format!("calc(0.875rem * {})", self.text_factor());
            let body_size = if self.min_font_px > 0.0 { format!("max({size}, {}px)", self.min_font_px) } else { size };
            let lh = if self.line_spacing == "loose" { 1.9 } else { 1.6 };
            let text = "body *:not(svg,svg *,img,picture,video,canvas)";
            parts.push(format!("html{{background:{bg}!important}}body{{background:transparent!important}}html,body{{color:{fg}!important}}"));
            parts.push(format!("body{{font-family:'Segoe UI',sans-serif!important;font-size:{body_size}!important;line-height:{lh}!important}}"));
            parts.push(format!("{text}{{font-family:inherit!important;font-size:1em!important;line-height:inherit!important;color:{fg}!important;background-color:transparent!important;background-image:none!important;text-shadow:none!important}}"));
            parts.push("h1{font-size:1.6em!important}h2{font-size:1.4em!important}h3{font-size:1.2em!important}h4,h5,h6{font-size:1.05em!important}".into());
            parts.push(if self.min_font_px > 0.0 { format!("small,sub,sup{{font-size:max(0.85em,{}px)!important}}", self.min_font_px) } else { "small,sub,sup{font-size:0.85em!important}".into() });
            parts.push(format!("a[href],a[href] *{{color:{link}!important}}a[href]{{text-decoration-line:underline!important}}"));
            parts.push("[align=\"justify\"],[style*=\"justify\"]{text-align:start!important}".into());
            parts.extend(self.spacing_css(text));
        } else {
            // the desktop app chrome cannot take letter/line spacing, so they apply to the message text
            let text = "body,body *:not(svg,svg *,img,picture,video,canvas)";
            match self.line_spacing.as_str() {
                "relaxed" => parts.push(format!("{text}{{line-height:1.6!important}}")),
                "loose" => parts.push(format!("{text}{{line-height:1.9!important}}")),
                _ => {}
            }
            parts.extend(self.spacing_css(text));
        }
        parts.join("\n")
    }

    fn spacing_css(&self, text: &str) -> Vec<String> {
        let mut v = vec![];
        match self.letter_spacing.as_str() {
            "wide" => v.push(format!("{text}{{letter-spacing:0.05em!important}}")),
            "wider" => v.push(format!("{text}{{letter-spacing:0.12em!important}}")),
            _ => {}
        }
        if self.word_spacing == "wide" { v.push(format!("{text}{{word-spacing:0.16em!important}}")); }
        if self.paragraph_spacing == "wide" { v.push("body p{margin-block-end:1.5em!important}".into()); }
        v
    }

    /// Script that scales message text (Bulwark applyMailTextSize) and shrinks wide messages
    /// to the pane (mailFitWidth). None when nothing needs it. With the override styles the
    /// sizes come from CSS, so only fitting remains.
    pub fn mail_script(&self) -> Option<String> {
        let (factor, min) = if self.override_active() { (1.0, 0.0) } else { (self.text_factor(), self.min_font_px) };
        let sizing = (factor - 1.0).abs() > 0.001 || min > 0.0;
        if !sizing && !self.fit_width { return None; }
        Some(format!(
            "addEventListener('DOMContentLoaded',function(){{var f={factor},m={min},fit={fit};var b=document.body,v=window;if(!b)return;\
if(f!=1||m>0){{var skip={{IMG:1,BR:1,HR:1,WBR:1,PICTURE:1,SOURCE:1,VIDEO:1,AUDIO:1,CANVAS:1,IFRAME:1,OBJECT:1,EMBED:1,STYLE:1,SCRIPT:1,TEMPLATE:1}};\
var els=[b];b.querySelectorAll('*').forEach(function(e){{if(skip[e.tagName]||e.closest('svg'))return;els.push(e)}});var any=false,sz=[];\
els.forEach(function(e){{var s=v.getComputedStyle(e);var p=parseFloat(s.fontSize);if(!(p>0))return;var lh=s.lineHeight.slice(-2)=='px'?parseFloat(s.lineHeight):null;\
var n=Math.max(p*f,m||0);if(Math.abs(n-p)>0.01)any=true;sz.push([e,n,lh===null?null:lh*n/p])}});\
if(any)sz.forEach(function(x){{x[0].style.setProperty('font-size',Math.round(x[1]*100)/100+'px','important');if(x[2]!==null)x[0].style.setProperty('line-height',Math.round(x[2]*100)/100+'px','important')}})}}\
if(fit){{var d=document.documentElement;var w=d.scrollWidth,c=d.clientWidth;if(w>c+2)b.style.zoom=(c/w).toFixed(4)}}}});",
            fit = self.fit_width
        ))
    }
}

/// Bulwark cvdHue + CVD_COLORS: the colour-blind-safe (Okabe-Ito) stand-in for a colour.
pub fn cvd_color(rgb: u32) -> u32 {
    let (r, g, b) = (((rgb >> 16) & 0xff) as f32 / 255.0, ((rgb >> 8) & 0xff) as f32 / 255.0, (rgb & 0xff) as f32 / 255.0);
    let max = r.max(g).max(b);
    let min = r.min(g).min(b);
    let delta = max - min;
    let l = (max + min) / 2.0;
    let sat = if delta == 0.0 { 0.0 } else { delta / (1.0 - (2.0 * l - 1.0).abs()) };
    if sat < 0.15 { return 0x999999; }
    let mut hue = (if max == r { ((g - b) / delta) % 6.0 } else if max == g { (b - r) / delta + 2.0 } else { (r - g) / delta + 4.0 }) * 60.0;
    if hue < 0.0 { hue += 360.0; }
    match hue {
        h if !(18.0..345.0).contains(&h) => 0xD55E00,
        h if h < 44.0 => 0xE69F00,
        h if h < 70.0 => 0xF0E442,
        h if h < 165.0 => 0x009E73,
        h if h < 200.0 => 0x56B4E9,
        h if h < 255.0 => 0x0072B2,
        _ => 0xCC79A7,
    }
}

/// A colour as the user's palette shows it (colour-blind-safe when chosen).
pub fn color(rgb: u32) -> u32 {
    if Settings::load().bool("colorBlindPalette", false) { cvd_color(rgb) } else { rgb }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn settings(v: serde_json::Value) -> Settings { Settings::with_prefs(v.as_object().unwrap().clone()) }

    #[test]
    fn defaults_follow_bulwark() {
        let a = A11y::from(&settings(json!({})), System::default());
        assert!(!a.more_contrast && !a.reduce_motion && !a.large_targets && !a.underline_links);
        assert_eq!(a.focus_ring, 0);
        assert!(a.single_keys);
        assert_eq!(a.toast_secs, 5);
        assert_eq!(a.unread_style, "count");
        assert!(a.mail_script().is_none());
        assert_eq!(a.mail_css(true), "");
        // "system" follows Windows
        let s = A11y::from(&settings(json!({})), System { high_contrast: true, reduce_motion: true, touch: true });
        assert!(s.more_contrast && s.reduce_motion && s.large_targets);
        let off = A11y::from(&settings(json!({"contrast": "normal", "reduceMotion": "off", "largeTargets": "off"})), System { high_contrast: true, reduce_motion: true, touch: true });
        assert!(!off.more_contrast && !off.reduce_motion && !off.large_targets);
        assert!(DEFAULTS.iter().any(|d| *d == ("focusRing", "off")));
        assert!(reset_keys().contains(&"fontSize") && !reset_keys().contains(&"doNotDisturb"));
    }

    #[test]
    fn mail_css_and_override() {
        let a = A11y::from(&settings(json!({"underlineLinks": true, "readingWidth": "comfortable", "letterSpacing": "wide"})), System::default());
        let css = a.mail_css(false);
        assert!(css.contains("text-decoration-line:underline"));
        assert!(css.contains("max-width:80ch"));
        assert!(css.contains("letter-spacing:0.05em"));
        let o = A11y::from(&settings(json!({"mailOverrideStyles": "high-contrast", "contrast": "more", "mailMinFontSize": 16})), System::default());
        assert!(o.override_active());
        let css = o.mail_css(true);
        assert!(css.contains("color:#ffffff") && css.contains("max(calc(0.875rem * 1), 16px)"));
        // override sizes come from CSS, so no sizing script
        assert!(o.mail_script().is_none());
        let n = A11y::from(&settings(json!({"mailOverrideStyles": "high-contrast", "contrast": "normal"})), System { high_contrast: true, ..Default::default() });
        assert!(!n.override_active());
        let s = A11y::from(&settings(json!({"mailTextScale": 1.25})), System::default());
        assert!(s.mail_script().unwrap().contains("f=1.25"));
        let f = A11y::from(&settings(json!({"fontSize": "large"})), System::default());
        assert!((f.text_factor() - 1.125).abs() < 1e-6);
    }

    #[test]
    fn cvd_palette() {
        assert_eq!(cvd_color(0xef4444), 0xD55E00); // red
        assert_eq!(cvd_color(0xf97316), 0xE69F00); // orange
        assert_eq!(cvd_color(0xeab308), 0xF0E442); // yellow
        assert_eq!(cvd_color(0x22c55e), 0x009E73); // green
        assert_eq!(cvd_color(0x3b82f6), 0x0072B2); // blue
        assert_eq!(cvd_color(0xa855f7), 0xCC79A7); // purple
        assert_eq!(cvd_color(0x9ca3af), 0x999999); // grey
    }
}
