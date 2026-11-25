//! Files depth, part 2 (G-013..015): how the folder on screen is shown and looked into.
//!
//! * Settings > Files (S-keyless-054..060): Bulwark keeps one synced object `filesSettings`
//!   (lib/files-settings.ts); the keyless schema rows edit its fields as `files:<field>`.
//! * The listing (Bulwark file-browser.tsx displayResources): hidden dot-files left out unless
//!   shown, a search of the folder on screen (not of its subfolders), folders first in both
//!   directions, then name (natural, case and accents ignored), size or date, ties by name. The
//!   column heads change the order for the session (handleSort); a new default applies at once.
//! * List and grid views with file-type icons and image thumbnails (file-views.tsx,
//!   file-item-parts.tsx): a thumbnail is asked for by the row or tile once it is drawn (the list
//!   and the grid create only what is in view), fetched and scaled down off the UI thread and
//!   kept in a bounded cache keyed by blob id (blobs never change).
//! * The details panel (file-details-panel.tsx) and the in-app preview of images, text and .eml
//!   (file-preview-modal.tsx, image-preview-modal.tsx, eml-preview.tsx). PDF, audio and video
//!   open in the default app (D-M5-15). An .eml is parsed by the server (Email/parse), its body
//!   sanitised like a message's and drawn by the light renderer (no script, no network).
use crate::jmap::Client;
use crate::{AppWindow, FilesV, Pd};
use serde_json::{json, Map, Value};
use slint::{ComponentHandle, Model, Rgba8Pixel, SharedPixelBuffer};
use std::cell::RefCell;
use std::cmp::Ordering;
use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::atomic::{AtomicBool, Ordering as AO};
use std::sync::{Arc, Mutex};

// ---------------------------------------------------------------- settings (pure)

/// The synced settings key (Bulwark settings-store `filesSettings`).
pub const SETTINGS_KEY: &str = "filesSettings";
/// The details panel stays open on this device (Bulwark localStorage `files-show-details`).
pub const DETAILS_KEY: &str = "filesShowDetails";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SortKey { Name, Size, Modified }

impl SortKey {
    pub fn as_str(self) -> &'static str { match self { SortKey::Name => "name", SortKey::Size => "size", SortKey::Modified => "modified" } }
    pub fn parse(s: &str) -> Option<SortKey> { match s { "name" => Some(SortKey::Name), "size" => Some(SortKey::Size), "modified" => Some(SortKey::Modified), _ => None } }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Dir { Asc, Desc }

impl Dir {
    pub fn as_str(self) -> &'static str { if self == Dir::Asc { "asc" } else { "desc" } }
}

/// Bulwark FilesSettings.
#[derive(Clone, Debug, PartialEq)]
pub struct FilesSettings {
    pub grid: bool,
    pub show_icons: bool,
    pub colored_icons: bool,
    pub sort_key: SortKey,
    pub sort_dir: Dir,
    pub show_hidden: bool,
    pub show_thumbnails: bool,
}

/// Bulwark DEFAULT_FILES_SETTINGS.
impl Default for FilesSettings {
    fn default() -> Self {
        FilesSettings { grid: false, show_icons: true, colored_icons: true, sort_key: SortKey::Name, sort_dir: Dir::Asc, show_hidden: false, show_thumbnails: true }
    }
}

/// The values each field may take (Bulwark CHOICES).
fn choices(field: &str) -> Option<Vec<Value>> {
    Some(match field {
        "defaultViewMode" => vec![json!("list"), json!("grid")],
        "defaultSortKey" => vec![json!("name"), json!("size"), json!("modified")],
        "defaultSortDir" => vec![json!("asc"), json!("desc")],
        "showIcons" | "coloredIcons" | "showHiddenFiles" | "showThumbnails" => vec![json!(true), json!(false)],
        _ => return None,
    })
}

const FIELDS: [&str; 7] = ["defaultViewMode", "showIcons", "coloredIcons", "defaultSortKey", "defaultSortDir", "showHiddenFiles", "showThumbnails"];

impl FilesSettings {
    /// Bulwark normalizeFilesSettings: known keys with valid values, the rest default.
    pub fn normalize(v: Option<&Value>) -> FilesSettings {
        let mut out = FilesSettings::default();
        let Some(o) = v.and_then(|v| v.as_object()) else { return out };
        for f in FIELDS {
            let Some(x) = o.get(f) else { continue };
            if !choices(f).is_some_and(|c| c.contains(x)) { continue; }
            out.apply(f, x);
        }
        out
    }

    fn apply(&mut self, field: &str, x: &Value) {
        let b = x.as_bool().unwrap_or(false);
        match field {
            "defaultViewMode" => self.grid = x == "grid",
            "showIcons" => self.show_icons = b,
            "coloredIcons" => self.colored_icons = b,
            "defaultSortKey" => self.sort_key = SortKey::parse(x.as_str().unwrap_or("")).unwrap_or(SortKey::Name),
            "defaultSortDir" => self.sort_dir = if x == "desc" { Dir::Desc } else { Dir::Asc },
            "showHiddenFiles" => self.show_hidden = b,
            "showThumbnails" => self.show_thumbnails = b,
            _ => {}
        }
    }

    pub fn field(&self, field: &str) -> Value {
        match field {
            "defaultViewMode" => json!(if self.grid { "grid" } else { "list" }),
            "showIcons" => json!(self.show_icons),
            "coloredIcons" => json!(self.colored_icons),
            "defaultSortKey" => json!(self.sort_key.as_str()),
            "defaultSortDir" => json!(self.sort_dir.as_str()),
            "showHiddenFiles" => json!(self.show_hidden),
            "showThumbnails" => json!(self.show_thumbnails),
            _ => Value::Null,
        }
    }

    pub fn to_json(&self) -> Value {
        Value::Object(FIELDS.iter().map(|f| (f.to_string(), self.field(f))).collect())
    }

    pub fn from_prefs(p: &Map<String, Value>) -> FilesSettings { FilesSettings::normalize(p.get(SETTINGS_KEY)) }
}

/// The settings as saved now.
pub fn settings() -> FilesSettings { FilesSettings::from_prefs(&crate::settings::Settings::load().prefs) }

/// Settings > Files rows, keyless in the extracted schema (S-keyless-054..060), by label.
pub fn row_key(label: &str) -> Option<&'static str> {
    Some(match label {
        "Show files as" => "files:defaultViewMode",
        "Default sort" => "files:defaultSortKey",
        "Sort direction" => "files:defaultSortDir",
        "Show file icons" => "files:showIcons",
        "Colored icons" => "files:coloredIcons",
        "Show thumbnails" => "files:showThumbnails",
        "Show hidden files" => "files:showHiddenFiles",
        _ => return None,
    })
}

/// The options of the select rows among them (Bulwark settings-schema page files).
pub fn row_options(key: &str) -> Option<Vec<(Value, &'static str)>> {
    Some(match key {
        "files:defaultViewMode" => vec![(json!("list"), "List"), (json!("grid"), "Grid")],
        "files:defaultSortKey" => vec![(json!("name"), "Name"), (json!("size"), "Size"), (json!("modified"), "Modified")],
        "files:defaultSortDir" => vec![(json!("asc"), "Ascending"), (json!("desc"), "Descending")],
        _ => return None,
    })
}

/// The current value of a `files:<field>` row.
pub fn row_value(prefs: &Map<String, Value>, key: &str) -> Value {
    FilesSettings::from_prefs(prefs).field(key.strip_prefix("files:").unwrap_or(key))
}

/// Change one field of the object in `prefs` (the others are kept, normalised); a value Bulwark
/// would not take changes nothing. Returns whether it changed.
pub fn set_field(prefs: &mut Map<String, Value>, field: &str, v: &Value) -> bool {
    let field = field.strip_prefix("files:").unwrap_or(field);
    if !choices(field).is_some_and(|c| c.contains(v)) { return false; }
    let mut s = FilesSettings::from_prefs(prefs);
    s.apply(field, v);
    prefs.insert(SETTINGS_KEY.into(), s.to_json());
    true
}

// ---------------------------------------------------------------- listing (pure)

/// One listed item, as the order sees it.
#[derive(Clone, Debug)]
pub struct Entry {
    pub name: String,
    pub dir: bool,
    pub size: u64,
    /// ms since the epoch, 0 when unknown (Bulwark `new Date(x || 0)`)
    pub modified: i64,
    /// lower case without accents: the base letters localeCompare's `sensitivity: "base"` compares
    fold: String,
}

impl Entry {
    pub fn new(name: &str, dir: bool, size: u64, modified: i64) -> Entry {
        Entry { name: name.to_string(), dir, size, modified, fold: fold(name) }
    }
}

/// Lower case, accents dropped: "Été" -> "ete".
pub fn fold(s: &str) -> String {
    let lower = s.to_lowercase();
    if lower.is_ascii() { return lower; }
    let nfd = icu_normalizer::DecomposingNormalizerBorrowed::new_nfd().normalize(&lower).into_owned();
    nfd.chars().filter(|c| !is_mark(*c)).collect()
}

fn is_mark(c: char) -> bool {
    matches!(c as u32, 0x0300..=0x036F | 0x1AB0..=0x1AFF | 0x1DC0..=0x1DFF | 0x20D0..=0x20FF | 0xFE20..=0xFE2F)
}

/// Where a character sorts in a collation's primary order: spaces, then punctuation and
/// symbols, then digits, then letters (as ICU's root order does), then by code point.
fn weight(c: char) -> (u8, u32) {
    let class = if c.is_whitespace() { 0 } else if c.is_ascii_punctuation() || (!c.is_alphanumeric() && !c.is_ascii_digit()) { 1 } else if c.is_ascii_digit() { 2 } else { 3 };
    (class, c as u32)
}

