//! Composer: new / reply / reply-all / forward / edit draft, identities, Cc/Bcc,
//! attachments (upload), drafts, and undo-send via EmailSubmission HOLDFOR.
use crate::jmap::{Attachment, Client, Outgoing};
use crate::rte::{self, model::{Block, Doc, Kind}};
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
    /// (text, html) signature per identity.
    signatures: Vec<(String, String)>,
    kind_new: bool,
    sig_done: bool,
    /// Composer document (for undo-send), and plain/receipt/tag options.
    body_doc: Option<Doc>,
    plain: bool,
    tags: Vec<bool>,
    /// Inline (cid) parts of the quoted original of a reply/forward, sent again when the quote keeps them.
    quoted_inline: Vec<(String, Attachment)>,
}

thread_local! {
    static ST: RefCell<State> = RefCell::new(State::default());
    static PENDING: RefCell<Option<(String, Outgoing, State)>> = const { RefCell::new(None) };
    static CLIENT: RefCell<Option<Arc<Client>>> = const { RefCell::new(None) };
}

/// Upload an inline image in the background and record its blob id in the document.
pub fn upload_inline(u: &AppWindow, cid: String, bytes: Vec<u8>, mime: String, _name: String) {
    let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { u.set_c_status("Not signed in.".into()); return };
    let w = u.as_weak();
    u.set_c_status("Uploading image…".into());
    std::thread::spawn(move || {
        let res = c.upload(bytes, &mime);
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok((blob, _)) => {
                rte::set_image_blob(&cid, &blob);
                if u.get_c_status().as_str() == "Uploading image…" { u.set_c_status("".into()); }
            }
            Err(e) => u.set_c_status(format!("Image upload failed: {e}").into()),
        });
    });
}

/// The quoted original of `m` as HTML (cleaned) and text.
fn quoted_of(m: &Value) -> (String, String) {
    let (_, text) = crate::body_text(m);
    let html = cid_html(m);
    if !html.trim().is_empty() {
        let h = rte::html::clean_quoted_html(&html);
        let t = if text.trim().is_empty() { rte::html::html_text(&h) } else { text };
        (h, t)
    } else {
        let h = text.lines().map(|l| format!("<div>{}</div>", if l.is_empty() { "<br>".into() } else { rte::html::esc(l) })).collect::<String>();
        (h, text)
    }
}

/// Bulwark formatRecipient: the bare address when there is no distinct name, otherwise
/// `Name <email>`, quoting (and escaping) the name only when it holds a special character.
pub fn format_recipient(name: &str, email: &str) -> String {
    let n = name.trim();
    if n.is_empty() || n.eq_ignore_ascii_case(email) { return email.to_string(); }
    if n.contains([',', '<', '>', '"', '@', ';', ':']) {
        format!("\"{}\" <{email}>", n.replace('\\', "\\\\").replace('"', "\\\""))
    } else {
        format!("{n} <{email}>")
    }
}

/// The HTML body with its original cid: image references (the reader swaps them for data: URIs).
fn cid_html(m: &Value) -> String {
    m["htmlBody"].as_array().map(|parts| parts.iter().filter_map(|p| {
        let pid = p["partId"].as_str()?;
        m["__cidBodyValues"][pid].as_str().or(m["bodyValues"][pid]["value"].as_str()).map(String::from)
    }).collect::<Vec<_>>().join("\n")).unwrap_or_default()
}

/// The cid parts of `m` that `html` references (Bulwark collectInlineImageCids).
fn inline_parts(m: &Value, html: &str) -> Vec<(String, Attachment)> {
    m["attachments"].as_array().map(|a| a.iter().filter_map(|a| {
        let cid = a["cid"].as_str()?.trim_matches(['<', '>']).to_string();
        html.contains(&format!("cid:{cid}")).then(|| (cid, Attachment {
            blob_id: a["blobId"].as_str().unwrap_or("").into(), name: a["name"].as_str().unwrap_or("image").into(),
            mime: a["type"].as_str().unwrap_or("application/octet-stream").into(), size: a["size"].as_u64().unwrap_or(0) as usize,
        }))
    }).collect()).unwrap_or_default()
}

pub enum ComposeKind { New, Reply, ReplyAll, Forward }

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

/// Addresses in Delivered-To and the like (Bulwark deliveryAddresses), in header priority.
fn delivery_addresses(m: &Value) -> Vec<String> {
    const NAMES: [&str; 4] = ["delivered-to", "x-original-to", "x-delivered-to", "x-original-delivered-to"];
    let headers = m["headers"].as_array().cloned().unwrap_or_default();
    let mut out: Vec<String> = vec![];
    for n in NAMES {
        for h in headers.iter().filter(|h| h["name"].as_str().is_some_and(|x| x.eq_ignore_ascii_case(n))) {
            let v = h["value"].as_str().unwrap_or("");
            let addr = v.split_once('<').and_then(|(_, r)| r.split_once('>')).map(|(a, _)| a.to_string())
                .or_else(|| v.split(|c: char| c.is_whitespace() || ",;\"'".contains(c)).find(|t| t.contains('@')).map(String::from));
            if let Some(a) = addr.map(|a| a.trim().trim_end_matches('.').to_string()).filter(|a| a.contains('@')) {
                if !out.iter().any(|x| x.eq_ignore_ascii_case(&a)) { out.push(a); }
            }
        }
    }
    out
}

/// What decides a reply's From (Bulwark email-composer + lib/reply-identity).
#[derive(Default, Clone)]
pub struct ReplyFrom {
    /// The original's sender, when the user sent it (keep sending as that identity).
    self_sent: Option<String>,
    /// To/Cc/Bcc of the original.
    recipients: Vec<String>,
    /// Delivered-To and friends.
    delivered: Vec<String>,
    /// Forwards never take the catch-all match.
    forward: bool,
}

fn domain_of(e: &str) -> String { e.rsplit_once('@').map(|(_, d)| d.to_lowercase()).unwrap_or_default() }

