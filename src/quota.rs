//! Storage quota as the Account page shows it (Bulwark components/settings/account-settings.tsx
//! "Storage usage": "{used} of {total} used", a percentage and a bar; lib/quota.ts picks the
//! quota). RFC 9425 Quota objects: `used`, `hardLimit`, `softLimit`, `warnLimit`, `resourceType`
//! (`octets` or `count`), `scope`, `types`.
//!
//! The pure parts live here (what to show for a Quota/get list); the request is
//! `Client::quotas` and the rows are built in server_settings.rs.
use serde_json::Value;

/// One line on the Account page.
#[derive(Debug, Clone, PartialEq)]
pub struct QuotaEntry {
    pub label: String,
    /// "1.2 GB of 15 GB used".
    pub detail: String,
    /// The right-hand text ("8% used"); empty when the quota has no limit.
    pub text: String,
    /// 0..=100 for the bar; None when there is no limit to measure against.
    pub percent: Option<u32>,
}

/// Bulwark formatFileSize (lib/utils.ts:418): 1024-based, one decimal under 10.
pub fn format_file_size(bytes: u64) -> String {
    if bytes == 0 { return "0 B".into(); }
    let units = ["B", "KB", "MB", "GB", "TB"];
    let mut i = ((bytes as f64).ln() / 1024f64.ln()).floor().max(0.0) as usize;
    i = i.min(units.len() - 1);
    if i == 0 { return format!("{bytes} B"); }
    let mut value = bytes as f64 / 1024f64.powi(i as i32);
    let round = |v: f64| if v < 10.0 { (v * 10.0).round() / 10.0 } else { v.round() };
    let mut r = round(value);
    // 1023.6 KB rounds to 1024 KB: show it as 1 MB instead
    if r >= 1024.0 && i < units.len() - 1 {
        i += 1;
        value /= 1024.0;
        r = (value * 10.0).round() / 10.0;
    }
    if r.fract() == 0.0 { format!("{} {}", r as u64, units[i]) } else { format!("{r} {}", units[i]) }
}

fn positive(v: &Value) -> Option<u64> { v.as_u64().filter(|n| *n > 0) }

/// The limit the user works against: the soft limit when the server sets one below the hard
/// limit (writes start failing there, RFC 9425 §4.1), else the hard limit (Bulwark fromQuota,
/// lib/quota.ts). `limit` is the pre-RFC spelling some servers send. 0 means none.
pub fn effective_limit(q: &Value) -> u64 {
    let hard = positive(&q["hardLimit"]).or_else(|| positive(&q["limit"]));
    match (positive(&q["softLimit"]), hard) {
        (Some(s), Some(h)) if s < h => s,
        (Some(s), None) => s,
        (_, h) => h.unwrap_or(0),
    }
}

/// The hard limit when it is above the one shown (inbound mail still lands up to it).
pub fn hard_above(q: &Value) -> Option<u64> {
    let total = effective_limit(q);
    positive(&q["hardLimit"]).filter(|h| *h > total && total > 0)
}

fn percent(used: u64, total: u64) -> u32 {
    if total == 0 { return 0; }
    ((used as f64 / total as f64 * 100.0).round() as u32).min(100)
}

fn is_octets(q: &Value) -> bool { q["resourceType"] == "octets" }

/// Whether the quota covers mail: no `types`, or Email (RFC 9425 data type name, what Stalwart
/// sends) or Mail (Bulwark accepts both).
pub fn covers_mail(q: &Value) -> bool {
    q["types"].as_array().is_none_or(|t| t.is_empty() || t.iter().any(|x| x == "Email" || x == "Mail"))
}

/// The storage quota to headline: an octets quota covering Mail, account scope first, then the
/// tightest; else any octets quota; else the pre-RFC `mail` shape (Bulwark toStorageQuota).
pub fn pick(list: &[Value]) -> Option<&Value> {
    let octets = || list.iter().filter(|q| is_octets(q));
    let room = |q: &&Value| effective_limit(q).saturating_sub(q["used"].as_u64().unwrap_or(0));
    octets().filter(|q| covers_mail(q)).min_by_key(|q| (q["scope"] != "account", room(q)))
        .or_else(|| octets().min_by_key(room))
        .or_else(|| list.iter().find(|q| q["resourceType"] == "mail" || q["scope"] == "mail"))
}

fn types_text(q: &Value) -> String {
    let t: Vec<&str> = q["types"].as_array().map(|a| a.iter().filter_map(|x| x.as_str()).collect()).unwrap_or_default();
    t.join(", ")
}

/// Label of a quota: the server's own `name`, else what it counts.
fn label(q: &Value, headline: bool) -> String {
    if headline { return "Storage usage".into(); }
    if let Some(n) = q["name"].as_str().filter(|n| !n.trim().is_empty()) { return n.trim().to_string(); }
    let what = if is_octets(q) { "Storage" } else { "Item count" };
    let t = types_text(q);
    let scope = q["scope"].as_str().filter(|s| *s != "account" && !s.is_empty());
    match (t.is_empty(), scope) {
        (true, None) => what.into(),
        (false, None) => format!("{what} ({t})"),
        (true, Some(s)) => format!("{what} ({s})"),
        (false, Some(s)) => format!("{what} ({t}, {s})"),
    }
}

fn amount(q: &Value, n: u64) -> String { if is_octets(q) { format_file_size(n) } else { n.to_string() } }