/// localeCompare with `numeric: true`: runs of digits compare by their value ("2" < "10").
pub fn natural(a: &str, b: &str) -> Ordering {
    let (mut x, mut y) = (a.chars().peekable(), b.chars().peekable());
    loop {
        match (x.peek().copied(), y.peek().copied()) {
            (None, None) => return Ordering::Equal,
            (None, Some(_)) => return Ordering::Less,
            (Some(_), None) => return Ordering::Greater,
            (Some(p), Some(q)) if p.is_ascii_digit() && q.is_ascii_digit() => {
                let mut da = String::new();
                while let Some(c) = x.peek().copied().filter(|c| c.is_ascii_digit()) { da.push(c); x.next(); }
                let mut db = String::new();
                while let Some(c) = y.peek().copied().filter(|c| c.is_ascii_digit()) { db.push(c); y.next(); }
                let (ta, tb) = (da.trim_start_matches('0'), db.trim_start_matches('0'));
                let o = ta.len().cmp(&tb.len()).then_with(|| ta.cmp(tb));
                if o != Ordering::Equal { return o; }
            }
            (Some(p), Some(q)) => {
                let o = weight(p).cmp(&weight(q));
                if o != Ordering::Equal { return o; }
                x.next();
                y.next();
            }
        }
    }
}

/// The tie-break, localeCompare without options: base letters, then accents, then case (lower
/// case first), then the code points.
pub fn tie(a: &Entry, b: &Entry) -> Ordering {
    a.fold.cmp(&b.fold)
        .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        .then_with(|| {
            // lower case before upper case at the first place they differ
            for (p, q) in a.name.chars().zip(b.name.chars()) {
                if p != q && p.to_lowercase().eq(q.to_lowercase()) { return if p.is_lowercase() { Ordering::Less } else { Ordering::Greater }; }
            }
            Ordering::Equal
        })
        .then_with(|| a.name.cmp(&b.name))
}

/// Bulwark's comparator: folders always first, then the key, then the name; the direction
/// turns the key and the tie-break round, never the folders.
pub fn compare(a: &Entry, b: &Entry, key: SortKey, dir: Dir) -> Ordering {
    if a.dir != b.dir { return if a.dir { Ordering::Less } else { Ordering::Greater }; }
    let mut c = match key {
        SortKey::Name => natural(&a.fold, &b.fold),
        SortKey::Size => a.size.cmp(&b.size),
        SortKey::Modified => a.modified.cmp(&b.modified),
    };
    if c == Ordering::Equal { c = tie(a, b); }
    if dir == Dir::Desc { c.reverse() } else { c }
}

/// A dot-file or dot-folder (Bulwark `name.startsWith(".")`).
pub fn is_hidden(name: &str) -> bool { name.starts_with('.') }

/// Bulwark's search: the name holds the query, ignoring case.
pub fn matches(name: &str, query: &str) -> bool {
    query.is_empty() || name.to_lowercase().contains(&query.to_lowercase())
}

/// The order of the listed items: indices into `items`, filtered and sorted.
pub fn arrange(items: &[Entry], show_hidden: bool, query: &str, key: SortKey, dir: Dir) -> Vec<usize> {
    let mut idx: Vec<usize> = (0..items.len()).filter(|&i| (show_hidden || !is_hidden(&items[i].name)) && matches(&items[i].name, query)).collect();
    idx.sort_by(|&i, &j| compare(&items[i], &items[j], key, dir));
    idx
}

/// A click on a column head (Bulwark handleSort): the same column turns round, another one
/// starts ascending for the name and descending for size and date.
pub fn sort_click(cur: (SortKey, Dir), clicked: SortKey) -> (SortKey, Dir) {
    if cur.0 == clicked { (clicked, if cur.1 == Dir::Asc { Dir::Desc } else { Dir::Asc }) }
    else { (clicked, if clicked == SortKey::Name { Dir::Asc } else { Dir::Desc }) }
}

/// A node's ISO date in ms (0 when missing or unreadable).
pub fn iso_ms(v: &Value) -> i64 {
    v.as_str().and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok()).map(|t| t.timestamp_millis()).unwrap_or(0)
}

/// The previous and next file of the listing (Bulwark previewNeighbours).
pub fn neighbours(order: &[String], cur: &str) -> (Option<String>, Option<String>) {
    let Some(i) = order.iter().position(|k| k == cur) else { return (None, None) };
    (i.checked_sub(1).map(|j| order[j].clone()), order.get(i + 1).cloned())
}

// ---------------------------------------------------------------- icons (pure)

/// The extension, lower case ("" without one; ".bashrc" has none).
fn ext_of(name: &str) -> String {
    match name.rsplit_once('.') { Some((stem, e)) if !stem.is_empty() => e.to_lowercase(), _ => String::new() }
}

const IMAGE_EXT: &[&str] = &["jpg", "jpeg", "png", "gif", "svg", "webp", "bmp", "ico", "avif"];
const TEXT_ICON_EXT: &[&str] = &["txt", "md", "markdown", "json", "xml", "html", "htm", "css", "js", "ts", "jsx", "tsx", "py", "rb", "java", "c", "cpp", "h", "hpp", "go", "rs",
    "sh", "bash", "zsh", "yaml", "yml", "toml", "ini", "cfg", "conf", "env", "log", "csv", "sql", "graphql", "vue", "svelte", "astro", "php", "pl", "swift", "kt", "scala", "r", "lua", "vim"];

/// Bulwark isImageFile (file-icons.tsx).
pub fn is_image_name(name: &str) -> bool { IMAGE_EXT.contains(&ext_of(name).as_str()) }

/// Icon kind and its colour (Bulwark fileIconFor, the same order and Tailwind colours).
pub fn icon_kind(name: &str) -> (&'static str, (u8, u8, u8)) {
    let e = ext_of(name);
    let e = e.as_str();
    let base = name.to_lowercase();
    let has = |l: &[&str]| l.contains(&e);
    if has(&["svg", "ai", "eps", "ps", "sketch", "fig", "xd", "gvdesign"]) { return ("vector", (0xf9, 0x73, 0x16)); }
    if has(&["obj", "fbx", "gltf", "glb", "stl", "3mf", "step", "stp", "iges", "igs", "blend", "3ds", "dae", "usdz", "usd", "usda", "usdc", "ply", "wrl", "c4d", "max", "ma", "mb", "dwg", "dxf"]) { return ("3d", (0x06, 0xb6, 0xd4)); }
    if has(IMAGE_EXT) { return ("image", (0x10, 0xb9, 0x81)); }
    if has(&["mp3", "wav", "ogg", "flac", "aac", "m4a", "wma", "opus"]) { return ("audio", (0xa8, 0x55, 0xf7)); }
    if has(&["mp4", "webm", "ogv", "mov", "avi", "mkv", "m4v"]) { return ("video", (0xec, 0x48, 0x99)); }
    if has(&["zip", "rar", "7z", "tar", "gz", "bz2", "xz", "zst", "lz", "lzma", "tgz", "tbz2", "txz", "cab", "iso", "img"]) { return ("archive", (0xd9, 0x77, 0x06)); }
    if has(&["exe", "msi", "dmg", "app", "appimage", "deb", "rpm", "snap", "flatpak", "bat", "cmd", "com", "scr", "ps1", "apk", "ipa", "jar", "run"]) { return ("exec", (0xef, 0x44, 0x44)); }
    if has(&["xls", "xlsx", "ods", "numbers", "tsv"]) { return ("sheet", (0x16, 0xa3, 0x4a)); }
    if has(&["ppt", "pptx", "odp", "key"]) { return ("slides", (0xea, 0x58, 0x0c)); }
    if has(&["doc", "docx", "odt", "rtf"]) { return ("doc", (0x25, 0x63, 0xeb)); }
    if has(&["ttf", "otf", "woff", "woff2", "eot"]) { return ("font", (0x63, 0x66, 0xf1)); }
    if has(&["db", "sqlite", "sqlite3", "mdb", "accdb"]) { return ("db", (0x64, 0x74, 0x8b)); }
    if e == "pdf" { return ("pdf", (0xdc, 0x26, 0x26)); }
    if has(TEXT_ICON_EXT) || ["dockerfile", "makefile", "readme", "license", "changelog"].contains(&base.as_str()) { return ("code", (0xca, 0x8a, 0x04)); }
    ("file", (0, 0, 0))
}

/// The stroke path (24 x 24) drawn for an icon kind.
pub fn icon_path(kind: &str) -> &'static str {
    match kind {
        "folder" => "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z",
        "image" => "M3 3h18v18H3z M8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z M21 15l-5-5L5 21",
        "vector" => "M12 19l7-7 3 3-7 7-3-3z M18 13l-1.5-7.5L2 2l3.5 14.5L13 18l5-5z M2 2l7.586 7.586 M11 13a2 2 0 1 0 0-4 2 2 0 0 0 0 4z",
        "3d" => "M21 16V8l-9-5-9 5v8l9 5 9-5z M3.27 6.96L12 12.01l8.73-5.05 M12 22.08V12",
        "audio" => "M9 18V5l12-2v13 M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0z M21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z",
        "video" => "M23 7l-7 5 7 5V7z M1 5h15v14H1z",
        "archive" => "M21 8v13H3V8 M1 3h22v5H1z M10 12h4",
        "exec" => "M4 17l6-6-6-6 M12 19h8",
        "sheet" => "M3 3h18v18H3z M3 9h18 M3 15h18 M9 3v18",
        "slides" => "M2 3h20v13H2z M12 16v5 M8 21h8",
        "doc" | "pdf" => "M14 2H6v20h12V8z M14 2v6h6 M8 13h8 M8 17h8 M8 9h2",
        "font" => "M4 7V4h16v3 M9 20h6 M12 4v16",
        "db" => "M12 8c4.97 0 9-1.34 9-3s-4.03-3-9-3-9 1.34-9 3 4.03 3 9 3z M21 12c0 1.66-4 3-9 3s-9-1.34-9-3 M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5",
        "code" => "M16 18l6-6-6-6 M8 6l-6 6 6 6",
        _ => "M14 2H6v20h12V8z M14 2v6h6",
    }
}

// ---------------------------------------------------------------- preview kinds and caps (pure)

/// Bulwark FilePreviewKind (lib/file-preview.ts); SVG is split from the raster images, as it is
/// drawn by another decoder.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kind { Image, Svg, Html, Eml, Pdf, Audio, Video, Markdown, Text, Unsupported }

