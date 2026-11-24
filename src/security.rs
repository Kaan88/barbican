//! Account security (Settings > Password & sign-in), after Bulwark's stores/account-security-store.ts
//! and components/settings/account-security-settings.tsx: change password, two-step sign-in (TOTP),
//! app passwords, API keys and encryption at rest, all through Stalwart's registry objects
//! (`x:AccountPassword`, `x:AppPassword`, `x:ApiKey`, `x:PublicKey`, `x:AccountSettings`).
//!
//! The page is only offered when the account has the `urn:stalwart:jmap` (or
//! `urn:inbuxa:jmap:registry`) capability (Bulwark lib/stalwart/management-capability.ts).
//! Request building and response parsing are pure functions (tested without a server); the jobs at
//! the end run them. No password, TOTP secret, app password or API key is ever logged, written to
//! disk or put in an error text: they live in [`State`] (memory only) until the page is left, and
//! `State` has no `Debug` on purpose.
use crate::jmap::Client;
use crate::server_settings::Shared;
use crate::SRow;
use serde_json::{json, Map, Value};
use slint::{ModelRc, SharedString, VecModel};

/// Account capabilities that carry the management objects, in Bulwark's order.
pub const CAPABILITIES: [&str; 2] = ["urn:stalwart:jmap", "urn:inbuxa:jmap:registry"];

/// The management capability of `account` (its own account capabilities, never the server-wide
/// list: Bulwark management-capability.ts).
pub fn management_capability(session: &Value, account: &str) -> Option<&'static str> {
    let caps = &session["accounts"][account]["accountCapabilities"];
    CAPABILITIES.iter().copied().find(|c| caps.get(*c).is_some_and(|v| !v.is_null()))
}

fn using(cap: &str) -> [&str; 2] { ["urn:ietf:params:jmap:core", cap] }

// ---------------------------------------------------------------- data

#[derive(Clone, Debug, PartialEq)]
pub struct Credential {
    pub id: String,
    pub description: String,
    pub created_at: Option<String>,
    pub expires_at: Option<String>,
    pub allowed_ips: Vec<String>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct PublicKey {
    pub id: String,
    pub description: String,
    pub expires_at: Option<String>,
    pub emails: Vec<String>,
}

#[derive(Clone, Debug, PartialEq, Default)]
pub struct Encryption {
    /// "Disabled", "Aes128" or "Aes256" (the `@type` of `encryptionAtRest`).
    pub kind: String,
    pub public_key: Option<String>,
    pub encrypt_on_append: bool,
    pub allow_spam_training: bool,
}

impl Encryption {
    pub fn enabled(&self) -> bool { !self.kind.is_empty() && self.kind != "Disabled" }
}

/// App passwords and API keys share their shape and differ in the object name.
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Kind { AppPassword, ApiKey }

impl Kind {
    pub fn object(self) -> &'static str { match self { Kind::AppPassword => "x:AppPassword", Kind::ApiKey => "x:ApiKey" } }
    pub fn set_method(self) -> &'static str { match self { Kind::AppPassword => "x:AppPassword/set", Kind::ApiKey => "x:ApiKey/set" } }
    fn noun(self) -> &'static str { match self { Kind::AppPassword => "app password", Kind::ApiKey => "API key" } }
}

// ---------------------------------------------------------------- request building

/// Password, app password and API key lists in one request (Bulwark fetchAuthInfo).
pub fn auth_info_calls(account: &str) -> Value {
    json!([
        ["x:AccountPassword/get", {"accountId": account, "ids": ["singleton"]}, "0"],
        ["x:AppPassword/query", {"accountId": account}, "1"],
        ["x:ApiKey/query", {"accountId": account}, "2"],
    ])
}

/// The credentials behind the ids the queries returned.
pub fn credentials_calls(account: &str, app_ids: &[String], key_ids: &[String]) -> Value {
    let mut calls = vec![];
    if !app_ids.is_empty() { calls.push(json!(["x:AppPassword/get", {"accountId": account, "ids": app_ids}, "app"])); }
    if !key_ids.is_empty() { calls.push(json!(["x:ApiKey/get", {"accountId": account, "ids": key_ids}, "key"])); }
    Value::Array(calls)
}

/// Encryption settings and the list of public keys (Bulwark fetchCryptoInfo and fetchPublicKeys).
pub fn crypto_calls(account: &str) -> Value {
    json!([
        ["x:AccountSettings/get", {"accountId": account, "ids": ["singleton"]}, "0"],
        ["x:PublicKey/query", {"accountId": account}, "1"],
    ])
}

pub fn public_keys_get_calls(account: &str, ids: &[String]) -> Value {
    json!([["x:PublicKey/get", {"accountId": account, "ids": ids}, "0"]])
}

/// With two-step sign-in on, Stalwart wants the current code for a password change. Only
/// `otpAuth/otpCode` is patched: sending a whole `otpAuth` object would reset `otpUrl` and turn
/// the second step off (Bulwark otpCodePatch).
pub fn change_password_calls(account: &str, current: &str, new: &str, otp_code: Option<&str>) -> Value {
    let mut patch = json!({"currentSecret": current, "secret": new});
    if let Some(code) = otp_code.map(str::trim).filter(|c| !c.is_empty()) { patch["otpAuth/otpCode"] = json!(code); }
    json!([["x:AccountPassword/set", {"accountId": account, "update": {"singleton": patch}}, "0"]])
}

pub fn enable_totp_calls(account: &str, current: &str, otp_url: &str, code: &str) -> Value {
    json!([["x:AccountPassword/set", {"accountId": account,
        "update": {"singleton": {"currentSecret": current, "otpAuth": {"otpUrl": otp_url, "otpCode": code}}}}, "0"]])
}

pub fn disable_totp_calls(account: &str, current: &str, code: Option<&str>) -> Value {
    let mut otp = json!({"otpUrl": null});
    if let Some(c) = code.map(str::trim).filter(|c| !c.is_empty()) { otp["otpCode"] = json!(c); }
    json!([["x:AccountPassword/set", {"accountId": account,
        "update": {"singleton": {"currentSecret": current, "otpAuth": otp}}}, "0"]])
}

pub struct CredentialInput { pub description: String, pub expires_at: Option<String>, pub allowed_ips: Vec<String> }

fn ip_map(ips: &[String]) -> Option<Value> {
    if ips.is_empty() { None } else { Some(Value::Object(ips.iter().map(|i| (i.clone(), json!(true))).collect::<Map<_, _>>())) }
}

pub fn create_credential_calls(account: &str, kind: Kind, input: &CredentialInput) -> Value {
    let mut body = json!({"description": input.description});
    if let Some(e) = input.expires_at.as_deref().filter(|e| !e.is_empty()) { body["expiresAt"] = json!(e); }
    if let Some(m) = ip_map(&input.allowed_ips) { body["allowedIps"] = m; }
    json!([[kind.set_method(), {"accountId": account, "create": {"new": body}}, "0"]])
}

pub fn destroy_calls(account: &str, method: &str, id: &str) -> Value {
    json!([[method, {"accountId": account, "destroy": [id]}, "0"]])
}

pub fn create_public_key_calls(account: &str, description: &str, key: &str, emails: &[String], expires_at: Option<&str>) -> Value {
    let mut body = json!({"description": description, "key": key,
        "emailAddresses": ip_map(emails).unwrap_or_else(|| json!({}))});
    if let Some(e) = expires_at.filter(|e| !e.is_empty()) { body["expiresAt"] = json!(e); }
    json!([["x:PublicKey/set", {"accountId": account, "create": {"new1": body}}, "0"]])
}

/// Turn encryption at rest on (`kind` "Aes128" / "Aes256", with a key) or off ("Disabled").
pub fn update_encryption_calls(account: &str, kind: &str, public_key: Option<&str>, on_append: bool, spam_training: bool) -> Result<Value, String> {
    let payload = if kind == "Disabled" {
        json!({"@type": "Disabled"})
    } else {
        if !matches!(kind, "Aes128" | "Aes256") { return Err("Pick an encryption algorithm.".into()); }
        let key = public_key.filter(|k| !k.is_empty()).ok_or("A public key is required to turn encryption on.")?;
        json!({"@type": kind, "publicKey": key, "encryptOnAppend": on_append, "allowSpamTraining": spam_training})
    };
    Ok(json!([["x:AccountSettings/set", {"accountId": account, "update": {"singleton": {"encryptionAtRest": payload}}}, "0"]]))
}

// ---------------------------------------------------------------- response parsing

/// The arguments of the first `method` response; else the server's method error; else a note.
pub fn result_of<'a>(responses: &'a [Value], method: &str) -> Result<&'a Value, String> {
    if let Some(r) = responses.iter().find(|r| r[0] == method) { return Ok(&r[1]); }
    if let Some(e) = responses.iter().find(|r| r[0] == "error") { return Err(crate::jmap::method_error(&e[1])); }
    Err(format!("The server's answer had no {method} part."))
}

fn set_failure(e: &Value, fallback: &str) -> String {
    e["description"].as_str().filter(|d| !d.is_empty()).or(e["type"].as_str()).unwrap_or(fallback).to_string()
}

