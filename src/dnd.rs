//! Drag and drop, and resizable panes.
//!
//! Bulwark: dragging mail onto a folder or tag (mail-app / sidebar drop targets), the folder
//! tree's reorder and re-parent (lib/folder-tree-dnd.ts: drop above or below a folder to put it
//! next to it, onto it to nest it; the new sibling order is written as sortOrder 1..n, see
//! email-store moveMailbox), files dropped onto folders (file-browser), events moved or resized
//! in the day and week grids (15-minute steps), and the resize handles between the panes
//! (components/layout/resize-handle.tsx; ui-store limits: sidebar 180-400, list 240-600;
//! double-click puts a pane back to its default; widths kept per device).
//!
//! The Slint side tracks the pointer (Pd.dnd-x / dnd-y) and each drop target names itself in
//! Pd.dnd-target while the pointer is over it; the drop lands here.

use crate::{AppWindow, Pd, Shared};
use serde_json::{json, Value};
use slint::{ComponentHandle, Model};
use std::collections::{HashMap, HashSet};

// ---------------- the drag ----------------

pub fn start(u: &AppWindow, kind: &str, id: &str) {
    let pd = u.global::<Pd>();
    let label = match kind {
        "mail" => {
            let n = ids_for_mail(id).len();
            if n > 1 { format!("Move {n} messages") } else {
                crate::mail::email(id).and_then(|m| m["subject"].as_str().map(String::from)).filter(|s| !s.is_empty()).unwrap_or_else(|| "(no subject)".into())
            }
        }
        "folder" => crate::mail::mailbox_info(id).1,
        "file" => crate::files::node_info(id).map(|i| i.0).unwrap_or_default(),
        _ => String::new(),
    };
    pd.set_dnd_kind(kind.into());
    pd.set_dnd_id(id.into());
    pd.set_dnd_label(label.into());
    pd.set_dnd_target("".into());
    pd.set_dnd_active(true);
}

/// Whether a message's only folder is already `f` (a move there would change nothing).
pub fn already_in(m: &Value, f: &str) -> bool {
    m["mailboxIds"].as_object().is_some_and(|o| o.len() == 1 && o.contains_key(f))
}

/// The messages a dragged row stands for: the selection when the row is in it, else the row.
fn ids_for_mail(id: &str) -> Vec<String> {
    let checked = crate::mail::checked_ids();
    if checked.iter().any(|c| c == id) { checked } else { vec![id.to_string()] }
}

pub fn drop(u: &AppWindow, st: &Shared) {
    let pd = u.global::<Pd>();
    let (kind, id, target) = (pd.get_dnd_kind().to_string(), pd.get_dnd_id().to_string(), pd.get_dnd_target().to_string());
    pd.set_dnd_active(false);
    pd.set_dnd_target("".into());
    if target.is_empty() || id.is_empty() { return; }
    match kind.as_str() {
        "mail" => {
            let ids = ids_for_mail(&id);
            if let Some(f) = target.strip_prefix("folder:inside:") {
                if f == "__scheduled" || f == crate::unified::FOLDER_ID { return; }
                // nothing to do for messages already there
                let ids: Vec<String> = ids.into_iter().filter(|i| !crate::mail::email(i).is_some_and(|m| already_in(&m, f))).collect();
                if ids.is_empty() { return; }
                crate::move_ids(u, st, ids, f);
            } else if let Some(t) = target.strip_prefix("tag:") {
                // a drop adds the tag (Bulwark: dropping on a tag labels the messages)
                crate::apply_routed(u, st, ids, &format!("tag:{t}"));
            }
        }
        "folder" => {
            let Some((zone, to)) = target.strip_prefix("folder:").and_then(|r| r.split_once(':')) else { return };
            let list = crate::mail::mailbox_list();
            match plan_folder_drop(&list, &id, to, zone) {
                Ok(patches) if patches.is_empty() => {}
                Ok(patches) => {
                    let Some(c) = st.lock().unwrap().clone() else { return };
                    let w = u.as_weak();
                    std::thread::spawn(move || {
                        let res = c.update("Mailbox/set", &c.account, patches)
                            .and_then(|(_, bad)| if bad.is_empty() { Ok(()) } else { Err(bad.join("; ")) });
                        let (w2, c2) = (w.clone(), c.clone());
                        let _ = w.upgrade_in_event_loop(move |u| {
                            if let Err(e) = res { crate::mail::show_toast(&u, &format!("Folder not moved: {e}"), false); }
                            // always re-read the tree (Bulwark #855: never trust the optimistic order)
                            crate::mail::load_mailboxes(w2, c2, false);
                        });
                    });
                }
                Err(e) => crate::mail::show_toast(u, &e, false),
            }
        }
        "file" => {
            let Some(c) = st.lock().unwrap().clone() else { return };
            if target == "file-trash" {
                // a drop only ever moves to the trash; what would be removed for good needs Delete + confirm
                if crate::files::deletes_permanently(&id) {
                    crate::mail::show_toast(u, "This would be deleted for good. Select it and press Delete instead.", false);
                } else {
                    crate::files::delete(u, c, &id);
                }
                return;
            }
            let Some(dest) = target.strip_prefix("file:") else { return };
            if dest == id { return; }
            // only where the Move to… menu would offer it: not into itself or below it
            if crate::files::move_targets(&id).iter().any(|(k, _)| k == dest) {
                crate::files::move_to(u, c, &id, dest);
            } else if crate::files::parent_of(&id).as_deref() != Some(dest) {
                crate::mail::show_toast(u, "Can't move it there", false);
            }
        }
        _ => {}
    }
}