/// Bulwark resolveReplyFrom's catch-all step (replyIdentityMatch "domain"): an address on one
/// of the user's domains that is no identity picks the first identity on that domain.
fn catch_all_identity(emails: &[String], received: &[String]) -> Option<usize> {
    let owned: Vec<String> = emails.iter().map(|e| domain_of(e)).filter(|d| !d.is_empty()).collect();
    let hit = received.iter().find(|r| {
        let r = r.trim().to_lowercase();
        !emails.contains(&r) && owned.contains(&domain_of(&r))
    })?;
    emails.iter().position(|e| domain_of(e) == domain_of(hit)).or(Some(0))
}

/// Identity index for a reply/forward, in Bulwark's order: the identity that sent a self-sent
/// original; one of the user's own identities in To/Cc/Bcc, then Delivered-To; with
/// autoSelectReplyIdentity (not on forwards) a catch-all address on a user's domain
/// (replyIdentityMatch "domain"); else None (the default identity).
fn resolve_reply_identity(ids: &[Value], r: &ReplyFrom, auto: bool, match_mode: &str) -> Option<usize> {
    let emails: Vec<String> = ids.iter().map(|i| i["email"].as_str().unwrap_or("").trim().to_lowercase()).collect();
    if let Some(s) = &r.self_sent {
        if let Some(i) = reply_identity(ids, std::slice::from_ref(s)) { return Some(i); }
    }
    if let Some(i) = reply_identity(ids, &r.recipients) { return Some(i); }
    if auto && !r.forward && match_mode == "domain" {
        if let Some(i) = catch_all_identity(&emails, &r.recipients).or_else(|| catch_all_identity(&emails, &r.delivered)) { return Some(i); }
    }
    reply_identity(ids, &r.delivered)
}

fn load_identities(u: &AppWindow, c: Arc<Client>, reply: ReplyFrom) {
    let w = u.as_weak();
    std::thread::spawn(move || {
        let ids = c.identities().unwrap_or_default();
        let ids2 = ids.clone();
        let sigs: Vec<(String, String)> = ids.iter().map(|i| (i["textSignature"].as_str().unwrap_or("").to_string(), i["htmlSignature"].as_str().unwrap_or("").to_string())).collect();
        let list: Vec<String> = ids.iter().map(|i| {
            let (n, e) = (i["name"].as_str().unwrap_or(""), i["email"].as_str().unwrap_or(""));
            if n.is_empty() { e.to_string() } else { format!("{n} <{e}>") }
        }).collect();
        let _ = w.upgrade_in_event_loop(move |u| {
            let ids = ids2;
            // the From and signature the composer starts with are no edit of the user's
            let unedited = crate::autosave::unedited(&u);
            ST.with(|s| { let mut s = s.borrow_mut(); s.identities = list.clone(); s.signatures = sigs.clone(); });
            let model: Vec<SharedString> = list.iter().map(|x| x.as_str().into()).collect();
            u.set_c_identities(ModelRc::new(VecModel::from(model)));
            // default to the identity of the signed-in address, as Bulwark does
            let me = c.username.to_lowercase();
            let p = crate::settings::Settings::load();
            let idx = resolve_reply_identity(&ids, &reply, p.bool("autoSelectReplyIdentity", false), &p.str("replyIdentityMatch", "domain"))
                .or_else(|| ids.iter().position(|i| i["email"].as_str().is_some_and(|e| e.to_lowercase() == me)))
                .unwrap_or(0);
            u.set_c_from_index(idx as i32);
            // the signature of the identity just picked
            apply_signature(&u);
            if unedited { crate::autosave::rebase(&u); }
        });
    });
}

/// Insert the identity's signature once (as rich blocks), following Bulwark's signature settings.
fn apply_signature(u: &AppWindow) {
    let prefs = crate::settings::Settings::load();
    let ((text, html), new, done) = ST.with(|s| {
        let s = s.borrow();
        (s.signatures.get(u.get_c_from_index() as usize).cloned().unwrap_or_default(), s.kind_new, s.sig_done)
    });
    if done || (text.trim().is_empty() && rte::html::html_text(&html).trim().is_empty()) { return; }
    let unedited = crate::autosave::unedited(u);
    let enabled = if new { prefs.bool("signatureNewMailEnabled", true) } else { prefs.bool("signatureReplyForwardEnabled", true) };
    if !enabled { return; }
    let mut sig: Vec<Block> = vec![];
    if prefs.bool("signatureSeparatorEnabled", true) { sig.push(Block::para("-- ")); }
    if !rte::html::html_text(&html).trim().is_empty() { sig.extend(rte::html::import(&html)); }
    else { sig.extend(rte::html::import_text(text.trim_end())); }
    let above = prefs.str("signaturePosition", "below_quote") == "above_quote";
    rte::with(|e| {
        let blocks = &mut e.doc.blocks;
        // the reply header line before the quoted block, if any
        let q = blocks.iter().position(|b| matches!(b.kind, Kind::Quoted { .. }));
        let at = match q {
            Some(qi) if above => { let h = if qi > 0 && !blocks[qi - 1].is_empty() { qi - 1 } else { qi }; sig.push(Block::para("")); h }
            _ => { sig.insert(0, Block::para("")); blocks.len() }
        };
        let at = at.min(blocks.len());
        for (k, b) in sig.into_iter().enumerate() { blocks.insert(at + k, b); }
        e.doc.fix();
    });
    for b in rte::doc().blocks { if let Kind::Image(img) = &b.kind { rte::register_image(img); } }
    rte::refresh(u);
    ST.with(|s| s.borrow_mut().sig_done = true);
    // the signature a message starts with is no edit (Bulwark: no draft for it)
    if unedited { crate::autosave::rebase(u); }
}

/// Work left for a save still in flight from a replaced composer: generation, what to do, the
/// old composer's content (taken at the reset) and its key.
type Orphan = (u64, crate::autosave::Leftover, Outgoing, u64);

