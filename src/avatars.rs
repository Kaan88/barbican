//! Sender pictures (Bulwark components/ui/avatar.tsx, app/api/favicon, app/api/bimi):
//! a BIMI logo for mail that passed DMARC for its From domain, a Gravatar photo, or the
//! company favicon, in that order, falling back to the initials. Everything here reaches
//! outside hosts, so it is off by default: "Sender pictures from the web" (`senderFavicons`:
//! favicons and BIMI logos; Bulwark has it on, Barbican asks first) and "Gravatar photos"
//! (`gravatarAvatars`; a plugin in Bulwark, built in here). In Junk no pictures are shown unless
//! `showAvatarsInJunk` (a logo lends a phishing mail credibility).
//!
//! Answers are kept on disk (config dir `avatars/`) for two weeks, a missing one for a day
//! (Bulwark's favicon cache TTLs), and in memory for the session.
use sha2::{Digest, Sha256};
use slint::{ComponentHandle, Rgba8Pixel, SharedPixelBuffer};
use std::cell::RefCell;
use std::collections::{HashMap, VecDeque};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;

const POSITIVE_TTL: Duration = Duration::from_secs(14 * 24 * 3600);
const NEGATIVE_TTL: Duration = Duration::from_secs(24 * 3600);
/// Icons are a few KB; a larger answer is not one (Bulwark MAX_ICON_BYTES).
const MAX_BYTES: usize = 256 * 1024;
const EDGE: u32 = 64;
const WORKERS: usize = 4;

/// Which pictures may be fetched (read once per list build).
#[derive(Clone, Copy, Default)]
pub struct Prefs { pub favicons: bool, pub gravatar: bool, pub in_junk: bool }

impl Prefs {
    pub fn load() -> Prefs {
        let p = crate::settings::Settings::load();
        Prefs::from_settings(&p, crate::crashguard::safe_mode())
    }
    /// `safe`: the last start crashed, so nothing is fetched from the web this run.
    pub(crate) fn from_settings(p: &crate::settings::Settings, safe: bool) -> Prefs {
        Prefs { favicons: !safe && p.bool("senderFavicons", false), gravatar: !safe && p.bool("gravatarAvatars", false), in_junk: p.bool("showAvatarsInJunk", false) }
    }
    pub fn any(&self) -> bool { self.favicons || self.gravatar }
}

/// A picture for a row: the image and whether it is a logo (drawn on white) or a photo.
#[derive(Clone)]
pub struct Pic { pub image: slint::Image, pub logo: bool }

enum Slot { Pending, Missing, Ready(Pic) }

/// What a worker fetched: raster pixels, SVG text (decoded on the UI thread), or nothing.
enum Fetched { Raster(SharedPixelBuffer<Rgba8Pixel>), Svg(Vec<u8>), None }

thread_local! {
    static SLOTS: RefCell<HashMap<String, Slot>> = RefCell::new(HashMap::new());
    static WIN: RefCell<Option<slint::Weak<crate::AppWindow>>> = const { RefCell::new(None) };
    static REDRAW: slint::Timer = slint::Timer::default();
}
/// The fetch queue (key, generation) and how many workers run, under one lock so a worker
/// that finds the queue empty and stops can never miss a key pushed in the same moment.
static WORK: Mutex<(VecDeque<(String, u64)>, usize)> = Mutex::new((VecDeque::new(), 0));
/// Bumped by "Refresh sender icons": answers of an older generation are dropped.
static GEN: AtomicU64 = AtomicU64::new(0);

/// The queue lock; a poisoned lock is still a usable queue.
fn work() -> std::sync::MutexGuard<'static, (VecDeque<(String, u64)>, usize)> {
    WORK.lock().unwrap_or_else(|e| e.into_inner())
}