fn entry(q: &Value, headline: bool) -> QuotaEntry {
    let used = q["used"].as_u64().unwrap_or(0);
    let total = effective_limit(q);
    let label = label(q, headline);
    if total == 0 {
        return QuotaEntry { label, detail: format!("{} used, no limit set", amount(q, used)), text: String::new(), percent: None };
    }
    let mut detail = format!("{} of {} used", amount(q, used), amount(q, total));
    if let Some(h) = hard_above(q) { detail.push_str(&format!(". Incoming mail is accepted up to {}.", amount(q, h))); }
    let p = percent(used, total);
    QuotaEntry { label, detail, text: format!("{p}% used"), percent: Some(p) }
}

/// What the Account page shows for a Quota/get list: the headline mail storage first (Bulwark's
/// one row), then every other quota resource the server reports (count quotas, quotas for other
/// data types), each with its own used / limit. An empty list shows nothing: Stalwart sends no
/// Quota objects for an account without a limit.
pub fn entries(list: &[Value]) -> Vec<QuotaEntry> {
    let head = pick(list);
    let mut out: Vec<QuotaEntry> = head.map(|q| entry(q, true)).into_iter().collect();
    for q in list {
        if head.is_some_and(|h| std::ptr::eq(h, q)) { continue; }
        // a quota that measures nothing (no usage, no limit) is not worth a row
        if q["used"].as_u64().unwrap_or(0) == 0 && effective_limit(q) == 0 { continue; }
        out.push(entry(q, false));
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn file_sizes_like_bulwark() {
        assert_eq!(format_file_size(0), "0 B");
        assert_eq!(format_file_size(512), "512 B");
        assert_eq!(format_file_size(1536), "1.5 KB");
        assert_eq!(format_file_size(10 * 1024), "10 KB");
        assert_eq!(format_file_size(1024 * 1024), "1 MB");
        assert_eq!(format_file_size(15 * 1024 * 1024 * 1024), "15 GB");
        // 1023.6 KB rounds up to 1 MB, not "1024 KB"
        assert_eq!(format_file_size(1_048_500), "1 MB");
    }

    #[test]
    fn soft_limit_is_the_total_when_below_hard() {
        let q = json!({"used": 5, "hardLimit": 100, "softLimit": 80});
        assert_eq!(effective_limit(&q), 80);
        assert_eq!(hard_above(&q), Some(100));
        assert_eq!(effective_limit(&json!({"hardLimit": 100, "softLimit": 150})), 100);
        assert_eq!(effective_limit(&json!({"hardLimit": 100, "softLimit": null})), 100);
        assert_eq!(effective_limit(&json!({"limit": 7})), 7);
        assert_eq!(effective_limit(&json!({"used": 3})), 0);
        assert_eq!(hard_above(&json!({"hardLimit": 100})), None);
    }

    #[test]
    fn picks_mail_octets_quota() {
        let list = vec![
            json!({"id": "1", "resourceType": "count", "scope": "account", "used": 1, "hardLimit": 9}),
            json!({"id": "2", "resourceType": "octets", "scope": "account", "types": ["Contact"], "used": 0, "hardLimit": 2}),
            json!({"id": "3", "resourceType": "octets", "scope": "account", "types": ["Email", "FileNode"], "used": 5, "hardLimit": 1000}),
        ];
        assert_eq!(pick(&list).unwrap()["id"], "3");
        // only a quota for other data: still a storage quota, but no mail one
        assert_eq!(pick(&list[..2]).unwrap()["id"], "2");
        assert!(pick(&list[..1]).is_none());
        // the pre-RFC shape
        assert_eq!(pick(&[json!({"id": "m", "resourceType": "mail", "used": 1, "limit": 5})]).unwrap()["id"], "m");
        assert!(pick(&[]).is_none());
    }

    #[test]
    fn one_quota_makes_bulwarks_row() {
        let list = vec![json!({"id": "3", "resourceType": "octets", "scope": "account", "types": ["Email"], "used": 3 * 1024 * 1024, "hardLimit": 30 * 1024 * 1024})];
        let e = entries(&list);
        assert_eq!(e, vec![QuotaEntry { label: "Storage usage".into(), detail: "3 MB of 30 MB used".into(), text: "10% used".into(), percent: Some(10) }]);
    }

    #[test]
    fn several_quota_resources_each_get_a_row() {
        let list = vec![
            json!({"id": "1", "resourceType": "octets", "scope": "account", "types": ["Email"], "used": 1024, "hardLimit": 2048, "softLimit": 1536}),
            json!({"id": "2", "resourceType": "count", "scope": "account", "types": ["CalendarEvent"], "used": 40, "hardLimit": 1000}),
            json!({"id": "3", "resourceType": "octets", "scope": "domain", "name": "Domain storage", "used": 900, "hardLimit": 1000}),
            json!({"id": "4", "resourceType": "count", "scope": "account", "used": 0}),
        ];
        let e = entries(&list);
        assert_eq!(e.len(), 3);
        assert_eq!(e[0].label, "Storage usage");
        assert_eq!(e[0].detail, "1 KB of 1.5 KB used. Incoming mail is accepted up to 2 KB.");
        assert_eq!(e[0].percent, Some(67));
        assert_eq!(e[1].label, "Item count (CalendarEvent)");
        assert_eq!(e[1].detail, "40 of 1000 used");
        assert_eq!(e[1].percent, Some(4));
        assert_eq!(e[2].label, "Domain storage");
        assert_eq!(e[2].percent, Some(90));
    }

    #[test]
    fn no_limit_shows_usage_without_a_bar_and_over_limit_caps_the_bar() {
        let e = entries(&[json!({"resourceType": "octets", "used": 2048})]);
        assert_eq!(e[0].detail, "2 KB used, no limit set");
        assert_eq!(e[0].percent, None);
        assert_eq!(e[0].text, "");
        let over = entries(&[json!({"resourceType": "octets", "used": 300, "hardLimit": 100})]);
        assert_eq!(over[0].percent, Some(100));
        assert!(entries(&[]).is_empty());
    }
}