thread_local! { static ORPHANS: RefCell<Vec<Orphan>> = const { RefCell::new(vec![]) }; }

fn reset(u: &AppWindow) {
    // a save still in flight belongs to the composer being replaced: what is typed since, or
    // a save or discard queued behind it, must not be lost with the reset (see run_save)
    if crate::autosave::in_flight() {
        crate::chips::commit_all(u);
        let key = content_key(u);
        if let Some((gen, left)) = crate::autosave::reset_leftover(key) {
            let o = outgoing(u);
            ORPHANS.with(|x| x.borrow_mut().push((gen, left, o, key)));
        }
    } else {
        crate::autosave::reset();
    }
    u.global::<crate::CompX>().set_saved("".into());
    ST.with(|s| { let ids = s.borrow().identities.clone(); *s.borrow_mut() = State { identities: ids, ..Default::default() }; });
    crate::chips::reset(u);
    u.set_c_to("".into());
    u.set_c_cc("".into());
    u.set_c_bcc("".into());
    u.set_c_show_ccbcc(false);
    u.set_c_show_cc(false);
    u.set_c_show_bcc(false);
    u.set_c_read_receipt(false);
    // Bulwark plainTextMode: every message starts (and stays) plain text
    u.set_c_plain(crate::settings::Settings::load().bool("plainTextMode", false));
    u.set_c_tag_on(ModelRc::new(VecModel::from(vec![false; crate::mail::TAGS.len()])));
    u.set_c_subject("".into());
    rte::set_doc(u, Doc::default());
    u.set_c_status("".into());
    u.set_can_undo(false);
    refresh_attachments(u);
}