/// Known multi-part TLDs (Bulwark avatar.tsx MULTI_PART_TLDS).
const MULTI_PART_TLDS: &[&str] = &[
    "co.uk", "org.uk", "me.uk", "ac.uk", "gov.uk", "net.uk", "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
    "co.kr", "or.kr", "go.kr", "ac.kr", "co.in", "net.in", "org.in", "ac.in", "gov.in",
    "co.nz", "org.nz", "net.nz", "govt.nz", "ac.nz", "co.za", "org.za", "net.za", "gov.za", "ac.za",
    "com.au", "net.au", "org.au", "edu.au", "gov.au", "com.br", "net.br", "org.br", "edu.br", "gov.br",
    "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn", "com.mx", "net.mx", "org.mx", "gob.mx", "edu.mx",
    "com.ar", "net.ar", "org.ar", "gob.ar", "edu.ar", "com.tw", "net.tw", "org.tw", "edu.tw", "gov.tw",
    "com.hk", "net.hk", "org.hk", "edu.hk", "gov.hk", "com.sg", "net.sg", "org.sg", "edu.sg", "gov.sg",
    "com.my", "net.my", "org.my", "edu.my", "gov.my", "com.ph", "net.ph", "org.ph", "edu.ph", "gov.ph",
    "com.pk", "net.pk", "org.pk", "edu.pk", "gov.pk", "com.ng", "net.ng", "org.ng", "edu.ng", "gov.ng",
    "co.il", "org.il", "net.il", "ac.il", "gov.il", "co.th", "or.th", "ac.th", "go.th", "in.th",
    "co.id", "or.id", "ac.id", "go.id", "web.id", "com.tr", "net.tr", "org.tr", "edu.tr", "gov.tr",
    "com.ua", "net.ua", "org.ua", "edu.ua", "gov.ua", "com.eg", "net.eg", "org.eg", "edu.eg", "gov.eg",
    "com.sa", "net.sa", "org.sa", "edu.sa", "gov.sa", "co.ke", "or.ke", "ac.ke", "go.ke", "ne.ke",
];

/// Mail providers whose favicon is the provider's logo, not the sender's (Bulwark PERSONAL_DOMAINS).
const PERSONAL_DOMAINS: &[&str] = &[
    "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "yahoo.fr",
    "yahoo.co.uk", "yahoo.co.jp", "aol.com", "icloud.com", "me.com", "mac.com", "mail.com", "proton.me",
    "protonmail.com", "pm.me", "tutanota.com", "tuta.com", "zoho.com", "yandex.com", "yandex.ru", "gmx.com",
    "gmx.net", "fastmail.com", "hey.com", "posteo.de", "mailbox.org", "example.com", "example.org",
];

/// "newsletter.example.co.uk" -> "example.co.uk" (Bulwark getRootDomain).
pub fn root_domain(domain: &str) -> String {
    let d = domain.trim().trim_end_matches('.').to_lowercase();
    let parts: Vec<&str> = d.split('.').collect();
    if parts.len() <= 2 { return d; }
    let last_two = parts[parts.len() - 2..].join(".");
    if MULTI_PART_TLDS.contains(&last_two.as_str()) { parts[parts.len() - 3..].join(".") } else { last_two }
}

/// A domain an icon may be asked for (Bulwark's favicon route isValidDomain: no SSRF to local names).
pub fn valid_domain(d: &str) -> bool {
    let d = d.to_lowercase();
    let label_ok = |l: &str| !l.is_empty() && l.len() <= 63 && !l.starts_with('-') && !l.ends_with('-') && l.chars().all(|c| c.is_ascii_alphanumeric() || c == '-');
    d.len() <= 253 && d.contains('.') && d.split('.').all(label_ok)
        && d != "localhost" && !d.ends_with(".local") && !d.ends_with(".internal") && !d.ends_with(".arpa")
}

fn domain_of(email: &str) -> String { email.rsplit_once('@').map(|(_, d)| d.trim().to_lowercase()).unwrap_or_default() }

/// Gravatar's address hash (SHA-256 of the trimmed, lowercased address).
pub fn gravatar_hash(email: &str) -> String {
    Sha256::digest(email.trim().to_lowercase().as_bytes()).iter().map(|b| format!("{b:02x}")).collect()
}

