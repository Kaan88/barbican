//! Which addresses a calendar feed (a webcal subscription) may be fetched from.
//!
//! A feed address is typed by the user (or synced from elsewhere), so it must not become a way
//! to reach this computer or its network: `http://localhost:8080/`, `http://192.168.1.1/`, the
//! cloud metadata address `169.254.169.254`, or a name whose DNS answer points at one of them
//! (DNS rebinding). The rules follow Bulwark's `lib/security/url-guard.ts` and
//! `lib/security/ip-ranges.ts` (1.21.2), which guard its `/api/fetch-ical` proxy:
//!   * [`is_blocked_ip`] is the pure address policy: loopback, RFC 1918, link-local, CGNAT,
//!     unique-local (fc00::/7), fe80::/10, `0.0.0.0` and `::`, multicast and reserved ranges, and
//!     the IPv6 forms that carry an IPv4 address (IPv4-mapped, IPv4-compatible, NAT64, 6to4) judged
//!     by the IPv4 address inside;
//!   * [`classify_host`] reads the host of a URL the way a browser or the OS resolver would, so the
//!     decimal (`2130706433`), hex (`0x7f000001`), octal (`0177.0.0.1`) and short (`127.1`) forms of
//!     an IPv4 address are seen for what they are, and names like `localhost`, `*.localhost`,
//!     `*.local`, `*.internal` are refused without a lookup;
//!   * [`follow`] fetches in hops: every hop (the first address and each redirect) is checked, the
//!     name is resolved once, every resolved address must pass, and the connection is made to those
//!     very addresses ([`Hop::pin`]), so a second DNS answer cannot change where it goes.
//!
//! The HTTP call itself is the caller's (`cal_extra::fetch_feed`); this module has no network code,
//! which is what makes it testable.
use reqwest::Url;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr, ToSocketAddrs};

/// Redirects followed for one feed (Bulwark `MAX_REDIRECTS`).
pub const MAX_REDIRECTS: usize = 5;

// ---------------- the address policy ----------------

fn v4_from(hi: u16, lo: u16) -> Ipv4Addr { Ipv4Addr::new((hi >> 8) as u8, hi as u8, (lo >> 8) as u8, lo as u8) }

/// An IPv4 address a feed must not be fetched from (Bulwark `PRIVATE_V4`).
pub fn is_blocked_v4(a: Ipv4Addr) -> bool {
    match a.octets() {
        [0, ..] => true,                                   // 0.0.0.0/8, this host
        [10, ..] => true,                                  // RFC 1918
        [127, ..] => true,                                 // loopback
        [169, 254, ..] => true,                            // link-local, cloud metadata
        [172, b, ..] if (16..=31).contains(&b) => true,    // RFC 1918
        [192, 168, ..] => true,                            // RFC 1918
        [192, 0, 0, _] => true,                            // IETF protocol assignments
        [198, 18 | 19, ..] => true,                        // benchmarking
        [100, b, ..] if (64..=127).contains(&b) => true,   // CGNAT 100.64.0.0/10
        [b, ..] if b >= 224 => true,                       // multicast, reserved, broadcast
        _ => false,
    }
}

/// An IPv6 address a feed must not be fetched from (Bulwark `isPrivateV6`).
pub fn is_blocked_v6(a: Ipv6Addr) -> bool {
    let g = a.segments();
    let lead = |n: usize| g[..n].iter().all(|x| *x == 0);
    if lead(7) && (g[7] == 0 || g[7] == 1) { return true; }           // :: and ::1
    if g[0] & 0xff00 == 0xff00 { return true; }                       // ff00::/8 multicast
    if g[0] & 0xffc0 == 0xfe80 { return true; }                       // fe80::/10 link-local
    if g[0] & 0xffc0 == 0xfec0 { return true; }                       // fec0::/10 site-local (deprecated)
    if g[0] & 0xfe00 == 0xfc00 { return true; }                       // fc00::/7 unique local
    if g[0] == 0x2001 && g[1] == 0 { return true; }                   // 2001::/32 Teredo
    if g[0] == 0x64 && g[1] == 0xff9b && g[2] == 1 { return true; }   // 64:ff9b:1::/48 local NAT64
    // forms that carry an IPv4 address are judged by it
    if (lead(5) && g[5] == 0xffff) || lead(6) { return is_blocked_v4(v4_from(g[6], g[7])); } // mapped, compatible
    if g[0] == 0x64 && g[1] == 0xff9b && g[2..6].iter().all(|x| *x == 0) { return is_blocked_v4(v4_from(g[6], g[7])); } // NAT64
    if g[0] == 0x2002 { return is_blocked_v4(v4_from(g[1], g[2])); } // 6to4
    false
}