const PREVIEW_IMAGE_EXT: &[&str] = &["jpg", "jpeg", "png", "gif", "webp", "svg", "avif", "bmp", "ico"];
const AUDIO_EXT: &[&str] = &["mp3", "wav", "ogg", "m4a", "flac", "aac", "opus"];
const VIDEO_EXT: &[&str] = &["mp4", "webm", "ogv", "mov", "m4v", "avi", "mkv"];
const TEXT_EXT: &[&str] = &["txt", "text", "log", "csv", "json", "xml", "css", "js", "mjs", "cjs", "ts", "tsx", "jsx",
    "yaml", "yml", "toml", "ini", "cfg", "conf", "env", "sql", "graphql", "html", "htm", "md", "markdown"];
const TEXT_MIME: &[&str] = &["application/json", "application/ld+json", "application/xml", "application/javascript", "application/x-javascript", "application/typescript"];
/// The raster formats the `image` crate here decodes.
const DECODABLE: &[&str] = &["jpg", "jpeg", "png", "gif", "webp", "bmp", "ico"];
const DECODABLE_MIME: &[&str] = &["image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/bmp", "image/x-icon", "image/vnd.microsoft.icon"];

fn essence(mime: &str) -> String { mime.split(';').next().unwrap_or("").trim().to_lowercase() }

/// Bulwark getFilePreviewKind, by type first, then by extension.
pub fn preview_kind(name: &str, mime: &str) -> Kind {
    let (e, m) = (ext_of(name), essence(mime));
    let (e, m) = (e.as_str(), m.as_str());
    if m == "image/svg+xml" || e == "svg" { return Kind::Svg; }
    if m.starts_with("image/") || PREVIEW_IMAGE_EXT.contains(&e) { return Kind::Image; }
    if m == "text/html" || m == "application/xhtml+xml" || e == "html" || e == "htm" { return Kind::Html; }
    if m == "message/rfc822" || e == "eml" { return Kind::Eml; }
    if m == "application/pdf" || e == "pdf" { return Kind::Pdf; }
    if m.starts_with("audio/") || AUDIO_EXT.contains(&e) { return Kind::Audio; }
    if m.starts_with("video/") || VIDEO_EXT.contains(&e) { return Kind::Video; }
    if e == "md" || e == "markdown" { return Kind::Markdown; }
    if m.starts_with("text/") || TEXT_MIME.contains(&m) || TEXT_EXT.contains(&e) { return Kind::Text; }
    Kind::Unsupported
}

/// The largest file of each kind the preview loads (bigger ones say so, with Open and Download).
pub const MAX_IMAGE_BYTES: u64 = 50 * 1024 * 1024;
pub const MAX_SVG_BYTES: u64 = 5 * 1024 * 1024;
pub const MAX_TEXT_BYTES: u64 = 20 * 1024 * 1024;
pub const MAX_HTML_BYTES: u64 = 5 * 1024 * 1024;
pub const MAX_EML_BYTES: u64 = 50 * 1024 * 1024;
/// The text shown of a text file; the rest is left to Download.
pub const TEXT_SHOW_BYTES: usize = 256 * 1024;
/// A picture is refused above this many pixels, read from its header before it is decoded.
pub const MAX_PIXELS: u64 = 50_000_000;
/// The preview's longest edge in pixels (it is drawn to fit the window).
pub const SHOW_EDGE: u32 = 3840;
/// Thumbnails are at most this big (Bulwark skips images over 15 MB, MAX_THUMBNAIL_BYTES).
pub const MAX_THUMB_BYTES: u64 = 15 * 1024 * 1024;
pub const THUMB_EDGE: u32 = 192;

/// What opening a file does.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Plan {
    /// shown in the app: the kind and whether it is within its cap
    Preview(Kind, bool),
    /// PDF, audio, video and everything else: the default app (D-M5-15)
    External(Kind),
}

pub fn plan(name: &str, mime: &str, size: u64) -> Plan {
    let k = preview_kind(name, mime);
    let cap = match k {
        Kind::Image => {
            let (e, m) = (ext_of(name), essence(mime));
            if !(DECODABLE.contains(&e.as_str()) || DECODABLE_MIME.contains(&m.as_str())) { return Plan::External(k); }
            MAX_IMAGE_BYTES
        }
        Kind::Svg => MAX_SVG_BYTES,
        Kind::Text | Kind::Markdown => MAX_TEXT_BYTES,
        Kind::Html => MAX_HTML_BYTES,
        Kind::Eml => MAX_EML_BYTES,
        Kind::Pdf | Kind::Audio | Kind::Video | Kind::Unsupported => return Plan::External(k),
    };
    Plan::Preview(k, size <= cap)
}

/// Bulwark canShowThumbnail: an image file with a blob, within the thumbnail cap.
pub fn can_thumb(name: &str, mime: &str, size: u64, has_blob: bool, on: bool) -> bool {
    on && has_blob && is_image_name(name) && size <= MAX_THUMB_BYTES && matches!(plan(name, mime, size), Plan::Preview(Kind::Image | Kind::Svg, true))
}

// ---------------------------------------------------------------- decoding (pure)

/// Windows-1252 at 0x80..0x9F (the rest is Latin-1).
const CP1252: [u16; 32] = [0x20AC, 0x81, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x8D, 0x017D, 0x8F,
    0x90, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x9D, 0x017E, 0x0178];

fn cp1252(b: &[u8]) -> String {
    b.iter().map(|&x| if (0x80..0xA0).contains(&x) { char::from_u32(CP1252[(x - 0x80) as usize] as u32).unwrap_or('\u{FFFD}') } else { x as char }).collect()
}

fn utf16(b: &[u8], le: bool) -> String {
    let units: Vec<u16> = b.chunks_exact(2).map(|p| if le { u16::from_le_bytes([p[0], p[1]]) } else { u16::from_be_bytes([p[0], p[1]]) }).collect();
    char::decode_utf16(units).map(|r| r.unwrap_or('\u{FFFD}')).collect()
}

/// The charset a type names, lower case.
fn charset(mime: &str) -> Option<String> {
    mime.split(';').skip(1).find_map(|p| {
        let (k, v) = p.split_once('=')?;
        (k.trim().eq_ignore_ascii_case("charset")).then(|| v.trim().trim_matches('"').to_lowercase())
    })
}

/// A text file's text, at most `cap` bytes of it: a byte order mark first, then the type's
/// charset, then UTF-8 when the bytes are UTF-8, else Windows-1252. Line ends become `\n`;
/// control characters other than tab and line ends are shown as U+FFFD. Returns the text and
/// whether it was cut.
pub fn decode_text(bytes: &[u8], mime: &str, cap: usize) -> (String, bool) {
    let cut = bytes.len() > cap;
    let b = &bytes[..bytes.len().min(cap)];
    let cs = charset(mime);
    let text = if let Some(r) = b.strip_prefix(&[0xEF, 0xBB, 0xBF]) {
        utf8_prefix(r)
    } else if let Some(r) = b.strip_prefix(&[0xFF, 0xFE]) {
        utf16(r, true)
    } else if let Some(r) = b.strip_prefix(&[0xFE, 0xFF]) {
        utf16(r, false)
    } else {
        match cs.as_deref() {
            Some("utf-16le" | "utf-16") => utf16(b, true),
            Some("utf-16be") => utf16(b, false),
            Some("windows-1252" | "iso-8859-1" | "latin1" | "us-ascii" | "ascii" | "cp1252") => cp1252(b),
            _ => match std::str::from_utf8(b) {
                Ok(s) => s.to_string(),
                // cut in the middle of a character: the whole characters before it
                Err(e) if e.error_len().is_none() => String::from_utf8_lossy(&b[..e.valid_up_to()]).into_owned(),
                Err(_) => cp1252(b),
            },
        }
    };
    let mut out = String::with_capacity(text.len());
    let mut chars = text.chars().peekable();
    while let Some(c) = chars.next() {
        match c {
            '\r' => { if chars.peek() == Some(&'\n') { chars.next(); } out.push('\n'); }
            '\n' | '\t' => out.push(c),
            c if c.is_control() => out.push('\u{FFFD}'),
            c => out.push(c),
        }
    }
    (out, cut)
}

fn utf8_prefix(b: &[u8]) -> String {
    match std::str::from_utf8(b) {
        Ok(s) => s.to_string(),
        Err(e) if e.error_len().is_none() => String::from_utf8_lossy(&b[..e.valid_up_to()]).into_owned(),
        Err(_) => String::from_utf8_lossy(b).into_owned(),
    }
}

/// Decode a raster image within the limits and scale it to fit `edge` (never up).
pub fn decode_image(bytes: &[u8], edge: u32) -> Result<SharedPixelBuffer<Rgba8Pixel>, String> {
    let open = || image::ImageReader::new(std::io::Cursor::new(bytes)).with_guessed_format().map_err(|e| e.to_string());
    let (w, h) = open()?.into_dimensions().map_err(|e| e.to_string())?;
    if w == 0 || h == 0 { return Err("The picture is empty.".into()); }
    if (w as u64) * (h as u64) > MAX_PIXELS { return Err("The picture is too large to preview.".into()); }
    let mut r = open()?;
    let mut lim = image::Limits::default();
    lim.max_image_width = Some(w);
    lim.max_image_height = Some(h);
    lim.max_alloc = Some(512 * 1024 * 1024);
    r.limits(lim);
    let img = r.decode().map_err(|e| e.to_string())?;
    let img = if w > edge || h > edge { img.thumbnail(edge, edge) } else { img };
    let t = img.to_rgba8();
    Ok(SharedPixelBuffer::clone_from_slice(t.as_raw(), t.width(), t.height()))
}

/// Draw an SVG to fit `edge`: no file or network reference is followed (only data: images).
pub fn render_svg(bytes: &[u8], edge: u32) -> Result<SharedPixelBuffer<Rgba8Pixel>, String> {
    use resvg::{tiny_skia, usvg};
    let mut opts = usvg::Options::default();
    opts.resources_dir = None;
    opts.image_href_resolver = usvg::ImageHrefResolver {
        resolve_data: usvg::ImageHrefResolver::default_data_resolver(),
        resolve_string: Box::new(|_, _| None),
    };
    let tree = usvg::Tree::from_data(bytes, &opts).map_err(|e| e.to_string())?;
    let s = tree.size();
    let longest = s.width().max(s.height());
    if !(longest > 0.0) { return Err("The picture is empty.".into()); }
    let k = edge as f32 / longest;
    let (w, h) = (((s.width() * k).round() as u32).clamp(1, edge), ((s.height() * k).round() as u32).clamp(1, edge));
    let mut pixmap = tiny_skia::Pixmap::new(w, h).ok_or("The picture is empty.")?;
    resvg::render(&tree, tiny_skia::Transform::from_scale(w as f32 / s.width(), h as f32 / s.height()), &mut pixmap.as_mut());
    let rgba: Vec<u8> = pixmap.pixels().iter().flat_map(|p| { let c = p.demultiply(); [c.red(), c.green(), c.blue(), c.alpha()] }).collect();
    Ok(SharedPixelBuffer::clone_from_slice(&rgba, w, h))
}