// ---------------- folder tree (folder-tree-dnd.ts) ----------------

fn role_rank(role: &str) -> i64 {
    match role { "inbox" => 0, "drafts" => 1, "sent" => 2, "archive" => 3, "junk" => 4, "trash" => 5, _ => 10 }
}

fn parent(m: &Value) -> String { m["parentId"].as_str().unwrap_or("").to_string() }
fn id_of(m: &Value) -> String { m["id"].as_str().unwrap_or("").to_string() }

/// Children of `p` in the order the sidebar shows them (role folders first, then sortOrder, name).
fn siblings(list: &[Value], p: &str) -> Vec<String> {
    let mut v: Vec<&Value> = list.iter().filter(|m| parent(m) == p).collect();
    v.sort_by_key(|m| (role_rank(m["role"].as_str().unwrap_or("")), m["sortOrder"].as_i64().unwrap_or(0), m["name"].as_str().unwrap_or("").to_lowercase()));
    v.into_iter().map(id_of).collect()
}

fn descendants(list: &[Value], root: &str) -> HashSet<String> {
    let mut out = HashSet::new();
    let mut stack = vec![root.to_string()];
    while let Some(p) = stack.pop() {
        for m in list.iter().filter(|m| parent(m) == p) {
            let id = id_of(m);
            if out.insert(id.clone()) { stack.push(id); }
        }
    }
    out
}

/// Mailbox/set patches for dropping folder `drag` onto `target`: `inside` nests it there (last),
/// `before` / `after` put it next to the target under the target's parent. The new parent's
/// children get sortOrder 1..n in their new order; only what changes is written.
pub fn plan_folder_drop(list: &[Value], drag: &str, target: &str, zone: &str) -> Result<Vec<(String, Value)>, String> {
    let by_id: HashMap<String, &Value> = list.iter().map(|m| (id_of(m), m)).collect();
    let Some(d) = by_id.get(drag) else { return Err("That folder is gone".into()) };
    let Some(t) = by_id.get(target) else { return Err("Can't drop it there".into()) };
    if drag == target { return Ok(vec![]); }
    let new_parent = if zone == "inside" { target.to_string() } else { parent(t) };
    if new_parent == drag || descendants(list, drag).contains(&new_parent) {
        return Err("A folder can't go inside itself".into());
    }
    let role = d["role"].as_str().unwrap_or("");
    if !role.is_empty() && new_parent != parent(d) {
        return Err("Special folders (Inbox, Sent, …) stay where they are".into());
    }
    let mut order: Vec<String> = siblings(list, &new_parent).into_iter().filter(|x| x != drag).collect();
    match zone {
        "inside" => order.push(drag.to_string()),
        z => {
            let at = order.iter().position(|x| x == target).unwrap_or(order.len());
            order.insert(if z == "after" { at + 1 } else { at }, drag.to_string());
        }
    }
    let mut patches = vec![];
    for (i, id) in order.iter().enumerate() {
        let m = by_id[id];
        let mut p = serde_json::Map::new();
        let so = (i + 1) as i64;
        // role folders sort by role first, so their sortOrder is never rewritten
        let has_role = !m["role"].as_str().unwrap_or("").is_empty();
        if !has_role && m["sortOrder"].as_i64() != Some(so) { p.insert("sortOrder".into(), json!(so)); }
        if id == drag && parent(m) != new_parent {
            p.insert("parentId".into(), if new_parent.is_empty() { Value::Null } else { json!(new_parent) });
        }
        if !p.is_empty() { patches.push((id.clone(), Value::Object(p))); }
    }
    Ok(patches)
}

