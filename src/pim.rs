//! Read-only automation probes for the PIM screens (contacts, files, filters, identities),
//! answered as one line of JSON. Nothing here changes state.
use crate::{AppWindow, CtX};
use serde_json::json;
use slint::{ComponentHandle, Model};

pub fn probe(u: &AppWindow, what: &str) -> String {
    let g = u.global::<CtX>();
    let v = match what {
        "contacts" => json!({
            "app": u.get_app(), "filter": u.get_ct_filter().to_string(), "id": u.get_ct_id().to_string(), "editing": u.get_ct_editing(),
            "cards": crate::contacts::cards().len(), "shown": u.get_contacts().row_count(), "books": crate::contacts::books().len(),
            "photo": g.get_has_photo(), "subtitle": g.get_subtitle().to_string(),
            "lines": g.get_lines().iter().map(|l| json!([l.section.to_string(), l.label.to_string(), l.value.to_string()])).collect::<Vec<_>>(),
            "status": g.get_status().to_string(), "dir": g.get_dir_status().to_string(),
        }),
        "fields" => json!({
            "given": g.get_given().to_string(), "surname": g.get_surname().to_string(), "org": g.get_org().to_string(),
            "fields": g.get_fields().iter().map(|f| json!([f.section.to_string(), f.key.to_string(), f.kind, f.kind2, f.a.to_string(), f.b.to_string()])).collect::<Vec<_>>(),
        }),
        "dupes" => json!({"groups": g.get_dupe_groups(), "status": g.get_dupe_status().to_string(),
            "rows": g.get_dupes().iter().map(|d| json!([d.group, d.id.to_string(), d.name.to_string(), d.keep])).collect::<Vec<_>>()}),
        "import" => json!({"open": g.get_import_open(), "summary": g.get_import_summary().to_string(), "dupes": g.get_import_dupes(), "busy": g.get_import_busy()}),
        "nav" => json!(u.get_ct_nav().iter().map(|n| json!([n.kind.to_string(), n.id.to_string(), n.label.to_string(), n.count.to_string()])).collect::<Vec<_>>()),
        other => return format!("unknown probe {other}"),
    };
    v.to_string()
}