/// A thumbnail of an image file's bytes.
pub fn thumbnail(name: &str, mime: &str, bytes: &[u8]) -> Option<SharedPixelBuffer<Rgba8Pixel>> {
    if preview_kind(name, mime) == Kind::Svg { render_svg(bytes, THUMB_EDGE).ok() } else { decode_image(bytes, THUMB_EDGE).ok() }
}

// ---------------------------------------------------------------- thumbnail cache (pure)

/// A cache bounded by entry count and bytes; the least recently used entry goes first.
pub struct Lru<V> {
    max_items: usize,
    max_bytes: usize,
    bytes: usize,
    tick: u64,
    map: HashMap<String, (u64, usize, V)>,
}

impl<V: Clone> Lru<V> {
    pub fn new(max_items: usize, max_bytes: usize) -> Self { Lru { max_items, max_bytes, bytes: 0, tick: 0, map: HashMap::new() } }
    pub fn len(&self) -> usize { self.map.len() }
    pub fn bytes(&self) -> usize { self.bytes }
    pub fn contains(&self, k: &str) -> bool { self.map.contains_key(k) }
    pub fn get(&mut self, k: &str) -> Option<V> {
        self.tick += 1;
        let t = self.tick;
        self.map.get_mut(k).map(|e| { e.0 = t; e.2.clone() })
    }
    pub fn insert(&mut self, k: String, v: V, size: usize) {
        if size > self.max_bytes { return; }
        self.tick += 1;
        if let Some(old) = self.map.insert(k, (self.tick, size, v)) { self.bytes -= old.1; }
        self.bytes += size;
        while self.map.len() > self.max_items || self.bytes > self.max_bytes {
            let Some(oldest) = self.map.iter().min_by_key(|e| e.1 .0).map(|e| e.0.clone()) else { break };
            if let Some(e) = self.map.remove(&oldest) { self.bytes -= e.1; }
        }
    }
}

pub const THUMB_ITEMS: usize = 400;
pub const THUMB_BYTES: usize = 48 * 1024 * 1024;

// ---------------------------------------------------------------- details (pure)

/// Bulwark kindOf: "Folder", "PDF file", else the type or "File".
pub fn kind_of(name: &str, dir: bool, mime: &str) -> String {
    if dir { return "Folder".into(); }
    let e = ext_of(name);
    if !e.is_empty() { return format!("{} file", e.to_uppercase()); }
    if mime.is_empty() { "File".into() } else { mime.into() }
}

/// The Type row: the file's type, else its kind.
pub fn type_row(name: &str, dir: bool, mime: &str) -> String {
    if dir || mime.is_empty() { kind_of(name, dir, mime) } else { mime.into() }
}

/// The Sharing row.
pub fn sharing(shared_in: bool, owner: Option<&str>, shared_with: usize) -> String {
    if shared_in { return owner.filter(|o| !o.is_empty()).map(|o| format!("Shared by {o}")).unwrap_or_else(|| "Shared with me".into()); }
    match shared_with { 0 => "Only you".into(), 1 => "Shared with 1 person".into(), n => format!("Shared with {n} people") }
}

/// "No folders, no files", "1 folder, 3 files" (Bulwark folder_summary).
pub fn folder_summary(folders: usize, files: usize) -> String {
    let f = match folders { 0 => "No folders".to_string(), 1 => "1 folder".into(), n => format!("{n} folders") };
    let g = match files { 0 => "no files".to_string(), 1 => "1 file".into(), n => format!("{n} files") };
    format!("{f}, {g}")
}

/// A path from the root names: "/" for the root, "/Documents/report.pdf".
pub fn path_of(names: &[String]) -> String {
    if names.is_empty() { "/".into() } else { format!("/{}", names.join("/")) }
}

/// A full date and time for the panel (Bulwark formatFullDate), in the list's style.
pub fn full_date(t: chrono::DateTime<chrono::Local>, clock: &str) -> String {
    t.format(&format!("%d %b %Y, {clock}")).to_string()
}

// ---------------------------------------------------------------- the view's state (UI thread)

struct View {
    sort: (SortKey, Dir),
    /// the saved default the order was last reset to
    seen: Option<(SortKey, Dir)>,
    query: String,
    query_cwd: Option<String>,
    /// the listed files in their order (no folders): the preview's previous and next
    order: Vec<String>,
    /// the settings at the last render
    fs: FilesSettings,
    thumbs: Lru<SharedPixelBuffer<Rgba8Pixel>>,
    inflight: HashSet<String>,
    failed: HashSet<String>,
    /// the file in the preview, and the generation of its load (a later one wins)
    preview: Option<String>,
    gen: u64,
    /// account id -> its name (the owner of a shared file)
    owners: HashMap<String, String>,
    client: Option<Arc<Client>>,
}

thread_local! {
    static VIEW: RefCell<View> = RefCell::new(View {
        sort: (SortKey::Name, Dir::Asc), seen: None, query: String::new(), query_cwd: None, order: vec![],
        fs: FilesSettings::default(), thumbs: Lru::new(THUMB_ITEMS, THUMB_BYTES), inflight: HashSet::new(), failed: HashSet::new(),
        preview: None, gen: 0, owners: HashMap::new(), client: None,
    });
}

/// The Files app was opened with this login: the owners of shared accounts, the client for
/// thumbnails and previews.
pub fn note_client(c: &Arc<Client>) {
    let owners: HashMap<String, String> = c.session["accounts"].as_object()
        .map(|o| o.iter().map(|(id, a)| (id.clone(), a["name"].as_str().unwrap_or("").to_string())).collect()).unwrap_or_default();
    VIEW.with(|v| { let mut v = v.borrow_mut(); v.owners = owners; v.client = Some(c.clone()); });
}

fn client() -> Option<Arc<Client>> { VIEW.with(|v| v.borrow().client.clone()) }

/// For files::render: the settings, the order and the search of the folder on screen (a search
/// ends when another folder opens, as in Bulwark).
pub fn begin_render(cwd: &str) -> (FilesSettings, (SortKey, Dir), String) {
    let fs = settings();
    VIEW.with(|v| {
        let mut v = v.borrow_mut();
        let d = (fs.sort_key, fs.sort_dir);
        if v.seen != Some(d) { v.seen = Some(d); v.sort = d; }
        if v.query_cwd.as_deref() != Some(cwd) { v.query_cwd = Some(cwd.to_string()); v.query.clear(); }
        v.fs = fs.clone();
        (fs, v.sort, v.query.clone())
    })
}

/// A node as an order entry; in the trash the date is when it was deleted.
pub fn entry(n: &Value, deleted_ms: Option<i64>) -> Entry {
    let dir = crate::files::is_dir(n);
    Entry::new(n["name"].as_str().unwrap_or(""), dir, if dir { 0 } else { n["size"].as_u64().unwrap_or(0) }, deleted_ms.unwrap_or_else(|| iso_ms(&n["modified"])))
}

/// The icon and thumbnail fields of a listed node's row.
pub fn decorate(row: &mut crate::FileRow, n: &Value, fs: &FilesSettings) {
    let name = n["name"].as_str().unwrap_or("");
    let dir = crate::files::is_dir(n);
    let (kind, (r, g, b)) = if dir { ("folder", (0, 0, 0)) } else { icon_kind(name) };
    row.icon = icon_path(kind).into();
    row.typed = kind != "file" && kind != "folder";
    row.tint = slint::Color::from_rgb_u8(r, g, b);
    let blob = n["blobId"].as_str().unwrap_or("");
    let can = !dir && can_thumb(name, &crate::files::mime(n), n["size"].as_u64().unwrap_or(0), !blob.is_empty(), fs.show_thumbnails);
    let cached = if can { VIEW.with(|v| v.borrow_mut().thumbs.get(blob)) } else { None };
    row.has_thumb = cached.is_some();
    row.want_thumb = can && cached.is_none() && VIEW.with(|v| !v.borrow().failed.contains(blob));
    row.thumb = cached.map(slint::Image::from_rgba8).unwrap_or_default();
}

/// After files::render: the view's flags, the order for the preview, the details panel.
pub fn end_render(u: &AppWindow, order: Vec<String>, folders: usize, unfiltered: usize) {
    let x = u.global::<FilesV>();
    let (fs, sort, query) = VIEW.with(|v| { let mut v = v.borrow_mut(); v.order = order; (v.fs.clone(), v.sort, v.query.clone()) });
    x.set_grid(fs.grid);
    x.set_show_icons(fs.show_icons);
    x.set_colored_icons(fs.colored_icons);
    x.set_sort_key(sort.0.as_str().into());
    x.set_sort_dir(sort.1.as_str().into());
    if x.get_query().as_str() != query { x.set_query(query.into()); }
    x.set_folder_count(folders as i32);
    x.set_unfiltered(unfiltered as i32);
    refresh_details(u);
    if x.get_p_open() { set_neighbours(u); }
}

// ---------------------------------------------------------------- thumbnails (fetch)

struct ThumbJob { c: Arc<Client>, owner: String, acc: String, blob: String, name: String, mime: String }

static QUEUE: Mutex<VecDeque<ThumbJob>> = Mutex::new(VecDeque::new());
static WORKING: AtomicBool = AtomicBool::new(false);

fn lock_queue() -> std::sync::MutexGuard<'static, VecDeque<ThumbJob>> { QUEUE.lock().unwrap_or_else(|e| e.into_inner()) }

