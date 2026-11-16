//! Opening a message in the reader without flicker (the reader's actions and banners are in
//! reader.rs; this module decides when what is shown changes):
//! - the message on screen (header and body) stays until the next one is ready, then both
//!   change together (the body view reports when the new page is laid out);
//! - while it stays, nothing can act on it: reply, forward and the attachments wait for the
//!   new message, and the list's actions already target the selected one;
//! - when loading takes over 150 ms a thin progress bar appears, the header shows the selected
//!   row and the old body fades (so the reader never shows one message's header over another's
//!   body, and key repeat visibly follows the selection);
//! - bodies and conversations come from an in-memory cache, and the neighbours in the list
//!   are prefetched (further ahead in the direction of travel), so moving through the list is
//!   instant;
//! - from an empty reader the header is filled from the list row straight away.
use crate::*;
use std::cell::Cell;
use std::collections::HashSet;
use std::time::{Duration, Instant};

thread_local! {
    static OPEN_SEQ: Cell<u64> = const { Cell::new(0) };
    static LOADING: slint::Timer = slint::Timer::default();
    /// A message is being fetched for the reader (it still shows the previous one).
    static WAITING: Cell<bool> = const { Cell::new(false) };
    /// The list position of the last message prefetched around, and the way the user is going
    /// (backwards; one row at a time), for the prefetch direction.
    static TRAVEL: Cell<(Option<usize>, bool, bool)> = const { Cell::new((None, false, false)) };
}

static PREFETCHING: Mutex<Option<HashSet<String>>> = Mutex::new(None);

/// A prefetch skips (and does not cache) a message whose inline images add up to more.
const PREFETCH_INLINE_MAX: u64 = 4_000_000;

fn threading(p: &settings::Settings) -> bool { !p.bool("disableThreading", false) }

fn mark_delay(p: &settings::Settings) -> i64 { p.prefs.get("markAsReadDelay").and_then(|v| v.as_i64()).unwrap_or(0) }

fn prefetching(id: &str) -> bool {
    PREFETCHING.lock().unwrap().as_ref().is_some_and(|p| p.contains(id))
}

/// Whether a message was unread when it was opened, from the list's copy (the freshest) or
/// the cached one. Taken before the reader marks the row read locally, so that the server is
/// told too. None: not known yet (decided from the loaded message).
fn was_unseen(list_kw: Option<&Value>, cached_kw: Option<&Value>) -> Option<bool> {
    [list_kw, cached_kw].into_iter().flatten().find(|k| k.is_object()).map(|k| k["$seen"].as_bool() != Some(true))
}

/// Total size of the inline images a message's body refers to.
fn inline_bytes(m: &Value) -> u64 {
    m["attachments"].as_array().map(|a| a.iter()
        .filter(|a| a["cid"].is_string() && a["type"].as_str().is_some_and(|t| t.starts_with("image/")))
        .map(|a| a["size"].as_u64().unwrap_or(0)).sum()).unwrap_or(0)
}

/// Fetch what the reader shows for a listed message: the body (cid images inlined) and,
/// with threading on, its conversation. Both land in the cache (unless the account signed
/// out meanwhile). A prefetch leaves messages with large inline images to the open itself.
fn fetch(c: &jmap::Client, id: &str, shared: Option<&String>, want_thread: bool, prefetch: bool) -> Result<(Arc<Value>, Option<Arc<Vec<Value>>>), String> {
    let acct = c.state_key();
    let gen = bodycache::generation();
    let cached = bodycache::with(|b| b.get(&acct, id));
    let m = match cached {
        Some(m) => m,
        None => {
            let mut m = c.email_body(unified::real_id(id))?;
            if prefetch && inline_bytes(&m) > PREFETCH_INLINE_MAX { return Err("large inline images".into()); }
            inline_cid_images(c, &mut m);
            // a group inbox message: replies must not mark the login's own message of that id
            if let Some(a) = shared { m["__jmap"] = json!(a); }
            bodycache::with(|b| b.put_gen(gen, &acct, id, m))
        }
    };
    let thread = if want_thread {
        match m["threadId"].as_str().and_then(|t| bodycache::with(|b| b.thread(&acct, t))) {
            Some(t) => Some(t),
            None => c.thread_emails(unified::real_id(id)).ok().map(|mut list| {
                list.sort_by_key(|x| x["receivedAt"].as_str().unwrap_or("").to_string());
                let tid = m["threadId"].as_str().unwrap_or("").to_string();
                bodycache::with(|b| b.put_thread_gen(gen, &acct, &tid, list))
            }),
        }
    } else { None };
    Ok((m, thread))
}