// ---------------- calendar (day / week grid) ----------------

/// Minutes per grid step when dragging an event.
pub const STEP_MIN: i64 = 15;

/// New start and duration of an event dragged by `days` columns and `minutes` (moved), or
/// stretched by `minutes` at its end (resized; never under one step).
pub fn moved_event(start: chrono::NaiveDateTime, dur: chrono::Duration, days: i64, minutes: i64, resize: bool) -> (chrono::NaiveDateTime, chrono::Duration) {
    let snap = |m: i64| (m as f64 / STEP_MIN as f64).round() as i64 * STEP_MIN;
    if resize {
        let d = (dur.num_minutes() + snap(minutes)).max(STEP_MIN);
        (start, chrono::Duration::minutes(d))
    } else {
        (start + chrono::Duration::days(days) + chrono::Duration::minutes(snap(minutes)), dur)
    }
}

/// ISO 8601 duration for a whole number of minutes (PT1H30M).
pub fn iso_minutes(m: i64) -> String {
    let (h, mm) = (m / 60, m % 60);
    match (h, mm) { (0, mm) => format!("PT{mm}M"), (h, 0) => format!("PT{h}H"), (h, mm) => format!("PT{h}H{mm}M") }
}

/// CalendarEvent/set arguments for a moved event: with attendees the server sends them the
/// new time (RFC 8984 / JMAP Calendars sendSchedulingMessages), like the answer path does.
pub fn update_args(id: &str, patch: Value, e: &Value) -> Value {
    let attendees = e["participants"].as_object().is_some_and(|p| !p.is_empty());
    let mut a = json!({"update": {id: patch}});
    if attendees { a["sendSchedulingMessages"] = json!(true); }
    a
}

pub fn event_dropped(u: &AppWindow, st: &Shared, id: &str, days: i32, minutes: i32, resize: bool) {
    if days == 0 && minutes == 0 { return; }
    let Some(e) = crate::calendar::raw_event(id) else { return };
    if e["recurrenceRules"].as_array().is_some_and(|a| !a.is_empty()) || e["recurrenceId"].is_string() {
        crate::mail::show_toast(u, "Open the event to change a repeating event", false);
        crate::calendar::render(u);
        return;
    }
    if e["showWithoutTime"] == true { crate::calendar::render(u); return; }
    let Some(start) = e["start"].as_str().and_then(|s| chrono::NaiveDateTime::parse_from_str(&s[..s.len().min(19)], "%Y-%m-%dT%H:%M:%S").ok()) else { return };
    let dur = crate::calendar::duration_of(&e);
    let (ns, nd) = moved_event(start, dur, days as i64, minutes as i64, resize);
    // a drag that snaps back to where it was writes nothing
    if ns == start && nd == dur { crate::calendar::render(u); return; }
    let patch = json!({"start": ns.format("%Y-%m-%dT%H:%M:%S").to_string(), "duration": iso_minutes(nd.num_minutes())});
    let Some(c) = st.lock().unwrap().clone() else { return };
    crate::calendar::patch_local(id, &patch);
    crate::calendar::render(u);
    crate::calendar::set_call(u, c, "CalendarEvent/set", update_args(id, patch, &e), "Event not moved");
}

// ---------------- panes ----------------

/// 0 = no width saved: the pane takes the width of the current style (Bento/Nordic, ui/app.slint).
pub const SIDE_DEFAULT: f32 = 0.0;
pub const LIST_DEFAULT: f32 = 0.0;
pub const SIDE_MIN: f32 = 180.0;
pub const SIDE_MAX: f32 = 400.0;
pub const LIST_MIN: f32 = 240.0;
pub const LIST_MAX: f32 = 600.0;