/// An address a feed must not be fetched from, either family.
pub fn is_blocked_ip(ip: IpAddr) -> bool {
    match ip { IpAddr::V4(a) => is_blocked_v4(a), IpAddr::V6(a) => is_blocked_v6(a) }
}

// ---------------- reading a host ----------------

/// One part of an `inet_aton` style address: decimal, `0x` hex or a leading-zero octal.
fn number_part(p: &str) -> Option<u64> {
    if p.is_empty() { return None; }
    let (digits, radix) = if let Some(h) = p.strip_prefix("0x").or_else(|| p.strip_prefix("0X")) { (h, 16) }
        else if p.len() > 1 && p.starts_with('0') { (&p[1..], 8) } else { (p, 10) };
    if digits.is_empty() { return Some(0); } // "0x" is 0 to the OS resolver
    if !digits.chars().all(|c| c.is_digit(radix)) { return None; }
    u64::from_str_radix(digits, radix).ok()
}

/// An IPv4 address written the way the OS resolver reads it: one to four parts, each decimal, hex
/// (`0x7f`) or octal (`0177`), the last part filling the remaining bytes (`127.1` is 127.0.0.1,
/// `2130706433` is 127.0.0.1). A dotted quad of decimals is the ordinary case.
pub fn parse_ipv4_loose(s: &str) -> Option<Ipv4Addr> {
    let s = s.strip_suffix('.').unwrap_or(s);
    let parts: Vec<&str> = s.split('.').collect();
    if parts.is_empty() || parts.len() > 4 { return None; }
    let nums: Vec<u64> = parts.iter().map(|p| number_part(p)).collect::<Option<_>>()?;
    let (last, head) = nums.split_last()?;
    if head.iter().any(|n| *n > 255) { return None; }
    let room = 8 * (4 - head.len() as u32); // bits the last part may fill
    if *last >> room != 0 { return None; }
    let mut v: u64 = *last;
    for (i, n) in head.iter().enumerate() { v |= n << (8 * (3 - i as u32)); }
    Some(Ipv4Addr::from(v as u32))
}

/// What a URL's host is.
#[derive(Debug, PartialEq)]
pub enum HostClass {
    /// An IP address, written in any form.
    Ip(IpAddr),
    /// A name that has to be resolved.
    Name(String),
    /// A name Barbican refuses without looking it up (localhost, .local, .internal ...).
    LocalName(String),
    /// Looks like an IPv4 address but is not one (`1.2.3.4.5`, `08.1.1.1`, `foo.123`).
    Invalid,
}

const BLOCKED_SUFFIXES: [&str; 6] = [".localhost", ".local", ".internal", ".arpa", ".localdomain", ".home.arpa"];

/// Read the host part of a URL (`url.host_str()`: lower-case, IDN as punycode, IPv6 in brackets).
pub fn classify_host(host: &str) -> HostClass {
    let bracketed = host.trim().starts_with('[');
    let h = host.trim().trim_start_matches('[').trim_end_matches(']').to_ascii_lowercase();
    if h.is_empty() { return HostClass::Invalid; }
    if bracketed || h.contains(':') {
        let bare = h.split('%').next().unwrap_or("");
        return bare.parse::<Ipv6Addr>().map(|a| HostClass::Ip(IpAddr::V6(a))).unwrap_or(HostClass::Invalid);
    }
    let name = h.strip_suffix('.').unwrap_or(&h);
    if name.is_empty() { return HostClass::Invalid; }
    // a host whose last label is a number is an IPv4 address or nothing (WHATWG "ends in a number")
    let last = name.rsplit('.').next().unwrap_or("");
    let numeric_end = !last.is_empty() && (last.chars().all(|c| c.is_ascii_digit()) || last.starts_with("0x"));
    if numeric_end { return parse_ipv4_loose(name).map(|a| HostClass::Ip(IpAddr::V4(a))).unwrap_or(HostClass::Invalid); }
    if name == "localhost" || BLOCKED_SUFFIXES.iter().any(|s| name.ends_with(s)) { return HostClass::LocalName(name.to_string()); }
    HostClass::Name(name.to_string())
}