/// The cache keys to try for a sender, best first (Bulwark: BIMI logo > Gravatar >
/// company favicon). `brand` is the From domain of a message that passed DMARC.
pub fn candidates(email: &str, brand: Option<&str>, p: &Prefs) -> Vec<String> {
    let domain = domain_of(email);
    if domain.is_empty() || !valid_domain(&domain) { return vec![]; }
    let mut v = vec![];
    if p.favicons && brand.is_some_and(|b| b.eq_ignore_ascii_case(&domain)) { v.push(format!("bimi:{domain}")); }
    if p.gravatar { v.push(format!("grav:{}", gravatar_hash(email))); }
    let root = root_domain(&domain);
    if p.favicons && !PERSONAL_DOMAINS.contains(&root.as_str()) { v.push(format!("fav:{root}")); }
    v
}

/// The picture for a sender if one is ready; starts fetching what is not known yet. None while
/// the best candidate is still on its way (the initials show meanwhile), or when there is none.
/// Never panics: a failure here shows the initials instead of taking the app down.
pub fn get(email: &str, brand: Option<&str>, p: &Prefs, junk: bool) -> Option<Pic> {
    guarded(|| get_with(email, brand, p, junk, enqueue)).flatten()
}

/// Runs `f`, turning a panic into None (the avatar code must never end the app).
fn guarded<T>(f: impl FnOnce() -> T) -> Option<T> {
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(f)).ok()
}

/// What the in-memory cache knows about a key.
enum Look { Ready(Pic), Pending, Missing }

/// The cache entry for a key; an unknown key is marked pending and handed to `start`.
fn look(key: &str, start: impl FnOnce(String)) -> Look {
    // The borrow must end before the key is inserted: a `match` on `s.borrow().get(..)` keeps
    // the Ref alive through its arms, and borrow_mut() there panicked on the first unknown key.
    let known = SLOTS.with(|s| s.borrow().get(key).map(|slot| match slot {
        Slot::Ready(pic) => Look::Ready(pic.clone()),
        Slot::Pending => Look::Pending,
        Slot::Missing => Look::Missing,
    }));
    match known {
        Some(l) => l,
        None => {
            SLOTS.with(|s| s.borrow_mut().insert(key.to_string(), Slot::Pending));
            start(key.to_string());
            Look::Pending
        }
    }
}

fn get_with(email: &str, brand: Option<&str>, p: &Prefs, junk: bool, start: impl Fn(String)) -> Option<Pic> {
    if !p.any() || (junk && !p.in_junk) { return None; }
    for key in candidates(email, brand, p) {
        match look(&key, &start) {
            Look::Ready(pic) => return Some(pic),
            Look::Pending => return None,
            Look::Missing => {}
        }
    }
    None
}

/// Sign-out: answers still being fetched must not write the cache again.
pub fn cancel_pending() {
    GEN.fetch_add(1, Ordering::SeqCst);
    work().0.clear();
    // the pictures fetched for this account's senders are not the next account's to see
    SLOTS.with(|s| s.borrow_mut().clear());
}

/// "Refresh sender icons": forget every answer (memory and disk) so they are fetched again.
pub fn refresh() {
    forget_memory();
    let _ = std::fs::remove_dir_all(crate::config_dir().join("avatars"));
}

/// Forget this session's pictures (the disk folder is left alone: "Refresh cached data" clears
/// that, src/cachedata.rs). Answers still being fetched are dropped when they arrive.
pub fn forget_memory() {
    GEN.fetch_add(1, Ordering::SeqCst);
    work().0.clear();
    SLOTS.with(|s| s.borrow_mut().clear());
}

/// The window to redraw when pictures arrive.
pub fn set_window(w: slint::Weak<crate::AppWindow>) { WIN.with(|x| *x.borrow_mut() = Some(w)); }

