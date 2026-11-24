//! Templates in the composer: inserting one (a dialog fills its {{placeholders}} first, Bulwark
//! components/templates/placeholder-fill-modal.tsx and the picker in template-picker.tsx) and
//! saving the draft as a template (quick-save-template-dialog.tsx). The pure parts are in
//! `templates.rs`; `render` here is the pure step between a template and the composer.
use crate::{rte, templates, AppWindow, CompX, Shared, TplField};
use serde_json::Value;
use slint::{ComponentHandle, ModelRc, VecModel};
use std::cell::RefCell;
use std::collections::HashMap;

struct Pending {
    template: Value,
    names: Vec<String>,
    values: HashMap<String, String>,
    /// The values the dialog opened with, and whether discarding changes was already asked about.
    initial: HashMap<String, String>,
    asked: bool,
}

thread_local! {
    static PENDING: RefCell<Option<Pending>> = const { RefCell::new(None) };
}

/// What goes into the composer for a template.
#[derive(Debug, PartialEq)]
pub struct Rendered {
    pub subject: String,
    pub body: String,
    pub html: bool,
}

/// Fill a template (an empty `values` leaves it as written, like Bulwark's "Insert as written")
/// and match it to the draft's format: an HTML body goes into a plain-text draft as its text.
/// An HTML body goes through the mail sanitiser again, whatever stored it.
pub fn render(t: &Value, values: &HashMap<String, String>, plain_draft: bool) -> Rendered {
    let html = t["isHTML"] == true;
    let (subject, body) = (t["subject"].as_str().unwrap_or(""), t["body"].as_str().unwrap_or(""));
    let fill = !values.is_empty();
    let subject = if fill { templates::substitute(subject, values, false) } else { subject.to_string() };
    let body = if fill { templates::substitute(body, values, html) } else { body.to_string() };
    if !html { return Rendered { subject, body, html: false }; }
    let body = templates::clean_html(&body);
    if plain_draft { Rendered { subject, body: rte::html::html_text(&body), html: false } } else { Rendered { subject, body, html: true } }
}

/// The dialog's preview: the body with the values filled in, as text.
fn preview(t: &Value, values: &HashMap<String, String>) -> String {
    let r = render(t, values, true);
    let p: String = r.body.trim().chars().take(400).collect();
    p
}