/// The conversation strip's rows for the open message.
fn thread_rows(id: &str, list: &[Value]) -> Vec<ThreadMsg> {
    list.iter().map(|m| ThreadMsg {
        id: unified::same_source(id, m["id"].as_str().unwrap_or("")).into(),
        sender: mail::sender(m).into(),
        date: mail::parse_time(&m["receivedAt"]).map(|t| t.format("%d %b %H:%M").to_string()).unwrap_or_default().into(),
        preview: mail::clean_preview(m["preview"].as_str().unwrap_or("")).into(),
        current: m["id"] == unified::real_id(id),
        unread: m["keywords"]["$seen"].as_bool() != Some(true),
    }).collect()
}

/// Open a listed message (the list row, a key, the conversation strip).
pub fn open(u: &AppWindow, st: &Shared, id: String) {
    shortcuts::note_opened(&id);
    let Some(c) = client_for(st, &id) else { return };
    READER_CLIENT.with(|r| *r.borrow_mut() = Some(c.clone()));
    let p = settings::Settings::load();
    let delay = mark_delay(&p);
    let want_thread = threading(&p);
    let seq = OPEN_SEQ.with(|s| { s.set(s.get() + 1); s.get() });
    let acct = c.state_key();
    let shared = mail::shared_account(&id);
    let cached = bodycache::with(|b| b.get(&acct, &id));
    // read state before the row is marked read locally (the server must still be told)
    let unseen = was_unseen(mail::email(&id).map(|m| m["keywords"].clone()).as_ref(), cached.as_ref().map(|m| &m["keywords"]));
    if delay == 0 { mail::mark_read_local(u, &id); }
    let hit = cached.and_then(|m| {
        let t = if want_thread { Some(bodycache::with(|b| b.thread(&acct, m["threadId"].as_str()?))?) } else { None };
        Some((m, t))
    });
    if let Some((m, t)) = hit {
        WAITING.with(|w| w.set(false));
        LOADING.with(|t| t.stop());
        present(u, &c, &id, m, t, delay, unseen, want_thread);
        return;
    }
    // a page still loading for the message before must not appear under this one
    mailview::cancel_pending();
    // what is on screen stays, but nothing may act on it: reply and attachments wait
    CUR_MSG.with(|x| *x.borrow_mut() = None);
    REPLY.with(|r| *r.borrow_mut() = None);
    WAITING.with(|w| w.set(true));
    if CUR.with(|x| x.borrow().is_none()) || u.get_r_loading() {
        // an empty reader, or one already showing the loading state: the row's header now
        prefill(u, &id);
    } else if !LOADING.with(|t| t.running()) {
        // started once per burst (key repeat keeps it), not restarted on every key
        let wk = u.as_weak();
        LOADING.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(150), move || {
            if !WAITING.with(|w| w.get()) { return; }
            let Some(u) = wk.upgrade() else { return };
            if u.get_current_mail().is_empty() { return; }
            u.set_r_loading(true);
            prefill(&u, u.get_current_mail().as_str());
            mailview::dim(true);
        }));
    }
    // key repeat: warm the messages ahead of this one while it loads
    prefetch(u, &id, want_thread);
    let w = u.as_weak();
    std::thread::spawn(move || {
        // already on its way (prefetched): wait for that instead of asking twice
        let t0 = Instant::now();
        while prefetching(&id) && t0.elapsed() < Duration::from_secs(5) { std::thread::sleep(Duration::from_millis(8)); }
        let res = fetch(&c, &id, shared.as_ref(), want_thread, false);
        let _ = w.upgrade_in_event_loop(move |u| {
            if OPEN_SEQ.with(|s| s.get()) != seq || u.get_current_mail().as_str() != id { return; }
            match res {
                Ok((m, t)) => present(&u, &c, &id, m, t, delay, unseen, want_thread),
                Err(e) => {
                    // nothing of the previous message may stay (header, reply target)
                    WAITING.with(|w| w.set(false));
                    LOADING.with(|t| t.stop());
                    u.set_r_loading(false);
                    prefill(&u, &id);
                    reader::clear(&u);
                    CUR.with(|x| *x.borrow_mut() = None);
                    let body = format!("<pre>{}</pre>", esc(&format!("Could not load message: {e}")));
                    let (doc, _, bg) = page(&body, false, false);
                    mailview::show(&doc, bg, false, None);
                }
            }
        });
    });
}