// ---------------- refusals ----------------

#[derive(Debug, PartialEq)]
pub enum Refusal {
    /// Not a usable address.
    BadUrl,
    /// Not http or https.
    NotHttp,
    /// The host is this computer or a local network, as an address or as a name.
    Local(String),
    /// The name resolved to a local address (only the name is told).
    ResolvesLocal(String),
    /// The name did not resolve.
    Unresolved(String),
    /// A redirect to an address that cannot be used.
    BadRedirect,
    TooManyRedirects,
}

impl Refusal {
    pub fn message(&self) -> String {
        match self {
            Refusal::BadUrl => "This isn't a calendar address.".into(),
            Refusal::NotHttp => "A calendar address must start with webcal://, https:// or http://.".into(),
            Refusal::Local(h) => format!("{h} is this computer or a computer on a local network. Barbican only subscribes to calendars on the internet."),
            Refusal::ResolvesLocal(h) => format!("{h} points to this computer or a local network. Barbican only subscribes to calendars on the internet."),
            Refusal::Unresolved(h) => format!("Could not find the calendar's server ({h})."),
            Refusal::BadRedirect => "The calendar address redirected to an address Barbican cannot use.".into(),
            Refusal::TooManyRedirects => format!("The calendar address redirected more than {MAX_REDIRECTS} times."),
        }
    }
}

/// The warning shown for an address that is not encrypted (it is accepted).
pub const PLAIN_HTTP_WARNING: &str = "This address is not encrypted: anyone on the way can read, or change, the calendar. Use https:// if the site offers it.";

/// Whether a typed address is plain http (`webcal://` is fetched over https).
pub fn is_plain_http(url: &str) -> bool { url.trim().to_ascii_lowercase().starts_with("http://") }

// ---------------- one hop ----------------

/// How names are turned into addresses (the OS resolver; a fake in tests).
pub type Resolver<'a> = &'a dyn Fn(&str, u16) -> std::io::Result<Vec<IpAddr>>;

/// The OS resolver.
pub fn system_resolve(host: &str, port: u16) -> std::io::Result<Vec<IpAddr>> {
    Ok((host, port).to_socket_addrs()?.map(|s| s.ip()).collect())
}

/// One request to make: a checked address.
#[derive(Debug)]
pub struct Hop {
    /// The address to request, without a user name or password.
    pub url: Url,
    /// The host name, for the connection's name check and `Host` header.
    pub host: String,
    /// The addresses that passed the check, to connect to instead of resolving the name again
    /// (None: the host is an IP address and needs no lookup).
    pub pin: Option<Vec<SocketAddr>>,
    /// The user name and password of the address, only for the first address's own origin.
    pub auth: Option<(String, String)>,
}

fn decode(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' {
            if let Some(v) = s.get(i + 1..i + 3).filter(|h| h.bytes().all(|c| c.is_ascii_hexdigit())).and_then(|h| u8::from_str_radix(h, 16).ok()) { out.push(v); i += 3; continue; }
        }
        out.push(b[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// Check one URL: scheme, host, and (for a name) every address it resolves to. Pure but for the
/// resolver it is given.
pub fn plan_hop(url: &Url, resolve: Resolver) -> Result<Hop, Refusal> {
    if !matches!(url.scheme(), "http" | "https") { return Err(Refusal::NotHttp); }
    let host_str = url.host_str().ok_or(Refusal::BadUrl)?;
    let port = url.port_or_known_default().ok_or(Refusal::BadUrl)?;
    let auth = (!url.username().is_empty() || url.password().is_some())
        .then(|| (decode(url.username()), decode(url.password().unwrap_or(""))));
    let mut clean = url.clone();
    let _ = clean.set_username("");
    let _ = clean.set_password(None);
    match classify_host(host_str) {
        HostClass::Invalid => Err(Refusal::BadUrl),
        HostClass::LocalName(h) => Err(Refusal::Local(h)),
        HostClass::Ip(ip) => if is_blocked_ip(ip) { Err(Refusal::Local(host_str.trim_matches(['[', ']']).to_string())) } else {
            Ok(Hop { url: clean, host: host_str.to_string(), pin: None, auth })
        },
        HostClass::Name(name) => {
            let ips = resolve(&name, port).map_err(|_| Refusal::Unresolved(name.clone()))?;
            if ips.is_empty() { return Err(Refusal::Unresolved(name)); }
            // one blocked answer among the others refuses the name: the client could pick it
            if ips.iter().any(|ip| is_blocked_ip(*ip)) { return Err(Refusal::ResolvesLocal(name)); }
            let pin = ips.into_iter().map(|ip| SocketAddr::new(ip, port)).collect();
            // pinned under the URL's own spelling: reqwest looks the override up by the exact host the
            // URL carries, so `example.org.` (trailing dot) must not be pinned as `example.org`
            Ok(Hop { url: clean, host: host_str.to_string(), pin: Some(pin), auth })
        }
    }
}

/// An address typed in the subscription dialog, checked without a lookup: what is refused by its
/// shape alone (a local address or name, a bad scheme). The lookup of a name happens at the fetch.
pub fn precheck(url: &str) -> Result<(), Refusal> {
    let u = Url::parse(url.trim()).map_err(|_| Refusal::BadUrl)?;
    plan_hop(&u, &|_, _| Ok(vec![IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))])).map(|_| ())
}