/// Put a template into the composer: the subject when the draft has none, the body at the caret,
/// the template's default recipients.
fn apply(u: &AppWindow, t: &Value, values: &HashMap<String, String>) {
    let r = render(t, values, u.get_c_plain());
    if u.get_c_subject().trim().is_empty() && !r.subject.is_empty() { u.set_c_subject(r.subject.into()); }
    let blocks = if r.html { rte::html::import(&r.body) } else { rte::html::import_text(&r.body) };
    rte::insert_blocks(u, blocks);
    let addrs = |k: &str| t["defaultRecipients"][k].as_array().map(|a| a.iter().filter_map(|x| x.as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    let (to, cc, bcc) = (addrs("to"), addrs("cc"), addrs("bcc"));
    // a reply or forward keeps the people it is addressed to (email-composer.tsx handleTemplateSelect)
    if crate::compose::is_new_message() {
        if !to.is_empty() { u.set_c_to(to.into()); }
        if !cc.is_empty() { u.set_c_cc(cc.into()); u.set_c_show_ccbcc(true); u.set_c_show_cc(true); }
        if !bcc.is_empty() { u.set_c_bcc(bcc.into()); u.set_c_show_ccbcc(true); u.set_c_show_bcc(true); }
    }
    // the sending identity, when the composing account has it
    if let Some(i) = t["identityId"].as_str().and_then(crate::compose::identity_position) { u.set_c_from_index(i as i32); }
}

/// The picker chose a template: straight in when it has no placeholders, else the dialog.
pub fn insert(u: &AppWindow, t: Value) {
    let html = t["isHTML"] == true;
    let (subject, body) = (t["subject"].as_str().unwrap_or(""), t["body"].as_str().unwrap_or(""));
    let names = templates::template_placeholders(subject, body, html);
    if names.is_empty() { apply(u, &t, &HashMap::new()); return; }
    let auto = templates::auto_fill(chrono::Local::now(), u.get_c_from_name().trim());
    let defaults = templates::placeholder_defaults(subject, body, html);
    let values: HashMap<String, String> = names.iter().map(|n| {
        let v = auto.get(n).filter(|v| !v.is_empty()).or_else(|| defaults.get(n)).cloned().unwrap_or_default();
        (n.clone(), v)
    }).collect();
    let x = u.global::<CompX>();
    let rows: Vec<TplField> = names.iter().map(|n| TplField {
        label: format!("{{{{{n}}}}}").into(), hint: templates::built_in_label(n).unwrap_or("").into(), value: values[n].clone().into(),
    }).collect();
    x.set_tpl_fill_name(t["name"].as_str().unwrap_or("").into());
    x.set_tpl_preview(preview(&t, &values).into());
    x.set_tpl_fields(ModelRc::new(VecModel::from(rows)));
    PENDING.with(|p| *p.borrow_mut() = Some(Pending { template: t, names, initial: values.clone(), values, asked: false }));
    x.set_tpl_fill_warn("".into());
    x.set_tpl_fill_open(true);
}

fn close_fill(u: &AppWindow) {
    u.global::<CompX>().set_tpl_fill_open(false);
    PENDING.with(|p| *p.borrow_mut() = None);
}

/// "Save as template…": the draft's subject is the name to start with.
fn open_save(u: &AppWindow) {
    if !u.get_composing() { return; }
    let x = u.global::<CompX>();
    x.set_tpl_save_name(u.get_c_subject().trim().into());
    x.set_tpl_save_error("".into());
    x.set_tpl_save_open(true);
}

/// The draft as the template stores it: the composer's HTML (through the sanitiser), or its text
/// when the draft is plain. Recipients are not kept (Bulwark's quick save leaves them out).
/// Inline (cid) images are left out, as they cannot live in an 8 KB entry: the flag says some were.
fn draft_fields(u: &AppWindow, name: &str) -> (templates::Fields, bool) {
    let html = !u.get_c_plain();
    let mut dropped = false;
    let body = if html {
        let mut d = rte::doc();
        let before = d.blocks.len();
        d.blocks.retain(|b| !matches!(&b.kind, rte::model::Kind::Image(i) if i.cid.is_some()));
        dropped = d.blocks.len() != before;
        rte::html::export(&d)
    } else { rte::to_text() };
    (templates::Fields { name: name.to_string(), subject: u.get_c_subject().to_string(), body, html, ..Default::default() }, dropped)
}

fn save_draft(u: &AppWindow, st: &Shared) {
    let x = u.global::<CompX>();
    let name = x.get_tpl_save_name().to_string();
    if let Some(e) = templates::validate_name(&name) { x.set_tpl_save_error(e.into()); return; }
    let (f, dropped) = draft_fields(u, &name);
    if let Err(e) = templates::build(None, &f, "1970-01-01T00:00:00.000Z", || "x".into()) { x.set_tpl_save_error(e.into()); return; }
    let Some(c) = crate::compose_client(st) else { x.set_tpl_save_error("Not signed in.".into()); return };
    if !templates::supported(&c) {
        x.set_tpl_save_error("This server keeps no metadata, so it cannot store templates (Stalwart 1.0 or later does).".into());
        return;
    }
    x.set_tpl_save_open(false);
    u.set_c_status("Saving template…".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let r = templates::save(&c, None, &f);
        let _ = w.upgrade_in_event_loop(move |u| u.set_c_status(match r { Ok(()) if dropped => "Saved as template (images are not saved in templates)".into(), Ok(()) => "Saved as template".into(), Err(e) => format!("Template not saved: {e}").into() }));
    });
}

pub fn wire(ui: &AppWindow, st: Shared) {
    let x = ui.global::<CompX>();
    let w = ui.as_weak();
    x.on_tpl_fill_edit(move |i, text| {
        let Some(u) = w.upgrade() else { return };
        let preview = PENDING.with(|p| {
            let mut p = p.borrow_mut();
            let p = p.as_mut()?;
            let name = p.names.get(i as usize)?.clone();
            p.values.insert(name, text.to_string());
            Some(preview(&p.template, &p.values))
        });
        if let Some(pv) = preview { u.global::<CompX>().set_tpl_preview(pv.into()); }
    });
    let w = ui.as_weak();
    x.on_tpl_fill_confirm(move |with_values| {
        let Some(u) = w.upgrade() else { return };
        let Some(p) = PENDING.with(|p| p.borrow_mut().take()) else { return };
        close_fill(&u);
        apply(&u, &p.template, &if with_values { p.values } else { HashMap::new() });
    });
    let w = ui.as_weak();
    x.on_tpl_fill_cancel(move || {
        let Some(u) = w.upgrade() else { return };
        // typed values are not thrown away by one click on the backdrop or Escape
        let changed = PENDING.with(|p| p.borrow_mut().as_mut().is_some_and(|p| { let c = p.values != p.initial && !p.asked; p.asked = true; c }));
        if changed { u.global::<CompX>().set_tpl_fill_warn("Click outside or press Escape again to discard what you typed.".into()); return; }
        close_fill(&u);
    });
    let w = ui.as_weak();
    x.on_tpl_save_open_compose(move || { if let Some(u) = w.upgrade() { open_save(&u); } });
    let w = ui.as_weak();
    x.on_tpl_save_confirm(move || { if let Some(u) = w.upgrade() { save_draft(&u, &st); } });
    let w = ui.as_weak();
    x.on_tpl_save_cancel(move || { if let Some(u) = w.upgrade() { u.global::<CompX>().set_tpl_save_open(false); } });
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn vals(p: &[(&str, &str)]) -> HashMap<String, String> { p.iter().map(|(a, b)| (a.to_string(), b.to_string())).collect() }

    #[test]
    fn renders_text_and_html_templates() {
        let t = json!({"subject": "Hi {{name}}", "body": "Dear {{name}} <3", "isHTML": false});
        let r = render(&t, &vals(&[("name", "Ann")]), false);
        assert_eq!((r.subject.as_str(), r.body.as_str(), r.html), ("Hi Ann", "Dear Ann <3", false));
        // no values: as written
        assert_eq!(render(&t, &HashMap::new(), false).body, "Dear {{name}} <3");
        let h = json!({"subject": "", "body": "<p>Hi {{name}}</p><script>x()</script>", "isHTML": true});
        let r = render(&h, &vals(&[("name", "A<b>")]), false);
        assert!(r.html && r.body.contains("Hi A&lt;b&gt;") && !r.body.contains("script"), "{}", r.body);
        // into a plain draft it becomes text
        let r = render(&h, &vals(&[("name", "Ann")]), true);
        assert!(!r.html && r.body.contains("Hi Ann") && !r.body.contains('<'));
    }

    #[test]
    fn preview_is_filled_text() {
        let t = json!({"subject": "", "body": "Hello {{n}}", "isHTML": false});
        assert_eq!(preview(&t, &vals(&[("n", "Bo")])), "Hello Bo");
    }
}