fn panes_path() -> std::path::PathBuf { crate::config_dir().join("panes.json") }

pub fn clamp_panes(side: f32, list: f32) -> (f32, f32) {
    let ok = |v: f32, lo: f32, hi: f32, d: f32| if v.is_finite() && v > 0.0 { v.clamp(lo, hi) } else { d };
    (ok(side, SIDE_MIN, SIDE_MAX, SIDE_DEFAULT), ok(list, LIST_MIN, LIST_MAX, LIST_DEFAULT))
}

pub fn load_panes(u: &AppWindow) {
    let v: Value = std::fs::read_to_string(panes_path()).ok().and_then(|s| serde_json::from_str(&s).ok()).unwrap_or(json!({}));
    let (s, l) = clamp_panes(v["sidebar"].as_f64().unwrap_or(SIDE_DEFAULT as f64) as f32, v["list"].as_f64().unwrap_or(LIST_DEFAULT as f64) as f32);
    let pd = u.global::<Pd>();
    pd.set_side_w(s);
    pd.set_list_w(l);
}

pub fn save_panes(u: &AppWindow) {
    let pd = u.global::<Pd>();
    let (s, l) = clamp_panes(pd.get_side_w(), pd.get_list_w());
    let p = panes_path();
    let _ = std::fs::create_dir_all(p.parent().unwrap());
    let _ = std::fs::write(p, json!({"sidebar": s.round(), "list": l.round()}).to_string());
}