fn strings(v: &Value) -> Vec<String> {
    v.as_array().map(|a| a.iter().filter_map(|x| x.as_str().map(String::from)).collect()).unwrap_or_default()
}

/// What `auth_info_calls` returned.
#[derive(Debug, PartialEq)]
pub struct AuthInfo { pub otp_enabled: bool, pub app_ids: Vec<String>, pub key_ids: Vec<String>, pub api_keys_available: bool }

pub fn parse_auth_info(responses: &[Value]) -> Result<AuthInfo, String> {
    let pw = result_of(responses, "x:AccountPassword/get")?;
    let otp_enabled = pw["list"][0]["otpAuth"]["otpUrl"].as_str().is_some_and(|u| !u.is_empty());
    let app_ids = strings(&result_of(responses, "x:AppPassword/query")?["ids"]);
    // A server that keeps API keys from this user answers with an error: the list is hidden, the
    // rest of the page keeps working.
    let (key_ids, api_keys_available) = match result_of(responses, "x:ApiKey/query") {
        Ok(r) => (strings(&r["ids"]), true),
        Err(_) => (vec![], false),
    };
    Ok(AuthInfo { otp_enabled, app_ids, key_ids, api_keys_available })
}

fn map_keys(v: &Value) -> Vec<String> {
    match v { Value::Object(o) => o.keys().cloned().collect(), Value::Array(a) => a.iter().map(|x| x.as_str().map(String::from).unwrap_or_else(|| x.to_string())).collect(), _ => vec![] }
}

fn opt_str(v: &Value) -> Option<String> { v.as_str().map(String::from) }

pub fn parse_credentials(list: &Value) -> Vec<Credential> {
    list.as_array().map(|a| a.iter().map(|raw| Credential {
        id: raw["id"].as_str().unwrap_or("").to_string(),
        description: raw["description"].as_str().unwrap_or("").to_string(),
        created_at: opt_str(&raw["createdAt"]),
        expires_at: opt_str(&raw["expiresAt"]),
        allowed_ips: map_keys(&raw["allowedIps"]),
    }).collect()).unwrap_or_default()
}

pub fn parse_encryption(responses: &[Value]) -> Result<Encryption, String> {
    let enc = &result_of(responses, "x:AccountSettings/get")?["list"][0]["encryptionAtRest"];
    Ok(Encryption {
        kind: enc["@type"].as_str().unwrap_or("Disabled").to_string(),
        public_key: enc["publicKey"].as_str().map(String::from),
        encrypt_on_append: enc["encryptOnAppend"] == true,
        allow_spam_training: enc["allowSpamTraining"] == true,
    })
}

pub fn parse_public_keys(list: &Value) -> Vec<PublicKey> {
    list.as_array().map(|a| a.iter().map(|raw| PublicKey {
        id: raw["id"].as_str().unwrap_or("").to_string(),
        description: raw["description"].as_str().unwrap_or("").to_string(),
        expires_at: opt_str(&raw["expiresAt"]),
        emails: map_keys(&raw["emailAddresses"]),
    }).collect()).unwrap_or_default()
}

/// A `/set` of the `singleton` object (password, account settings): the server's own message when
/// it refused (wrong current password, weak password, wrong code, ...). It comes back with HTTP 200.
pub fn check_singleton_set(responses: &[Value], method: &str, fallback: &str) -> Result<(), String> {
    let r = result_of(responses, method)?;
    match r["notUpdated"].get("singleton") { Some(e) => Err(set_failure(e, fallback)), None => Ok(()) }
}

/// A created app password or API key: its id and the secret, which the server shows once.
pub fn parse_created_credential(responses: &[Value], kind: Kind) -> Result<(String, String), String> {
    let r = result_of(responses, kind.set_method())?;
    if let Some(e) = r["notCreated"].get("new") { return Err(set_failure(e, &format!("The {} was not created.", kind.noun()))); }
    let c = &r["created"]["new"];
    match (c["id"].as_str(), c["secret"].as_str()) {
        (Some(id), Some(s)) if !id.is_empty() && !s.is_empty() => Ok((id.to_string(), s.to_string())),
        _ => Err("The server did not return the new credential.".into()),
    }
}

pub fn check_created_public_key(responses: &[Value]) -> Result<String, String> {
    let r = result_of(responses, "x:PublicKey/set")?;
    if let Some(e) = r["notCreated"].get("new1") { return Err(set_failure(e, "The public key was not added.")); }
    r["created"]["new1"]["id"].as_str().filter(|i| !i.is_empty()).map(String::from).ok_or_else(|| "The server did not return the new public key.".into())
}

pub fn check_destroyed(responses: &[Value], method: &str, id: &str) -> Result<(), String> {
    let r = result_of(responses, method)?;
    if let Some(e) = r["notDestroyed"].get(id) { return Err(set_failure(e, "The server did not delete it.")); }
    Ok(())
}

// ---------------------------------------------------------------- form input

/// Addresses separated by commas or white space (Bulwark parseIpList).
pub fn parse_ip_list(raw: &str) -> Vec<String> {
    raw.split(|c: char| c == ',' || c.is_whitespace()).map(str::trim).filter(|s| !s.is_empty()).map(String::from).collect()
}

/// "YYYY-MM-DD" as the UTC midnight JMAP wants; empty is no expiry; anything else is an error.
pub fn expiry_from_date(raw: &str) -> Result<Option<String>, String> {
    let r = raw.trim();
    if r.is_empty() { return Ok(None); }
    chrono::NaiveDate::parse_from_str(r, "%Y-%m-%d").map(|d| Some(format!("{d}T00:00:00Z"))).map_err(|_| "Write the expiry date as YYYY-MM-DD.".to_string())
}

/// The rules of Bulwark's password form.
pub fn check_new_password(new: &str, confirm: &str) -> Result<(), String> {
    if new.chars().count() < 8 { return Err("Password must be at least 8 characters".into()); }
    if new != confirm { return Err("New passwords do not match".into()); }
    Ok(())
}

// ---------------------------------------------------------------- TOTP

const B32: &[u8; 32] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

/// RFC 4648 base32 without padding (what authenticator apps read).
pub fn base32(data: &[u8]) -> String {
    let (mut out, mut buf, mut bits) = (String::new(), 0u32, 0u32);
    for &b in data {
        buf = (buf << 8) | b as u32;
        bits += 8;
        while bits >= 5 { bits -= 5; out.push(B32[((buf >> bits) & 31) as usize] as char); }
    }
    if bits > 0 { out.push(B32[((buf << (5 - bits)) & 31) as usize] as char); }
    out
}

fn pct(s: &str) -> String {
    s.bytes().map(|b| if b.is_ascii_alphanumeric() || b"-_.~".contains(&b) { (b as char).to_string() } else { format!("%{b:02X}") }).collect()
}

fn hmac_sha1(key: &[u8], msg: &[u8]) -> [u8; 20] {
    use sha1::{Digest, Sha1};
    let mut k = [0u8; 64];
    if key.len() > 64 { k[..20].copy_from_slice(&Sha1::digest(key)); } else { k[..key.len()].copy_from_slice(key); }
    let mut inner = Sha1::new();
    inner.update(k.map(|b| b ^ 0x36));
    inner.update(msg);
    let mut outer = Sha1::new();
    outer.update(k.map(|b| b ^ 0x5c));
    outer.update(inner.finalize());
    outer.finalize().into()
}

/// The 6-digit code of RFC 6238 (SHA-1, 30 s) for `unix` seconds.
pub fn totp_code(secret: &[u8], unix: u64) -> String {
    let h = hmac_sha1(secret, &(unix / 30).to_be_bytes());
    let o = (h[19] & 15) as usize;
    let n = u32::from_be_bytes([h[o] & 0x7f, h[o + 1], h[o + 2], h[o + 3]]);
    format!("{:06}", n % 1_000_000)
}

/// Whether `code` is the code for `unix` or one step either side (Bulwark: `validate({window: 1})`).
pub fn totp_valid(secret: &[u8], code: &str, unix: u64) -> bool {
    let code = code.trim();
    [unix.saturating_sub(30), unix, unix + 30].iter().any(|t| totp_code(secret, *t) == code)
}

/// A fresh 20-byte secret and the `otpauth://` link that carries it (Bulwark generateTotp).
pub struct Totp { pub secret: Vec<u8>, pub uri: String }

impl Totp {
    pub fn generate(label: &str, issuer: &str) -> Result<Totp, String> {
        let mut secret = vec![0u8; 20];
        getrandom::getrandom(&mut secret).map_err(|_| "Could not get random numbers for the secret.".to_string())?;
        let uri = Self::uri(&secret, label, issuer);
        Ok(Totp { secret, uri })
    }

    pub fn uri(secret: &[u8], label: &str, issuer: &str) -> String {
        let label = if label.is_empty() { "account" } else { label };
        format!("otpauth://totp/{}:{}?issuer={}&secret={}&algorithm=SHA1&digits=6&period=30", pct(issuer), pct(label), pct(issuer), base32(secret))
    }

    /// The secret in groups of four letters, for typing it into an authenticator by hand.
    pub fn grouped(&self) -> String {
        base32(&self.secret).as_bytes().chunks(4).map(|c| String::from_utf8_lossy(c).to_string()).collect::<Vec<_>>().join(" ")
    }
}