/// A row or tile with an image came into view: fetch its thumbnail once.
pub fn want_thumb(u: &AppWindow, key: &str) {
    let Some(c) = client() else { return };
    let Some(n) = crate::files::node(key) else { return };
    let (name, mime, blob) = (n["name"].as_str().unwrap_or("").to_string(), crate::files::mime(&n), n["blobId"].as_str().unwrap_or("").to_string());
    let on = VIEW.with(|v| v.borrow().fs.show_thumbnails);
    if !can_thumb(&name, &mime, n["size"].as_u64().unwrap_or(0), !blob.is_empty(), on) { return; }
    let todo = VIEW.with(|v| {
        let mut v = v.borrow_mut();
        if v.thumbs.contains(&blob) || v.failed.contains(&blob) || v.inflight.contains(&blob) { return false; }
        v.inflight.insert(blob.clone());
        true
    });
    if !todo {
        // already here (the row was drawn before it came): show it
        if let Some(buf) = VIEW.with(|v| v.borrow_mut().thumbs.get(&blob)) { show_thumb(u, &blob, buf); }
        return;
    }
    lock_queue().push_back(ThumbJob { owner: c.state_key(), c, acc: crate::files::acc_of(&n), blob, name, mime });
    if !WORKING.swap(true, AO::SeqCst) {
        let w = u.as_weak();
        std::thread::spawn(move || thumb_worker(w));
    }
}

fn thumb_worker(w: slint::Weak<AppWindow>) {
    loop {
        // the newest first: what was scrolled to last is on screen
        let job = lock_queue().pop_back();
        let Some(j) = job else {
            WORKING.store(false, AO::SeqCst);
            // a job queued between the pop and the store: carry on unless another worker took it
            if lock_queue().is_empty() || WORKING.swap(true, AO::SeqCst) { return; }
            continue;
        };
        let buf = if crate::livesync::is_current(&j.owner) {
            j.c.download_from(&j.acc, &j.blob, &j.name, &j.mime).ok().and_then(|b| thumbnail(&j.name, &j.mime, &b))
        } else { None };
        let (owner, blob) = (j.owner, j.blob);
        let _ = w.upgrade_in_event_loop(move |u| thumb_done(&u, &owner, blob, buf));
    }
}

fn thumb_done(u: &AppWindow, owner: &str, blob: String, buf: Option<SharedPixelBuffer<Rgba8Pixel>>) {
    VIEW.with(|v| {
        let mut v = v.borrow_mut();
        v.inflight.remove(&blob);
        match &buf {
            Some(b) => { let size = (b.width() * b.height() * 4) as usize; v.thumbs.insert(blob.clone(), b.clone(), size); }
            None => { if v.failed.len() > 2000 { v.failed.clear(); } v.failed.insert(blob.clone()); }
        }
    });
    // another login's picture is cached under its blob id, never shown in this one's rows
    if !crate::livesync::is_current(owner) { return; }
    if let Some(b) = buf { show_thumb(u, &blob, b); } else { drop_want(u, &blob); }
}

/// The keys of the nodes whose content is `blob` (one pass over the nodes).
fn keys_with_blob(blob: &str) -> HashSet<String> {
    crate::files::with_nodes(|ns| ns.iter().filter(|n| n["blobId"].as_str() == Some(blob)).map(crate::files::key).collect())
}

/// Put a thumbnail into the rows (and the panel) that show `blob`.
fn show_thumb(u: &AppWindow, blob: &str, buf: SharedPixelBuffer<Rgba8Pixel>) {
    if !VIEW.with(|v| v.borrow().fs.show_thumbnails) { return; }
    let img = slint::Image::from_rgba8(buf);
    let keys = keys_with_blob(blob);
    let rows = u.get_files();
    for i in 0..rows.row_count() {
        let Some(mut r) = rows.row_data(i) else { continue };
        if r.folder || r.has_thumb || !keys.contains(r.id.as_str()) { continue; }
        r.thumb = img.clone();
        r.has_thumb = true;
        r.want_thumb = false;
        rows.set_row_data(i, r);
    }
    let x = u.global::<FilesV>();
    if x.get_details_open() && keys.contains(x.get_d_id().as_str()) {
        x.set_d_thumb(img);
        x.set_d_has_thumb(true);
    }
}

/// No thumbnail for `blob`: the rows stop asking.
fn drop_want(u: &AppWindow, blob: &str) {
    let keys = keys_with_blob(blob);
    let rows = u.get_files();
    for i in 0..rows.row_count() {
        let Some(mut r) = rows.row_data(i) else { continue };
        if r.want_thumb && keys.contains(r.id.as_str()) { r.want_thumb = false; rows.set_row_data(i, r); }
    }
}

// ---------------------------------------------------------------- the details panel

fn names_to(k: &str) -> Vec<String> {
    let mut names = vec![];
    let mut cur = k.to_string();
    let mut guard = 0;
    while !cur.is_empty() && guard < 64 {
        guard += 1;
        let Some(n) = crate::files::node(&cur) else { break };
        names.push(n["name"].as_str().unwrap_or("").to_string());
        cur = crate::files::parent_key(&n);
    }
    names.reverse();
    names
}

fn date_of(v: &Value, clock: &str) -> String {
    crate::mail::parse_time(v).map(|t| full_date(t, clock)).unwrap_or_default()
}

fn clock() -> &'static str {
    if crate::settings::Settings::load().str("timeFormat", "24h") == "12h" { "%-I:%M %p" } else { "%H:%M" }
}

/// Fill the panel for the selected item, or the folder on screen when none is selected.
pub fn refresh_details(u: &AppWindow) {
    let x = u.global::<FilesV>();
    if !x.get_details_open() { return; }
    let sel = u.global::<Pd>().get_file_sel().to_string();
    let listed = u.get_files();
    let shown = (0..listed.row_count()).any(|i| listed.row_data(i).is_some_and(|r| r.id.as_str() == sel));
    let ck = clock();
    match crate::files::node(&sel).filter(|_| shown) {
        Some(n) => {
            let name = n["name"].as_str().unwrap_or("").to_string();
            let dir = crate::files::is_dir(&n);
            let mime = if dir { String::new() } else { n["type"].as_str().or(n["mediaType"].as_str()).unwrap_or("").to_string() };
            let shared_in = n["_shared"] == true;
            let owner = VIEW.with(|v| v.borrow().owners.get(&crate::files::acc_of(&n)).cloned());
            let mut row = crate::FileRow::default();
            let fs = VIEW.with(|v| v.borrow().fs.clone());
            decorate(&mut row, &n, &fs);
            x.set_d_id(sel.clone().into());
            x.set_d_name(name.clone().into());
            x.set_d_folder(dir);
            x.set_d_icon(row.icon);
            x.set_d_tint(row.tint);
            x.set_d_typed(row.typed);
            x.set_d_has_thumb(row.has_thumb);
            x.set_d_thumb(row.thumb);
            if row.want_thumb { want_thumb(u, &sel); }
            x.set_d_kind(kind_of(&name, dir, &mime).into());
            x.set_d_type(type_row(&name, dir, &mime).into());
            x.set_d_size(if dir { "".into() } else { crate::human_size(n["size"].as_u64().unwrap_or(0)).into() });
            x.set_d_modified(date_of(&n["modified"], ck).into());
            x.set_d_created(date_of(&n["created"], ck).into());
            x.set_d_sharing(sharing(shared_in, owner.as_deref(), n["shareWith"].as_object().map_or(0, |m| m.len())).into());
            x.set_d_path(path_of(&names_to(&sel)).into());
            x.set_d_can_preview(!dir && matches!(plan(&name, &mime, n["size"].as_u64().unwrap_or(0)), Plan::Preview(..)));
            x.set_d_can_star(dir && !shared_in);
            x.set_d_starred(dir && crate::files::is_starred(&sel));
            x.set_d_summary("".into());
        }
        None => {
            let (mut folders, mut files, mut bytes) = (0usize, 0usize, 0u64);
            let sizes: HashMap<String, u64> = crate::files::with_nodes(|ns| ns.iter().filter(|n| !crate::files::is_dir(n)).map(|n| (crate::files::key(n), n["size"].as_u64().unwrap_or(0))).collect());
            for i in 0..listed.row_count() {
                let Some(r) = listed.row_data(i) else { continue };
                if r.folder { folders += 1; } else {
                    files += 1;
                    bytes += sizes.get(r.id.as_str()).copied().unwrap_or(0);
                }
            }
            let names = u.get_crumb_names();
            let folder = names.row_count().checked_sub(1).and_then(|i| names.row_data(i)).map(|s| s.to_string()).unwrap_or_else(|| "My files".into());
            let cwd = crate::files::cwd();
            x.set_d_id("".into());
            x.set_d_name(folder.into());
            x.set_d_summary(folder_summary(folders, files).into());
            x.set_d_size(crate::human_size(bytes).into());
            x.set_d_path(if cwd == crate::files::TRASH_LOC { "Trash".into() } else { path_of(&names_to(&cwd)).into() });
            x.set_d_has_thumb(false);
        }
    }
}

pub fn toggle_details(u: &AppWindow) {
    let x = u.global::<FilesV>();
    let open = !x.get_details_open();
    x.set_details_open(open);
    crate::settings::with_live(|s| { s.prefs.insert(DETAILS_KEY.into(), json!(open)); s.save(); });
    refresh_details(u);
}

// ---------------------------------------------------------------- the preview

fn set_neighbours(u: &AppWindow) {
    let cur = VIEW.with(|v| v.borrow().preview.clone()).unwrap_or_default();
    let (p, n) = VIEW.with(|v| neighbours(&v.borrow().order, &cur));
    let x = u.global::<FilesV>();
    x.set_p_has_prev(p.is_some());
    x.set_p_has_next(n.is_some());
}

/// What the preview shows once its load is done.
enum Shown {
    Image(SharedPixelBuffer<Rgba8Pixel>),
    Text(String, bool),
    Doc { page: SharedPixelBuffer<Rgba8Pixel>, height: f32, head: Option<EmlHead> },
    Error(String),
}

#[derive(Default)]
struct EmlHead { from: String, to: String, cc: String, date: String, subject: String, atts: String }

fn addr(a: &Value) -> String {
    let (n, e) = (a["name"].as_str().unwrap_or(""), a["email"].as_str().unwrap_or(""));
    if !n.is_empty() && !e.is_empty() { format!("{n} <{e}>") } else if !e.is_empty() { e.into() } else { n.into() }
}