/// What one request answered.
pub enum Step {
    Done(Vec<u8>),
    /// A redirect: its `Location`, if it had one.
    Redirect(Option<String>),
}

/// Fetch `start` hop by hop: every address (the first, and each redirect target) is checked by
/// [`plan_hop`] before `fetch` is asked to request it, `fetch` must not follow redirects itself
/// and must connect to `hop.pin` when it is set.
pub fn follow(start: &str, resolve: Resolver, mut fetch: impl FnMut(&Hop) -> Result<Step, String>) -> Result<Vec<u8>, String> {
    let first = Url::parse(start.trim()).map_err(|_| Refusal::BadUrl.message())?;
    let origin = first.origin();
    let creds = (!first.username().is_empty() || first.password().is_some()).then(|| (decode(first.username()), decode(first.password().unwrap_or(""))));
    let mut url = first;
    for hop_no in 0..=MAX_REDIRECTS {
        let mut hop = plan_hop(&url, resolve).map_err(|r| if hop_no == 0 { r.message() } else { redirect_message(r) })?;
        // the sign-in details of the address never go to another origin
        hop.auth = if url.origin() == origin { creds.clone() } else { None };
        match fetch(&hop)? {
            Step::Done(body) => return Ok(body),
            Step::Redirect(location) => {
                let loc = location.as_deref().map(str::trim).filter(|l| !l.is_empty()).ok_or_else(|| Refusal::BadRedirect.message())?;
                let next = hop.url.join(loc).map_err(|_| Refusal::BadRedirect.message())?;
                if !next.username().is_empty() || next.password().is_some() { return Err(Refusal::BadRedirect.message()); }
                if hop_no == MAX_REDIRECTS { return Err(Refusal::TooManyRedirects.message()); }
                url = next;
            }
        }
    }
    Err(Refusal::TooManyRedirects.message())
}