// ---------------------------------------------------------------- remembered sign-in

/// The remembered password sign-in of this user on this server; `None` when the session was not
/// remembered (then nothing is written or removed).
pub fn remembered_password_entry(accounts: &[Value], user: &str, base: &str) -> Option<Value> {
    let base = crate::jmap::normalize_base(base);
    accounts.iter().find(|a| a["kind"] == "password" && a["user"].as_str().is_some_and(|u| u.eq_ignore_ascii_case(user))
        && crate::jmap::normalize_base(a["server"].as_str().unwrap_or("")).eq_ignore_ascii_case(&base)).cloned()
}

/// The entry as it is to be stored after a password change: only when it holds exactly the password
/// that was just replaced (a remembered app password stays what it is).
pub fn remembered_with_password(accounts: &[Value], user: &str, base: &str, old_password: &str, new_password: &str) -> Option<Value> {
    let a = remembered_password_entry(accounts, user, base)?;
    (a["pass"] == old_password).then(|| json!({"kind": "password", "server": a["server"], "user": a["user"], "pass": new_password}))
}

/// What the authenticator app lists the entry under (Bulwark totp-issuer.ts: a stock install names
/// the account's server).
pub const TOTP_ISSUER: &str = "Stalwart";

// ---------------------------------------------------------------- page state

/// A secret shown once, right after it was created.
pub struct Shown { pub label: String, pub secret: String, pub kind: Kind }

#[derive(Default)]
pub struct State {
    /// The page has been loaded for this account at least once.
    pub checked: bool,
    /// Page session: bumped by `reset`. `owner` is the account's `state_key`.
    pub epoch: u64,
    pub owner: String,
    /// A server job is running (one at a time).
    pub busy: bool,
    pub confirm_at: Option<std::time::Instant>,
    pub cap: Option<&'static str>,
    /// The account's name as the server reports it (the label of the authenticator entry).
    pub account: String,
    /// Password sessions only: a single-sign-on or token session has no password to change.
    pub password_session: bool,
    /// "Show advanced settings": API keys and encryption at rest are advanced in Bulwark.
    pub advanced: bool,
    pub otp_enabled: bool,
    pub app_passwords: Vec<Credential>,
    pub api_keys: Vec<Credential>,
    pub api_keys_available: bool,
    pub encryption: Encryption,
    pub public_keys: Vec<PublicKey>,
    pub crypto_available: bool,
    pub status: String,
    pub pw_cur: String, pub pw_new: String, pub pw_confirm: String, pub pw_otp: String,
    pub totp: Option<Totp>,
    pub totp_pw: String, pub totp_code: String,
    pub app_desc: String, pub app_expires: String, pub app_ips: String,
    pub key_desc: String, pub key_expires: String, pub key_ips: String,
    pub shown: Option<Shown>,
    pub confirm: String,
    pub pk_desc: String, pub pk_key: String,
    pub enc_pick: Option<String>,
    pub enc_algo: i32, pub enc_append: bool, pub enc_spam: bool,
}

impl State {
    pub fn available(&self) -> bool { self.cap.is_some() }

    /// Drop everything typed or shown: passwords, codes, the new secret, the TOTP secret.
    /// Everything of this page session goes: the account it was for, what was typed, the secrets and
    /// the lists. The epoch moves on, so a job or load still running for the old page cannot write
    /// into the new one.
    pub fn reset(&mut self) {
        let epoch = self.epoch + 1;
        *self = State::default();
        self.epoch = epoch;
    }

    pub fn forget_secrets(&mut self) {
        self.pw_cur.clear(); self.pw_new.clear(); self.pw_confirm.clear(); self.pw_otp.clear();
        self.totp = None; self.totp_pw.clear(); self.totp_code.clear();
        self.shown = None;
        self.pk_key.clear();
        self.confirm.clear();
        self.enc_pick = None;
    }
}

fn s(v: &str) -> SharedString { SharedString::from(v) }
fn row(kind: &str, key: &str, label: &str, desc: &str, text: &str, on: bool) -> SRow {
    SRow { kind: s(kind), key: s(key), label: s(label), desc: s(desc), options: ModelRc::new(VecModel::from(Vec::<SharedString>::new())), index: 0, on, text: s(text) }
}

fn credential_desc(c: &Credential) -> String {
    let date = |d: &str| d.get(..10).unwrap_or(d).to_string();
    let mut parts = vec![];
    if let Some(d) = &c.created_at { parts.push(format!("created {}", date(d))); }
    if let Some(d) = &c.expires_at { parts.push(format!("expires {}", date(d))); }
    if !c.allowed_ips.is_empty() { parts.push(format!("only from {}", c.allowed_ips.join(", "))); }
    parts.join(" · ")
}

pub const ALGOS: [(&str, &str); 2] = [("Aes256", "AES-256"), ("Aes128", "AES-128")];

fn credential_rows(out: &mut Vec<SRow>, st: &State, kind: Kind) {
    let (p, list, desc, exp, ips, title, blurb, none, new_label, hint) = match kind {
        Kind::AppPassword => ("app", &st.app_passwords, &st.app_desc, &st.app_expires, &st.app_ips, "App passwords",
            "Passwords for apps that do not support two-step sign-in (a mail app on your phone, for example).", "No app passwords yet.", "App name", "e.g. Thunderbird, iPhone Mail"),
        Kind::ApiKey => ("key", &st.api_keys, &st.key_desc, &st.key_expires, &st.key_ips, "API keys",
            "Keys for scripts and integrations that talk to the server directly.", "No API keys yet.", "Key name", "e.g. Backup script, CI runner"),
    };
    out.push(row("section", "", title, blurb, "", false));
    if let Some(sh) = st.shown.as_ref().filter(|x| x.kind == kind) {
        out.push(row("textarea", "server.sec.shown", &format!("New {}: {}", kind.noun(), sh.label),
            &format!("Copy this {} now: it is not shown again.", kind.noun()), &sh.secret, false));
        out.push(row("button", "server.sec.secret.copy", "", "", "Copy", false));
        out.push(row("button", "server.sec.secret.done", "", "", "Done", false));
    }
    if list.is_empty() { out.push(row("value", "", none, "", "", false)); }
    for c in list.iter() {
        let key = format!("server.sec.revoke.{p}:{}", c.id);
        let label = if st.confirm == key { "Click again to delete" } else { "Delete" };
        out.push(row("button", &key, if c.description.is_empty() { &c.id } else { &c.description }, &credential_desc(c), label, false));
    }
    out.push(row("text", &format!("server.sec.{p}.desc"), new_label, hint, desc, false));
    out.push(row("text", &format!("server.sec.{p}.expires"), "Expires (optional)", "YYYY-MM-DD; empty for never.", exp, false));
    out.push(row("text", &format!("server.sec.{p}.ips"), "Allowed IPs (optional)", "Comma- or space-separated, e.g. 10.0.0.5, 192.168.1.0/24. Empty allows any address.", ips, false));
    out.push(row("button", &format!("server.sec.{p}.create"), "", "", "Create", false));
}

/// The page's rows; `None` when the account has no management capability (the page keeps only what
/// it had before: the section is hidden, as in Bulwark).
pub fn rows(st: &State) -> Option<Vec<SRow>> {
    st.cap?;
    let mut out = vec![];
    if !st.status.is_empty() { out.push(row("value", "", &st.status, "", "", false)); }
    if st.password_session {
        out.push(row("section", "", "Change password", "Your new password signs this app in at once. Other apps and devices need it too.", "", false));
        out.push(row("password", "server.sec.pw.cur", "Current password", "", &st.pw_cur, false));
        out.push(row("password", "server.sec.pw.new", "New password", "At least 8 characters.", &st.pw_new, false));
        out.push(row("password", "server.sec.pw.confirm", "Confirm new password", "", &st.pw_confirm, false));
        if st.otp_enabled { out.push(row("text", "server.sec.pw.otp", "Verification code", "The code from your authenticator app.", &st.pw_otp, false)); }
        out.push(row("button", "server.sec.pw.change", "", "", "Change password", false));

        out.push(row("section", "", "Two-step sign-in", "Add an extra layer of security with a time-based one-time password.", "", false));
        if let Some(t) = &st.totp {
            out.push(row("textarea", "server.sec.totp.secret", "Secret key", "Type this into your authenticator app (Google Authenticator, Authy, ...), or copy the link below into it.", &t.grouped(), false));
            out.push(row("textarea", "server.sec.totp.uri", "Setup link", "", &t.uri, false));
            out.push(row("button", "server.sec.totp.copy", "", "", "Copy link", false));
            out.push(row("password", "server.sec.totp.pw", "Current password", "", &st.totp_pw, false));
            out.push(row("text", "server.sec.totp.code", "Verification code", "The 6-digit code your app shows now.", &st.totp_code, false));
            out.push(row("button", "server.sec.totp.confirm", "", "", "Turn on", false));
            out.push(row("button", "server.sec.totp.cancel", "", "", "Cancel", false));
        } else if st.otp_enabled {
            out.push(row("value", "", "Two-step sign-in is on", "Enter your password and a code to turn it off.", "Enabled", false));
            out.push(row("password", "server.sec.totp.pw", "Current password", "", &st.totp_pw, false));
            out.push(row("text", "server.sec.totp.code", "Verification code", "", &st.totp_code, false));
            out.push(row("button", "server.sec.totp.disable", "", "", "Turn off", false));
        } else {
            out.push(row("value", "", "Two-step sign-in is off", "", "Disabled", false));
            out.push(row("button", "server.sec.totp.start", "", "", "Set up two-step sign-in", false));
        }
    }
    credential_rows(&mut out, st, Kind::AppPassword);
    if st.advanced && st.api_keys_available { credential_rows(&mut out, st, Kind::ApiKey); }
    if st.advanced && st.crypto_available { crypto_rows(&mut out, st); }
    Some(out)
}