pub fn wire(u: &AppWindow, st: Shared) {
    load_panes(u);
    let pd = u.global::<Pd>();
    let w = u.as_weak();
    pd.on_dnd_start(move |k, id| { if let Some(u) = w.upgrade() { start(&u, &k, &id); } });
    let (w, s) = (u.as_weak(), st.clone());
    pd.on_dnd_drop(move || { if let Some(u) = w.upgrade() { drop(&u, &s); } });
    let w = u.as_weak();
    pd.on_dnd_cancel(move || { if let Some(u) = w.upgrade() { u.global::<Pd>().set_dnd_active(false); u.global::<Pd>().set_dnd_target("".into()); } });
    let (w, s) = (u.as_weak(), st.clone());
    pd.on_event_dropped(move |id, days, minutes, resize| { if let Some(u) = w.upgrade() { event_dropped(&u, &s, &id, days, minutes, resize); } });
    let w = u.as_weak();
    pd.on_panes_changed(move || { if let Some(u) = w.upgrade() { save_panes(&u); } });
    let _ = u.get_folders().row_count();
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDate;

    #[test]
    fn mail_already_in_target_is_skipped() {
        assert!(already_in(&json!({"mailboxIds": {"in": true}}), "in"));
        assert!(!already_in(&json!({"mailboxIds": {"in": true}}), "a"));
        // in two folders: the move still consolidates it
        assert!(!already_in(&json!({"mailboxIds": {"in": true, "a": true}}), "in"));
        assert!(!already_in(&json!({}), "in"));
    }

    #[test]
    fn tiny_drag_snaps_back_to_no_change() {
        let s = NaiveDate::from_ymd_opt(2026, 1, 5).unwrap().and_hms_opt(9, 0, 0).unwrap();
        let d = chrono::Duration::minutes(60);
        assert_eq!(moved_event(s, d, 0, 5, false), (s, d));
        assert_eq!(moved_event(s, d, 0, -7, true), (s, d));
        assert_ne!(moved_event(s, d, 0, 8, false), (s, d));
    }

    #[test]
    fn move_notifies_attendees_only_when_there_are_some() {
        let p = json!({"start": "2026-01-05T10:00:00"});
        let a = update_args("e1", p.clone(), &json!({"participants": {"x": {"email": "a@b"}}}));
        assert_eq!(a["sendSchedulingMessages"], true);
        assert_eq!(a["update"]["e1"], p);
        let a = update_args("e1", p.clone(), &json!({"participants": {}}));
        assert!(a.get("sendSchedulingMessages").is_none());
        assert!(update_args("e1", p, &json!({})).get("sendSchedulingMessages").is_none());
    }

    fn tree() -> Vec<Value> {
        vec![
            json!({"id": "in", "name": "Inbox", "role": "inbox", "parentId": null, "sortOrder": 0}),
            json!({"id": "a", "name": "Alpha", "parentId": null, "sortOrder": 1}),
            json!({"id": "b", "name": "Beta", "parentId": null, "sortOrder": 2}),
            json!({"id": "c", "name": "Gamma", "parentId": null, "sortOrder": 3}),
            json!({"id": "a1", "name": "Child", "parentId": "a", "sortOrder": 1}),
            json!({"id": "a11", "name": "Grandchild", "parentId": "a1", "sortOrder": 1}),
        ]
    }

    #[test]
    fn reorder_among_siblings() {
        // Gamma before Alpha: Inbox stays first (role folders lead) and its sortOrder is left alone
        let p = plan_folder_drop(&tree(), "c", "a", "before").unwrap();
        assert_eq!(p, vec![
            ("c".to_string(), json!({"sortOrder": 2})),
            ("a".to_string(), json!({"sortOrder": 3})),
            ("b".to_string(), json!({"sortOrder": 4})),
        ]);
        // Alpha after Beta
        let p = plan_folder_drop(&tree(), "a", "b", "after").unwrap();
        assert!(p.contains(&("a".to_string(), json!({"sortOrder": 3}))));
        assert!(p.contains(&("c".to_string(), json!({"sortOrder": 4}))));
        // Beta keeps its place: nothing written for it
        assert!(!p.iter().any(|x| x.0 == "b"));
        // no-op drop
        assert!(plan_folder_drop(&tree(), "a", "a", "inside").unwrap().is_empty());
    }

    #[test]
    fn reparent() {
        // Beta into Gamma: becomes its only child
        let p = plan_folder_drop(&tree(), "b", "c", "inside").unwrap();
        assert_eq!(p, vec![("b".to_string(), json!({"sortOrder": 1, "parentId": "c"}))]);
        // the grandchild out to the top level, after Beta
        let p = plan_folder_drop(&tree(), "a11", "b", "after").unwrap();
        let mine = p.iter().find(|x| x.0 == "a11").unwrap();
        assert_eq!(mine.1["parentId"], Value::Null);
        assert_eq!(mine.1["sortOrder"], 4);
    }

    #[test]
    fn refuses_cycles_and_moving_special_folders() {
        assert!(plan_folder_drop(&tree(), "a", "a11", "inside").is_err());
        assert!(plan_folder_drop(&tree(), "a", "a1", "after").is_err());
        assert!(plan_folder_drop(&tree(), "in", "a", "inside").is_err());
        // reordering Inbox among the top level is fine
        assert!(plan_folder_drop(&tree(), "in", "b", "after").is_ok());
        assert!(plan_folder_drop(&tree(), "zz", "a", "inside").is_err());
    }

    #[test]
    fn event_moves_snap_to_quarters() {
        let s = NaiveDate::from_ymd_opt(2026, 11, 16).unwrap().and_hms_opt(9, 0, 0).unwrap();
        let d = chrono::Duration::minutes(60);
        let (ns, nd) = moved_event(s, d, 1, 37, false);
        assert_eq!(ns.format("%Y-%m-%d %H:%M").to_string(), "2026-11-17 09:30");
        assert_eq!(nd, d);
        let (ns, nd) = moved_event(s, d, 0, -52, false);
        assert_eq!(ns.format("%H:%M").to_string(), "08:15");
        assert_eq!(nd, d);
        let (_, nd) = moved_event(s, d, 0, 22, true);
        assert_eq!(nd.num_minutes(), 75);
        let (_, nd) = moved_event(s, d, 0, -200, true);
        assert_eq!(nd.num_minutes(), STEP_MIN);
        assert_eq!(iso_minutes(90), "PT1H30M");
        assert_eq!(iso_minutes(120), "PT2H");
        assert_eq!(iso_minutes(15), "PT15M");
    }

    #[test]
    fn pane_limits() {
        assert_eq!(clamp_panes(100.0, 900.0), (SIDE_MIN, LIST_MAX));
        assert_eq!(clamp_panes(250.0, 300.0), (250.0, 300.0));
        assert_eq!(clamp_panes(f32::NAN, 0.0), (SIDE_DEFAULT, LIST_DEFAULT));
        // nothing saved: the style decides the width
        assert_eq!(clamp_panes(0.0, -5.0), (0.0, 0.0));
    }
}