fn enqueue(key: String) {
    let mut work = work();
    work.0.push_back((key, GEN.load(Ordering::SeqCst)));
    if work.1 >= WORKERS { return; }
    work.1 += 1;
    drop(work);
    let w = WIN.with(|x| x.borrow().clone());
    std::thread::spawn(move || {
        while let Some((key, gen)) = next_job() {
            // a panic in a fetch or decode is an answer of "none" (initials), and the worker lives on
            let fetched = guarded(|| fetch(&key, gen)).unwrap_or(Fetched::None);
            if let Some(w) = &w {
                let _ = w.upgrade_in_event_loop(move |u| arrived(&u, key, gen, fetched));
            }
        }
    });
}

/// The next key for a worker; when there is none the worker is counted out under the same lock.
fn next_job() -> Option<(String, u64)> {
    let mut work = work();
    let job = work.0.pop_front();
    if job.is_none() { work.1 = work.1.saturating_sub(1); }
    job
}

fn arrived(u: &crate::AppWindow, key: String, gen: u64, f: Fetched) {
    // fetched before a refresh: the slot was cleared and is asked for again
    if gen != GEN.load(Ordering::SeqCst) { return; }
    let logo = !key.starts_with("grav:");
    let slot = guarded(|| match f {
        Fetched::Raster(buf) => Slot::Ready(Pic { image: slint::Image::from_rgba8(buf), logo }),
        Fetched::Svg(bytes) => match slint::Image::load_from_svg_data(&bytes) {
            Ok(image) => Slot::Ready(Pic { image, logo }),
            Err(_) => Slot::Missing,
        },
        Fetched::None => Slot::Missing,
    }).unwrap_or(Slot::Missing);
    SLOTS.with(|s| s.borrow_mut().insert(key, slot));
    // a picture arrived, or a missing logo or photo lets the next candidate in: the rows and
    // the reader look again (coalesced)
    let w = u.as_weak();
    REDRAW.with(|t| if !t.running() {
        t.start(slint::TimerMode::SingleShot, Duration::from_millis(150), move || {
            if let Some(u) = w.upgrade() { crate::mail::refresh_avatars(&u); }
        });
    });
}

// ---------------- fetching and the disk cache ----------------

fn cache_path(key: &str) -> std::path::PathBuf {
    let name: String = Sha256::digest(key.as_bytes()).iter().take(16).map(|b| format!("{b:02x}")).collect();
    crate::config_dir().join("avatars").join(name)
}

/// A cached answer still fresh: Some(bytes) (empty = known missing), None = ask again.
fn cached(key: &str) -> Option<Vec<u8>> {
    let p = cache_path(key);
    let age = std::fs::metadata(&p).ok()?.modified().ok()?.elapsed().unwrap_or(Duration::MAX);
    let bytes = std::fs::read(&p).ok()?;
    let ttl = if bytes.is_empty() { NEGATIVE_TTL } else { POSITIVE_TTL };
    (age < ttl).then_some(bytes)
}

fn store(key: &str, bytes: &[u8]) {
    let p = cache_path(key);
    if let Some(dir) = p.parent() { let _ = std::fs::create_dir_all(dir); }
    let _ = std::fs::write(p, bytes);
}

pub(crate) fn http() -> Option<reqwest::blocking::Client> {
    reqwest::blocking::Client::builder()
        .timeout(Duration::from_secs(6))
        // the icon services answer directly; a redirect would go somewhere nobody approved
        .redirect(reqwest::redirect::Policy::none())
        .user_agent("Barbican")
        .build().ok()
}

/// Why a download gave nothing: a definitive answer (cached as missing) or a passing failure
/// (timeout, offline, server error; not cached, asked again next time).
#[derive(Debug, PartialEq)]
enum Miss { Definitive, Transient }

fn get_capped(url: &str) -> Result<(Vec<u8>, String), Miss> {
    use std::io::Read;
    let mut r = http().ok_or(Miss::Transient)?.get(url).send().map_err(|_| Miss::Transient)?;
    let st = r.status();
    if st.is_server_error() || st.as_u16() == 429 || st.as_u16() == 408 { return Err(Miss::Transient); }
    if !st.is_success() { return Err(Miss::Definitive); }
    if r.content_length().is_some_and(|n| n > MAX_BYTES as u64) { return Err(Miss::Definitive); }
    let ct = r.headers().get(reqwest::header::CONTENT_TYPE).and_then(|v| v.to_str().ok()).unwrap_or("").to_lowercase();
    // read at most one byte past the cap, whatever the server streams
    let mut bytes = Vec::new();
    (&mut r).take(MAX_BYTES as u64 + 1).read_to_end(&mut bytes).map_err(|_| Miss::Transient)?;
    if bytes.len() < 10 || bytes.len() > MAX_BYTES { return Err(Miss::Definitive); }
    Ok((bytes, ct))
}