/// Header from the list row, for a message opened from an empty reader (or still loading).
fn prefill(u: &AppWindow, id: &str) {
    let Some(m) = mail::email(id) else { return };
    let from = mail::sender(&m);
    let email = m["from"][0]["email"].as_str().unwrap_or("");
    if u.get_r_email().as_str() != email { glance::clear_sender(u); }
    u.set_r_from(from.clone().into());
    u.set_r_email(email.into());
    u.set_r_initials(initials(&from).into());
    u.set_r_color(mail::tint(&from));
    u.set_r_subject(m["subject"].as_str().unwrap_or("(no subject)").into());
    u.set_r_date(parse_time(&m["receivedAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default().into());
    u.set_r_flagged(m["keywords"]["$flagged"].as_bool() == Some(true));
    let host = READER_CLIENT.with(|r| r.borrow().as_ref().map(|c| crate::avatars::host_of(&c.base))).unwrap_or_default();
    mail::set_reader_avatar(u, Some(&m), &host);
    schedule::banner(u, Some(&m));
    u.set_r_to("".into());
    u.set_attachments(ModelRc::default());
    u.set_att_cards(ModelRc::default());
    u.set_thread_msgs(ModelRc::default());
    u.set_plugin_rows(ModelRc::default());
    u.set_spam_verdict("".into());
    u.set_images_blocked(false);
    // the banners (unsubscribe, receipt, invitation) belong to the message that was shown
    reader::clear(u);
}

/// Bulwark markAsReadDelay (ms): 0 = instantly, N = after N ms if still open, -1 = never.
fn mark_read(w: slint::Weak<AppWindow>, c: Arc<jmap::Client>, id: String, delay: i64) {
    std::thread::spawn(move || {
        if delay > 0 { std::thread::sleep(Duration::from_millis(delay as u64)); }
        let still = Arc::new(std::sync::atomic::AtomicBool::new(delay == 0));
        if delay > 0 {
            let (s2, i2) = (still.clone(), id.clone());
            let _ = w.upgrade_in_event_loop(move |u| s2.store(u.get_current_mail().as_str() == i2, std::sync::atomic::Ordering::SeqCst));
            std::thread::sleep(Duration::from_millis(100));
        }
        if still.load(std::sync::atomic::Ordering::SeqCst) {
            let _ = c.set_keyword(&[unified::real_id(&id).to_string()], "$seen", true);
            let _ = w.upgrade_in_event_loop(move |u| mail::mark_read_local(&u, &id));
        }
    });
}

/// Show a loaded message: the body first goes into the view hidden, then header and body
/// change together. `unseen`: whether it was unread when opened (None: see its keywords).
#[allow(clippy::too_many_arguments)]
fn present(u: &AppWindow, c: &Arc<jmap::Client>, id: &str, m: Arc<Value>, thread: Option<Arc<Vec<Value>>>, delay: i64, unseen: Option<bool>, want_thread: bool) {
    // the list's copy has the current keywords (a cached body may predate a flag or read)
    let live = mail::email(id);
    let kw = live.as_ref().map(|l| l["keywords"].clone()).filter(|k| !k.is_null()).unwrap_or_else(|| m["keywords"].clone());
    let unseen = unseen.unwrap_or_else(|| m["keywords"]["$seen"].as_bool() != Some(true));
    if delay >= 0 && unseen { mark_read(u.as_weak(), c.clone(), id.to_string(), delay); }
    if m["keywords"]["$draft"].as_bool() == Some(true) { compose::open_draft(u, c.clone(), &m); }

    let f = &m["from"][0];
    let from = mail::sender(&m);
    let email = f["email"].as_str().unwrap_or("").to_string();
    let names = |k: &str| m[k].as_array().map(|a| a.iter().map(|x| {
        let e = x["email"].as_str().unwrap_or("");
        if e.eq_ignore_ascii_case(&c.username) || MY_ADDRS.with(|a| a.borrow().iter().any(|x| x.eq_ignore_ascii_case(e))) { "me".to_string() } else { x["name"].as_str().filter(|n| !n.is_empty()).unwrap_or(e).to_string() }
    }).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    let mut to = names("to");
    let cc = names("cc");
    if !cc.is_empty() { to = format!("{to}  ·  Cc: {cc}"); }
    let hide_inline = settings::Settings::load().bool("hideInlineImageAttachments", true);
    let atts: Vec<Value> = m["attachments"].as_array().cloned().unwrap_or_default().into_iter()
        .filter(|a| !(hide_inline && mail::is_inline_image(a)))
        .collect();
    let att_rows: Vec<Att> = atts.iter().enumerate().map(|(i, a)| Att {
        name: a["name"].as_str().unwrap_or("attachment").into(),
        size: human_size(a["size"].as_u64().unwrap_or(0)).into(),
        index: i as i32,
    }).collect();
    let subject = m["subject"].as_str().unwrap_or("(no subject)").to_string();
    let date = parse_time(&m["receivedAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default();
    let (tid, trows) = match &thread {
        Some(t) => (m["threadId"].as_str().map(String::from), thread_rows(id, t)),
        None => (None, vec![]),
    };
    let dis = disabled_plugins();
    let prow: Vec<PRow> = PLUGINS.with(|p| plugins::email_details(&p.borrow(), &dis, &m)).into_iter()
        .map(|(label, value, tone)| PRow { label: label.into(), value: value.into(), tone: tone.into() }).collect();
    let spam_plugin = PLUGINS.with(|p| p.borrow().iter().any(|x| x.id == "spam-score")) && !dis.iter().any(|d| d == "spam-score");
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    let sp = spam::analyze(&headers).filter(|_| !spam_plugin).filter(|sp| sp.verdict != "clean" || u.get_spam_show_clean());
    let (_, text) = body_text(&m);
    let reply = (email.clone(), m["subject"].as_str().unwrap_or("").to_string(), m["messageId"][0].as_str().map(String::from), text, format!("{date}, {from}"));
    let flagged = kw["$flagged"].as_bool() == Some(true);

    {
        let (html, text) = body_text(&m);
        let is_html = m["htmlBody"].as_array().map(|a| a.iter().any(|p| p["type"] == "text/html")).unwrap_or(false);
        let html = (is_html && !html.is_empty()).then_some(html);
        let remote = html.as_deref().is_some_and(has_remote_images);
        let allow = u.get_auto_images();
        CUR.with(|c| *c.borrow_mut() = Some(Current { html, text, remote, allow }));
    }
    let atts2 = atts.clone();
    CUR_MSG.with(|x| *x.borrow_mut() = Some((m.clone(), atts)));
    REPLY.with(|r| *r.borrow_mut() = Some(reply));
    THREAD_ID.with(|t| *t.borrow_mut() = tid);

    let wk = u.as_weak();
    let (c2, id2) = (c.clone(), id.to_string());
    let header = move || -> bool {
        let Some(u) = wk.upgrade() else { return false };
        // another message was opened meanwhile: this page is not to be shown
        if u.get_current_mail().as_str() != id2 { return false; }
        WAITING.with(|w| w.set(false));
        LOADING.with(|t| t.stop());
        u.set_r_loading(false);
        // the sender panel: only a different sender empties it (it refills from the server)
        if u.get_r_email().as_str() != email { glance::clear_sender(&u); }
        glance::sender(u.as_weak(), c2.clone(), email.clone(), id2.clone());
        u.set_r_email(email.into());
        u.set_r_initials(initials(&from).into());
        u.set_r_color(mail::tint(&from));
        mail::set_reader_avatar(&u, Some(&m), &crate::avatars::host_of(&c2.base));
        schedule::banner(&u, mail::email(&id2).as_ref());
        u.set_r_flagged(flagged);
        u.set_r_to(to.into());
        u.set_attachments(ModelRc::new(VecModel::from(att_rows)));
        attachments::show(&u, c2.clone(), &id2, &atts2);
        u.set_thread_msgs(ModelRc::new(VecModel::from(trows)));
        u.set_r_subject(subject.into());
        u.set_r_from(from.into());
        u.set_r_date(date.into());
        u.set_ai_result("".into());
        u.set_plugin_rows(ModelRc::new(VecModel::from(prow)));
        match sp {
            Some(sp) => { u.set_spam_verdict(sp.verdict.into()); u.set_spam_detail(sp.detail.into()); }
            None => { u.set_spam_verdict("".into()); u.set_spam_detail("".into()); }
        }
        // banners (unsubscribe, read receipt, invitation, long body) and their loads
        reader::loaded(&u, c2.clone(), &m, &id2);
        // only a message that was unread changes the folder counts
        if unseen { load_mailboxes(u.as_weak(), c2.clone(), false); }
        prefetch(&u, &id2, want_thread);
        true
    };
    render_current_then(u, Some(Box::new(header)));
}

/// Load the messages next to the open one in the list into the cache: two ahead in the
/// direction of travel (three while moving) and one behind, and its conversation neighbours.
fn prefetch(u: &AppWindow, id: &str, want_thread: bool) {
    let mails = u.get_mails();
    let n = mails.row_count();
    let pos = (0..n).find(|&i| mails.row_data(i).is_some_and(|r| r.id == id));
    let mut ids: Vec<String> = vec![];
    if let Some(i) = pos {
        let (last, was_back, was_moving) = TRAVEL.with(|t| t.get());
        // the same message again (opened, then shown): keep the direction it was reached in
        let (back, moving) = match last {
            Some(l) if l == i => (was_back, was_moving),
            Some(l) => (l > i, l.abs_diff(i) == 1),
            None => (false, false),
        };
        TRAVEL.with(|t| t.set((Some(i), back, moving)));
        let step = |k: usize| if back { i.checked_sub(k) } else { Some(i + k) };
        let behind = if back { Some(i + 1) } else { i.checked_sub(1) };
        let ahead = if moving { 3 } else { 2 };
        ids.extend((1..=ahead).filter_map(step).chain(behind)
            .filter_map(|j| (j < n).then(|| mails.row_data(j)).flatten()).map(|r| r.id.to_string()));
    }
    // and the messages next to it in its conversation
    let th = u.get_thread_msgs();
    if let Some(k) = (0..th.row_count()).find(|&k| th.row_data(k).is_some_and(|t| t.current)) {
        ids.extend([k.wrapping_sub(1), k + 1].into_iter().filter_map(|k| th.row_data(k)).map(|t| t.id.to_string()));
    }
    ids.retain(|x| x != id);
    fetch_ahead(ids, want_thread);
}

/// A list was loaded: warm the cache with its first messages, so opening the top one (the
/// usual first click) needs no round trip either.
pub fn prefetch_top(u: &AppWindow) {
    let mails = u.get_mails();
    let ids: Vec<String> = (0..mails.row_count().min(2)).filter_map(|i| mails.row_data(i)).map(|r| r.id.to_string()).collect();
    fetch_ahead(ids, threading(&settings::Settings::load()));
}

fn fetch_ahead(ids: Vec<String>, want_thread: bool) {
    let Some(st) = SHARED.with(|s| s.borrow().clone()) else { return };
    let mut jobs = vec![];
    for rid in ids {
        let Some(c) = client_for(&st, &rid) else { continue };
        if bodycache::with(|b| b.contains(&c.state_key(), &rid)) { continue; }
        if !PREFETCHING.lock().unwrap().get_or_insert_with(Default::default).insert(rid.clone()) { continue; }
        jobs.push((rid, c));
    }
    if jobs.is_empty() { return; }
    std::thread::spawn(move || {
        for (rid, c) in jobs {
            let shared = mail::shared_account(&rid);
            let _ = fetch(&c, &rid, shared.as_ref(), want_thread, true);
            if let Some(p) = PREFETCHING.lock().unwrap().as_mut() { p.remove(&rid); }
        }
    });
}

thread_local! {
    /// The session state, for prefetching (set once at start).
    pub static SHARED: std::cell::RefCell<Option<Shared>> = const { std::cell::RefCell::new(None) };
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unread_state_comes_from_the_list_first() {
        let unread = json!({});
        let read = json!({"$seen": true});
        assert_eq!(was_unseen(Some(&unread), Some(&read)), Some(true), "the list's copy wins");
        assert_eq!(was_unseen(Some(&read), Some(&unread)), Some(false));
        assert_eq!(was_unseen(None, Some(&unread)), Some(true), "a sibling not in the list: the cached copy");
        assert_eq!(was_unseen(Some(&Value::Null), Some(&read)), Some(false));
        assert_eq!(was_unseen(None, None), None);
    }

    #[test]
    fn inline_image_size() {
        let m = json!({"attachments": [
            {"cid": "<a>", "type": "image/png", "size": 3_000_000},
            {"cid": "<b>", "type": "image/jpeg", "size": 2_000_000},
            {"type": "application/pdf", "size": 9_000_000}
        ]});
        assert_eq!(inline_bytes(&m), 5_000_000);
    }
}