/// Open the composer for a new message, a reply, reply-all or forward of `m`.
pub fn open(u: &AppWindow, c: Arc<Client>, kind: ComposeKind, m: Option<&Value>) {
    reset(u);
    CLIENT.with(|x| *x.borrow_mut() = Some(c.clone()));
    crate::chips::prefetch(u, c.clone());
    ST.with(|s| s.borrow_mut().kind_new = matches!(kind, ComposeKind::New));
    // replies and forwards come from the identity the message reached (Bulwark)
    let reply = match (&kind, m) {
        (ComposeKind::Reply | ComposeKind::ReplyAll | ComposeKind::Forward, Some(m)) => {
            let sender = m["from"][0]["email"].as_str().unwrap_or("").to_string();
            let mine = crate::my_addrs_cached();
            ReplyFrom {
                self_sent: mine.iter().any(|a| a.eq_ignore_ascii_case(&sender)).then_some(sender),
                recipients: ["to", "cc", "bcc"].iter()
                    .flat_map(|k| m[*k].as_array().cloned().unwrap_or_default())
                    .filter_map(|x| x["email"].as_str().map(String::from)).collect(),
                delivered: delivery_addresses(m),
                forward: matches!(kind, ComposeKind::Forward),
            }
        }
        _ => ReplyFrom::default(),
    };
    load_identities(u, c.clone(), reply);
    if let Some(m) = m {
        let me = c.username.to_lowercase();
        let subj = m["subject"].as_str().unwrap_or("").to_string();
        let who = { let (n, e) = (crate::mail::sender(m), m["from"][0]["email"].as_str().unwrap_or("")); if n.eq_ignore_ascii_case(e) || e.is_empty() { n } else { format!("{n} <{e}>") } };
        let date = crate::mail::parse_time(&m["receivedAt"]).map(|t| t.format("%a %d %b %Y, %H:%M").to_string()).unwrap_or_default();
        let (qhtml, qtext) = quoted_of(m);
        let msgid = m["messageId"][0].as_str().map(String::from);
        let mut refs: Vec<String> = m["references"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
        if let Some(id) = &msgid { refs.push(id.clone()); }
        let addr = |x: &Value| -> String {
            let (n, e) = (x["name"].as_str().unwrap_or(""), x["email"].as_str().unwrap_or(""));
            format_recipient(n, e)
        };
        match kind {
            ComposeKind::Forward => {
                let inline = inline_parts(m, &qhtml);
                u.set_c_subject((if subj.to_lowercase().starts_with("fwd:") { subj.clone() } else { format!("Fwd: {subj}") }).into());
                let blocks = vec![Block::para(""), Block::para("---------- Forwarded message ----------"),
                    Block::para(&format!("From: {who}\nDate: {date}\nSubject: {subj}")),
                    Block::new(Kind::Quoted { html: qhtml, text: qtext, open: true, cite: false })];
                rte::set_doc(u, Doc { blocks });
                // carry the original attachments
                let atts: Vec<Attachment> = m["attachments"].as_array().cloned().unwrap_or_default().iter()
                    .filter(|a| !inline.iter().any(|(_, i)| a["blobId"].as_str() == Some(i.blob_id.as_str()))).map(|a| Attachment {
                    blob_id: a["blobId"].as_str().unwrap_or("").into(), name: a["name"].as_str().unwrap_or("attachment").into(),
                    mime: a["type"].as_str().unwrap_or("application/octet-stream").into(), size: a["size"].as_u64().unwrap_or(0) as usize,
                }).collect();
                ST.with(|s| { let mut s = s.borrow_mut(); s.attachments = atts; s.quoted_inline = inline; s.forwarding = true; s.answering = m["id"].as_str().filter(|_| m["__jmap"].is_null()).map(String::from); });
            }
            ComposeKind::Reply | ComposeKind::ReplyAll => {
                let src = if m["replyTo"].as_array().is_some_and(|a| !a.is_empty()) { &m["replyTo"] } else { &m["from"] };
                let mut to: Vec<String> = src.as_array().map(|a| a.iter().map(addr).collect()).unwrap_or_default();
                let mut cc: Vec<String> = vec![];
                if matches!(kind, ComposeKind::ReplyAll) {
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
                if !cc.is_empty() { u.set_c_cc(cc.join(", ").into()); u.set_c_show_ccbcc(true); u.set_c_show_cc(true); }
                u.set_c_subject((if subj.to_lowercase().starts_with("re:") { subj.clone() } else { format!("Re: {subj}") }).into());
                let inline = inline_parts(m, &qhtml);
                let blocks = vec![Block::para(""), Block::para(&format!("On {date}, {who} wrote:")),
                    Block::new(Kind::Quoted { html: qhtml, text: qtext, open: false, cite: true })];
                rte::set_doc(u, Doc { blocks });
                ST.with(|s| {
                    let mut s = s.borrow_mut();
                    s.answering = m["id"].as_str().filter(|_| m["__jmap"].is_null()).map(String::from);
                    s.in_reply_to = msgid.into_iter().collect();
                    s.references = refs;
                    s.quoted_inline = inline;
                });
            }
            ComposeKind::New => {}
        }
    }
    refresh_attachments(u);
    u.invoke_tpl_refresh();
    if m.is_some() { rte::focus(u); } else { u.set_rte_focus_on_open(false); }
    u.set_composing(true);
    crate::autosave::start(u, false);
}

/// Re-open a saved draft for editing.
pub fn open_draft(u: &AppWindow, c: Arc<Client>, m: &Value) {
    reset(u);
    CLIENT.with(|x| *x.borrow_mut() = Some(c.clone()));
    crate::chips::prefetch(u, c.clone());
    ST.with(|s| s.borrow_mut().sig_done = true);
    load_identities(u, c, ReplyFrom::default());
    let join = |k: &str| m[k].as_array().map(|a| a.iter().map(|x| {
        let (n, e) = (x["name"].as_str().unwrap_or(""), x["email"].as_str().unwrap_or(""));
        format_recipient(n, e)
    }).collect::<Vec<_>>().join(", ")).unwrap_or_default();
    u.set_c_to(join("to").into());
    u.set_c_cc(join("cc").into());
    u.set_c_bcc(join("bcc").into());
    u.set_c_show_ccbcc(!join("cc").is_empty() || !join("bcc").is_empty());
    u.set_c_show_cc(!join("cc").is_empty());
    u.set_c_show_bcc(!join("bcc").is_empty());
    u.set_c_subject(m["subject"].as_str().unwrap_or("").into());
    let (html, _) = crate::body_text(m);
    let blocks = if html.trim().is_empty() { rte::html::import_text(&text_of(m)) } else { rte::html::import_with_quotes(&rte::html::clean_quoted_html(&html)) };
    rte::set_doc(u, Doc { blocks });
    // inline images referenced by cid stay in the body (fetched for display), not in the attachment list
    let all = m["attachments"].as_array().cloned().unwrap_or_default();
    // the reader already turned cid: references into data: URIs, so inline parts are matched in order
    let has_data_imgs = html.contains("src=\"data:image/");
    let is_inline = |a: &Value| a["cid"].as_str().is_some_and(|c| html.contains(&format!("cid:{c}")))
        || (has_data_imgs && a["cid"].is_string() && a["disposition"] == "inline" && a["type"].as_str().is_some_and(|t| t.starts_with("image/")));
    let atts: Vec<Attachment> = all.iter().filter(|a| !is_inline(a)).map(|a| Attachment {
        blob_id: a["blobId"].as_str().unwrap_or("").into(), name: a["name"].as_str().unwrap_or("attachment").into(),
        mime: a["type"].as_str().unwrap_or("application/octet-stream").into(), size: a["size"].as_u64().unwrap_or(0) as usize,
    }).collect();
    for a in all.iter().filter(|a| is_inline(a)) {
        let (cid, blob) = (a["cid"].as_str().unwrap_or("").to_string(), a["blobId"].as_str().unwrap_or("").to_string());
        let (name, mime) = (a["name"].as_str().unwrap_or("image").to_string(), a["type"].as_str().unwrap_or("image/png").to_string());
        if !html.contains(&format!("cid:{cid}")) { rte::adopt_data_image(&cid, &blob, &mime, &name); continue; }
        rte::set_image_info(&cid, &blob, &mime, &name);
        let Some(c2) = CLIENT.with(|x| x.borrow().clone()) else { continue };
        let w = u.as_weak();
        std::thread::spawn(move || {
            let Ok(bytes) = c2.download(&blob, &name, &mime) else { return };
            let _ = w.upgrade_in_event_loop(move |u| rte::set_image_bytes(&u, &cid, bytes));
        });
    }
    ST.with(|s| {
        let mut s = s.borrow_mut();
        s.attachments = atts;
        s.draft_id = m["id"].as_str().map(String::from);
        s.in_reply_to = m["inReplyTo"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
        s.references = m["references"].as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default();
    });
    refresh_attachments(u);
    rte::focus(u);
    u.set_composing(true);
    crate::autosave::start(u, true);
}

fn outgoing(u: &AppWindow) -> Outgoing {
    ST.with(|s| {
        let s = s.borrow();
        let from = s.identities.get(u.get_c_from_index() as usize).cloned().unwrap_or_default();
        let from_email = from.rsplit_once('<').map(|(_, e)| e.trim_end_matches('>').to_string()).unwrap_or(from);
        Outgoing {
            from: from_email,
            // contact groups go out as their members, IDN domains in ASCII form
            to: crate::recipients::expand_for_send(&u.get_c_to()),
            cc: crate::recipients::expand_for_send(&u.get_c_cc()),
            bcc: crate::recipients::expand_for_send(&u.get_c_bcc()),
            subject: u.get_c_subject().to_string(),
            text: rte::to_text(),
            html: (!u.get_c_plain()).then(rte::to_html),
            inline: if u.get_c_plain() { vec![] } else {
                rte::inline_images().into_iter().filter_map(|(cid, blob, mime, name, size)| Some((cid, Attachment { blob_id: blob?, name, mime, size })))
                    .chain(s.quoted_inline.iter().cloned()).collect()
            },
            attachments: s.attachments.clone(),
            in_reply_to: s.in_reply_to.clone(),
            references: s.references.clone(),
            answering: s.answering.clone(),
            forwarding: s.forwarding,
            replaces_draft: s.draft_id.clone(),
            hold_seconds: 0,
            send_at: None,
            read_receipt: u.get_c_read_receipt(),
            keywords: {
                use slint::Model;
                let on = u.get_c_tag_on();
                crate::mail::TAGS.iter().enumerate().filter(|(i, _)| on.row_data(*i).unwrap_or(false)).map(|(_, t)| format!("$label:{}", t.0)).collect()
            },
        }
    })
}

/// A cheap fingerprint of the composer (editor version and the fields), to notice changes.
pub fn cheap_key(u: &AppWindow) -> u64 {
    use std::hash::{Hash, Hasher};
    use slint::Model;
    let mut h = std::collections::hash_map::DefaultHasher::new();
    rte::with(|e| e.version).hash(&mut h);
    (u.get_c_to().as_str(), u.get_c_cc().as_str(), u.get_c_bcc().as_str(), u.get_c_subject().as_str()).hash(&mut h);
    (u.get_c_from_index(), u.get_c_plain(), u.get_c_read_receipt()).hash(&mut h);
    u.get_c_tag_on().iter().collect::<Vec<_>>().hash(&mut h);
    ST.with(|s| s.borrow().attachments.iter().map(|a| (a.name.clone(), a.size)).collect::<Vec<_>>()).hash(&mut h);
    h.finish()
}

/// What a draft would hold, hashed (Bulwark compares this before each autosave). Blob ids
/// stay out of it: they change with every saved version.
pub fn content_key(u: &AppWindow) -> u64 {
    use std::hash::{Hash, Hasher};
    let o = outgoing(u);
    let mut h = std::collections::hash_map::DefaultHasher::new();
    (&o.from, &o.to, &o.cc, &o.bcc, &o.subject, o.html.as_ref().unwrap_or(&o.text)).hash(&mut h);
    o.attachments.iter().map(|a| (&a.name, &a.mime, a.size)).collect::<Vec<_>>().hash(&mut h);
    (&o.keywords, o.read_receipt).hash(&mut h);
    h.finish()
}

/// Toggle tag `i` (index into `mail::TAGS`) for the message being composed.
pub fn toggle_tag(u: &AppWindow, i: i32) {
    use slint::Model;
    let on = u.get_c_tag_on();
    let mut v: Vec<bool> = (0..crate::mail::TAGS.len()).map(|k| on.row_data(k).unwrap_or(false)).collect();
    if let Some(x) = v.get_mut(i as usize) { *x = !*x; }
    u.set_c_tag_on(ModelRc::new(VecModel::from(v)));
}

pub fn attach(u: &AppWindow, c: Arc<Client>) {
    let Some(files) = crate::automation::pick_files() else { return };
    attach_files(u, c, files);
}

/// Attach files dropped on the composer.
pub fn attach_paths(u: &AppWindow, files: Vec<std::path::PathBuf>) {
    let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { return };
    attach_files(u, c, files);
}

fn attach_files(u: &AppWindow, c: Arc<Client>, files: Vec<std::path::PathBuf>) {
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

/// Bulwark lib/placeholder-check findUnfilledPlaceholders (English words): template fields
/// ({{name}}), bracketed fill-ins ([TIME], [Your Name], [insert date]), TODO-style markers
/// and circle stand-ins (〇〇). The subject is only checked for template fields.
pub fn find_placeholders(subject: &str, body: &str) -> Vec<String> {
    let mut found: Vec<String> = vec![];
    let mut add = |s: &str| { if !found.iter().any(|f| f == s) { found.push(s.to_string()); } };
    fn templates(t: &str, add: &mut dyn FnMut(&str)) {
        let mut rest = t;
        while let Some(i) = rest.find("{{") {
            let after = &rest[i + 2..];
            let Some(j) = after.find("}}") else { break };
            let inner = &after[..j];
            let name = inner.split(':').next().unwrap_or("").trim();
            if !inner.contains('\n') && !inner.contains('{') && !name.is_empty() && name.chars().all(|c| c.is_alphanumeric() || "_.-".contains(c)) {
                add(&rest[i..i + 2 + j + 2]);
            }
            rest = &after[j + 2..];
        }
    }
    templates(subject, &mut add);
    templates(body, &mut add);
    // brackets: plain, full-width and lenticular
    let chars: Vec<(usize, char)> = body.char_indices().collect();
    let mut k = 0;
    while k < chars.len() {
        let (start, c) = chars[k];
        if matches!(c, '[' | '［' | '【') {
            if let Some(e) = (k + 1..chars.len().min(k + 45)).find(|&e| matches!(chars[e].1, ']' | '］' | '】' | '[' | '［' | '【' | '\n')) {
                if matches!(chars[e].1, ']' | '］' | '】') {
                    let end = chars[e].0 + chars[e].1.len_utf8();
                    let inner: String = chars[k + 1..e].iter().map(|x| x.1).collect();
                    let link = body[end..].starts_with('(');
                    if !link && is_fill_in(&inner, c == '[') { add(&body[start..end]); k = e + 1; continue; }
                }
            }
        }
        k += 1;
    }
    for w in body.split(|c: char| !(c.is_alphanumeric() || c == '_')) {
        if ["TODO", "TBD", "TBA", "TBC", "FIXME", "XXX"].contains(&w) { add(w); }
    }
    let circles: String = body.chars().filter(|c| matches!(c, '〇' | '○' | '◯')).collect();
    if circles.chars().count() >= 2 {
        // only runs of two or more
        let mut run = String::new();
        for ch in body.chars().chain(std::iter::once(' ')) {
            if matches!(ch, '〇' | '○' | '◯') { run.push(ch); } else { if run.chars().count() >= 2 { add(&run.clone()); } run.clear(); }
        }
    }
    found
}

/// [CAPS], [Title Words] (1-3 capitalised words) or a prompt like [your name] / [insert date].
fn is_fill_in(inner: &str, ascii_bracket: bool) -> bool {
    let n = inner.chars().count();
    let caps = ascii_bracket && (2..=30).contains(&n)
        && inner.chars().next().is_some_and(|c| c.is_uppercase() || c.is_ascii_digit())
        && inner.chars().last().is_some_and(|c| c.is_uppercase() || c.is_ascii_digit())
        && inner.chars().all(|c| c.is_uppercase() || c.is_ascii_digit() || " _./-".contains(c))
        && inner.chars().filter(|c| c.is_uppercase()).count() >= 2;
    let words: Vec<&str> = inner.split(' ').collect();
    let title = ascii_bracket && (1..=3).contains(&words.len()) && words.iter().all(|w| {
        let mut cs = w.chars();
        cs.next().is_some_and(|c| c.is_uppercase()) && cs.clone().count() >= 1 && cs.all(|c| c.is_lowercase() || "'’-".contains(c))
    });
    let lower = inner.to_lowercase();
    let prompt = n <= 46 && ["your ", "insert ", "enter ", "add "].iter().any(|p| lower.starts_with(p) && lower.len() > p.len());
    caps || title || prompt
}

/// Composer checks Bulwark runs before sending (attachment reminder, empty subject, placeholders).
fn pre_send_warning(o: &Outgoing, prefs: &crate::settings::Settings) -> Option<String> {
    if prefs.bool("emptySubjectWarningEnabled", true) && o.subject.trim().is_empty() {
        return Some("The subject is empty. Press Send again to send anyway.".into());
    }
    if prefs.bool("placeholderWarningEnabled", true) {
        // only what the user wrote: not the quoted original
        let body: String = o.text.lines().filter(|l| !l.starts_with('>')).collect::<Vec<_>>().join("\n");
        let found = find_placeholders(&o.subject, &body);
        if !found.is_empty() {
            return Some(format!("Still to fill in: {}. Press Send again to send anyway.", found.join(", ")));
        }
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
    send_at(u, c, prefs, None);
}

/// Schedule send: the server holds the message until `at` (Unix ms).
pub fn send_scheduled(u: &AppWindow, c: Arc<Client>, prefs: &crate::settings::Settings, at: i64) {
    send_at(u, c, prefs, Some(at));
}

fn send_at(u: &AppWindow, c: Arc<Client>, prefs: &crate::settings::Settings, at: Option<i64>) {
    if crate::autosave::in_flight() {
        // the draft is being saved: send once it is, so the send replaces that draft
        crate::autosave::queue(match at { Some(t) => crate::autosave::Queued::Schedule(t), None => crate::autosave::Queued::Send });
        return;
    }
    if !u.get_c_plain() && rte::inline_images().iter().any(|i| i.1.is_none()) {
        u.set_c_status("Wait for the images to finish uploading.".into());
        return;
    }
    crate::chips::commit_all(u);
    let all = crate::recipients::parse_list(&format!("{}, {}, {}", u.get_c_to(), u.get_c_cc(), u.get_c_bcc()));
    let bad = crate::recipients::invalid_addresses(&all);
    if !bad.is_empty() {
        // Bulwark validation.invalid_recipients: never send a malformed mailbox
        u.set_c_status(format!("Not a valid address: {}", bad.join(", ")).into());
        return;
    }
    let mut o = outgoing(u);
    if crate::jmap::parse_addresses(&format!("{},{},{}", o.to, o.cc, o.bcc)).is_empty() {
        u.set_c_status("Add at least one recipient.".into());
        return;
    }
    let warned = u.get_c_status().ends_with("send anyway.");
    // picking a time is the confirmation for a scheduled send
    if at.is_none() && prefs.bool("sendConfirmation", false) && !warned && !u.get_c_status().ends_with("to send.") {
        u.set_c_status("Press Send again to send.".into());
        return;
    }
    o.read_receipt = o.read_receipt || prefs.bool("requestReadReceiptDefault", false);
    if !warned {
        if let Some(w) = pre_send_warning(&o, prefs) { u.set_c_status(w.into()); return; }
        let draft = serde_json::json!({"to": o.to, "cc": o.cc, "bcc": o.bcc, "subject": o.subject, "text": o.text,
            "attachments": o.attachments.iter().map(|a| a.name.clone()).collect::<Vec<_>>()});
        if let Some(w) = crate::plugin_compose_check(&draft) { u.set_c_status(format!("{w} Press Send again to send anyway.").into()); return; }
    }
    let delay = prefs.prefs.get("sendDelaySeconds").and_then(|v| v.as_u64()).unwrap_or(5);
    let snapshot = ST.with(|s| {
        use slint::Model;
        let mut x = s.borrow().clone();
        x.body_doc = Some(rte::doc());
        x.plain = u.get_c_plain();
        x.tags = u.get_c_tag_on().iter().collect();
        x
    });
    if let Some(t) = at {
        o.send_at = Some(t);
        u.set_sending(true);
        u.set_c_status("".into());
        let w = u.as_weak();
        let gen = crate::autosave::begin();
        std::thread::spawn(move || {
            let res = c.send_message(&o);
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |u| {
                let queued = crate::autosave::finish(gen, None);
                u.set_sending(false);
                match res {
                    Ok(_) => {
                        u.set_composing(false);
                        let when = chrono::DateTime::from_timestamp_millis(t).map(|d| d.with_timezone(&chrono::Local))
                            .map(|d| crate::schedule::label(d, crate::settings::Settings::load().str("timeFormat", "24h") == "12h", chrono::Local::now()))
                            .unwrap_or_default();
                        crate::mail::show_toast(&u, &format!("Scheduled to send at {when}"), false);
                    }
                    Err(e) => { u.set_c_status(e.into()); run_queued(&u, queued); }
                }
            });
            crate::mail::load_mailboxes(w2, c, false);
        });
        return;
    }
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
    let gen = crate::autosave::begin();
    std::thread::spawn(move || {
        let res = c.send_message(&o);
        let w2 = w.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let queued = crate::autosave::finish(gen, None);
            u.set_sending(false);
            if res.is_err() { run_queued(&u, queued); }
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
        let res = if id.starts_with("local-") { Ok(None) } else { c.cancel_submission(&id) };
        let _ = w.upgrade_in_event_loop(move |u| match res {
            Ok(restored) => {
                let (doc, plain, tags) = (snap.body_doc.clone().unwrap_or_default(), snap.plain, snap.tags.clone());
                ST.with(|s| { *s.borrow_mut() = snap; s.borrow_mut().draft_id = restored; });
                u.set_c_plain(plain);
                u.set_c_read_receipt(o.read_receipt);
                u.set_c_tag_on(ModelRc::new(VecModel::from(tags)));
                u.set_c_to(o.to.into()); u.set_c_cc(o.cc.clone().into()); u.set_c_bcc(o.bcc.clone().into());
                u.set_c_show_ccbcc(!o.cc.is_empty() || !o.bcc.is_empty());
                u.set_c_show_cc(!o.cc.is_empty());
                u.set_c_show_bcc(!o.bcc.is_empty());
                u.set_c_subject(o.subject.into()); rte::set_doc(&u, doc);
                refresh_attachments(&u);
                u.set_c_status("Sending was canceled.".into());
                u.set_composing(true);
                crate::autosave::start(&u, ST.with(|s| s.borrow().draft_id.is_some()));
            }
            Err(e) => crate::mail::show_toast(&u, &format!("Too late to undo: {e}"), false),
        });
    });
}

pub fn save_draft(u: &AppWindow, c: Arc<Client>, close: bool) {
    crate::chips::commit_all(u);
    if crate::autosave::in_flight() {
        crate::autosave::queue(crate::autosave::Queued::Save(close));
        if close { u.set_composing(false); }
        return;
    }
    let has_draft = ST.with(|s| s.borrow().draft_id.is_some());
    if close && !has_draft && crate::autosave::unedited(u) {
        // closed before anything was typed: nothing is kept (Bulwark)
        u.set_composing(false);
        return;
    }
    if has_draft && crate::autosave::is_saved(u) {
        // autosave already stored all of it
        u.set_c_status("Draft saved".into());
        if close { u.set_composing(false); crate::mail::show_toast(u, "Draft saved", false); }
        return;
    }
    run_save(u, c, false, close);
}

/// The debounced save while typing (src/autosave.rs decided it is due).
pub fn autosave_now(u: &AppWindow) {
    let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { return };
    // images still uploading have no blob yet: wait for them
    if !u.get_c_plain() && rte::inline_images().iter().any(|i| i.1.is_none()) { return; }
    if crate::recipients::parse_list(&format!("{},{},{}", u.get_c_to(), u.get_c_cc(), u.get_c_bcc())).is_empty()
        && u.get_c_subject().trim().is_empty() && rte::to_text().trim().is_empty() && ST.with(|s| s.borrow().attachments.is_empty()) {
        return;
    }
    run_save(u, c, true, false);
}

fn run_save(u: &AppWindow, c: Arc<Client>, auto: bool, close: bool) {
    let o = outgoing(u);
    let key = content_key(u);
    let first = o.replaces_draft.is_none();
    let gen = crate::autosave::begin_save(key);
    let x = u.global::<crate::CompX>();
    x.set_saved("Saving…".into());
    if !auto { u.set_c_status("Saving…".into()); }
    let w = u.as_weak();
    std::thread::spawn(move || {
        let res = c.save_draft(&o, o.replaces_draft.as_deref());
        let w2 = w.clone();
        let c2 = c.clone();
        let _ = w.upgrade_in_event_loop(move |u| {
            let mine = crate::autosave::gen() == gen;
            let x = u.global::<crate::CompX>();
            match &res {
                Ok(id) if mine => {
                    ST.with(|s| s.borrow_mut().draft_id = Some(id.clone()));
                    x.set_saved(format!("Draft saved {}", chrono::Local::now().format("%H:%M")).into());
                    if !auto { u.set_c_status("Draft saved".into()); }
                    if close { u.set_composing(false); crate::mail::show_toast(&u, "Draft saved", false); }
                }
                Ok(_) => {}
                Err(e) if mine => {
                    x.set_saved("Draft not saved".into());
                    if !auto || close { u.set_c_status(e.clone().into()); }
                    if close { u.set_composing(true); }
                }
                Err(_) => {}
            }
            let queued = crate::autosave::finish(gen, res.as_ref().ok().map(|_| key));
            run_queued(&u, queued);
            if !mine { finish_orphan(&u, gen, res.as_ref().ok().cloned(), &c2); }
            if first || !auto { crate::mail::load_mailboxes(w2, c2, false); }
        });
    });
}

/// The save of a composer that was replaced while it ran finished (`saved`: the draft it made):
/// save the content the user had at the reset over it, or remove it when a discard waited.
fn finish_orphan(u: &AppWindow, gen: u64, saved: Option<String>, c: &Arc<Client>) {
    let Some((_, left, mut o, _)) = ORPHANS.with(|x| {
        let mut x = x.borrow_mut();
        x.iter().position(|o| o.0 == gen).map(|i| x.remove(i))
    }) else { return };
    use crate::autosave::Leftover;
    let w = u.as_weak();
    let c = c.clone();
    match left {
        Leftover::Discard => {
            let Some(id) = saved.or(o.replaces_draft.clone()) else { return };
            std::thread::spawn(move || { let _ = c.destroy(&[id]); crate::mail::load_mailboxes(w, c, false); });
        }
        Leftover::Save => {
            if saved.is_some() { o.replaces_draft = saved; }
            std::thread::spawn(move || {
                let res = c.save_draft(&o, o.replaces_draft.as_deref());
                let w2 = w.clone();
                let _ = w.upgrade_in_event_loop(move |u| match res {
                    Ok(_) => crate::mail::show_toast(&u, "Draft saved", false),
                    Err(e) => crate::mail::show_toast(&u, &format!("Draft not saved: {e}"), false),
                });
                crate::mail::load_mailboxes(w2, c, false);
            });
        }
    }
}

/// What waited for a save: send, an explicit save, or a discard of the draft.
fn run_queued(u: &AppWindow, q: Option<crate::autosave::Queued>) {
    use crate::autosave::Queued;
    match q {
        Some(Queued::Send) => u.invoke_send_now(),
        Some(Queued::Schedule(t)) => {
            let Some(c) = CLIENT.with(|c| c.borrow().clone()) else { return };
            send_scheduled(u, c, &crate::settings::Settings::load(), t);
        }
        Some(Queued::Save(close)) => u.invoke_save_draft(close),
        Some(Queued::Discard) => u.invoke_discard_draft(),
        None => {}
    }
}

pub fn discard(u: &AppWindow, c: Arc<Client>) {
    if crate::autosave::in_flight() {
        // a save is creating the draft: remove it once it exists
        crate::autosave::queue(crate::autosave::Queued::Discard);
        u.set_composing(false);
        return;
    }
    let draft = ST.with(|s| s.borrow_mut().draft_id.take());
    u.set_composing(false);
    if let Some(id) = draft {
        let w = u.as_weak();
        std::thread::spawn(move || {
            let _ = c.destroy(&[id]);
            // the list and folder state live on the UI thread
            let w2 = w.clone();
            let _ = w.upgrade_in_event_loop(move |_| { crate::mail::load_mailboxes(w2.clone(), c.clone(), false); crate::mail::sync(w2, c); });
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn recipients_round_trip() {
        assert_eq!(format_recipient("", "a@b.c"), "a@b.c");
        assert_eq!(format_recipient("a@b.c", "a@b.c"), "a@b.c");
        assert_eq!(format_recipient("Jane Doe", "j@d.o"), "Jane Doe <j@d.o>");
        let odd = format_recipient("Doe, \"J\"", "j@d.o");
        assert_eq!(odd, "\"Doe, \\\"J\\\"\" <j@d.o>");
        let back = crate::jmap::parse_addresses(&format!("{odd}, x@y.z"));
        assert_eq!(back.len(), 2);
        assert_eq!(back[0]["name"], "Doe, \"J\"");
        assert_eq!(back[0]["email"], "j@d.o");
    }

    #[test]
    fn quoted_inline_parts_use_cid_html() {
        let m = json!({"htmlBody": [{"partId": "1"}], "bodyValues": {"1": {"value": "<img src=\"data:image/png;base64,AA\">"}},
            "__cidBodyValues": {"1": "<img src=\"cid:i1@x\">"},
            "attachments": [{"cid": "<i1@x>", "blobId": "B1", "type": "image/png", "name": "a.png", "size": 3}, {"blobId": "B2", "name": "doc.pdf"}]});
        let h = cid_html(&m);
        assert!(h.contains("cid:i1@x"));
        let p = inline_parts(&m, &h);
        assert_eq!(p.len(), 1);
        assert_eq!(p[0].0, "i1@x");
        assert_eq!(p[0].1.blob_id, "B1");
    }

    fn ids() -> Vec<Value> {
        vec![json!({"email": "me@a.com"}), json!({"email": "info@b.org"}), json!({"email": "me+news@a.com"})]
    }

    #[test]
    fn reply_identity_order() {
        let r = |rec: &[&str], del: &[&str], fwd: bool| ReplyFrom {
            self_sent: None, recipients: rec.iter().map(|s| s.to_string()).collect(),
            delivered: del.iter().map(|s| s.to_string()).collect(), forward: fwd,
        };
        // own identity in the recipients, always
        assert_eq!(resolve_reply_identity(&ids(), &r(&["x@y.z", "INFO@b.org"], &[], false), false, "exact"), Some(1));
        // sub-address tag: the untagged identity wins
        assert_eq!(resolve_reply_identity(&ids(), &r(&["me+shop@a.com"], &[], false), false, "exact"), Some(0));
        // catch-all on a user's domain only with the setting and domain matching
        assert_eq!(resolve_reply_identity(&ids(), &r(&["sales@b.org"], &[], false), true, "domain"), Some(1));
        assert_eq!(resolve_reply_identity(&ids(), &r(&["sales@b.org"], &[], false), true, "exact"), None);
        assert_eq!(resolve_reply_identity(&ids(), &r(&["sales@b.org"], &[], false), false, "domain"), None);
        // never on a forward
        assert_eq!(resolve_reply_identity(&ids(), &r(&["sales@b.org"], &[], true), true, "domain"), None);
        // Delivered-To after the headers
        assert_eq!(resolve_reply_identity(&ids(), &r(&["list@x.net"], &["info@b.org"], false), false, "exact"), Some(1));
        // self-sent: keep the sending identity
        let s = ReplyFrom { self_sent: Some("info@b.org".into()), recipients: vec!["me@a.com".into()], ..Default::default() };
        assert_eq!(resolve_reply_identity(&ids(), &s, false, "exact"), Some(1));
    }

    #[test]
    fn placeholders() {
        let f = |s: &str, b: &str| find_placeholders(s, b);
        assert_eq!(f("Hi {{ name }}", "Meet at [TIME] with [Your Name], TODO: agenda"), vec!["{{ name }}", "[TIME]", "[Your Name]", "TODO"]);
        assert_eq!(f("", "see [insert date here] and 〇〇様"), vec!["[insert date here]", "〇〇"]);
        // ordinary brackets, links and lowercase words are text
        assert!(f("", "[Yours truly] [Anlage 2] [link](http://x) [A] todo xxxl").is_empty());
        assert!(f("{{bad name}}", "").is_empty());
    }

    #[test]
    fn delivered_to_headers() {
        let m = json!({"headers": [
            {"name": "X-Original-To", "value": " a@b.c"},
            {"name": "Delivered-To", "value": "Someone <d@e.f>"},
            {"name": "delivered-to", "value": "a@b.c"},
        ]});
        assert_eq!(delivery_addresses(&m), vec!["d@e.f".to_string(), "a@b.c".to_string()]);
    }
}