fn fetch(key: &str, gen: u64) -> Fetched {
    let current = || gen == GEN.load(Ordering::SeqCst);
    let bytes = match cached(key) {
        Some(b) => b,
        None => match download(key) {
            Ok(got) => { if current() { store(key, &got); } got }
            Err(Miss::Definitive) => { if current() { store(key, &[]); } vec![] }
            Err(Miss::Transient) => vec![],
        },
    };
    decode(key, &bytes)
}

/// What a key's picture is fetched from.
fn download(key: &str) -> Result<Vec<u8>, Miss> {
    let (kind, v) = key.split_once(':').ok_or(Miss::Definitive)?;
    match kind {
        "fav" => get_capped(&format!("https://icons.duckduckgo.com/ip3/{}.ico", crate::jmap::pct(v)))
            .and_then(|(b, ct)| if ct.contains("svg") || ct.contains("html") { Err(Miss::Definitive) } else { Ok(b) }),
        "grav" => get_capped(&format!("https://gravatar.com/avatar/{v}?s=128&d=404")).map(|r| r.0),
        "bimi" => {
            let url = txt_records(&format!("default._bimi.{v}")).iter().find_map(|t| bimi_logo_url(t)).ok_or(Miss::Definitive)?;
            get_capped(&url).and_then(|(b, ct)| if ct.contains("svg") || b.starts_with(b"<") { Ok(b) } else { Err(Miss::Definitive) })
        }
        _ => Err(Miss::Definitive),
    }
}

fn decode(key: &str, bytes: &[u8]) -> Fetched {
    if bytes.is_empty() { return Fetched::None; }
    if key.starts_with("bimi:") { return Fetched::Svg(bytes.to_vec()); }
    match raster(bytes) { Some(b) => Fetched::Raster(b), None => Fetched::None }
}

/// Decode an icon or photo and fit it in a square (an icon of 1-2 px is the "no icon" answer).
pub fn raster(bytes: &[u8]) -> Option<SharedPixelBuffer<Rgba8Pixel>> {
    let img = image::load_from_memory(bytes).ok()?;
    if img.width() <= 2 || img.height() <= 2 { return None; }
    let t = img.resize_to_fill(EDGE, EDGE, image::imageops::FilterType::Triangle).to_rgba8();
    Some(SharedPixelBuffer::clone_from_slice(t.as_raw(), t.width(), t.height()))
}

/// The logo URL of a BIMI record ("v=BIMI1; l=https://…/logo.svg; a=…"); https only. The record
/// is read by `bimi::pick_record`, the one parse the sender logo and the verified badge share.
pub fn bimi_logo_url(record: &str) -> Option<String> {
    crate::bimi::pick_record(&[record.to_string()]).and_then(|r| r.logo_url).filter(|u| safe_https_host(u))
}

/// A logo URL named by a sender's DNS may only reach a public host by name: no IP literals,
/// no local or internal names, no other port, no credentials (no probing of the local network).
pub fn safe_https_host(u: &str) -> bool { safe_public_host(u, false) }

/// `safe_https_host`, and with `allow_http` plain http too (a certificate's CRL address is http).
pub fn safe_public_host(u: &str, allow_http: bool) -> bool {
    let Ok(url) = reqwest::Url::parse(u) else { return false };
    let scheme_ok = url.scheme() == "https" || (allow_http && url.scheme() == "http");
    if !scheme_ok || url.port().is_some() || !url.username().is_empty() || url.password().is_some() { return false; }
    // an IP literal (v6 in brackets, or all-numeric v4) is never a brand's logo host
    let Some(d) = url.host_str() else { return false };
    !d.starts_with('[') && d.parse::<std::net::IpAddr>().is_err() && valid_domain(d) && !d.split('.').all(|l| l.chars().all(|c| c.is_ascii_digit()))
}

