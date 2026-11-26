//! The mail search box: suggestions (recent searches, people), query-syntax chips, recent
//! searches, and highlighted search hits (SearchSnippet/get).
//!
//! Bulwark: components/search/search-box.tsx + search-suggestions.tsx (dropdown, arrows, Enter,
//! Escape), components/search/search-chips.tsx (one removable chip per active filter, "Clear
//! all" from two), lib/search-snippet.ts + search-snippet-text.tsx (the server's `<mark>`ed
//! subject and preview in the list instead of the plain ones).

use crate::gsearch;
use crate::search_history::{self, Suggestion};
use crate::{AppWindow, Pd, SearchChip, SuggestRow};
use serde_json::{json, Value};
use slint::{ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;
use std::sync::Arc;

thread_local! {
    static SUGG: RefCell<Vec<Suggestion>> = const { RefCell::new(Vec::new()) };
    /// Snippets by list id: (subject segments, preview segments), only those with a mark.
    static SNIP: RefCell<HashMap<String, (Vec<(String, bool)>, Vec<(String, bool)>)>> = RefCell::new(HashMap::new());
}

// ---------------- suggestions ----------------

/// The search text changed (or the box got focus): rebuild the dropdown.
pub fn edited(u: &AppWindow, text: &str) {
    let recent = search_history::load();
    let people = if text.trim().is_empty() { vec![] } else {
        let mut cands = crate::contacts::people();
        cands.extend(crate::mail::senders());
        search_history::people_matching(text, &cands)
    };
    let list = search_history::build(text, &recent, &people);
    let rows: Vec<SuggestRow> = list.iter().enumerate().map(|(i, s)| match s {
        Suggestion::Recent(q) => SuggestRow {
            header: if i == 0 { "Recent searches".into() } else { "".into() },
            icon: "\u{E81C}".into(), label: q.as_str().into(), sub: "".into(), removable: true,
        },
        Suggestion::Person { name, email } => {
            let first = list.iter().position(|x| matches!(x, Suggestion::Person { .. })) == Some(i);
            SuggestRow {
                header: if first { "People".into() } else { "".into() },
                icon: "\u{E77B}".into(),
                label: format!("Search mail from {email}").into(),
                sub: name.as_str().into(), removable: false,
            }
        }
    }).collect();
    SUGG.with(|s| *s.borrow_mut() = list);
    let pd = u.global::<Pd>();
    pd.set_sugg_rows(ModelRc::new(VecModel::from(rows)));
    pd.set_sugg_active(-1);
}

pub fn move_active(u: &AppWindow, d: i32) -> bool {
    let pd = u.global::<Pd>();
    let n = SUGG.with(|s| s.borrow().len()) as i32;
    if n == 0 || pd.get_sugg_hidden() { return false; }
    // Bulwark: down from the box enters the list; up from the first row leaves it
    let i = pd.get_sugg_active();
    pd.set_sugg_active(if d > 0 { (i + 1).min(n - 1) } else { (i - 1).max(-1) });
    true
}

/// Enter in the box: a highlighted suggestion wins over searching the typed text.
pub fn accept(u: &AppWindow) -> bool {
    let i = u.global::<Pd>().get_sugg_active();
    if i < 0 { return false; }
    pick(u, i);
    true
}

pub fn pick(u: &AppWindow, i: i32) {
    let Some(s) = SUGG.with(|s| s.borrow().get(i.max(0) as usize).cloned()) else { return };
    let q = match s { Suggestion::Recent(q) => q, Suggestion::Person { email, .. } => search_history::person_query(&email) };
    let pd = u.global::<Pd>();
    pd.set_sugg_active(-1);
    pd.set_sugg_hidden(true);
    u.set_search(q.clone().into());
    u.invoke_do_search(q.into());
}

pub fn remove(u: &AppWindow, i: i32) {
    let Some(Suggestion::Recent(q)) = SUGG.with(|s| s.borrow().get(i.max(0) as usize).cloned()) else { return };
    search_history::remove(&q);
    edited(u, &u.get_search());
}

/// A search ran: remember it, hide the dropdown, show its chips.
pub fn searched(u: &AppWindow, text: &str) {
    if !text.trim().is_empty() { search_history::add(text); }
    u.global::<Pd>().set_sugg_hidden(true);
    refresh_chips(u);
}

// ---------------- chips ----------------

/// One chip per operator in the search text and per field of the filter panel.
pub fn refresh_chips(u: &AppWindow) {
    let p = gsearch::parse(&u.get_search());
    let mut chips: Vec<SearchChip> = gsearch::chips(&p).into_iter()
        .map(|(k, l, v)| SearchChip { key: format!("q:{k}").into(), label: l.into(), value: v.into() }).collect();
    if u.get_sf_active() {
        for (k, l, v) in [("from", "From", u.get_sf_from()), ("to", "To", u.get_sf_to()), ("subject", "Subject", u.get_sf_subject()), ("body", "Body", u.get_sf_body()),
                          ("after", "After", u.get_sf_after()), ("before", "Before", u.get_sf_before()), ("min", "Larger than (KB)", u.get_sf_min()), ("max", "Smaller than (KB)", u.get_sf_max())] {
            if !v.trim().is_empty() { chips.push(SearchChip { key: format!("sf:{k}").into(), label: l.into(), value: v.trim().into() }); }
        }
        if u.get_sf_unread() { chips.push(SearchChip { key: "sf:unread".into(), label: "Unread".into(), value: "".into() }); }
        if u.get_sf_starred() { chips.push(SearchChip { key: "sf:starred".into(), label: "Starred".into(), value: "Yes".into() }); }
        if u.get_sf_attach() { chips.push(SearchChip { key: "sf:attach".into(), label: "Attachments".into(), value: "Yes".into() }); }
    }
    u.global::<Pd>().set_chips(ModelRc::new(VecModel::from(chips)));
}

/// Remove one chip ("" removes them all) and run the search again.
pub fn remove_chip(u: &AppWindow, key: &str) {
    let mut sf_changed = false;
    if key.is_empty() {
        let p = gsearch::parse(&u.get_search());
        let mut q = u.get_search().to_string();
        for (k, _, _) in gsearch::chips(&p) { q = gsearch::without_operator(&q, k); }
        u.set_search(q.into());
        if u.get_sf_active() { u.invoke_sf_clear(); }
    } else if let Some(k) = key.strip_prefix("q:") {
        u.set_search(gsearch::without_operator(&u.get_search(), k).into());
    } else if let Some(k) = key.strip_prefix("sf:") {
        sf_changed = true;
        match k {
            "from" => u.set_sf_from("".into()), "to" => u.set_sf_to("".into()), "subject" => u.set_sf_subject("".into()),
            "body" => u.set_sf_body("".into()), "after" => u.set_sf_after("".into()), "before" => u.set_sf_before("".into()),
            "min" => u.set_sf_min("".into()), "max" => u.set_sf_max("".into()),
            "unread" => u.set_sf_unread(false), "starred" => u.set_sf_starred(false), "attach" => u.set_sf_attach(false),
            _ => {}
        }
    }
    if sf_changed { u.invoke_sf_apply(); } else if !key.is_empty() || !u.get_sf_active() { u.invoke_do_search(u.get_search()); }
    refresh_chips(u);
}

// ---------------- SearchSnippet/get ----------------

/// After a list page loaded: ask the server why each hit matched (RFC 8621 §5.1), when the
/// filter has a term to highlight. Rows without a mark keep their plain subject and preview.
pub fn fetch_snippets(w: crate::ui_types::Weak<AppWindow>, c: Arc<crate::jmap::Client>, filter: Value, ids: Vec<String>) {
    // the merged lists are not one JMAP filter of this account
    if !gsearch::has_snippet_terms(&filter) || filter["unified"] == true || filter["scheduled"] == true {
        SNIP.with(|s| s.borrow_mut().clear());
        return;
    }
    let ids: Vec<String> = ids.into_iter().filter(|i| !SNIP.with(|s| s.borrow().contains_key(i))).collect();
    if ids.is_empty() { return; }
    std::thread::spawn(move || {
        let sf = gsearch::snippet_filter(&filter);
        let mut found: Vec<(String, Vec<(String, bool)>, Vec<(String, bool)>)> = vec![];
        for chunk in ids.chunks(c.max_get()) {
            let real: Vec<&str> = chunk.iter().map(|i| crate::unified::real_id(i)).collect();
            let Ok(r) = c.call(json!([["SearchSnippet/get", {"accountId": c.account, "filter": sf, "emailIds": real}, "s"]])) else { return };
            for s in r[0][1]["list"].as_array().cloned().unwrap_or_default() {
                let Some(eid) = s["emailId"].as_str() else { continue };
                let subj = s["subject"].as_str().map(gsearch::snippet_segments).filter(|x| gsearch::has_marked(x)).unwrap_or_default();
                let prev = s["preview"].as_str().map(gsearch::snippet_segments).filter(|x| gsearch::has_marked(x)).unwrap_or_default();
                if subj.is_empty() && prev.is_empty() { continue; }
                if let Some(list_id) = chunk.iter().find(|i| crate::unified::real_id(i) == eid) { found.push((list_id.clone(), subj, prev)); }
            }
        }
        if found.is_empty() { return; }
        let _ = w.upgrade_in_event_loop(move |u| {
            // the list moved on to another filter meanwhile: these belong to the old one
            if crate::mail::list_filter() != filter { return; }
            SNIP.with(|s| { let mut s = s.borrow_mut(); for (id, a, b) in found { s.insert(id, (a, b)); } });
            crate::mail::rebuild_list(&u);
        });
    });
}

thread_local! { static ACCENT: std::cell::RefCell<String> = std::cell::RefCell::new("#c8234a".into()); }

/// Hex form of a colour for markdown highlights.
pub fn hex(c: crate::ui_types::Color) -> String { format!("#{:02x}{:02x}{:02x}", c.red(), c.green(), c.blue()) }

/// Highlights take Theme.accent; called whenever the theme or contrast changes.
pub fn set_accent(c: crate::ui_types::Color) { ACCENT.with(|a| *a.borrow_mut() = hex(c)); }

/// A new list started: drop the old one's snippets.
pub fn clear_snippets() { SNIP.with(|s| s.borrow_mut().clear()); }

/// Tests: the server's snippet for a message, without asking for it.
#[cfg(test)]
pub(crate) fn seed_snippet(id: &str, subject: Vec<(String, bool)>, preview: Vec<(String, bool)>) {
    SNIP.with(|s| { s.borrow_mut().insert(id.to_string(), (subject, preview)); });
}

/// The highlighted subject and preview of a row, as StyledText (empty when it has none).
pub fn styled(id: &str, subject: &str, preview: &str) -> Option<(slint::StyledText, slint::StyledText)> {
    SNIP.with(|s| {
        let s = s.borrow();
        let (subj, prev) = s.get(id)?;
        let color = ACCENT.with(|a| a.borrow().clone());
        let color = color.as_str();
        let (ps, pp) = (vec![(subject.to_string(), false)], vec![(preview.to_string(), false)]);
        let a = gsearch::snippet_markdown(if subj.is_empty() { &ps } else { subj }, color);
        let b = gsearch::snippet_markdown(if prev.is_empty() { &pp } else { prev }, color);
        Some((slint::StyledText::from_markdown(&a).ok()?, slint::StyledText::from_markdown(&b).ok()?))
    })
}

pub fn wire(u: &AppWindow) {
    let pd = u.global::<Pd>();
    let w = u.as_weak();
    pd.on_search_edited(move |t| { if let Some(u) = w.upgrade() { u.global::<Pd>().set_sugg_hidden(false); edited(&u, &t); } });
    let w = u.as_weak();
    pd.on_sugg_pick(move |i| { if let Some(u) = w.upgrade() { pick(&u, i); } });
    let w = u.as_weak();
    pd.on_sugg_remove(move |i| { if let Some(u) = w.upgrade() { remove(&u, i); } });
    let w = u.as_weak();
    pd.on_chip_remove(move |k| { if let Some(u) = w.upgrade() { remove_chip(&u, &k); } });
}