fn redirect_message(r: Refusal) -> String {
    match r {
        Refusal::Local(h) | Refusal::ResolvesLocal(h) => format!("The calendar address redirected to {h}, which is this computer or a local network. Barbican does not follow it."),
        Refusal::NotHttp | Refusal::BadUrl => Refusal::BadRedirect.message(),
        other => other.message(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::{Cell, RefCell};

    fn v4(s: &str) -> Ipv4Addr { s.parse().unwrap() }
    fn v6(s: &str) -> Ipv6Addr { s.parse().unwrap() }
    fn blocked(s: &str) -> bool { is_blocked_ip(s.parse().unwrap()) }
    fn u(s: &str) -> Url { Url::parse(s).unwrap() }
    fn public(_: &str, _: u16) -> std::io::Result<Vec<IpAddr>> { Ok(vec!["93.184.216.34".parse().unwrap()]) }

    #[test]
    fn ipv4_policy() {
        for a in ["0.0.0.0", "0.1.2.3", "127.0.0.1", "127.255.255.254", "10.0.0.1", "10.255.255.255", "172.16.0.1", "172.31.255.255", "192.168.0.1",
            "192.168.255.255", "169.254.169.254", "169.254.0.1", "100.64.0.1", "100.127.255.255", "224.0.0.1", "239.255.255.255", "240.0.0.1",
            "255.255.255.255", "198.18.0.1", "198.19.255.255", "192.0.0.8"] {
            assert!(blocked(a), "{a} must be refused");
        }
        for a in ["8.8.8.8", "1.1.1.1", "93.184.216.34", "172.15.255.255", "172.32.0.1", "192.167.255.255", "192.169.0.1", "100.63.255.255",
            "100.128.0.1", "169.253.255.255", "169.255.0.1", "198.17.255.255", "198.20.0.1", "223.255.255.255", "11.0.0.1", "126.255.255.255", "128.0.0.1", "192.0.1.1"] {
            assert!(!blocked(a), "{a} must be allowed");
        }
    }

    #[test]
    fn ipv6_policy() {
        for a in ["::", "::1", "fe80::1", "febf::1", "fe80::abcd:1", "fec0::1", "fc00::1", "fd12:3456::1", "fdff::1", "ff02::1", "ff00::",
            "2001::1", "2001:0:4136:e378:8000:63bf:3fff:fdd2", "64:ff9b:1::1",
            // carrying an IPv4 address that is not allowed
            "::ffff:127.0.0.1", "::ffff:7f00:1", "::ffff:10.1.2.3", "::ffff:192.168.0.1", "::ffff:169.254.169.254", "::ffff:0:0", "::127.0.0.1", "::10.0.0.1",
            "64:ff9b::7f00:1", "64:ff9b::a00:1", "2002:7f00:1::", "2002:c0a8:101::1", "2002:a9fe:a9fe::"] {
            assert!(blocked(a), "{a} must be refused");
        }
        for a in ["2606:4700:4700::1111", "2a00:1450:4001:81b::200e", "2001:db8::1", "2001:4860:4860::8888", "fe7f::1", "fbff::1", "2fec::1",
            // carrying a public IPv4 address
            "::ffff:8.8.8.8", "::ffff:808:808", "64:ff9b::808:808", "2002:808:808::", "::8.8.8.8"] {
            assert!(!blocked(a), "{a} must be allowed");
        }
        assert!(is_blocked_v6(v6("::1")) && !is_blocked_v6(v6("2606:4700::1")));
        assert!(is_blocked_v4(v4("127.0.0.1")) && !is_blocked_v4(v4("1.1.1.1")));
    }

    #[test]
    fn ipv4_in_every_written_form() {
        let lo = Some(v4("127.0.0.1"));
        for s in ["127.0.0.1", "2130706433", "0x7f000001", "0X7F000001", "017700000001", "0177.0.0.1", "0x7f.0.0.1", "0x7f.0x0.0x0.0x1", "127.1", "127.0.1", "0x7f.1",
            "0177.0.0.01", "127.0.0.1.", "0177.1", "0x7f.0x1"] {
            assert_eq!(parse_ipv4_loose(s), lo, "{s}");
        }
        assert_eq!(parse_ipv4_loose("0"), Some(v4("0.0.0.0")));
        assert_eq!(parse_ipv4_loose("0x"), Some(v4("0.0.0.0")));
        assert_eq!(parse_ipv4_loose("4294967295"), Some(v4("255.255.255.255")));
        assert_eq!(parse_ipv4_loose("3232235777"), Some(v4("192.168.1.1")));
        assert_eq!(parse_ipv4_loose("0xc0a80101"), Some(v4("192.168.1.1")));
        assert_eq!(parse_ipv4_loose("0300.0250.0.1"), Some(v4("192.168.0.1")));
        assert_eq!(parse_ipv4_loose("169.16689662"), Some(v4("169.254.169.254")));
        assert_eq!(parse_ipv4_loose("8.8.8.8"), Some(v4("8.8.8.8")));
        for s in ["", ".", "4294967296", "256.1.1.1", "1.16777216", "1.2.65536", "1.2.3.256", "1.2.3.4.5", "1..2", "08.1.1.1", "0xg1", "1.2.3.-4", "a.b.c.d", "0x100000000", "99999999999999999999"] {
            assert_eq!(parse_ipv4_loose(s), None, "{s}");
        }
    }

    #[test]
    fn hosts_are_classified() {
        assert_eq!(classify_host("example.com"), HostClass::Name("example.com".into()));
        assert_eq!(classify_host("Example.COM."), HostClass::Name("example.com".into()));
        assert_eq!(classify_host("xn--bcher-kva.example"), HostClass::Name("xn--bcher-kva.example".into()));
        assert_eq!(classify_host("cal.0x7f.example"), HostClass::Name("cal.0x7f.example".into()));
        for h in ["localhost", "LOCALHOST", "localhost.", "app.localhost", "printer.local", "nas.internal", "x.localdomain", "1.0.0.127.in-addr.arpa", "router.home.arpa"] {
            assert!(matches!(classify_host(h), HostClass::LocalName(_)), "{h}");
        }
        assert_eq!(classify_host("[::1]"), HostClass::Ip(IpAddr::V6(v6("::1"))));
        assert_eq!(classify_host("::ffff:7f00:1"), HostClass::Ip(IpAddr::V6(v6("::ffff:7f00:1"))));
        assert_eq!(classify_host("2130706433"), HostClass::Ip(IpAddr::V4(v4("127.0.0.1"))));
        assert_eq!(classify_host("0x7f.1"), HostClass::Ip(IpAddr::V4(v4("127.0.0.1"))));
        assert_eq!(classify_host("0177.0.0.1"), HostClass::Ip(IpAddr::V4(v4("127.0.0.1"))));
        assert_eq!(classify_host("foo.123"), HostClass::Invalid);
        assert_eq!(classify_host("1.2.3.4.5"), HostClass::Invalid);
        assert_eq!(classify_host("[nonsense]"), HostClass::Invalid);
        assert_eq!(classify_host(""), HostClass::Invalid);
    }

    #[test]
    fn urls_with_hidden_local_hosts_are_refused() {
        for s in ["http://localhost/a.ics", "https://localhost:8443/a.ics", "http://LocalHost./a.ics", "http://127.0.0.1/a.ics", "http://127.1/a.ics", "http://2130706433/a.ics",
            "http://0x7f000001/a.ics", "http://0177.0.0.1/a.ics", "http://0/a.ics", "http://0.0.0.0/a.ics", "http://[::]/a.ics", "http://[::1]/a.ics",
            "http://[::ffff:127.0.0.1]/a.ics", "http://[::ffff:7f00:1]/a.ics", "http://[0:0:0:0:0:ffff:7f00:1]/a.ics", "http://[fe80::1]/a.ics", "http://[fd00::1]:8080/a.ics",
            "http://10.0.0.5/a.ics", "http://192.168.1.1/a.ics", "http://3232235777/a.ics", "http://172.16.0.1/a.ics", "http://169.254.169.254/latest/meta-data",
            "http://100.64.0.1/a.ics", "https://nas.local/a.ics", "https://app.localhost/a.ics", "https://user:pw@127.0.0.1/a.ics"] {
            let r = plan_hop(&u(s), &|_, _| panic!("no lookup for {s}"));
            assert!(matches!(r, Err(Refusal::Local(_))), "{s}: {r:?}");
        }
        for s in ["http://8.8.8.8/a.ics", "https://[2606:4700:4700::1111]/a.ics", "http://0x08080808/a.ics", "http://134744072/a.ics"] {
            let h = plan_hop(&u(s), &|_, _| panic!("no lookup for {s}")).unwrap_or_else(|e| panic!("{s}: {e:?}"));
            assert!(h.pin.is_none());
        }
        assert_eq!(plan_hop(&u("ftp://example.com/a.ics"), &public).unwrap_err(), Refusal::NotHttp);
        assert_eq!(precheck("https://localhost/x"), Err(Refusal::Local("localhost".into())));
        assert_eq!(precheck("http://192.168.0.1/x"), Err(Refusal::Local("192.168.0.1".into())));
        assert_eq!(precheck("https://example.com/x.ics"), Ok(()));
        assert_eq!(precheck("not a url"), Err(Refusal::BadUrl));
        assert!(Refusal::Local("localhost".into()).message().contains("localhost"));
    }

    #[test]
    fn a_name_is_resolved_once_checked_and_pinned() {
        let calls = Cell::new(0);
        let r = |h: &str, p: u16| -> std::io::Result<Vec<IpAddr>> { calls.set(calls.get() + 1); assert_eq!((h, p), ("cal.example.org", 8443)); Ok(vec!["93.184.216.34".parse().unwrap(), "2606:2800::1".parse().unwrap()]) };
        let hop = plan_hop(&u("https://cal.example.org:8443/a.ics"), &r).unwrap();
        assert_eq!(calls.get(), 1);
        assert_eq!(hop.host, "cal.example.org");
        // a trailing dot stays in the pinned host name (the lookup still uses the dotless name)
        let dotted_url = u("http://x.example.org./");
        let seen = std::cell::RefCell::new(String::new());
        let dotted = plan_hop(&dotted_url, &|h, _| { *seen.borrow_mut() = h.to_string(); Ok(vec!["93.184.216.34".parse().unwrap()]) }).unwrap();
        assert_eq!(dotted.host, dotted_url.host_str().unwrap());
        assert_eq!(dotted.host, "x.example.org.");
        assert_eq!(*seen.borrow(), "x.example.org");
        assert_eq!(hop.pin.as_deref(), Some(&["93.184.216.34:8443".parse().unwrap(), "[2606:2800::1]:8443".parse().unwrap()][..]));
        // a name that resolves to a local address is refused, one bad answer among good ones too
        for bad in ["127.0.0.1", "10.1.1.1", "192.168.1.9", "169.254.169.254", "::1", "fd00::5", "::ffff:127.0.0.1", "0.0.0.0", "fe80::1"] {
            let ip: IpAddr = bad.parse().unwrap();
            let only = plan_hop(&u("http://rebind.example.net/a.ics"), &move |_, _| Ok(vec![ip]));
            assert_eq!(only.unwrap_err(), Refusal::ResolvesLocal("rebind.example.net".into()), "{bad}");
            let mixed = plan_hop(&u("http://rebind.example.net/a.ics"), &move |_, _| Ok(vec!["93.184.216.34".parse().unwrap(), ip]));
            assert_eq!(mixed.unwrap_err(), Refusal::ResolvesLocal("rebind.example.net".into()), "{bad}");
        }
        assert_eq!(plan_hop(&u("http://nowhere.example/"), &|_, _| Err(std::io::Error::other("nxdomain"))).unwrap_err(), Refusal::Unresolved("nowhere.example".into()));
        assert_eq!(plan_hop(&u("http://nowhere.example/"), &|_, _| Ok(vec![])).unwrap_err(), Refusal::Unresolved("nowhere.example".into()));
    }

    #[test]
    fn the_sign_in_of_the_address_is_split_off_and_decoded() {
        let h = plan_hop(&u("https://ann%40example.test:p%3Aw%20d@cal.example.org/a.ics"), &public).unwrap();
        assert_eq!(h.url.as_str(), "https://cal.example.org/a.ics");
        assert_eq!(h.auth, Some(("ann@example.test".into(), "p:w d".into())));
        assert_eq!(plan_hop(&u("https://cal.example.org/a.ics"), &public).unwrap().auth, None);
        assert_eq!(decode("100%"), "100%");
        assert_eq!(decode("%zz%4"), "%zz%4");
        assert_eq!(decode("a%2Fb"), "a/b");
    }

    /// Run `follow` over scripted answers; returns the result and the urls requested.
    fn run(start: &str, resolve: Resolver, script: Vec<Step>) -> (Result<Vec<u8>, String>, Vec<(String, Option<Vec<SocketAddr>>, bool)>) {
        let seen = RefCell::new(vec![]);
        let script = RefCell::new(script.into_iter());
        let r = follow(start, resolve, |hop| {
            seen.borrow_mut().push((hop.url.to_string(), hop.pin.clone(), hop.auth.is_some()));
            Ok(script.borrow_mut().next().expect("no more answers"))
        });
        (r, seen.into_inner())
    }
    fn go(l: &str) -> Step { Step::Redirect(Some(l.into())) }

    #[test]
    fn every_redirect_hop_is_checked() {
        // a feed that redirects to another public host (relative and absolute) is followed
        let (r, seen) = run("https://a.example.org/x.ics", &public, vec![go("/y.ics"), go("https://b.example.org/z.ics"), Step::Done(b"ok".to_vec())]);
        assert_eq!(r.unwrap(), b"ok");
        assert_eq!(seen.iter().map(|s| s.0.as_str()).collect::<Vec<_>>(), ["https://a.example.org/x.ics", "https://a.example.org/y.ics", "https://b.example.org/z.ics"]);
        // to loopback, to the LAN, to metadata, in tricky forms: refused, and never requested
        for to in ["http://127.0.0.1/x.ics", "http://localhost:8080/", "http://192.168.1.1/", "http://169.254.169.254/latest", "http://2130706433/", "http://[::ffff:7f00:1]/", "http://0x7f.1/", "http://printer.local/x"] {
            let (r, seen) = run("https://a.example.org/x.ics", &public, vec![go(to), Step::Done(b"leak".to_vec())]);
            let e = r.unwrap_err();
            assert!(e.contains("redirected to"), "{to}: {e}");
            assert_eq!(seen.len(), 1, "{to} must not be requested");
        }
        // to a name that resolves to a private address (rebinding on the second hop)
        let r = |h: &str, _p: u16| -> std::io::Result<Vec<IpAddr>> { Ok(vec![if h == "evil.example.net" { "10.0.0.7".parse().unwrap() } else { "93.184.216.34".parse().unwrap() }]) };
        let (res, seen) = run("https://a.example.org/x.ics", &r, vec![go("https://evil.example.net/x.ics"), Step::Done(b"leak".to_vec())]);
        assert!(res.unwrap_err().contains("evil.example.net"));
        assert_eq!(seen.len(), 1);
        // a redirect to another scheme, with a login in it, or without a Location
        for to in ["ftp://a.example.org/x", "file:///etc/passwd", "https://u:p@b.example.org/x"] {
            let (res, seen) = run("https://a.example.org/x.ics", &public, vec![go(to)]);
            assert!(res.is_err() && seen.len() == 1, "{to}");
        }
        assert!(run("https://a.example.org/x.ics", &public, vec![Step::Redirect(None)]).0.is_err());
        assert!(run("https://a.example.org/x.ics", &public, vec![go("  ")]).0.is_err());
        // https to http is allowed (plain http is accepted with a warning), the hop is checked all the same
        assert!(run("https://a.example.org/x.ics", &public, vec![go("http://b.example.org/x"), Step::Done(vec![])]).0.is_ok());
    }

    #[test]
    fn five_redirects_are_followed_the_sixth_is_not() {
        let mut five: Vec<Step> = (0..5).map(|i| go(&format!("/r{i}"))).collect();
        five.push(Step::Done(b"end".to_vec()));
        assert_eq!(run("https://a.example.org/", &public, five).0.unwrap(), b"end");
        let six: Vec<Step> = (0..6).map(|i| go(&format!("/r{i}"))).collect();
        let (r, seen) = run("https://a.example.org/", &public, six);
        assert!(r.unwrap_err().contains("more than 5"));
        assert_eq!(seen.len(), 6);
    }

    #[test]
    fn the_connection_goes_to_the_address_that_was_checked() {
        // a rebinding server: public on the first question, loopback on every later one
        let asked = Cell::new(0);
        let r = |_: &str, _: u16| -> std::io::Result<Vec<IpAddr>> { asked.set(asked.get() + 1); Ok(vec![if asked.get() == 1 { "93.184.216.34".parse().unwrap() } else { "127.0.0.1".parse().unwrap() }]) };
        let (res, seen) = run("https://rebind.example.org/x.ics", &r, vec![Step::Done(b"ok".to_vec())]);
        assert!(res.is_ok());
        assert_eq!(asked.get(), 1, "the name is resolved once per hop");
        assert_eq!(seen[0].1.as_deref(), Some(&["93.184.216.34:443".parse().unwrap()][..]), "and the request is pinned to that answer");
    }

    #[test]
    fn credentials_stay_on_the_origin_they_were_typed_for() {
        let (r, seen) = run("https://ann%40example.test:secret@a.example.org/x.ics", &public, vec![go("/y.ics"), go("https://b.example.org/z.ics"), go("https://a.example.org/w.ics"), Step::Done(vec![])]);
        assert!(r.is_ok());
        assert_eq!(seen.iter().map(|s| s.2).collect::<Vec<_>>(), [true, true, false, true]);
        assert!(seen.iter().all(|s| !s.0.contains("secret") && !s.0.contains('@')), "no hop's address carries the login");
        // the first address is refused before anything is requested
        let (r, seen) = run("http://admin:admin@192.168.0.1/cal.ics", &public, vec![Step::Done(vec![])]);
        assert!(r.unwrap_err().contains("192.168.0.1"));
        assert!(seen.is_empty());
        assert!(run("nonsense", &public, vec![]).0.is_err());
    }

    #[test]
    fn plain_http_is_recognised() {
        assert!(is_plain_http("http://example.com/a.ics"));
        assert!(is_plain_http("  HTTP://example.com/a.ics"));
        assert!(!is_plain_http("https://example.com/a.ics"));
        assert!(!is_plain_http("webcal://example.com/a.ics"));
        assert!(PLAIN_HTTP_WARNING.contains("not encrypted"));
    }
}