fn crypto_rows(out: &mut Vec<SRow>, st: &State) {
    out.push(row("section", "", "Encryption at rest", "Encrypt the mail the server stores for you with your public key (S/MIME or PGP). Keep the private key safe: without it the encrypted mail cannot be read.", "", false));
    let enc = &st.encryption;
    let algo = ALGOS.iter().find(|a| a.0 == enc.kind).map(|a| a.1).unwrap_or(&enc.kind);
    out.push(row("value", "", if enc.enabled() { "Encryption is on" } else { "Encryption is off" }, "", if enc.enabled() { algo } else { "Disabled" }, false));
    if enc.enabled() {
        let key = if st.confirm == "server.sec.enc.disable" { "Click again to turn off" } else { "Turn off encryption" };
        out.push(row("button", "server.sec.enc.disable", "", "New mail is stored without encryption from then on.", key, false));
    }
    out.push(row("section", "", "Public keys", "", "", false));
    if st.public_keys.is_empty() { out.push(row("value", "", "No public keys yet.", "", "", false)); }
    for k in &st.public_keys {
        let used = enc.enabled() && enc.public_key.as_deref() == Some(&k.id);
        let mut d = vec![];
        if used { d.push(format!("{} encryption in use", algo)); }
        if !k.emails.is_empty() { d.push(k.emails.join(", ")); }
        if let Some(e) = &k.expires_at { d.push(format!("expires {}", e.get(..10).unwrap_or(e))); }
        let label = if k.description.is_empty() { &k.id } else { &k.description };
        if !used && st.enc_pick.as_deref() != Some(&k.id) {
            out.push(row("button", &format!("server.sec.enc.pick:{}", k.id), label, &d.join(" · "), "Use for encryption", false));
        } else {
            out.push(row("value", "", label, &d.join(" · "), if used { "In use" } else { "Selected" }, false));
        }
        let del = format!("server.sec.pk.remove:{}", k.id);
        out.push(row("button", &del, "", if used { "Deleting this key turns encryption off first." } else { "" }, if st.confirm == del { "Click again to delete" } else { "Delete key" }, false));
    }
    if st.enc_pick.is_some() {
        out.push(row("section", "", "Turn on encryption with this key", "", "", false));
        let mut r = row("select", "server.sec.enc.algo", "Encryption algorithm", "", "", false);
        r.options = ModelRc::new(VecModel::from(ALGOS.iter().map(|a| s(a.1)).collect::<Vec<_>>()));
        r.index = st.enc_algo;
        out.push(r);
        out.push(row("toggle", "server.sec.enc.append", "Encrypt new emails on upload", "", "", st.enc_append));
        out.push(row("toggle", "server.sec.enc.spam", "Allow spam training before encrypting emails", "", "", st.enc_spam));
        out.push(row("button", "server.sec.enc.enable", "", "", if st.confirm == "server.sec.enc.enable" { "Click again to turn on" } else { "Turn on" }, false));
        out.push(row("button", "server.sec.enc.cancel", "", "", "Cancel", false));
    }
    out.push(row("text", "server.sec.pk.desc", "Public key name", "e.g. My Key", &st.pk_desc, false));
    out.push(row("textarea", "server.sec.pk.key", "Public key (S/MIME or PGP, ASCII-armored)", "Paste the public key here.", &st.pk_key, false));
    out.push(row("button", "server.sec.pk.add", "", "", "Add public key", false));
}

// ---------------------------------------------------------------- edits and buttons

/// Typing in a field of the page. Nothing is sent.
pub fn edit(st: &mut State, field: &str, value: &Value) {
    let text = || value.as_str().unwrap_or("").to_string();
    match field {
        "pw.cur" => st.pw_cur = text(),
        "pw.new" => st.pw_new = text(),
        "pw.confirm" => st.pw_confirm = text(),
        "pw.otp" => st.pw_otp = text(),
        "totp.pw" => st.totp_pw = text(),
        "totp.code" => st.totp_code = text(),
        "app.desc" => st.app_desc = text(),
        "app.expires" => st.app_expires = text(),
        "app.ips" => st.app_ips = text(),
        "key.desc" => st.key_desc = text(),
        "key.expires" => st.key_expires = text(),
        "key.ips" => st.key_ips = text(),
        "pk.desc" => st.pk_desc = text(),
        "pk.key" => st.pk_key = text(),
        "enc.algo" => st.enc_algo = value.as_i64().unwrap_or(0).clamp(0, ALGOS.len() as i64 - 1) as i32,
        "enc.append" => st.enc_append = value.as_bool().unwrap_or(false),
        "enc.spam" => st.enc_spam = value.as_bool().unwrap_or(false),
        // read-only text (the secret and link being shown): ignore edits
        _ => {}
    }
}

pub type Job = Box<dyn FnOnce(&Client, &Shared) -> Result<(), String> + Send>;
/// What a finished job changes on the page; dropped when the page was reset while it ran.
type Update = Box<dyn FnOnce(&mut State) + Send>;

/// A confirming press counts only this long after the first one, so a double click is not a confirm.
const CONFIRM_DELAY: std::time::Duration = std::time::Duration::from_millis(500);

fn noop() -> Option<Job> { Some(Box::new(|_, _| Ok(()))) }
fn refuse(st: &mut State, msg: &str) -> Option<Job> { st.status = msg.to_string(); noop() }

fn now_unix() -> u64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0)
}

/// Run `calls` against the account's management capability; a failed request is its message.
fn send(c: &Client, calls: Value) -> Result<Vec<Value>, String> {
    let cap = management_capability(&c.session, &c.account).ok_or("This server has no account security settings.")?;
    c.request_using(&using(cap), calls)
}

/// Server work of the page: one job at a time (a second press while one runs is refused, so a
/// double click cannot create two credentials or race a password switch), and only for the account
/// and page session it was started on. Its update of the page is applied only if that page session
/// is still the current one (a reset by closing Settings, signing out or switching accounts bumps
/// the epoch), so a late answer never writes a secret into another account's page.
fn run(st: &mut State, work: impl FnOnce(&Client) -> Result<Update, String> + Send + 'static) -> Option<Job> {
    if st.busy { st.status = "Wait for the previous action to finish.".into(); return noop(); }
    st.busy = true;
    let (epoch, owner) = (st.epoch, st.owner.clone());
    Some(Box::new(move |c: &Client, k: &Shared| {
        if !owns(&k.lock().unwrap().sec, epoch, &owner, &c.state_key()) {
            return Err("The account changed. Open the page again.".into());
        }
        let r = work(c);
        let mut g = k.lock().unwrap();
        if g.sec.epoch != epoch { return r.map(|_| ()); }
        g.sec.busy = false;
        match r { Ok(update) => { update(&mut g.sec); Ok(()) } Err(e) => Err(e) }
    }))
}

/// Whether the page state still belongs to the page session and account a job started on.
fn owns(st: &State, epoch: u64, owner: &str, key: &str) -> bool { st.epoch == epoch && !owner.is_empty() && st.owner == owner && owner == key }

/// What to do with the running session after the password changed (`header_pass` is the password
/// the session signs in with: the typed one, `typed$code` for a session started with a two-step
/// code, or something else, such as an app password).
#[derive(Debug, PartialEq)]
pub enum Followup { Switch, EndSession, Keep }

pub fn classify_session(header_pass: &str, typed_current: &str, otp_on: bool) -> Followup {
    if header_pass == typed_current { return if otp_on { Followup::EndSession } else { Followup::Switch }; }
    // a session signed in with "password$code": there is no code to send with the new password
    if header_pass.strip_prefix(typed_current).is_some_and(|rest| rest.starts_with('$')) { return Followup::EndSession; }
    Followup::Keep
}

/// The message when the key was in use: encryption is already off, the key is still there.
pub fn key_not_deleted(err: &str) -> String { format!("Encryption was turned off, but the key was not deleted: {err}") }

const ENCRYPT_WARNING: &str = "Mail stored encrypted can only be read with your private key, and Barbican cannot show encrypted messages. Without the key the mail is lost. Press the button again to turn encryption on.";

/// A button on the page (`what` is the key after `server.sec.`). Validation problems are written to
/// the page's status line right away; the job does the server work on a background thread.
pub fn button(st: &mut State, what: &str) -> Option<Job> { button_at(st, what, std::time::Instant::now()) }