/// TXT records of a name, through the system resolver.
#[cfg(windows)]
pub(crate) fn txt_records(name: &str) -> Vec<String> {
    use windows::Win32::NetworkManagement::Dns::*;
    let wide: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
    let mut head: *mut DNS_RECORDA = std::ptr::null_mut();
    let mut out = vec![];
    unsafe {
        let status = DnsQuery_W(windows::core::PCWSTR(wide.as_ptr()), DNS_TYPE_TEXT, DNS_QUERY_STANDARD, None, &mut head, None);
        if status.0 != 0 || head.is_null() { return out; }
        let mut rec = head as *mut DNS_RECORDW;
        while !rec.is_null() {
            let r = &*rec;
            if r.wType == DNS_TYPE_TEXT.0 {
                let txt = &r.Data.TXT;
                let arr = txt.pStringArray.as_ptr();
                // a record split in several strings is one value (RFC 7208 §3.3 style)
                let joined: String = (0..txt.dwStringCount as usize).filter_map(|i| (*arr.add(i)).to_string().ok()).collect();
                out.push(joined);
            }
            rec = r.pNext;
        }
        DnsFree(Some(head as *const _), DnsFreeRecordList);
    }
    out
}

#[cfg(not(windows))]
pub(crate) fn txt_records(_name: &str) -> Vec<String> { vec![] }

