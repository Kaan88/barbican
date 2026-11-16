//! Composer: new / reply / reply-all / forward / edit draft, identities, Cc/Bcc,
//! attachments (upload), drafts, and undo-send via EmailSubmission HOLDFOR.
use crate::jmap::{Attachment, Client, Outgoing};
use crate::{AppWindow, Att};
use serde_json::Value;
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};
use std::cell::RefCell;
use std::sync::Arc;

#[derive(Default, Clone)]
struct State {
    attachments: Vec<Attachment>,
    draft_id: Option<String>,
    answering: Option<String>,
    forwarding: bool,
    in_reply_to: Vec<String>,
    references: Vec<String>,
    identities: Vec<String>,
    signatures: Vec<String>,
    kind_new: bool,
    sig_done: bool,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    static PENDING: RefCell<Option<(String, Outgoing, State)>> = const { RefCell::new(None) };
}

pub enum Kind { New, Reply, ReplyAll, Forward }

fn text_of(m: &Value) -> String {
    let (html, text) = crate::body_text(m);
    if text.trim().is_empty() { crate::html_to_text(&html) } else { text }
}

fn refresh_attachments(u: &AppWindow) {
    ST.with(|s| {
        let rows: Vec<Att> = s.borrow().attachments.iter().enumerate().map(|(i, a)| Att {
            name: a.name.as_str().into(), size: crate::human_size(a.size as u64).into(), index: i as i32,
        }).collect();
        u.set_c_attachments(ModelRc::new(VecModel::from(rows)));
    });
}

/// The identity a reply should come from: one of the user's own identities the message was
/// addressed to (exact address first, then the address without its sub-address tag, using
/// Bulwark's subAddressDelimiter), as Bulwark's findReplyIdentityId.
fn reply_identity(ids: &[Value], received: &[String]) -> Option<usize> {
    let delim = crate::settings::Settings::load().str("subAddressDelimiter", "+");
    let base = |e: &str| -> String {
        let e = e.trim().to_lowercase();
        match e.split_once('@') {
            Some((local, dom)) if !delim.is_empty() => format!("{}@{dom}", local.split(delim.as_str()).next().unwrap_or(local)),
            _ => e,
        }
    };
    let emails: Vec<String> = ids.iter().map(|i| i["email"].as_str().unwrap_or("").trim().to_lowercase()).collect();
    for a in received {
        if let Some(i) = emails.iter().position(|e| *e == a.trim().to_lowercase()) { return Some(i); }
    }
    for a in received {
        let b = base(a);
        // prefer the untagged identity for a base match
        let hits: Vec<usize> = emails.iter().enumerate().filter(|(_, e)| base(e) == b).map(|(i, _)| i).collect();
        if let Some(i) = hits.iter().copied().find(|&i| base(&emails[i]) == emails[i]).or(hits.first().copied()) { return Some(i); }
    }
    None
}

fn load_identities(u: &AppWindow, c: Arc<Client>, received: Vec<String>) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let ids = c.identities().unwrap_or_default();
        let ids2 = ids.clone();
        let sigs: Vec<String> = ids.iter().map(|i| i["textSignature"].as_str().unwrap_or("").to_string()).collect();
        let list: Vec<String> = ids.iter().map(|i| {
            let (n, e) = (i["name"].as_str().unwrap_or(""), i["email"].as_str().unwrap_or(""));
            if n.is_empty() { e.to_string() } else { format!("{n} <{e}>") }
        }).collect();
        let _ = w.upgrade_in_event_loop(move |u| {
            let ids = ids2;
            ST.with(|s| { let mut s = s.borrow_mut(); s.identities = list.clone(); s.signatures = sigs.clone(); });
            apply_signature(&u);
            let model: Vec<SharedString> = list.iter().map(|x| x.as_str().into()).collect();
            u.set_c_identities(ModelRc::new(VecModel::from(model)));
            // default to the identity of the signed-in address, as Bulwark does
            let me = c.username.to_lowercase();
            let idx = reply_identity(&ids, &received)
                .or_else(|| ids.iter().position(|i| i["email"].as_str().is_some_and(|e| e.to_lowercase() == me)))
                .unwrap_or(0);
            u.set_c_from_index(idx as i32);
        });
    });
}

