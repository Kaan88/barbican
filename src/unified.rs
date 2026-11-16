//! Bulwark's unified mailbox ("All inboxes"): a pool of connected clients for every
//! remembered account, and a merged Inbox query across them.
//! Clients connect lazily on background threads; the active session's client is reused.
use crate::jmap::Client;
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

pub const FOLDER_ID: &str = "__unified";

/// Connected clients keyed by remembered account user (the `user` of oauth::accounts()).
static POOL: Mutex<Option<HashMap<String, Arc<Client>>>> = Mutex::new(None);
/// Remembered user of the active session.
static ACTIVE: Mutex<String> = Mutex::new(String::new());

/// Connect a client from a remembered account Value (server/user/kind/pass or refresh/endpoint/client).
pub fn connect(v: &Value) -> Result<Client, String> {
    let server = v["server"].as_str().unwrap_or("");
    let user = v["user"].as_str().unwrap_or("");
    if v["kind"] == "oauth" {
        let t = crate::oauth::Tokens {
            access: String::new(),
            refresh: v["refresh"].as_str().map(String::from),
            expires: None,
            token_endpoint: v["endpoint"].as_str().unwrap_or("").into(),
            client_id: v["client"].as_str().unwrap_or("").into(),
        };
        Client::connect_oauth(server, t)
    } else {
        Client::connect(server, user, v["pass"].as_str().unwrap_or(""), "")
    }
}

/// Register the active session's client under its remembered user.
pub fn set_active(user: &str, c: Arc<Client>) {
    *ACTIVE.lock().unwrap() = user.to_string();
    POOL.lock().unwrap().get_or_insert_with(HashMap::new).insert(user.to_string(), c);
}

pub fn active_user() -> String { ACTIVE.lock().unwrap().clone() }

pub fn is_active(c: &Client) -> bool {
    let a = active_user();
    POOL.lock().unwrap().as_ref().and_then(|p| p.get(&a)).is_some_and(|x| std::ptr::eq(x.as_ref(), c))
}

/// Client for a remembered user, connecting it (blocking) if needed.
pub fn client(user: &str) -> Option<Arc<Client>> {
    if let Some(c) = POOL.lock().unwrap().as_ref().and_then(|p| p.get(user).cloned()) { return Some(c); }
    let v = crate::oauth::accounts().into_iter().find(|a| a["user"].as_str() == Some(user))?;
    let c = Arc::new(connect(&v).ok()?);
    POOL.lock().unwrap().get_or_insert_with(HashMap::new).entry(user.to_string()).or_insert(c.clone());
    Some(c)
}

/// Client for a remembered user only if already connected (never blocks).
pub fn cached(user: &str) -> Option<Arc<Client>> {
    POOL.lock().unwrap().as_ref().and_then(|p| p.get(user).cloned())
}

/// Connect every remembered account in background threads.
pub fn warm_up() {
    for v in crate::oauth::accounts() {
        let Some(user) = v["user"].as_str().map(String::from) else { continue };
        if cached(&user).is_some() { continue; }
        std::thread::spawn(move || { let _ = client(&user); });
    }
}

/// Drop every pooled client (sign-out).
pub fn clear() { *POOL.lock().unwrap() = None; ACTIVE.lock().unwrap().clear(); }

/// Whether the "All inboxes" folder is shown (Settings > Appearance > Unified mailbox).
pub fn enabled() -> bool { crate::settings::Settings::load().bool("enableUnifiedMailbox", false) }

/// Stable colour index for an account (Bulwark's account dot).
pub fn account_color(user: &str) -> u32 {
    const C: [u32; 8] = [0x3b82f6, 0x22c55e, 0xf97316, 0xa855f7, 0xec4899, 0x14b8a6, 0xeab308, 0xef4444];
    let i = crate::oauth::accounts().iter().position(|a| a["user"].as_str() == Some(user)).unwrap_or(0);
    C[i % C.len()]
}

/// Query each account's Inbox (first `limit`), tag each email with `__account`, merge by receivedAt desc.
pub fn fetch(limit: usize) -> Result<(Vec<Value>, usize, HashMap<String, usize>), String> {
    let users: Vec<String> = crate::oauth::accounts().iter().filter_map(|a| a["user"].as_str().map(String::from)).collect();
    let users = if users.is_empty() { vec![active_user()] } else { users };
    let handles: Vec<_> = users.into_iter().map(|user| std::thread::spawn(move || {
        let c = client(&user).ok_or_else(|| format!("{user}: not connected"))?;
        let inbox = c.mailboxes()?.into_iter().find(|m| m["role"] == "inbox").and_then(|m| m["id"].as_str().map(String::from))
            .ok_or_else(|| format!("{user}: no inbox"))?;
        let (mut emails, total, threads) = c.list(json!({"inMailbox": inbox}), 0, limit)?;
        for m in emails.iter_mut() { m["__account"] = json!(user); }
        Ok::<_, String>((emails, total, threads))
    })).collect();
    let (mut all, mut total, mut threads, mut errs) = (vec![], 0, HashMap::new(), vec![]);
    for h in handles {
        match h.join().unwrap_or_else(|_| Err("thread panicked".into())) {
            Ok((e, t, th)) => { all.extend(e); total += t; threads.extend(th); }
            Err(e) => errs.push(e),
        }
    }
    if all.is_empty() && !errs.is_empty() { return Err(errs.join("; ")); }
    all.sort_by(|a, b| b["receivedAt"].as_str().unwrap_or("").cmp(a["receivedAt"].as_str().unwrap_or("")));
    // no merged paging: what is loaded is the whole list
    total = total.min(all.len());
    Ok((all, total, threads))
}
