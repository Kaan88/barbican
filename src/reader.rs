//! Opening a message in the reader without flicker:
//! - the message on screen (header and body) stays until the next one is ready, then both
//!   change together (the body view reports when the new page is laid out);
//! - a thin progress bar appears only when loading takes over 150 ms;
//! - bodies and conversations come from an in-memory cache, and the neighbours in the list
//!   are prefetched, so moving through the list is instant;
//! - from an empty reader the header is filled from the list row straight away.
use crate::*;
use std::cell::Cell;
use std::collections::HashSet;
use std::time::Duration;

thread_local! {
    static OPEN_SEQ: Cell<u64> = const { Cell::new(0) };
    static LOADING: slint::Timer = slint::Timer::default();
}

static PREFETCHING: Mutex<Option<HashSet<String>>> = Mutex::new(None);

fn threading() -> bool { !settings::Settings::load().bool("disableThreading", false) }

fn mark_delay() -> i64 { settings::Settings::load().prefs.get("markAsReadDelay").and_then(|v| v.as_i64()).unwrap_or(0) }

/// Fetch what the reader shows for a listed message: the body (cid images inlined) and,
/// with threading on, its conversation. Both land in the cache.
fn fetch(c: &jmap::Client, id: &str, shared: Option<&String>, want_thread: bool) -> Result<(Arc<Value>, Option<Arc<Vec<Value>>>), String> {
    let acct = c.state_key();
    let cached = bodycache::with(|b| b.get(&acct, id));
    let m = match cached {
        Some(m) => m,
        None => {
            let mut m = c.email_body(unified::real_id(id))?;
            inline_cid_images(c, &mut m);
            // a group inbox message: replies must not mark the login's own message of that id
            if let Some(a) = shared { m["__jmap"] = json!(a); }
            bodycache::with(|b| b.put(&acct, id, m))
        }
    };
    let thread = if want_thread {
        match m["threadId"].as_str().and_then(|t| bodycache::with(|b| b.thread(&acct, t))) {
            Some(t) => Some(t),
            None => c.thread_emails(unified::real_id(id)).ok().map(|mut list| {
                list.sort_by_key(|x| x["receivedAt"].as_str().unwrap_or("").to_string());
                let tid = m["threadId"].as_str().unwrap_or("").to_string();
                bodycache::with(|b| b.put_thread(&acct, &tid, list))
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
    let Some(c) = client_for(st, &id) else { return };
    READER_CLIENT.with(|r| *r.borrow_mut() = Some(c.clone()));
    let delay = mark_delay();
    if delay == 0 { mail::mark_read_local(u, &id); }
    let seq = OPEN_SEQ.with(|s| { s.set(s.get() + 1); s.get() });
    let acct = c.state_key();
    let want_thread = threading();
    let shared = mail::shared_account(&id);
    let hit = bodycache::with(|b| {
        let m = b.get(&acct, &id)?;
        let t = if want_thread { Some(b.thread(&acct, m["threadId"].as_str()?)?) } else { None };
        Some((m, t))
    });
    if let Some((m, t)) = hit {
        present(u, &c, &id, m, t, delay);
        return;
    }
    // not cached: the reader keeps what it shows; from an empty reader, the list row's header
    if CUR.with(|x| x.borrow().is_none()) { prefill(u, &id); }
    let wk = u.as_weak();
    LOADING.with(|t| t.start(slint::TimerMode::SingleShot, Duration::from_millis(150), move || {
        if OPEN_SEQ.with(|s| s.get()) == seq { if let Some(u) = wk.upgrade() { u.set_r_loading(true); } }
    }));
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = fetch(&c, &id, shared.as_ref(), want_thread);
        let _ = w.upgrade_in_event_loop(move |u| {
            if OPEN_SEQ.with(|s| s.get()) != seq || u.get_current_mail().as_str() != id { return; }
            match res {
                Ok((m, t)) => present(&u, &c, &id, m, t, delay),
                Err(e) => {
                    // nothing of the previous message may stay (header, reply target)
                    LOADING.with(|t| t.stop());
                    u.set_r_loading(false);
                    prefill(&u, &id);
                    CUR.with(|x| *x.borrow_mut() = None);
                    CUR_MSG.with(|x| *x.borrow_mut() = None);
                    REPLY.with(|r| *r.borrow_mut() = None);
                    let body = format!("<pre>{}</pre>", esc(&format!("Could not load message: {e}")));
                    mailview::show(&wrap_html(&body), doc_bg(&body), None);
                }
            }
        });
    });
}

/// Header from the list row, for a message opened from an empty reader.
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
    u.set_r_to("".into());
    u.set_attachments(ModelRc::default());
    u.set_thread_msgs(ModelRc::default());
    u.set_plugin_rows(ModelRc::default());
    u.set_spam_verdict("".into());
    u.set_images_blocked(false);
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
/// change together.
fn present(u: &AppWindow, c: &Arc<jmap::Client>, id: &str, m: Arc<Value>, thread: Option<Arc<Vec<Value>>>, delay: i64) {
    // the list's copy has the current keywords (a cached body may predate a flag or read)
    let live = mail::email(id);
    let kw = live.as_ref().map(|l| l["keywords"].clone()).filter(|k| !k.is_null()).unwrap_or_else(|| m["keywords"].clone());
    let unseen = kw["$seen"].as_bool() != Some(true);
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
    CUR_MSG.with(|x| *x.borrow_mut() = Some(((*m).clone(), atts)));
    REPLY.with(|r| *r.borrow_mut() = Some(reply));
    THREAD_ID.with(|t| *t.borrow_mut() = tid);

    let wk = u.as_weak();
    let (c2, id2) = (c.clone(), id.to_string());
    let header = move || {
        let Some(u) = wk.upgrade() else { return };
        if u.get_current_mail().as_str() != id2 { return; }
        LOADING.with(|t| t.stop());
        u.set_r_loading(false);
        // the sender panel: only a different sender empties it (it refills from the server)
        if u.get_r_email().as_str() != email { glance::clear_sender(&u); }
        glance::sender(u.as_weak(), c2.clone(), email.clone(), id2.clone());
        u.set_r_email(email.into());
        u.set_r_initials(initials(&from).into());
        u.set_r_color(mail::tint(&from));
        u.set_r_flagged(flagged);
        u.set_r_to(to.into());
        u.set_attachments(ModelRc::new(VecModel::from(att_rows)));
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
        // only a message that was unread changes the folder counts
        if unseen { load_mailboxes(u.as_weak(), c2.clone(), false); }
        prefetch(&u, &id2);
    };
    render_current_then(u, Some(Box::new(header)));
}

/// Load the messages next to the open one in the list into the cache.
fn prefetch(u: &AppWindow, id: &str) {
    let mails = u.get_mails();
    let n = mails.row_count();
    let i = (0..n).find(|&i| mails.row_data(i).is_some_and(|r| r.id == id)).unwrap_or(usize::MAX - 4);
    let want_thread = threading();
    let mut ids: Vec<String> = [i + 1, i.wrapping_sub(1), i + 2].into_iter()
        .filter_map(|j| (j < n).then(|| mails.row_data(j)).flatten()).map(|r| r.id.to_string()).collect();
    // and the messages next to it in its conversation
    let th = u.get_thread_msgs();
    if let Some(k) = (0..th.row_count()).find(|&k| th.row_data(k).is_some_and(|t| t.current)) {
        ids.extend([k.wrapping_sub(1), k + 1].into_iter().filter_map(|k| th.row_data(k)).map(|t| t.id.to_string()));
    }
    fetch_ahead(ids, want_thread);
}

/// A list was loaded: warm the cache with its first messages, so opening the top one (the
/// usual first click) needs no round trip either.
pub fn prefetch_top(u: &AppWindow) {
    let mails = u.get_mails();
    let ids: Vec<String> = (0..mails.row_count().min(2)).filter_map(|i| mails.row_data(i)).map(|r| r.id.to_string()).collect();
    fetch_ahead(ids, threading());
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
            let _ = fetch(&c, &rid, shared.as_ref(), want_thread);
            if let Some(p) = PREFETCHING.lock().unwrap().as_mut() { p.remove(&rid); }
        }
    });
}

thread_local! {
    /// The session state, for prefetching (set once at start).
    pub static SHARED: std::cell::RefCell<Option<Shared>> = const { std::cell::RefCell::new(None) };
}