/// Insert the identity's signature once, following Bulwark's signature settings.
fn apply_signature(u: &AppWindow) {
    let prefs = crate::settings::Settings::load();
    let (sig, new, done) = ST.with(|s| {
        let s = s.borrow();
        (s.signatures.get(u.get_c_from_index() as usize).cloned().unwrap_or_default(), s.kind_new, s.sig_done)
    });
    if done || sig.trim().is_empty() { return; }
    let enabled = if new { prefs.bool("signatureNewMailEnabled", true) } else { prefs.bool("signatureReplyForwardEnabled", true) };
    if !enabled { return; }
    let sep = if prefs.bool("signatureSeparatorEnabled", true) { "-- \n" } else { "" };
    let block = format!("{sep}{}", sig.trim_end());
    let body = u.get_c_body().to_string();
    let above = prefs.str("signaturePosition", "below_quote") == "above_quote";
    let out = if new || body.trim().is_empty() {
        format!("\n\n{block}")
    } else if above {
        // replies/forwards start with two blank lines before the quote
        format!("\n\n{block}{}", body.trim_start_matches('\n').split_once('\n').map(|_| format!("\n\n{}", body.trim_start_matches('\n'))).unwrap_or_default())
    } else {
        format!("{}\n\n{block}", body.trim_end())
    };
    u.set_c_body(out.into());
    ST.with(|s| s.borrow_mut().sig_done = true);
}

fn reset(u: &AppWindow) {
    ST.with(|s| { let ids = s.borrow().identities.clone(); *s.borrow_mut() = State { identities: ids, ..Default::default() }; });
    u.set_c_to("".into());
    u.set_c_cc("".into());
    u.set_c_bcc("".into());
    u.set_c_show_ccbcc(false);
    u.set_c_subject("".into());
    u.set_c_body("".into());
    u.set_c_status("".into());
    u.set_can_undo(false);
    refresh_attachments(u);
}

