//! The composer's sub-address helper (Bulwark components/identity/sub-address-helper.tsx): a
//! tag typed or picked turns From into `user{delimiter}tag@domain` of the chosen identity. The
//! rules are in `identities` (lib/sub-addressing.ts); this is the dialog's glue.
use crate::identities as ids;
use crate::{AppWindow, CompX};
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};

fn strings(v: Vec<String>) -> ModelRc<SharedString> {
    ModelRc::new(VecModel::from(v.into_iter().map(SharedString::from).collect::<Vec<_>>()))
}

/// The recipients the suggestions are made for (Bulwark: the To field).
fn recipients(u: &AppWindow) -> Vec<String> {
    crate::jmap::parse_addresses(&u.get_c_to()).iter().filter_map(|a| a["email"].as_str().map(String::from)).collect()
}

fn preview(u: &AppWindow, tag: &str) -> String {
    let base = crate::compose::from_address(u);
    if tag.is_empty() { base } else { ids::generate_sub_address(&base, tag, &ids::delimiter()) }
}

fn open(u: &AppWindow) {
    let x = u.global::<CompX>();
    let recent = ids::recent_tags(crate::settings::Settings::load().prefs.get(ids::KEY_SUB_TAGS));
    x.set_sub_input("".into());
    x.set_sub_error("".into());
    x.set_sub_preview(preview(u, "").into());
    x.set_sub_recent(strings(recent.into_iter().take(5).collect()));
    x.set_sub_suggested(strings(ids::suggestions_for(&recipients(u))));
    x.set_sub_help(format!("Emails sent to user{}tag@domain.com will arrive in your inbox", ids::delimiter()).into());
    x.set_sub_open(true);
}

fn typed(u: &AppWindow, t: &str) {
    let x = u.global::<CompX>();
    x.set_sub_input(t.into());
    x.set_sub_error(ids::tag_error(t).unwrap_or_default().into());
    x.set_sub_preview(preview(u, t).into());
}

fn use_tag(u: &AppWindow, t: &str) {
    let x = u.global::<CompX>();
    if let Some(e) = ids::tag_error(t) { x.set_sub_error(e.into()); return; }
    // remembered as Bulwark's helper does: the recent tags, and the tag for the first domain
    let domain = recipients(u).iter().find_map(|r| ids::extract_domain(r));
    let tag = t.to_string();
    crate::settings::update_pref(ids::KEY_SUB_TAGS, |v| ids::note_tag(v, &tag, domain.as_deref()));
    crate::compose::set_sub_tag(u, t);
    x.set_sub_open(false);
}

/// Sign-out: the dialog is drawn over the whole window (the sign-in page too), so it closes and
/// forgets the account's recent and suggested tags and what was typed.
pub fn reset(u: &AppWindow) {
    let x = u.global::<CompX>();
    x.set_sub_open(false);
    x.set_sub_input("".into());
    x.set_sub_error("".into());
    x.set_sub_preview("".into());
    x.set_sub_recent(strings(vec![]));
    x.set_sub_suggested(strings(vec![]));
}

pub fn wire(ui: &AppWindow) {
    let x = ui.global::<CompX>();
    let w = ui.as_weak();
    x.on_sub_open_compose(move || { if let Some(u) = w.upgrade() { open(&u); } });
    let w = ui.as_weak();
    x.on_sub_edit(move |t| { if let Some(u) = w.upgrade() { typed(&u, &t); } });
    let w = ui.as_weak();
    x.on_sub_use(move |t| { if let Some(u) = w.upgrade() { use_tag(&u, t.trim()); } });
    let w = ui.as_weak();
    x.on_sub_cancel(move || { if let Some(u) = w.upgrade() { u.global::<CompX>().set_sub_open(false); } });
    let w = ui.as_weak();
    x.on_sub_clear(move || { if let Some(u) = w.upgrade() { crate::compose::set_sub_tag(&u, ""); } });
}