fn addrs(v: &Value) -> String { v.as_array().map(|l| l.iter().map(addr).filter(|s| !s.is_empty()).collect::<Vec<_>>().join(", ")).unwrap_or_default() }

/// A body for the light renderer: sanitised like a message (mailview), on white.
fn doc_page(body: &str) -> String {
    format!("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>html,body{{background:#ffffff;color:#1f2328}}\
        body{{margin:18px;font-family:'Segoe UI',system-ui,sans-serif;font-size:14px;line-height:1.45;overflow-wrap:anywhere}}\
        img{{max-width:100%;height:auto}}pre{{white-space:pre-wrap;font-family:Consolas,'Cascadia Mono',monospace;font-size:13px;margin:0}}</style></head><body>{}</body></html>",
        crate::mailview::sanitize_mail_html_opts(body, false))
}

fn render_doc(body: &str, width: f32, scale: f32) -> (SharedPixelBuffer<Rgba8Pixel>, f32) {
    let (buf, pw, ph) = crate::lite::render(&doc_page(body), width, scale);
    (SharedPixelBuffer::clone_from_slice(&buf, pw, ph), ph as f32 / scale)
}

/// Open the in-app preview of file `key` (Bulwark FilePreviewModal / ImagePreviewModal).
pub fn open_preview(u: &AppWindow, c: Arc<Client>, key: &str) {
    let Some(n) = crate::files::node(key) else { return };
    note_client(&c);
    let gen = VIEW.with(|v| { let mut v = v.borrow_mut(); v.gen += 1; v.preview = Some(key.to_string()); v.gen });
    let name = n["name"].as_str().unwrap_or("file").to_string();
    let mime = crate::files::mime(&n);
    let size = n["size"].as_u64().unwrap_or(0);
    let x = u.global::<FilesV>();
    x.set_p_name(name.clone().into());
    x.set_p_sub(format!("{} · {}", crate::human_size(size), kind_of(&name, false, &mime)).into());
    x.set_p_text("".into());
    x.set_p_note("".into());
    x.set_p_image(Default::default());
    x.set_p_doc(Default::default());
    x.set_p_eml(false);
    x.set_p_open(true);
    set_neighbours(u);
    let decl = n["type"].as_str().or(n["mediaType"].as_str()).unwrap_or("").to_string();
    let kind = match plan(&name, &decl, size) {
        Plan::External(k) => {
            x.set_p_state("none".into());
            x.set_p_message(match k {
                Kind::Pdf | Kind::Audio | Kind::Video => "This file opens in another app.",
                _ => "There is no preview for this file.",
            }.into());
            return;
        }
        Plan::Preview(_, false) => {
            x.set_p_state("none".into());
            x.set_p_message("This file is too large to preview.".into());
            return;
        }
        Plan::Preview(k, true) => k,
    };
    x.set_p_state("loading".into());
    let (acc, blob) = (crate::files::acc_of(&n), n["blobId"].as_str().unwrap_or("").to_string());
    let width = x.get_p_width().clamp(320.0, 1100.0);
    let scale = u.window().scale_factor();
    let w = u.as_weak();
    std::thread::spawn(move || {
        let shown = (|| -> Result<Shown, String> {
            if kind == Kind::Eml {
                let m = crate::reader::parse_eml_in(&c, &acc, &blob)?;
                let atts = m["attachments"].as_array().map(|l| l.iter().map(|a| format!("{} ({})", a["name"].as_str().unwrap_or("attachment"), crate::human_size(a["size"].as_u64().unwrap_or(0)))).collect::<Vec<_>>().join(", ")).unwrap_or_default();
                let subject = m["subject"].as_str().unwrap_or("").to_string();
                let head = EmlHead {
                    from: addrs(&m["from"]), to: addrs(&m["to"]), cc: addrs(&m["cc"]),
                    date: crate::mail::parse_time(&m["sentAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default(),
                    subject: if subject.is_empty() { "(no subject)".into() } else { subject }, atts,
                };
                let (page, height) = render_doc(&crate::body_html(&m), width, scale);
                return Ok(Shown::Doc { page, height, head: Some(head) });
            }
            let bytes = c.download_from(&acc, &blob, &name, &mime)?;
            Ok(match kind {
                Kind::Image => Shown::Image(decode_image(&bytes, SHOW_EDGE)?),
                Kind::Svg => Shown::Image(render_svg(&bytes, 1600)?),
                Kind::Html => {
                    let (t, _) = decode_text(&bytes, &mime, MAX_HTML_BYTES as usize);
                    let (page, height) = render_doc(&t, width, scale);
                    Shown::Doc { page, height, head: None }
                }
                _ => { let (t, cut) = decode_text(&bytes, &mime, TEXT_SHOW_BYTES); Shown::Text(t, cut) }
            })
        })().unwrap_or_else(Shown::Error);
        let _ = w.upgrade_in_event_loop(move |u| preview_done(&u, gen, shown));
    });
}

fn preview_done(u: &AppWindow, gen: u64, shown: Shown) {
    // closed, or another file opened meanwhile
    if VIEW.with(|v| v.borrow().gen) != gen { return; }
    let x = u.global::<FilesV>();
    if !x.get_p_open() { return; }
    match shown {
        Shown::Image(b) => { x.set_p_image(slint::Image::from_rgba8(b)); x.set_p_state("image".into()); }
        Shown::Text(t, cut) => {
            x.set_p_text(t.into());
            x.set_p_note(if cut { "Only the beginning of the file is shown. Download it to see all of it.".into() } else { "".into() });
            x.set_p_state("text".into());
        }
        Shown::Doc { page, height, head } => {
            x.set_p_doc(slint::Image::from_rgba8(page));
            x.set_p_doc_height(height);
            if let Some(h) = head {
                x.set_p_eml(true);
                x.set_p_from(h.from.into());
                x.set_p_to(h.to.into());
                x.set_p_cc(h.cc.into());
                x.set_p_date(h.date.into());
                x.set_p_subject(h.subject.into());
                x.set_p_atts(h.atts.into());
            }
            x.set_p_state("doc".into());
        }
        Shown::Error(e) => { x.set_p_message(format!("Couldn't load the preview. {e}").into()); x.set_p_state("error".into()); }
    }
}

pub fn close_preview(u: &AppWindow) {
    VIEW.with(|v| { let mut v = v.borrow_mut(); v.gen += 1; v.preview = None; });
    let x = u.global::<FilesV>();
    x.set_p_open(false);
    // the pictures go with it
    x.set_p_image(Default::default());
    x.set_p_doc(Default::default());
    x.set_p_text("".into());
}

/// ← / → in the preview: the previous or next file of the listing.
pub fn preview_go(u: &AppWindow, delta: i32) {
    let cur = VIEW.with(|v| v.borrow().preview.clone()).unwrap_or_default();
    let (p, n) = VIEW.with(|v| neighbours(&v.borrow().order, &cur));
    let Some(next) = (if delta < 0 { p } else { n }) else { return };
    let Some(c) = client() else { return };
    crate::files::note_recent(&next);
    u.global::<Pd>().set_file_sel(next.clone().into());
    open_preview(u, c, &next);
}

fn preview_key() -> Option<String> { VIEW.with(|v| v.borrow().preview.clone()) }

// ---------------------------------------------------------------- wiring

/// Whether opening the file shows it in the app (images, text, .eml); else the default app.
pub fn opens_in_app(n: &Value) -> bool {
    let decl = n["type"].as_str().or(n["mediaType"].as_str()).unwrap_or("");
    matches!(plan(n["name"].as_str().unwrap_or(""), decl, n["size"].as_u64().unwrap_or(0)), Plan::Preview(..))
}

/// The panel's state on this device, read once at start.
pub fn init(ui: &AppWindow) {
    let p = crate::settings::Settings::load();
    ui.global::<FilesV>().set_details_open(p.bool(DETAILS_KEY, false));
    let fs = FilesSettings::from_prefs(&p.prefs);
    let x = ui.global::<FilesV>();
    x.set_grid(fs.grid);
    x.set_show_icons(fs.show_icons);
    x.set_colored_icons(fs.colored_icons);
}

pub fn wire(ui: &AppWindow, st: crate::Shared) {
    init(ui);
    let x = ui.global::<FilesV>();
    let cl = move || st.lock().unwrap_or_else(|e| e.into_inner()).clone();
    let w = ui.as_weak();
    x.on_sort(move |k| {
        let Some(u) = w.upgrade() else { return };
        let Some(k) = SortKey::parse(&k) else { return };
        VIEW.with(|v| { let mut v = v.borrow_mut(); v.sort = sort_click(v.sort, k); });
        crate::files::render(&u);
    });
    let w = ui.as_weak();
    x.on_search(move |q| {
        let Some(u) = w.upgrade() else { return };
        VIEW.with(|v| v.borrow_mut().query = q.to_string());
        crate::files::render(&u);
    });
    let w = ui.as_weak();
    x.on_set_view(move |mode| {
        let Some(u) = w.upgrade() else { return };
        crate::settings::with_live(|s| { if set_field(&mut s.prefs, "defaultViewMode", &json!(mode.as_str())) { s.save(); } });
        crate::files::render(&u);
    });
    let w = ui.as_weak();
    x.on_want_thumb(move |k| { if let Some(u) = w.upgrade() { want_thumb(&u, &k); } });
    let w = ui.as_weak();
    x.on_toggle_details(move || { if let Some(u) = w.upgrade() { toggle_details(&u); } });
    let w = ui.as_weak();
    x.on_selected(move |_| { if let Some(u) = w.upgrade() { refresh_details(&u); } });
    let (w, c2) = (ui.as_weak(), cl.clone());
    x.on_d_action(move |a| {
        let Some(u) = w.upgrade() else { return };
        let id = u.global::<FilesV>().get_d_id().to_string();
        if id.is_empty() { return; }
        match a.as_str() {
            "open" => u.invoke_fl_activate(id.into()),
            "preview" => { if let Some(c) = c2() { crate::files::note_recent(&id); open_preview(&u, c, &id); } }
            "download" => u.invoke_fl_download(id.into()),
            "star" => { crate::files::toggle_star(&u, &id); }
            _ => {}
        }
    });
    let w = ui.as_weak();
    x.on_p_close(move || { if let Some(u) = w.upgrade() { close_preview(&u); } });
    let w = ui.as_weak();
    x.on_p_go(move |d| { if let Some(u) = w.upgrade() { preview_go(&u, d); } });
    let w = ui.as_weak();
    x.on_p_download(move || { if let (Some(u), Some(k)) = (w.upgrade(), preview_key()) { u.invoke_fl_download(k.into()); } });
    let (w, c3) = (ui.as_weak(), cl);
    x.on_p_external(move || {
        let (Some(_u), Some(k), Some(c)) = (w.upgrade(), preview_key(), c3()) else { return };
        if let Some(n) = crate::files::node(&k) { crate::files::open_external(c, n); }
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn e(name: &str, dir: bool, size: u64, modified: i64) -> Entry { Entry::new(name, dir, size, modified) }

    fn names(items: &[Entry], order: &[usize]) -> Vec<String> { order.iter().map(|&i| items[i].name.clone()).collect() }

    fn sample() -> Vec<Entry> {
        vec![
            e("b10.txt", false, 300, 3_000), e("B2.txt", false, 100, 1_000), e("a.txt", false, 200, 2_000),
            e("Zeta", true, 0, 5_000), e("alpha", true, 0, 4_000), e(".env", false, 10, 9_000), e(".git", true, 0, 9_000),
        ]
    }

    #[test]
    fn sorting_by_name_is_natural_and_puts_folders_first_both_ways() {
        let items = sample();
        let asc = arrange(&items, false, "", SortKey::Name, Dir::Asc);
        assert_eq!(names(&items, &asc), ["alpha", "Zeta", "a.txt", "B2.txt", "b10.txt"]);
        let desc = arrange(&items, false, "", SortKey::Name, Dir::Desc);
        assert_eq!(names(&items, &desc), ["Zeta", "alpha", "b10.txt", "B2.txt", "a.txt"]);
    }

    #[test]
    fn sorting_by_size_and_date_with_name_ties() {
        let items = sample();
        assert_eq!(names(&items, &arrange(&items, false, "", SortKey::Size, Dir::Asc)), ["alpha", "Zeta", "B2.txt", "a.txt", "b10.txt"]);
        assert_eq!(names(&items, &arrange(&items, false, "", SortKey::Size, Dir::Desc)), ["Zeta", "alpha", "b10.txt", "a.txt", "B2.txt"]);
        assert_eq!(names(&items, &arrange(&items, false, "", SortKey::Modified, Dir::Asc)), ["alpha", "Zeta", "B2.txt", "a.txt", "b10.txt"]);
        assert_eq!(names(&items, &arrange(&items, false, "", SortKey::Modified, Dir::Desc)), ["Zeta", "alpha", "b10.txt", "a.txt", "B2.txt"]);
        // equal sizes fall back to the name, turned round with the direction
        let same = vec![e("b", false, 5, 0), e("a", false, 5, 0), e("C", false, 5, 0)];
        assert_eq!(names(&same, &arrange(&same, true, "", SortKey::Size, Dir::Asc)), ["a", "b", "C"]);
        assert_eq!(names(&same, &arrange(&same, true, "", SortKey::Size, Dir::Desc)), ["C", "b", "a"]);
        // a missing date counts as 0, the oldest
        let dates = vec![e("x", false, 0, 0), e("y", false, 0, 7)];
        assert_eq!(names(&dates, &arrange(&dates, true, "", SortKey::Modified, Dir::Asc)), ["x", "y"]);
    }

    #[test]
    fn names_compare_like_locale_compare() {
        assert_eq!(natural("file2", "file10"), Ordering::Less);
        assert_eq!(natural("file010", "file10"), Ordering::Equal);
        assert_eq!(natural(&fold("Été"), &fold("ete")), Ordering::Equal);
        assert_eq!(natural("a b", "ab"), Ordering::Less);
        assert_eq!(natural("_x", "1x"), Ordering::Less);
        assert_eq!(natural("1x", "ax"), Ordering::Less);
        // the tie-break: accents, then lower case first
        assert_eq!(tie(&e("a", false, 0, 0), &e("A", false, 0, 0)), Ordering::Less);
        assert_eq!(tie(&e("e", false, 0, 0), &e("é", false, 0, 0)), Ordering::Less);
        assert_eq!(tie(&e("x", false, 0, 0), &e("x", false, 0, 0)), Ordering::Equal);
    }

    #[test]
    fn hidden_files_are_left_out_unless_shown() {
        let items = sample();
        assert!(!names(&items, &arrange(&items, false, "", SortKey::Name, Dir::Asc)).iter().any(|n| n.starts_with('.')));
        let all = names(&items, &arrange(&items, true, "", SortKey::Name, Dir::Asc));
        assert_eq!(all, [".git", "alpha", "Zeta", ".env", "a.txt", "B2.txt", "b10.txt"]);
        assert!(is_hidden(".bashrc") && !is_hidden("a.b") && !is_hidden(""));
    }

    #[test]
    fn search_filters_the_folder_by_name_ignoring_case() {
        let items = sample();
        assert_eq!(names(&items, &arrange(&items, false, "B", SortKey::Name, Dir::Asc)), ["B2.txt", "b10.txt"]);
        assert_eq!(names(&items, &arrange(&items, false, "TXT", SortKey::Name, Dir::Asc)), ["a.txt", "B2.txt", "b10.txt"]);
        // a hidden file stays hidden while searching
        assert!(arrange(&items, false, "env", SortKey::Name, Dir::Asc).is_empty());
        assert_eq!(names(&items, &arrange(&items, true, "env", SortKey::Name, Dir::Asc)), [".env"]);
        assert!(matches("Ärger.txt", "ärg") && matches("x", "") && !matches("x", "y"));
    }

    #[test]
    fn column_heads_turn_the_order_round_or_start_a_new_one() {
        assert_eq!(sort_click((SortKey::Name, Dir::Asc), SortKey::Name), (SortKey::Name, Dir::Desc));
        assert_eq!(sort_click((SortKey::Name, Dir::Desc), SortKey::Name), (SortKey::Name, Dir::Asc));
        assert_eq!(sort_click((SortKey::Name, Dir::Asc), SortKey::Size), (SortKey::Size, Dir::Desc));
        assert_eq!(sort_click((SortKey::Size, Dir::Asc), SortKey::Modified), (SortKey::Modified, Dir::Desc));
        assert_eq!(sort_click((SortKey::Size, Dir::Desc), SortKey::Name), (SortKey::Name, Dir::Asc));
    }

    #[test]
    fn settings_follow_bulwark_defaults_and_normalisation() {
        let d = FilesSettings::normalize(None);
        assert_eq!(d, FilesSettings::default());
        assert!(!d.grid && d.show_icons && d.colored_icons && d.sort_key == SortKey::Name && d.sort_dir == Dir::Asc && !d.show_hidden && d.show_thumbnails);
        let s = FilesSettings::normalize(Some(&json!({"defaultViewMode": "grid", "defaultSortKey": "size", "defaultSortDir": "desc", "showHiddenFiles": true,
            "showIcons": "yes", "coloredIcons": false, "showThumbnails": 0, "extra": 1})));
        assert!(s.grid && s.sort_key == SortKey::Size && s.sort_dir == Dir::Desc && s.show_hidden && !s.colored_icons);
        // a value of the wrong kind keeps the default
        assert!(s.show_icons && s.show_thumbnails);
        assert_eq!(FilesSettings::normalize(Some(&json!([1, 2]))), FilesSettings::default());
        assert_eq!(FilesSettings::normalize(Some(&s.to_json())), s);
    }

    #[test]
    fn settings_rows_map_to_the_object_fields() {
        let labels = ["Show files as", "Default sort", "Sort direction", "Show file icons", "Colored icons", "Show thumbnails", "Show hidden files"];
        let keys: Vec<&str> = labels.iter().map(|l| row_key(l).unwrap()).collect();
        assert_eq!(keys, ["files:defaultViewMode", "files:defaultSortKey", "files:defaultSortDir", "files:showIcons", "files:coloredIcons", "files:showThumbnails", "files:showHiddenFiles"]);
        assert!(row_key("Show file").is_none());
        assert_eq!(row_options("files:defaultViewMode").unwrap().iter().map(|o| o.1).collect::<Vec<_>>(), ["List", "Grid"]);
        assert_eq!(row_options("files:defaultSortKey").unwrap().len(), 3);
        assert_eq!(row_options("files:defaultSortDir").unwrap()[1].0, json!("desc"));
        assert!(row_options("files:showIcons").is_none());
        let mut p = Map::new();
        assert_eq!(row_value(&p, "files:defaultViewMode"), json!("list"));
        assert!(set_field(&mut p, "files:defaultViewMode", &json!("grid")));
        assert!(set_field(&mut p, "showHiddenFiles", &json!(true)));
        assert!(!set_field(&mut p, "files:defaultSortKey", &json!("colour")));
        assert!(!set_field(&mut p, "files:nope", &json!(true)));
        assert_eq!(row_value(&p, "files:defaultViewMode"), json!("grid"));
        assert_eq!(row_value(&p, "files:showHiddenFiles"), json!(true));
        assert_eq!(p[SETTINGS_KEY]["defaultSortKey"], json!("name"));
        assert_eq!(p[SETTINGS_KEY].as_object().unwrap().len(), 7);
    }

    #[test]
    fn preview_kinds_follow_bulwark() {
        assert_eq!(preview_kind("a.PNG", ""), Kind::Image);
        assert_eq!(preview_kind("photo", "image/jpeg"), Kind::Image);
        assert_eq!(preview_kind("logo.svg", "application/octet-stream"), Kind::Svg);
        assert_eq!(preview_kind("x", "image/svg+xml; charset=utf-8"), Kind::Svg);
        assert_eq!(preview_kind("page.htm", ""), Kind::Html);
        assert_eq!(preview_kind("mail.eml", ""), Kind::Eml);
        assert_eq!(preview_kind("fwd", "message/rfc822"), Kind::Eml);
        assert_eq!(preview_kind("r.pdf", ""), Kind::Pdf);
        assert_eq!(preview_kind("s.mp3", ""), Kind::Audio);
        assert_eq!(preview_kind("m.mkv", ""), Kind::Video);
        assert_eq!(preview_kind("README.md", ""), Kind::Markdown);
        assert_eq!(preview_kind("data.json", ""), Kind::Text);
        assert_eq!(preview_kind("x.bin", "application/json"), Kind::Text);
        assert_eq!(preview_kind("notes", "text/plain"), Kind::Text);
        assert_eq!(preview_kind("a.docx", ""), Kind::Unsupported);
        assert_eq!(preview_kind(".env", ""), Kind::Unsupported);
    }

    #[test]
    fn previews_have_caps_and_pdf_opens_outside() {
        assert_eq!(plan("a.png", "image/png", 1000), Plan::Preview(Kind::Image, true));
        assert_eq!(plan("a.png", "", MAX_IMAGE_BYTES + 1), Plan::Preview(Kind::Image, false));
        assert_eq!(plan("a.avif", "image/avif", 10), Plan::External(Kind::Image));
        assert_eq!(plan("a.svg", "", MAX_SVG_BYTES), Plan::Preview(Kind::Svg, true));
        assert_eq!(plan("a.svg", "", MAX_SVG_BYTES + 1), Plan::Preview(Kind::Svg, false));
        assert_eq!(plan("a.txt", "", MAX_TEXT_BYTES + 1), Plan::Preview(Kind::Text, false));
        assert_eq!(plan("a.eml", "", 10), Plan::Preview(Kind::Eml, true));
        assert_eq!(plan("a.html", "", MAX_HTML_BYTES + 1), Plan::Preview(Kind::Html, false));
        assert_eq!(plan("a.pdf", "application/pdf", 10), Plan::External(Kind::Pdf));
        assert_eq!(plan("a.mp4", "", 10), Plan::External(Kind::Video));
        assert_eq!(plan("a.zip", "", 10), Plan::External(Kind::Unsupported));
        // thumbnails: image files with a blob, up to 15 MB, when the setting is on
        assert!(can_thumb("a.jpg", "image/jpeg", MAX_THUMB_BYTES, true, true));
        assert!(!can_thumb("a.jpg", "image/jpeg", MAX_THUMB_BYTES + 1, true, true));
        assert!(!can_thumb("a.jpg", "image/jpeg", 10, false, true));
        assert!(!can_thumb("a.jpg", "image/jpeg", 10, true, false));
        assert!(!can_thumb("a.txt", "text/plain", 10, true, true));
        assert!(!can_thumb("a.avif", "image/avif", 10, true, true));
        assert!(can_thumb("a.svg", "", 10, true, true));
    }

    #[test]
    fn neighbours_go_through_the_listed_files() {
        let order: Vec<String> = ["a", "b", "c"].iter().map(|s| s.to_string()).collect();
        assert_eq!(neighbours(&order, "a"), (None, Some("b".into())));
        assert_eq!(neighbours(&order, "b"), (Some("a".into()), Some("c".into())));
        assert_eq!(neighbours(&order, "c"), (Some("b".into()), None));
        assert_eq!(neighbours(&order, "zz"), (None, None));
    }

    #[test]
    fn text_is_decoded_safely() {
        assert_eq!(decode_text(b"hello\r\nworld\rend", "", 100), ("hello\nworld\nend".into(), false));
        assert_eq!(decode_text("\u{FEFF}é".as_bytes(), "", 100).0, "é");
        assert_eq!(decode_text(&[0xFF, 0xFE, b'h', 0, 0xE9, 0], "", 100).0, "hé");
        assert_eq!(decode_text(&[0xFE, 0xFF, 0, b'h', 0, 0xE9], "", 100).0, "hé");
        assert_eq!(decode_text(&[b'h', 0, b'i', 0], "text/plain; charset=\"UTF-16LE\"", 100).0, "hi");
        // not UTF-8: Windows-1252
        assert_eq!(decode_text(&[0x93, b'q', 0x94, 0xE9, 0x80], "", 100).0, "\u{201C}q\u{201D}é€");
        assert_eq!(decode_text(&[0xE9], "text/plain;charset=iso-8859-1", 100).0, "é");
        // controls are shown, never passed on; tabs stay
        assert_eq!(decode_text(b"a\x00b\x1bc\td", "", 100).0, "a\u{FFFD}b\u{FFFD}c\td");
        // the cap cuts between characters
        let (t, cut) = decode_text("aé".as_bytes(), "", 2);
        assert_eq!((t.as_str(), cut), ("a", true));
        let (t, cut) = decode_text(b"abcdef", "", 4);
        assert_eq!((t.as_str(), cut), ("abcd", true));
    }

    fn png(w: u32, h: u32) -> Vec<u8> {
        let mut out = vec![];
        image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(w, h, image::Rgba([1, 2, 3, 255])))
            .write_to(&mut std::io::Cursor::new(&mut out), image::ImageFormat::Png).unwrap();
        out
    }

    #[test]
    fn images_are_decoded_within_limits_and_scaled_down() {
        let b = decode_image(&png(400, 100), 200).unwrap();
        assert_eq!((b.width(), b.height()), (200, 50));
        let b = decode_image(&png(30, 20), 200).unwrap();
        assert_eq!((b.width(), b.height()), (30, 20));
        assert!(decode_image(b"not a picture", 200).is_err());
        // a header that claims more pixels than the cap is refused before decoding
        let mut big = png(1, 1);
        // IHDR width and height at bytes 16..24
        big[16..20].copy_from_slice(&20_000u32.to_be_bytes());
        big[20..24].copy_from_slice(&20_000u32.to_be_bytes());
        let crc = crc32fast::hash(&big[12..29]);
        big[29..33].copy_from_slice(&crc.to_be_bytes());
        assert_eq!(decode_image(&big, 200).unwrap_err(), "The picture is too large to preview.");
        let t = thumbnail("a.png", "image/png", &png(600, 300)).unwrap();
        assert_eq!((t.width(), t.height()), (THUMB_EDGE, THUMB_EDGE / 2));
    }

    #[test]
    fn svg_is_drawn_without_following_references() {
        let svg = br#"<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="40" height="20"><rect width="40" height="20" fill="red"/><image href="C:/Windows/win.ini" width="10" height="10"/><image xlink:href="https://example.com/x.png" width="10" height="10"/></svg>"#;
        let b = render_svg(svg, 100).unwrap();
        assert_eq!((b.width(), b.height()), (100, 50));
        let px = b.as_slice()[0];
        assert_eq!((px.r, px.a), (255, 255));
        assert!(render_svg(b"<nope", 100).is_err());
    }

    #[test]
    fn the_thumbnail_cache_is_bounded() {
        let mut c: Lru<u32> = Lru::new(3, 100);
        c.insert("a".into(), 1, 10);
        c.insert("b".into(), 2, 10);
        c.insert("c".into(), 3, 10);
        assert_eq!(c.get("a"), Some(1));
        c.insert("d".into(), 4, 10);
        // b was the least recently used
        assert!(c.contains("a") && !c.contains("b") && c.contains("c") && c.contains("d"));
        assert_eq!(c.len(), 3);
        // the byte bound evicts too
        c.insert("e".into(), 5, 90);
        // c and a go (least recently used first), d and e fill the 100 bytes
        assert!(c.bytes() <= 100 && c.contains("e") && c.contains("d"));
        assert!(!c.contains("a") && !c.contains("c"));
        assert_eq!(c.len(), 2);
        // a value bigger than the whole cache is not kept
        c.insert("f".into(), 6, 101);
        assert!(!c.contains("f") && c.contains("e"));
        // replacing a key keeps the count of bytes right
        c.insert("e".into(), 7, 40);
        assert_eq!((c.len(), c.bytes(), c.get("e")), (2, 50, Some(7)));
    }

    #[test]
    fn details_are_formatted_like_bulwark() {
        assert_eq!(kind_of("Docs", true, ""), "Folder");
        assert_eq!(kind_of("report.pdf", false, "application/pdf"), "PDF file");
        assert_eq!(kind_of("README", false, "text/plain"), "text/plain");
        assert_eq!(kind_of("README", false, ""), "File");
        assert_eq!(type_row("report.pdf", false, "application/pdf"), "application/pdf");
        assert_eq!(type_row("report.pdf", false, ""), "PDF file");
        assert_eq!(type_row("Docs", true, ""), "Folder");
        assert_eq!(sharing(false, None, 0), "Only you");
        assert_eq!(sharing(false, None, 1), "Shared with 1 person");
        assert_eq!(sharing(false, None, 3), "Shared with 3 people");
        assert_eq!(sharing(true, Some("Alex"), 0), "Shared by Alex");
        assert_eq!(sharing(true, Some(""), 0), "Shared with me");
        assert_eq!(folder_summary(0, 0), "No folders, no files");
        assert_eq!(folder_summary(1, 1), "1 folder, 1 file");
        assert_eq!(folder_summary(2, 5), "2 folders, 5 files");
        assert_eq!(path_of(&[]), "/");
        assert_eq!(path_of(&["Docs".into(), "a.txt".into()]), "/Docs/a.txt");
        use chrono::TimeZone;
        let t = chrono::Local.with_ymd_and_hms(2026, 3, 4, 15, 7, 0).unwrap();
        assert_eq!(full_date(t, "%H:%M"), "04 Mar 2026, 15:07");
        assert_eq!(full_date(t, "%-I:%M %p"), "04 Mar 2026, 3:07 PM");
        assert_eq!(iso_ms(&json!("2026-01-01T00:00:00Z")), 1_767_225_600_000);
        assert_eq!(iso_ms(&json!("nope")), 0);
        assert_eq!(iso_ms(&Value::Null), 0);
    }

    #[test]
    fn icons_follow_the_type() {
        assert_eq!(icon_kind("a.png").0, "image");
        assert_eq!(icon_kind("a.svg").0, "vector");
        assert_eq!(icon_kind("a.pdf").0, "pdf");
        assert_eq!(icon_kind("a.tar.gz").0, "archive");
        assert_eq!(icon_kind("Makefile").0, "code");
        assert_eq!(icon_kind("a.xlsx").0, "sheet");
        assert_eq!(icon_kind("noext").0, "file");
        assert_eq!(icon_kind(".bashrc").0, "file");
        assert!(icon_path("image").starts_with("M3 3h18"));
        assert!(is_image_name("x.JPG") && !is_image_name("x.pdf"));
    }
}