/// Open the composer for a new message, a reply, reply-all or forward of `m`.
pub fn open(u: &AppWindow, c: Arc<Client>, kind: Kind, m: Option<&Value>) {
    reset(u);
    ST.with(|s| s.borrow_mut().kind_new = matches!(kind, Kind::New));
    // replies come from the own identity the message was sent to (not forwards, like Bulwark)
    let received: Vec<String> = match (&kind, m) {
        (Kind::Reply | Kind::ReplyAll, Some(m)) => ["to", "cc", "bcc"].iter()
            .flat_map(|k| m[*k].as_array().cloned().unwrap_or_default())
            .filter_map(|x| x["email"].as_str().map(String::from)).collect(),
        _ => vec![],
    };
    load_identities(u, c.clone(), received);
    if let Some(m) = m {
        let me = c.username.to_lowercase();
        let subj = m["subject"].as_str().unwrap_or("").to_string();
        let who = format!("{} <{}>", crate::mail::sender(m), m["from"][0]["email"].as_str().unwrap_or(""));
        let date = crate::mail::parse_time(&m["receivedAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default();
        let text = text_of(m);
        let msgid = m["messageId"][0].as_str().map(String::from);
        let mut refs: Vec<String> = m["references"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
        if let Some(id) = &msgid { refs.push(id.clone()); }
        let addr = |x: &Value| -> String {
            let (n, e) = (x["name"].as_str().unwrap_or(""), x["email"].as_str().unwrap_or(""));
            if n.is_empty() { e.to_string() } else { format!("\"{n}\" <{e}>") }
        };
        match kind {
            Kind::Forward => {
                u.set_c_subject((if subj.to_lowercase().starts_with("fwd:") { subj.clone() } else { format!("Fwd: {subj}") }).into());
                u.set_c_body(format!("\n\n---------- Forwarded message ----------\nFrom: {who}\nDate: {date}\nSubject: {subj}\n\n{text}").into());
                // carry the original attachments
                let atts: Vec<Attachment> = m["attachments"].as_array().cloned().unwrap_or_default().iter().map(|a| Attachment {
                    blob_id: a["blobId"].as_str().unwrap_or("").into(), name: a["name"].as_str().unwrap_or("attachment").into(),
                    mime: a["type"].as_str().unwrap_or("application/octet-stream").into(), size: a["size"].as_u64().unwrap_or(0) as usize,
                }).collect();
                ST.with(|s| { let mut s = s.borrow_mut(); s.attachments = atts; s.forwarding = true; s.answering = m["id"].as_str().map(String::from); });
            }
            Kind::Reply | Kind::ReplyAll => {
                let src = if m["replyTo"].as_array().is_some_and(|a| !a.is_empty()) { &m["replyTo"] } else { &m["from"] };
                let mut to: Vec<String> = src.as_array().map(|a| a.iter().map(addr).collect()).unwrap_or_default();
                let mut cc: Vec<String> = vec![];
                if matches!(kind, Kind::ReplyAll) {
                    let seen = |list: &Vec<String>, e: &str| list.iter().any(|x| x.to_lowercase().contains(&e.to_lowercase()));
                    for x in m["to"].as_array().cloned().unwrap_or_default() {
                        let e = x["email"].as_str().unwrap_or("");
                        if !e.is_empty() && e.to_lowercase() != me && !seen(&to, e) { to.push(addr(&x)); }
                    }
                    for x in m["cc"].as_array().cloned().unwrap_or_default() {
                        let e = x["email"].as_str().unwrap_or("");
                        if !e.is_empty() && e.to_lowercase() != me && !seen(&to, e) && !seen(&cc, e) { cc.push(addr(&x)); }
                    }
                }
                u.set_c_to(to.join(", ").into());
                if !cc.is_empty() { u.set_c_cc(cc.join(", ").into()); u.set_c_show_ccbcc(true); }
                u.set_c_subject((if subj.to_lowercase().starts_with("re:") { subj.clone() } else { format!("Re: {subj}") }).into());
                let quoted: String = text.lines().map(|l| format!("> {l}\n")).collect();
                u.set_c_body(format!("\n\nOn {date}, {who} wrote:\n{quoted}").into());
                ST.with(|s| {
                    let mut s = s.borrow_mut();
                    s.answering = m["id"].as_str().map(String::from);
                    s.in_reply_to = msgid.into_iter().collect();
                    s.references = refs;
                });
            }
            Kind::New => {}
        }
    }
    refresh_attachments(u);
    u.invoke_tpl_refresh();
    u.set_composing(true);
}

/// Re-open a saved draft for editing.
pub fn open_draft(u: &AppWindow, c: Arc<Client>, m: &Value) {
    reset(u);
    ST.with(|s| s.borrow_mut().sig_done = true);
    load_identities(u, c, vec![]);
    let join = |k: &str| m[k].as_array().map(|a| a.iter().map(|x| {
        let (n, e) = (x["name"].as_str().unwrap_or(""), x["email"].as_str().unwrap_or(""));
        if n.is_empty() { e.to_string() } else { format!("\"{n}\" <{e}>") }
    }).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    u.set_c_to(join("to").into());
    u.set_c_cc(join("cc").into());
    u.set_c_bcc(join("bcc").into());
    u.set_c_show_ccbcc(!join("cc").is_empty() || !join("bcc").is_empty());
    u.set_c_subject(m["subject"].as_str().unwrap_or("").into());
    u.set_c_body(text_of(m).into());
    let atts: Vec<Attachment> = m["attachments"].as_array().cloned().unwrap_or_default().iter().map(|a| Attachment {
        blob_id: a["blobId"].as_str().unwrap_or("").into(), name: a["name"].as_str().unwrap_or("attachment").into(),
        mime: a["type"].as_str().unwrap_or("application/octet-stream").into(), size: a["size"].as_u64().unwrap_or(0) as usize,
    }).collect();
    ST.with(|s| {
        let mut s = s.borrow_mut();
        s.attachments = atts;
        s.draft_id = m["id"].as_str().map(String::from);
        s.in_reply_to = m["inReplyTo"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
        s.references = m["references"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
    });
    refresh_attachments(u);
    u.set_composing(true);
}

fn outgoing(u: &AppWindow) -> Outgoing {
    ST.with(|s| {
        let s = s.borrow();
        let from = s.identities.get(u.get_c_from_index() as usize).cloned().unwrap_or_default();
        let from_email = from.rsplit_once('<').map(|(_, e)| e.trim_end_matches('>').to_string()).unwrap_or(from);
        Outgoing {
            from: from_email,
            to: u.get_c_to().to_string(),
            cc: u.get_c_cc().to_string(),
            bcc: u.get_c_bcc().to_string(),
            subject: u.get_c_subject().to_string(),
            text: u.get_c_body().to_string(),
            html: None,
            attachments: s.attachments.clone(),
            in_reply_to: s.in_reply_to.clone(),
            references: s.references.clone(),
            answering: s.answering.clone(),
            forwarding: s.forwarding,
            replaces_draft: s.draft_id.clone(),
            hold_seconds: 0,
            read_receipt: false,
        }
    })
}

pub fn attach(u: &AppWindow, c: Arc<Client>) {
    let Some(files) = rfd::FileDialog::new().pick_files() else { return };
    let w = u.as_weak();
    u.set_c_status("Uploading…".into());
    std::thread::spawn(move || {
        let mut added = vec![];
        let mut err = None;
        for f in files {
            let name = f.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| "file".into());
            let mime = mime_for(&name);
            match std::fs::read(&f).map_err(|e| e.to_string()).and_then(|b| c.upload(b, mime)) {
                Ok((blob_id, size)) => added.push(Attachment { blob_id, name, mime: mime.into(), size }),
                Err(e) => { err = Some(e); break; }
            }
        }
        let _ = w.upgrade_in_event_loop(move |u| {
            ST.with(|s| s.borrow_mut().attachments.extend(added));
            refresh_attachments(&u);
            u.set_c_status(err.unwrap_or_default().into());
        });
    });
}

pub fn remove_attachment(u: &AppWindow, i: i32) {
    ST.with(|s| { let mut s = s.borrow_mut(); if (i as usize) < s.attachments.len() { s.attachments.remove(i as usize); } });
    refresh_attachments(u);
}

pub fn mime_for(name: &str) -> &'static str {
    match name.rsplit('.').next().unwrap_or("").to_lowercase().as_str() {
        "pdf" => "application/pdf", "png" => "image/png", "jpg" | "jpeg" => "image/jpeg", "gif" => "image/gif",
        "webp" => "image/webp", "svg" => "image/svg+xml", "txt" => "text/plain", "csv" => "text/csv", "html" | "htm" => "text/html",
        "zip" => "application/zip", "doc" => "application/msword",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xls" => "application/vnd.ms-excel", "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "ppt" => "application/vnd.ms-powerpoint", "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "ics" => "text/calendar", "eml" => "message/rfc822", "mp3" => "audio/mpeg", "mp4" => "video/mp4",
        _ => "application/octet-stream",
    }
}

/// Composer checks Bulwark runs before sending (attachment reminder, empty subject).
fn pre_send_warning(o: &Outgoing, prefs: &crate::settings::Settings) -> Option<String> {
    if prefs.bool("emptySubjectWarningEnabled", true) && o.subject.trim().is_empty() {
        return Some("The subject is empty. Press Send again to send anyway.".into());
    }
    if prefs.bool("attachmentReminderEnabled", true) && o.attachments.is_empty() {
        let body: String = o.text.lines().filter(|l| !l.starts_with('>')).collect::<Vec<_>>().join(" ").to_lowercase();
        if ["attached", "attachment", "enclosed", "anbei", "ekte", "ek'te", "pièce jointe", "adjunto"].iter().any(|k| body.contains(k)) {
            return Some("You mention an attachment but none is attached. Press Send again to send anyway.".into());
        }
    }
    None
}

pub fn send(u: &AppWindow, c: Arc<Client>, prefs: &crate::settings::Settings) {
    let mut o = outgoing(u);
    if crate::jmap::parse_addresses(&format!("{},{},{}", o.to, o.cc, o.bcc)).is_empty() {
        u.set_c_status("Add at least one recipient.".into());
        return;
    }
    let warned = u.get_c_status().ends_with("send anyway.");
    if prefs.bool("sendConfirmation", false) && !warned && !u.get_c_status().ends_with("to send.") {
        u.set_c_status("Press Send again to send.".into());
        return;
    }
    o.read_receipt = prefs.bool("requestReadReceiptDefault", false);
    if !warned {
        if let Some(w) = pre_send_warning(&o, prefs) { u.set_c_status(w.into()); return; }
        let draft = serde_json::json!({"to": o.to, "cc": o.cc, "bcc": o.bcc, "subject": o.subject, "text": o.text,
            "attachments": o.attachments.iter().map(|a| a.name.clone()).collect::<Vec<_>>()});
        if let Some(w) = crate::plugin_compose_check(&draft) { u.set_c_status(format!("{w} Press Send again to send anyway.").into()); return; }
    }
    let delay = prefs.prefs.get("sendDelaySeconds").and_then(|v| v.as_u64()).unwrap_or(5);
    let snapshot = ST.with(|s| s.borrow().clone());
    if delay > 0 && !c.supports_delayed_send() {
        // Bulwark-style undo window kept in the app: send when the timer runs out.
        u.set_composing(false);
        let id = format!("local-{}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_millis()).unwrap_or(0));
        PENDING.with(|p| *p.borrow_mut() = Some((id.clone(), o.clone(), snapshot)));
        crate::mail::show_toast(u, &format!("Sending in {delay} s…"), true);
        let w = u.as_weak();
        slint::Timer::single_shot(std::time::Duration::from_secs(delay), move || {
            let still = PENDING.with(|p| p.borrow().as_ref().is_some_and(|x| x.0 == id));
            if !still { return; }
            let Some((_, o, _)) = PENDING.with(|p| p.borrow_mut().take()) else { return };
            let (w2, c2) = (w.clone(), c.clone());
            std::thread::spawn(move || {
                let res = c2.send_message(&o);
                let w3 = w2.clone();
                let _ = w2.upgrade_in_event_loop(move |u| match res {
                    Ok(_) => crate::mail::show_toast(&u, "Message sent", false),
                    Err(e) => crate::mail::show_toast(&u, &format!("Not sent: {e}"), false),
                });
                crate::mail::load_mailboxes(w3, c2, false);
            });
        });
        return;
    }
    o.hold_seconds = delay;
    u.set_sending(true);
    u.set_c_status("".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.send_message(&o);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            u.set_sending(false);
            match res {
                Ok(sub) => {
                    u.set_composing(false);
                    match sub {
                        Some(id) => {
                            PENDING.with(|p| *p.borrow_mut() = Some((id, o, snapshot)));
                            crate::mail::show_toast(&u, "Sending…", true);
                        }
                        None => crate::mail::show_toast(&u, "Message sent", false),
                    }
                }
                Err(e) => u.set_c_status(e.into()),
            }
        });
        crate::mail::load_mailboxes(w2, c, false);
    });
}

/// Undo the last held send and put the message back in the composer.
pub fn undo_send(u: &AppWindow, c: Arc<Client>) {
    let Some((id, o, snap)) = PENDING.with(|p| p.borrow_mut().take()) else { return };
    u.set_toast("".into());
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = if id.starts_with("local-") { Ok(()) } else { c.cancel_submission(&id) };
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(()) => {
                ST.with(|s| { *s.borrow_mut() = snap; s.borrow_mut().draft_id = None; });
                u.set_c_to(o.to.into()); u.set_c_cc(o.cc.clone().into()); u.set_c_bcc(o.bcc.clone().into());
                u.set_c_show_ccbcc(!o.cc.is_empty() || !o.bcc.is_empty());
                u.set_c_subject(o.subject.into()); u.set_c_body(o.text.into());
                refresh_attachments(&u);
                u.set_c_status("Sending was canceled.".into());
                u.set_composing(true);
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Too late to undo: {e}"), false),
        });
    });
}

pub fn save_draft(u: &AppWindow, c: Arc<Client>, close: bool) {
    let o = outgoing(u);
    let w = u.as_weak();
    u.set_c_status("Saving…".into());
    std::thread::spawn(move || {
        let res = c.save_draft(&o, o.replaces_draft.as_deref());
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(id) => {
                ST.with(|s| s.borrow_mut().draft_id = Some(id));
                u.set_c_status("Draft saved".into());
                if close { u.set_composing(false); crate::mail::show_toast(&u, "Draft saved", false); }
            }
            Err(e) => u.set_c_status(e.into()),
        });
        crate::mail::load_mailboxes(w2, c, false);
    });
}

pub fn discard(u: &AppWindow, c: Arc<Client>) {
    let draft = ST.with(|s| s.borrow().draft_id.clone());
    u.set_composing(false);
    if let Some(id) = draft {
        let w = u.as_weak();
        std::thread::spawn(move || {
            let _ = c.destroy(&[id]);
            crate::mail::load_mailboxes(w.clone(), c.clone(), false);
            crate::mail::reload(w, c);
        });
    }
}