fn button_at(st: &mut State, what: &str, now: std::time::Instant) -> Option<Job> {
    // a second press confirms, but not within a double click; any other press cancels the wait
    let key = format!("server.sec.{what}");
    let pending = std::mem::take(&mut st.confirm);
    let armed = st.confirm_at.take();
    let confirmed = pending == key && armed.is_some_and(|t| now.saturating_duration_since(t) >= CONFIRM_DELAY);
    if pending == key && !confirmed { st.confirm = pending; st.confirm_at = armed; return noop(); }
    let arm = |st: &mut State| { st.confirm = key.clone(); st.confirm_at = Some(now); };
    st.status.clear();
    let epoch = st.epoch;
    match what {
        "pw.change" => {
            if st.pw_cur.is_empty() { return refuse(st, "Enter your current password."); }
            if let Err(e) = check_new_password(&st.pw_new, &st.pw_confirm) { return refuse(st, &e); }
            if st.otp_enabled && st.pw_otp.trim().is_empty() { return refuse(st, "Enter a verification code."); }
            let (cur, new, otp, otp_on) = (st.pw_cur.clone(), st.pw_new.clone(), st.pw_otp.clone(), st.otp_enabled);
            run(st, move |c| {
                let r = send(c, change_password_calls(&c.account, &cur, &new, otp_on.then_some(otp.as_str())))?;
                check_singleton_set(&r, "x:AccountPassword/set", "Failed to change password")?;
                let follow = c.basic_password().map_or(Followup::Keep, |p| classify_session(&p, &cur, otp_on));
                let note = match follow {
                    Followup::Switch => {
                        c.set_basic_password(&new);
                        // a remembered sign-in is rewritten only when it holds the password just replaced
                        if let Some(user) = c.basic_user() {
                            if let Some(v) = remembered_with_password(&crate::oauth::accounts(), &user, &c.base, &cur, &new) {
                                crate::oauth::remember(&v, crate::oauth::Origin::SignIn);
                            }
                        }
                        "Password changed.".to_string()
                    }
                    Followup::EndSession => { c.end_session(); "Password changed. Sign in again with your new password.".to_string() }
                    Followup::Keep => "Password changed. This session signs in with something else (an app password), which still works.".to_string(),
                };
                Ok(Box::new(move |s: &mut State| {
                    s.pw_cur.clear(); s.pw_new.clear(); s.pw_confirm.clear(); s.pw_otp.clear();
                    s.status = note;
                }) as Update)
            })
        }
        "totp.start" => {
            st.totp_pw.clear(); st.totp_code.clear();
            match Totp::generate(&st.account, TOTP_ISSUER) {
                Ok(t) => { st.totp = Some(t); noop() }
                Err(e) => refuse(st, &e),
            }
        }
        "totp.cancel" => { st.totp = None; st.totp_pw.clear(); st.totp_code.clear(); noop() }
        "totp.copy" => {
            let uri = st.totp.as_ref()?.uri.clone();
            Some(Box::new(move |_, k: &Shared| {
                let ok = crate::platform::copy_secret(&uri);
                let mut g = k.lock().unwrap();
                if g.sec.epoch == epoch { g.sec.status = if ok { "Link copied.".into() } else { "Could not copy the link.".into() }; }
                Ok(())
            }))
        }
        "totp.confirm" => {
            let t = st.totp.as_ref()?;
            if st.totp_pw.is_empty() { return refuse(st, "Password is required"); }
            if st.totp_code.trim().is_empty() { return refuse(st, "Verification code is required"); }
            if !totp_valid(&t.secret, &st.totp_code, now_unix()) { return refuse(st, "Invalid verification code. Check your authenticator app and try again."); }
            let (pw, code, url) = (st.totp_pw.clone(), st.totp_code.trim().to_string(), t.uri.clone());
            run(st, move |c| {
                let r = send(c, enable_totp_calls(&c.account, &pw, &url, &code))?;
                check_singleton_set(&r, "x:AccountPassword/set", "Failed to enable 2FA")?;
                // a remembered password has no code: every start would fail and count as a bad login
                let forgot = forget_remembered_password(c);
                Ok(Box::new(move |s: &mut State| {
                    s.totp = None; s.totp_pw.clear(); s.totp_code.clear(); s.otp_enabled = true;
                    s.status = if forgot { "Two-step sign-in is on. Barbican forgot your remembered password: sign in with your password and a code from now on.".into() }
                        else { "Two-step sign-in is on.".into() };
                }) as Update)
            })
        }
        "totp.disable" => {
            if st.totp_pw.is_empty() { return refuse(st, "Password is required"); }
            if st.totp_code.trim().is_empty() { return refuse(st, "Verification code is required"); }
            let (pw, code) = (st.totp_pw.clone(), st.totp_code.clone());
            run(st, move |c| {
                let r = send(c, disable_totp_calls(&c.account, &pw, Some(&code)))?;
                check_singleton_set(&r, "x:AccountPassword/set", "Failed to disable 2FA")?;
                Ok(Box::new(|s: &mut State| {
                    s.totp_pw.clear(); s.totp_code.clear(); s.otp_enabled = false;
                    s.status = "Two-step sign-in is off.".into();
                }) as Update)
            })
        }
        "app.create" | "key.create" => {
            let kind = if what == "app.create" { Kind::AppPassword } else { Kind::ApiKey };
            let (desc, exp, ips) = if kind == Kind::AppPassword { (&st.app_desc, &st.app_expires, &st.app_ips) } else { (&st.key_desc, &st.key_expires, &st.key_ips) };
            if desc.trim().is_empty() { return refuse(st, &format!("Give the {} a name.", kind.noun())); }
            let expires_at = match expiry_from_date(exp) { Ok(e) => e, Err(e) => return refuse(st, &e) };
            let input = CredentialInput { description: desc.trim().to_string(), expires_at, allowed_ips: parse_ip_list(ips) };
            run(st, move |c| {
                let r = send(c, create_credential_calls(&c.account, kind, &input))?;
                let (_id, secret) = parse_created_credential(&r, kind)?;
                Ok(Box::new(move |s: &mut State| {
                    if kind == Kind::AppPassword { s.app_desc.clear(); s.app_expires.clear(); s.app_ips.clear(); }
                    else { s.key_desc.clear(); s.key_expires.clear(); s.key_ips.clear(); }
                    s.shown = Some(Shown { label: input.description.clone(), secret, kind });
                    s.status = format!("The {} was created.", kind.noun());
                }) as Update)
            })
        }
        "secret.copy" => {
            let secret = st.shown.as_ref()?.secret.clone();
            Some(Box::new(move |_, k: &Shared| {
                let ok = crate::platform::copy_secret(&secret);
                let mut g = k.lock().unwrap();
                if g.sec.epoch == epoch { g.sec.status = if ok { "Copied.".into() } else { "Could not copy.".into() }; }
                Ok(())
            }))
        }
        "secret.done" => { st.shown = None; noop() }
        w if w.starts_with("revoke.app:") || w.starts_with("revoke.key:") => {
            if !confirmed { arm(st); return noop(); }
            let kind = if w.starts_with("revoke.app:") { Kind::AppPassword } else { Kind::ApiKey };
            let id = w.split_once(':')?.1.to_string();
            run(st, move |c| {
                let r = send(c, destroy_calls(&c.account, kind.set_method(), &id))?;
                check_destroyed(&r, kind.set_method(), &id)?;
                Ok(Box::new(move |s: &mut State| s.status = format!("The {} was deleted.", kind.noun())) as Update)
            })
        }
        "pk.add" => {
            if st.pk_desc.trim().is_empty() || st.pk_key.trim().is_empty() { return refuse(st, "Give the key a name and paste the public key."); }
            let (d, key) = (st.pk_desc.trim().to_string(), st.pk_key.trim().to_string());
            run(st, move |c| {
                let r = send(c, create_public_key_calls(&c.account, &d, &key, &[], None))?;
                check_created_public_key(&r)?;
                Ok(Box::new(|s: &mut State| { s.pk_desc.clear(); s.pk_key.clear(); s.status = "Public key added.".into(); }) as Update)
            })
        }
        w if w.starts_with("pk.remove:") => {
            if !confirmed { arm(st); return noop(); }
            let id = w.split_once(':')?.1.to_string();
            // a key that encrypts the mail is not pulled out from under it: encryption goes off first
            let in_use = st.encryption.enabled() && st.encryption.public_key.as_deref() == Some(&id);
            run(st, move |c| {
                if in_use {
                    let off = update_encryption_calls(&c.account, "Disabled", None, false, false)?;
                    check_singleton_set(&send(c, off)?, "x:AccountSettings/set", "Failed to update encryption settings")?;
                }
                let deleted = send(c, destroy_calls(&c.account, "x:PublicKey/set", &id)).and_then(|r| check_destroyed(&r, "x:PublicKey/set", &id));
                if let Err(e) = deleted { return Err(if in_use { key_not_deleted(&e) } else { e }); }
                Ok(Box::new(|s: &mut State| s.status = "Public key deleted.".into()) as Update)
            })
        }
        w if w.starts_with("enc.pick:") => { st.enc_pick = Some(w.split_once(':')?.1.to_string()); st.enc_algo = 0; st.enc_append = false; st.enc_spam = false; noop() }
        "enc.cancel" => { st.enc_pick = None; noop() }
        "enc.enable" => {
            let key = st.enc_pick.clone()?;
            if !confirmed { arm(st); st.status = ENCRYPT_WARNING.into(); return noop(); }
            let kind = ALGOS[st.enc_algo.clamp(0, ALGOS.len() as i32 - 1) as usize].0;
            let (a, sp) = (st.enc_append, st.enc_spam);
            run(st, move |c| {
                let calls = update_encryption_calls(&c.account, kind, Some(&key), a, sp)?;
                check_singleton_set(&send(c, calls)?, "x:AccountSettings/set", "Failed to update encryption settings")?;
                Ok(Box::new(|s: &mut State| { s.enc_pick = None; s.status = "Encryption at rest is on.".into(); }) as Update)
            })
        }
        "enc.disable" => {
            if !confirmed { arm(st); return noop(); }
            run(st, move |c| {
                let calls = update_encryption_calls(&c.account, "Disabled", None, false, false)?;
                check_singleton_set(&send(c, calls)?, "x:AccountSettings/set", "Failed to update encryption settings")?;
                Ok(Box::new(|s: &mut State| s.status = "Encryption at rest is off.".into()) as Update)
            })
        }
        _ => None,
    }
}