/// The host of the account's JMAP URL.
pub fn host_of(base: &str) -> String { reqwest::Url::parse(base).ok().and_then(|u| u.host_str().map(str::to_string)).unwrap_or_default() }

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn root_domains_and_validity() {
        assert_eq!(root_domain("newsletter.example.com"), "example.com");
        assert_eq!(root_domain("mail.shop.example.co.uk"), "example.co.uk");
        assert_eq!(root_domain("Example.COM."), "example.com");
        assert!(valid_domain("mail.example.org"));
        assert!(!valid_domain("localhost") && !valid_domain("printer.local") && !valid_domain("a..b") && !valid_domain("-x.com") && !valid_domain("x_y.com"));
    }

    #[test]
    fn candidate_order_follows_bulwark() {
        let all = Prefs { favicons: true, gravatar: true, in_junk: false };
        let c = candidates("Jane@News.Acme.org", Some("news.acme.org"), &all);
        assert_eq!(c.len(), 3);
        assert_eq!(c[0], "bimi:news.acme.org");
        assert!(c[1].starts_with("grav:"));
        assert_eq!(c[2], "fav:acme.org");
        // no logo without a DMARC pass for the From domain itself
        assert_eq!(candidates("a@x.acme.org", Some("acme.org"), &all).len(), 2);
        // personal providers get no favicon; off settings ask for nothing
        assert_eq!(candidates("a@gmail.com", None, &Prefs { favicons: true, ..Default::default() }), Vec::<String>::new());
        assert!(candidates("a@b.com", None, &Prefs::default()).is_empty());
        assert!(candidates("not-an-address", None, &all).is_empty());
    }

    #[test]
    fn junk_and_off_show_nothing() {
        let p = Prefs { favicons: true, gravatar: false, in_junk: false };
        assert!(get("a@b.com", None, &p, true).is_none());
        assert!(get("a@b.com", None, &Prefs::default(), false).is_none());
    }

    #[test]
    fn gravatar_hash_is_sha256_of_the_normalised_address() {
        assert_eq!(gravatar_hash(" Test@Example.com "), gravatar_hash("test@example.com"));
        assert_eq!(gravatar_hash("test@example.com").len(), 64);
    }

    #[test]
    fn bimi_records_and_dmarc() {
        assert_eq!(bimi_logo_url("v=BIMI1; l=https://x.com/l.svg; a=https://x.com/c.pem").as_deref(), Some("https://x.com/l.svg"));
        assert_eq!(bimi_logo_url("v=BIMI1; l=http://x.com/l.svg"), None);
        assert_eq!(bimi_logo_url("v=spf1 include:x"), None);
        assert_eq!(host_of("https://mail.mine.org:443/jmap"), "mail.mine.org");
        // logo URLs only to public names
        for bad in ["https://10.0.0.5/l.svg", "https://localhost/l.svg", "https://[::1]/l.svg", "https://x.local/l.svg", "https://a.com:8443/l.svg", "https://u:p@a.com/l.svg", "https://127.0.0.1/x"] {
            assert!(!safe_https_host(bad), "{bad}");
            assert_eq!(bimi_logo_url(&format!("v=BIMI1; l={bad}")), None);
        }
        assert!(safe_https_host("https://brand.example.com/logo.svg"));
    }

    #[test]
    fn idle_worker_is_counted_out_under_the_queue_lock() {
        let mut w = WORK.lock().unwrap();
        w.0.clear();
        w.1 = 1;
        drop(w);
        assert!(next_job().is_none());
        assert_eq!(WORK.lock().unwrap().1, 0);
    }

    #[test]
    fn rasters_fit_a_square_and_reject_pixels() {
        let mut png = vec![];
        image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(32, 16, image::Rgba([1, 2, 3, 255])))
            .write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png).unwrap();
        let b = raster(&png).unwrap();
        assert_eq!((b.width(), b.height()), (EDGE, EDGE));
        let mut dot = vec![];
        image::DynamicImage::ImageRgba8(image::RgbaImage::new(1, 1)).write_to(&mut std::io::Cursor::new(&mut dot), image::ImageFormat::Png).unwrap();
        assert!(raster(&dot).is_none());
    }

    /// A one-shot local HTTP server: answers each connection with the next canned response.
    fn serve(responses: Vec<Vec<u8>>) -> String {
        use std::io::{Read, Write};
        let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let addr = l.local_addr().unwrap();
        std::thread::spawn(move || {
            for r in responses {
                let Ok((mut c, _)) = l.accept() else { return };
                let mut buf = [0u8; 2048];
                let _ = c.read(&mut buf);
                let _ = c.write_all(&r);
            }
        });
        format!("http://{addr}/avatar")
    }

    fn http_reply(status: &str, ct: &str, body: &[u8]) -> Vec<u8> {
        let head = ["HTTP/1.1 ".to_string() + status, format!("Content-Type: {ct}"), format!("Content-Length: {}", body.len()), "Connection: close".into(), String::new(), String::new()];
        let mut r = head.join("\r\n").into_bytes();
        r.extend_from_slice(body);
        r
    }

    #[test]
    fn an_unknown_key_is_marked_pending_and_started_once() {
        // regression: the first unknown key used to panic with "RefCell already borrowed"
        let started = RefCell::new(vec![]);
        assert!(matches!(look("grav:aa", |k| started.borrow_mut().push(k)), Look::Pending));
        assert!(matches!(look("grav:aa", |k| started.borrow_mut().push(k)), Look::Pending));
        assert_eq!(*started.borrow(), vec!["grav:aa".to_string()]);
        SLOTS.with(|s| s.borrow_mut().insert("grav:aa".into(), Slot::Missing));
        assert!(matches!(look("grav:aa", |_| panic!("not again")), Look::Missing));
        SLOTS.with(|s| s.borrow_mut().insert("grav:aa".into(), Slot::Ready(Pic { image: slint::Image::default(), logo: false })));
        assert!(matches!(look("grav:aa", |_| panic!("not again")), Look::Ready(_)));
    }

    #[test]
    fn gravatar_on_asks_for_the_photo_once_and_falls_back_to_initials() {
        let p = Prefs { favicons: false, gravatar: true, in_junk: false };
        let started = RefCell::new(vec![]);
        let go = || get_with("jane@example.test", None, &p, false, |k| started.borrow_mut().push(k));
        assert!(go().is_none());
        assert!(go().is_none());
        assert_eq!(*started.borrow(), vec![format!("grav:{}", gravatar_hash("jane@example.test"))]);
        // no photo for this address: no picture, and no other candidate is asked for
        SLOTS.with(|s| s.borrow_mut().insert(started.borrow()[0].clone(), Slot::Missing));
        assert!(go().is_none());
        assert_eq!(started.borrow().len(), 1);
        // a photo that arrived is returned
        SLOTS.with(|s| s.borrow_mut().insert(started.borrow()[0].clone(), Slot::Ready(Pic { image: slint::Image::default(), logo: false })));
        assert!(go().is_some_and(|pic| !pic.logo));
    }

    #[test]
    fn a_panic_in_avatar_code_is_contained() {
        assert_eq!(guarded(|| 7), Some(7));
        assert!(guarded(|| -> u8 { panic!("boom") }).is_none());
    }

    #[test]
    fn answers_from_the_gravatar_server_are_classified() {
        let mut png = vec![];
        image::DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(128, 128, image::Rgba([9, 8, 7, 255])))
            .write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png).unwrap();
        let url = serve(vec![
            http_reply("404 Not Found", "text/plain", b"Not Found"),
            http_reply("500 Internal Server Error", "text/plain", b"oops"),
            http_reply("429 Too Many Requests", "text/plain", b"slow down"),
            http_reply("302 Found", "text/plain", b"moved"),
            http_reply("200 OK", "text/html", b"<html>not a picture at all</html>"),
            http_reply("200 OK", "image/png", &vec![0u8; MAX_BYTES + 10]),
            http_reply("200 OK", "image/png", b"x"),
            http_reply("200 OK", "image/png", &png),
        ]);
        let miss = |r: Result<(Vec<u8>, String), Miss>| r.unwrap_err();
        assert_eq!(miss(get_capped(&url)), Miss::Definitive); // 404: no photo, cached as missing
        assert_eq!(miss(get_capped(&url)), Miss::Transient);
        assert_eq!(miss(get_capped(&url)), Miss::Transient);
        assert_eq!(miss(get_capped(&url)), Miss::Definitive); // redirects are not followed
        // an HTML body comes through the download, and the decoder turns it down
        let (html, _) = get_capped(&url).unwrap();
        assert!(matches!(decode("grav:x", &html), Fetched::None));
        assert_eq!(miss(get_capped(&url)), Miss::Definitive); // over the cap
        assert_eq!(miss(get_capped(&url)), Miss::Definitive); // too small to be an image
        let (bytes, ct) = get_capped(&url).unwrap();
        assert_eq!(ct, "image/png");
        assert!(matches!(decode("grav:x", &bytes), Fetched::Raster(b) if (b.width(), b.height()) == (EDGE, EDGE)));
        // a refused connection is a passing failure
        assert_eq!(miss(get_capped("http://127.0.0.1:9/avatar")), Miss::Transient);
    }

    #[test]
    fn undecodable_bytes_give_no_picture() {
        assert!(matches!(decode("grav:x", &[]), Fetched::None));
        assert!(matches!(decode("grav:x", b"GIF89a-but-not-really"), Fetched::None));
        assert!(matches!(decode("fav:example.com", &[0u8; 64]), Fetched::None));
        // a truncated PNG
        let mut png = vec![];
        image::DynamicImage::ImageRgba8(image::RgbaImage::new(16, 16)).write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png).unwrap();
        png.truncate(png.len() / 2);
        assert!(matches!(decode("grav:x", &png), Fetched::None));
    }

    #[test]
    fn safe_mode_turns_the_network_pictures_off() {
        let mut m = serde_json::Map::new();
        m.insert("senderFavicons".into(), json!(true));
        m.insert("gravatarAvatars".into(), json!(true));
        m.insert("showAvatarsInJunk".into(), json!(true));
        let st = crate::settings::Settings::with_prefs(m);
        let on = Prefs::from_settings(&st, false);
        assert!(on.favicons && on.gravatar && on.any());
        let safe = Prefs::from_settings(&st, true);
        assert!(!safe.favicons && !safe.gravatar && !safe.any());
        assert!(safe.in_junk);
    }
}