/// Forget the remembered password sign-in of this account (see [`remembered_password_entry`]).
fn forget_remembered_password(c: &Client) -> bool {
    let Some(user) = c.basic_user() else { return false };
    let Some(e) = remembered_password_entry(&crate::oauth::accounts(), &user, &c.base) else { return false };
    crate::oauth::forget_account(&[e["user"].as_str().unwrap_or("")], e["server"].as_str().unwrap_or(""));
    true
}

// ---------------------------------------------------------------- loading

/// Read what the page shows. Runs on a background thread; the error, if any, is the page's.
/// Its result is dropped when the page was reset (another account, Settings closed) while it ran.
pub fn load(c: &Client, cache: &Shared) -> Result<(), String> {
    let cap = management_capability(&c.session, &c.account);
    let password_session = c.tokens().is_none();
    let key = c.state_key();
    let epoch = {
        let mut g = cache.lock().unwrap();
        // another account than the one this page state was made for: nothing of it stays
        if !g.sec.owner.is_empty() && g.sec.owner != key { g.sec.reset(); }
        g.sec.checked = true;
        g.sec.cap = cap;
        g.sec.password_session = password_session;
        g.sec.account = c.username.clone();
        g.sec.owner = key;
        g.sec.epoch
    };
    let Some(cap) = cap else { return Ok(()) };
    let u = using(cap);
    let r = c.request_using(&u, auth_info_calls(&c.account))?;
    let info = parse_auth_info(&r)?;
    let mut apps = vec![];
    let mut keys = vec![];
    let follow = credentials_calls(&c.account, &info.app_ids, &info.key_ids);
    if follow.as_array().is_some_and(|a| !a.is_empty()) {
        let r = c.request_using(&u, follow)?;
        if !info.app_ids.is_empty() { apps = parse_credentials(&result_of(&r, "x:AppPassword/get")?["list"]); }
        if !info.key_ids.is_empty() { keys = parse_credentials(&result_of(&r, "x:ApiKey/get")?["list"]); }
    }
    // encryption at rest is a courtesy: a server that refuses it hides that section only
    let crypto = (|| -> Result<(Encryption, Vec<PublicKey>), String> {
        let r = c.request_using(&u, crypto_calls(&c.account))?;
        let enc = parse_encryption(&r)?;
        let ids = strings(&result_of(&r, "x:PublicKey/query")?["ids"]);
        let list = if ids.is_empty() { vec![] } else {
            let g = c.request_using(&u, public_keys_get_calls(&c.account, &ids))?;
            parse_public_keys(&result_of(&g, "x:PublicKey/get")?["list"])
        };
        Ok((enc, list))
    })();
    let mut g = cache.lock().unwrap();
    if g.sec.epoch != epoch { return Ok(()); }
    g.sec.otp_enabled = info.otp_enabled;
    g.sec.app_passwords = apps;
    g.sec.api_keys = keys;
    g.sec.api_keys_available = info.api_keys_available;
    match crypto {
        Ok((enc, list)) => { g.sec.encryption = enc; g.sec.public_keys = list; g.sec.crypto_available = true; }
        Err(_) => { g.sec.crypto_available = false; }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn session(cap: Option<&str>) -> Value {
        let caps = match cap { Some(c) => json!({"urn:ietf:params:jmap:mail": {}, c: {}}), None => json!({"urn:ietf:params:jmap:mail": {}}) };
        json!({"capabilities": {"urn:stalwart:jmap": {}}, "accounts": {"a1": {"accountCapabilities": caps}}})
    }

    #[test]
    fn capability_comes_from_the_account_not_the_server() {
        assert_eq!(management_capability(&session(Some("urn:stalwart:jmap")), "a1"), Some("urn:stalwart:jmap"));
        assert_eq!(management_capability(&session(Some("urn:inbuxa:jmap:registry")), "a1"), Some("urn:inbuxa:jmap:registry"));
        // the server-wide list names it, the account does not
        assert_eq!(management_capability(&session(None), "a1"), None);
        assert_eq!(management_capability(&session(Some("urn:stalwart:jmap")), "other"), None);
    }

    #[test]
    fn page_is_hidden_without_the_capability() {
        assert!(rows(&State::default()).is_none());
    }

    #[test]
    fn password_request_matches_bulwark() {
        let c = change_password_calls("a1", "old", "new-secret", None);
        assert_eq!(c, json!([["x:AccountPassword/set", {"accountId": "a1", "update": {"singleton": {"currentSecret": "old", "secret": "new-secret"}}}, "0"]]));
        let c = change_password_calls("a1", "old", "new-secret", Some(" 123456 "));
        assert_eq!(c[0][1]["update"]["singleton"]["otpAuth/otpCode"], "123456");
        assert!(c[0][1]["update"]["singleton"].get("otpAuth").is_none(), "a whole otpAuth object would reset the url");
        assert!(change_password_calls("a1", "o", "n", Some("  "))[0][1]["update"]["singleton"].get("otpAuth/otpCode").is_none());
    }

    #[test]
    fn totp_requests_match_bulwark() {
        let on = enable_totp_calls("a1", "pw", "otpauth://x", "123456");
        assert_eq!(on[0][1]["update"]["singleton"], json!({"currentSecret": "pw", "otpAuth": {"otpUrl": "otpauth://x", "otpCode": "123456"}}));
        let off = disable_totp_calls("a1", "pw", Some(" 654321 "));
        assert_eq!(off[0][1]["update"]["singleton"], json!({"currentSecret": "pw", "otpAuth": {"otpUrl": null, "otpCode": "654321"}}));
        assert_eq!(disable_totp_calls("a1", "pw", None)[0][1]["update"]["singleton"]["otpAuth"], json!({"otpUrl": null}));
    }

    #[test]
    fn credential_requests() {
        let input = CredentialInput { description: "Phone".into(), expires_at: Some("2027-01-31T00:00:00Z".into()), allowed_ips: vec!["10.0.0.5".into()] };
        let c = create_credential_calls("a1", Kind::AppPassword, &input);
        assert_eq!(c, json!([["x:AppPassword/set", {"accountId": "a1", "create": {"new": {"description": "Phone", "expiresAt": "2027-01-31T00:00:00Z", "allowedIps": {"10.0.0.5": true}}}}, "0"]]));
        let bare = CredentialInput { description: "CI".into(), expires_at: None, allowed_ips: vec![] };
        assert_eq!(create_credential_calls("a1", Kind::ApiKey, &bare), json!([["x:ApiKey/set", {"accountId": "a1", "create": {"new": {"description": "CI"}}}, "0"]]));
        assert_eq!(destroy_calls("a1", "x:ApiKey/set", "k1"), json!([["x:ApiKey/set", {"accountId": "a1", "destroy": ["k1"]}, "0"]]));
        assert_eq!(credentials_calls("a1", &["p".into()], &[]), json!([["x:AppPassword/get", {"accountId": "a1", "ids": ["p"]}, "app"]]));
        assert_eq!(credentials_calls("a1", &[], &[]), json!([]));
    }

    #[test]
    fn encryption_requests() {
        let on = update_encryption_calls("a1", "Aes256", Some("k1"), true, false).unwrap();
        assert_eq!(on[0][1]["update"]["singleton"]["encryptionAtRest"], json!({"@type": "Aes256", "publicKey": "k1", "encryptOnAppend": true, "allowSpamTraining": false}));
        let off = update_encryption_calls("a1", "Disabled", Some("k1"), true, true).unwrap();
        assert_eq!(off[0][1]["update"]["singleton"]["encryptionAtRest"], json!({"@type": "Disabled"}));
        assert!(update_encryption_calls("a1", "Aes128", None, false, false).is_err());
        assert!(update_encryption_calls("a1", "Rot13", Some("k"), false, false).is_err());
        let k = create_public_key_calls("a1", "Mine", "-----BEGIN PGP", &["ann@example.test".into()], None);
        assert_eq!(k[0][1]["create"]["new1"]["emailAddresses"], json!({"ann@example.test": true}));
        assert_eq!(create_public_key_calls("a1", "M", "k", &[], None)[0][1]["create"]["new1"]["emailAddresses"], json!({}));
    }

    #[test]
    fn auth_info_parses_and_hides_api_keys_the_server_refuses() {
        let r = vec![
            json!(["x:AccountPassword/get", {"list": [{"otpAuth": {"otpUrl": "otpauth://totp/x"}}]}, "0"]),
            json!(["x:AppPassword/query", {"ids": ["p1", "p2"]}, "1"]),
            json!(["x:ApiKey/query", {"ids": ["k1"]}, "2"]),
        ];
        assert_eq!(parse_auth_info(&r).unwrap(), AuthInfo { otp_enabled: true, app_ids: vec!["p1".into(), "p2".into()], key_ids: vec!["k1".into()], api_keys_available: true });
        let r = vec![
            json!(["x:AccountPassword/get", {"list": [{"otpAuth": null}]}, "0"]),
            json!(["x:AppPassword/query", {"ids": []}, "1"]),
            json!(["error", {"type": "forbidden"}, "2"]),
        ];
        let i = parse_auth_info(&r).unwrap();
        assert!(!i.otp_enabled && i.app_ids.is_empty() && !i.api_keys_available);
        // the password object itself failing is an error of the page
        assert!(parse_auth_info(&[json!(["error", {"type": "forbidden"}, "0"])]).unwrap_err().contains("permission"));
    }

    #[test]
    fn credentials_and_keys_parse() {
        let l = json!([{"id": "p1", "description": "Phone", "createdAt": "2026-11-01T10:00:00Z", "expiresAt": null, "allowedIps": {"10.0.0.5": true, "192.168.1.0/24": true}}, {"id": "p2"}]);
        let c = parse_credentials(&l);
        assert_eq!(c[0], Credential { id: "p1".into(), description: "Phone".into(), created_at: Some("2026-11-01T10:00:00Z".into()), expires_at: None, allowed_ips: vec!["10.0.0.5".into(), "192.168.1.0/24".into()] });
        assert_eq!(c[1].description, "");
        assert_eq!(credential_desc(&c[0]), "created 2026-11-01 · only from 10.0.0.5, 192.168.1.0/24");
        let k = parse_public_keys(&json!([{"id": "k1", "description": "Mine", "key": "x", "emailAddresses": {"ann@example.test": true}}, {"id": "k2", "emailAddresses": ["bob@example.test"]}]));
        assert_eq!(k[0].emails, vec!["ann@example.test"]);
        assert_eq!(k[1].emails, vec!["bob@example.test"]);
        let e = parse_encryption(&[json!(["x:AccountSettings/get", {"list": [{"encryptionAtRest": {"@type": "Aes128", "publicKey": "k1", "encryptOnAppend": true}}]}, "0"])]).unwrap();
        assert_eq!(e, Encryption { kind: "Aes128".into(), public_key: Some("k1".into()), encrypt_on_append: true, allow_spam_training: false });
        assert!(e.enabled());
        let none = parse_encryption(&[json!(["x:AccountSettings/get", {"list": [{}]}, "0"])]).unwrap();
        assert!(!none.enabled());
    }

    #[test]
    fn set_answers_surface_the_server_message() {
        let bad = [json!(["x:AccountPassword/set", {"notUpdated": {"singleton": {"type": "invalidProperties", "description": "Current password is wrong"}}}, "0"])];
        assert_eq!(check_singleton_set(&bad, "x:AccountPassword/set", "Failed").unwrap_err(), "Current password is wrong");
        let typed = [json!(["x:AccountPassword/set", {"notUpdated": {"singleton": {"type": "forbidden"}}}, "0"])];
        assert_eq!(check_singleton_set(&typed, "x:AccountPassword/set", "Failed").unwrap_err(), "forbidden");
        assert!(check_singleton_set(&[json!(["x:AccountPassword/set", {"updated": {"singleton": null}}, "0"])], "x:AccountPassword/set", "F").is_ok());
        assert!(check_singleton_set(&[json!(["error", {"type": "forbidden"}, "0"])], "x:AccountPassword/set", "F").is_err());
    }

    #[test]
    fn created_secret_is_read_and_failures_are_errors() {
        let ok = [json!(["x:AppPassword/set", {"created": {"new": {"id": "p9", "secret": "abc-def"}}}, "0"])];
        assert_eq!(parse_created_credential(&ok, Kind::AppPassword).unwrap(), ("p9".to_string(), "abc-def".to_string()));
        let nosecret = [json!(["x:ApiKey/set", {"created": {"new": {"id": "k9"}}}, "0"])];
        assert!(parse_created_credential(&nosecret, Kind::ApiKey).is_err());
        let refused = [json!(["x:ApiKey/set", {"notCreated": {"new": {"type": "forbidden", "description": "Not allowed"}}}, "0"])];
        assert_eq!(parse_created_credential(&refused, Kind::ApiKey).unwrap_err(), "Not allowed");
        assert_eq!(check_created_public_key(&[json!(["x:PublicKey/set", {"created": {"new1": {"id": "k1"}}}, "0"])]).unwrap(), "k1");
        assert!(check_created_public_key(&[json!(["x:PublicKey/set", {"notCreated": {"new1": {"type": "invalidProperties"}}}, "0"])]).is_err());
        assert!(check_destroyed(&[json!(["x:ApiKey/set", {"destroyed": ["k1"]}, "0"])], "x:ApiKey/set", "k1").is_ok());
        assert!(check_destroyed(&[json!(["x:ApiKey/set", {"notDestroyed": {"k1": {"type": "notFound"}}}, "0"])], "x:ApiKey/set", "k1").is_err());
    }

    #[test]
    fn form_input() {
        assert_eq!(parse_ip_list("10.0.0.5, 192.168.1.0/24  ::1\n"), vec!["10.0.0.5", "192.168.1.0/24", "::1"]);
        assert!(parse_ip_list("  ,, ").is_empty());
        assert_eq!(expiry_from_date("2027-01-31").unwrap().as_deref(), Some("2027-01-31T00:00:00Z"));
        assert_eq!(expiry_from_date(" ").unwrap(), None);
        assert!(expiry_from_date("31/01/2027").is_err());
        assert!(check_new_password("short", "short").is_err());
        assert!(check_new_password("long-enough", "different1").is_err());
        assert!(check_new_password("long-enough", "long-enough").is_ok());
    }

    #[test]
    fn rfc_6238_codes() {
        let secret = b"12345678901234567890";
        assert_eq!(totp_code(secret, 59), "287082");
        assert_eq!(totp_code(secret, 1111111109), "081804");
        assert_eq!(totp_code(secret, 1234567890), "005924");
        assert!(totp_valid(secret, "287082", 59 + 30));
        assert!(totp_valid(secret, " 287082 ", 59 - 30 + 29));
        assert!(!totp_valid(secret, "287082", 59 + 120));
        assert!(!totp_valid(secret, "", 59));
    }

    #[test]
    fn base32_and_link() {
        assert_eq!(base32(b""), "");
        assert_eq!(base32(b"f"), "MY");
        assert_eq!(base32(b"fooba"), "MZXW6YTB");
        assert_eq!(base32(b"foobar"), "MZXW6YTBOI");
        let uri = Totp::uri(b"foobar", "ann@example.test", "Barbican");
        assert_eq!(uri, "otpauth://totp/Barbican:ann%40example.test?issuer=Barbican&secret=MZXW6YTBOI&algorithm=SHA1&digits=6&period=30");
        let t = Totp::generate("ann@example.test", TOTP_ISSUER).unwrap();
        assert_eq!(t.secret.len(), 20);
        assert!(t.uri.contains(&format!("secret={}", base32(&t.secret))));
        assert_eq!(t.grouped().replace(' ', ""), base32(&t.secret));
        assert!(t.grouped().split(' ').all(|g| g.len() <= 4));
    }

    #[test]
    fn remembered_sign_in_is_rewritten_only_when_it_holds_the_replaced_password() {
        let accounts = vec![
            json!({"kind": "oauth", "server": "https://mail.example.test", "user": "ann@example.test"}),
            json!({"kind": "password", "server": "mail.example.test", "user": "Ann@example.test", "pass": "old"}),
        ];
        let base = "https://mail.example.test";
        let v = remembered_with_password(&accounts, "ann@example.test", base, "old", "new-pass").unwrap();
        assert_eq!(v, json!({"kind": "password", "server": "mail.example.test", "user": "Ann@example.test", "pass": "new-pass"}));
        // a remembered app password (not what was typed as the current password) stays
        assert!(remembered_with_password(&accounts, "ann@example.test", base, "typed-other", "new-pass").is_none());
        assert!(remembered_with_password(&accounts, "bob@example.test", base, "old", "x").is_none());
        assert!(remembered_with_password(&accounts, "ann@example.test", "https://other.example.test", "old", "x").is_none());
        assert!(remembered_with_password(&[], "ann@example.test", base, "old", "x").is_none());
        // the entry the TOTP switch-on forgets
        assert_eq!(remembered_password_entry(&accounts, "ann@example.test", base).unwrap()["user"], "Ann@example.test");
        assert!(remembered_password_entry(&accounts[..1], "ann@example.test", base).is_none(), "an oauth entry has no password");
    }

    #[test]
    fn session_follows_a_password_change_only_when_it_used_that_password() {
        assert_eq!(classify_session("old-pass", "old-pass", false), Followup::Switch);
        // two-step sign-in is on: the new password has no code to go with it
        assert_eq!(classify_session("old-pass", "old-pass", true), Followup::EndSession);
        assert_eq!(classify_session("old-pass$123456", "old-pass", true), Followup::EndSession);
        assert_eq!(classify_session("old-pass$123456", "old-pass", false), Followup::EndSession);
        // an app password session keeps its own secret
        assert_eq!(classify_session("app-secret", "old-pass", false), Followup::Keep);
        assert_eq!(classify_session("old-passX", "old-pass", false), Followup::Keep);
    }

    #[test]
    fn issuer_is_the_servers_name_like_bulwark() {
        assert_eq!(TOTP_ISSUER, "Stalwart");
        assert!(key_not_deleted("forbidden").starts_with("Encryption was turned off, but the key was not deleted: forbidden"));
    }

    #[test]
    fn a_reset_page_belongs_to_nobody() {
        let mut st = page();
        st.owner = "acct-a".into();
        st.shown = Some(Shown { label: "Phone".into(), secret: "s3cret".into(), kind: Kind::AppPassword });
        st.busy = true;
        let epoch = st.epoch;
        assert!(owns(&st, epoch, "acct-a", "acct-a"));
        assert!(!owns(&st, epoch, "acct-a", "acct-b"), "the client is another account's");
        st.reset();
        assert!(st.shown.is_none() && !st.busy && st.owner.is_empty() && st.cap.is_none() && st.totp.is_none());
        assert_eq!(st.epoch, epoch + 1);
        assert!(!owns(&st, epoch, "acct-a", "acct-a"), "a job of the old page may not write into the new one");
        assert!(rows(&st).is_none());
    }

    #[test]
    fn one_server_job_at_a_time() {
        let mut st = page();
        edit(&mut st, "app.desc", &json!("Phone"));
        assert!(button(&mut st, "app.create").is_some());
        assert!(st.busy);
        // a double click: the second press only says to wait
        assert!(button(&mut st, "app.create").is_some());
        assert_eq!(st.status, "Wait for the previous action to finish.");
        assert!(st.shown.is_none());
    }

    #[test]
    fn confirming_press_is_refused_inside_a_double_click() {
        let mut st = page();
        let t0 = std::time::Instant::now();
        let key = "revoke.app:p1";
        assert!(button_at(&mut st, key, t0).is_some());
        assert_eq!(st.confirm, "server.sec.revoke.app:p1");
        // 100 ms later: still waiting, nothing started
        assert!(button_at(&mut st, key, t0 + std::time::Duration::from_millis(100)).is_some());
        assert!(!st.busy);
        assert_eq!(st.confirm, "server.sec.revoke.app:p1");
        // a real second click starts the job
        assert!(button_at(&mut st, key, t0 + std::time::Duration::from_millis(600)).is_some());
        assert!(st.busy && st.confirm.is_empty());
    }

    #[test]
    fn turning_encryption_on_warns_and_needs_a_second_press() {
        let mut st = page();
        st.enc_pick = Some("k1".into());
        let t0 = std::time::Instant::now();
        assert!(button_at(&mut st, "enc.enable", t0).is_some());
        assert!(!st.busy);
        assert!(st.status.contains("private key") && st.status.contains("cannot show encrypted messages"));
        let r = rows(&st).unwrap();
        assert_eq!(r.iter().find(|x| x.key == "server.sec.enc.enable").unwrap().text.as_str(), "Click again to turn on");
        assert!(button_at(&mut st, "enc.enable", t0 + std::time::Duration::from_millis(700)).is_some());
        assert!(st.busy);
    }

    fn page() -> State {
        State { cap: Some("urn:stalwart:jmap"), password_session: true, advanced: true, api_keys_available: true, crypto_available: true, ..Default::default() }
    }

    #[test]
    fn page_rows_follow_the_state() {
        let mut st = page();
        let r = rows(&st).unwrap();
        assert!(r.iter().any(|x| x.key == "server.sec.pw.change"));
        assert!(!r.iter().any(|x| x.key == "server.sec.pw.otp"), "no code field while two-step sign-in is off");
        assert!(r.iter().any(|x| x.key == "server.sec.totp.start"));
        st.otp_enabled = true;
        let r = rows(&st).unwrap();
        assert!(r.iter().any(|x| x.key == "server.sec.pw.otp") && r.iter().any(|x| x.key == "server.sec.totp.disable"));
        // an SSO session has no password and no second step
        st.password_session = false;
        let r = rows(&st).unwrap();
        assert!(!r.iter().any(|x| x.key.starts_with("server.sec.pw.") || x.key.starts_with("server.sec.totp.")));
        assert!(r.iter().any(|x| x.key == "server.sec.app.create"));
        // advanced-only sections follow the switch and the server's answer
        st.advanced = false;
        let r = rows(&st).unwrap();
        assert!(!r.iter().any(|x| x.key == "server.sec.key.create" || x.key == "server.sec.pk.add"));
        st.advanced = true; st.api_keys_available = false; st.crypto_available = false;
        let r = rows(&st).unwrap();
        assert!(!r.iter().any(|x| x.key == "server.sec.key.create" || x.key == "server.sec.pk.add"));
    }

    #[test]
    fn revoking_needs_a_second_press_and_shows_the_secret_once() {
        let mut st = page();
        st.app_passwords = vec![Credential { id: "p1".into(), description: "Phone".into(), created_at: None, expires_at: None, allowed_ips: vec![] }];
        let key = "revoke.app:p1";
        assert!(button(&mut st, key).is_some());
        assert_eq!(st.confirm, "server.sec.revoke.app:p1");
        let r = rows(&st).unwrap();
        assert_eq!(r.iter().find(|x| x.key == "server.sec.revoke.app:p1").unwrap().text.as_str(), "Click again to delete");
        // another button cancels the pending delete
        button(&mut st, "secret.done");
        assert!(st.confirm.is_empty());
        st.shown = Some(Shown { label: "Phone".into(), secret: "s3cret".into(), kind: Kind::AppPassword });
        let r = rows(&st).unwrap();
        assert!(r.iter().any(|x| x.key == "server.sec.shown" && x.text.as_str() == "s3cret"));
        button(&mut st, "secret.done");
        assert!(rows(&st).unwrap().iter().all(|x| x.text.as_str() != "s3cret"));
    }

    #[test]
    fn password_form_is_checked_before_anything_is_sent() {
        let mut st = page();
        button(&mut st, "pw.change");
        assert_eq!(st.status, "Enter your current password.");
        st.pw_cur = "old".into(); st.pw_new = "short".into(); st.pw_confirm = "short".into();
        button(&mut st, "pw.change");
        assert_eq!(st.status, "Password must be at least 8 characters");
        st.pw_new = "long-enough".into(); st.pw_confirm = "long-enough".into(); st.otp_enabled = true;
        button(&mut st, "pw.change");
        assert_eq!(st.status, "Enter a verification code.");
        st.pw_otp = "123456".into();
        st.status.clear();
        assert!(button(&mut st, "pw.change").is_some());
        assert!(st.status.is_empty());
        st.forget_secrets();
        assert!(st.pw_cur.is_empty() && st.pw_new.is_empty() && st.pw_otp.is_empty());
    }

    #[test]
    fn totp_setup_checks_the_code_locally() {
        let mut st = page();
        button(&mut st, "totp.start");
        assert!(st.totp.is_some());
        st.totp_pw = "pw".into(); st.totp_code = "000000".into();
        // a code that is not the current one never reaches the server (1 in a million: retry once)
        let secret = st.totp.as_ref().unwrap().secret.clone();
        if totp_valid(&secret, "000000", now_unix()) { st.totp_code = "000001".into(); }
        let _ = button(&mut st, "totp.confirm");
        assert!(st.status.starts_with("Invalid verification code"));
        st.totp_code = totp_code(&secret, now_unix());
        assert!(button(&mut st, "totp.confirm").is_some());
        assert!(st.status.is_empty());
        button(&mut st, "totp.cancel");
        assert!(st.totp.is_none() && st.totp_pw.is_empty());
    }

    #[test]
    fn credential_form_is_checked() {
        let mut st = page();
        button(&mut st, "app.create");
        assert_eq!(st.status, "Give the app password a name.");
        edit(&mut st, "app.desc", &json!("Phone"));
        edit(&mut st, "app.expires", &json!("soon"));
        button(&mut st, "app.create");
        assert_eq!(st.status, "Write the expiry date as YYYY-MM-DD.");
        edit(&mut st, "app.expires", &json!("2027-02-03"));
        assert!(button(&mut st, "app.create").is_some());
        assert!(st.status.is_empty());
    }
}
