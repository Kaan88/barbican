//! Sending identities (RFC 8621 §6) as Bulwark manages them: creating and deleting an address,
//! its automatic Bcc, the signature (plain or formatted), the order the addresses are offered in
//! and the default sender, and the sub-address helper of the composer's From.
//!
//! * The form, its checks and the Identity/set shapes follow Bulwark identity-form.tsx and
//!   lib/jmap/client.ts createIdentity / updateIdentity / deleteIdentity.
//! * The signature limit and the text made from a formatted signature follow
//!   components/identity/signature-editor.tsx.
//! * The order is lib/identity-order.ts sortIdentities, with the default sender moved to the
//!   front (identity-manager-modal.tsx handleSetPrimary, auth-store.ts applyPreferredIdentity).
//!   The default sender is kept in prefs.json under Bulwark's own key and format,
//!   `preferredIdentityIds` {accountKey: identityId}, keyed by Bulwark's account id
//!   (`username@host`, lib/account-utils.ts generateAccountId), so a settings export carries it.
//! * Sub-addressing is lib/sub-addressing.ts. Recent tags and the tags used per domain are
//!   Bulwark's browser-local `identity-storage` state (stores/identity-store.ts); Barbican keeps
//!   the same shape in prefs.json under its own key `subAddressTags`.
use serde_json::{json, Map, Value};

pub const KEY_PREFERRED: &str = "preferredIdentityIds";
pub const KEY_SUB_TAGS: &str = "subAddressTags";
/// Stalwart's Identity/set caps each signature field at 2047 UTF-8 bytes (signature-editor.tsx:13).
pub const SIGNATURE_MAX_BYTES: usize = 2047;
/// The size shows from here on, before the limit is reached (signature-editor.tsx:15).
pub const SIGNATURE_SHOW_SIZE_FROM: usize = SIGNATURE_MAX_BYTES * 9 / 10;

// ---------------- the account key and the order ----------------

/// Bulwark's account id (generateAccountId): the login and the server's host name, as
/// `new URL(serverUrl).hostname` gives it.
pub fn account_key(username: &str, server: &str) -> String {
    format!("{username}@{}", url_hostname(server))
}

/// The host name of a server address as a URL parser gives it (`URL.hostname`): no scheme,
/// user, port or path; lower case; a Unicode domain in its ASCII (`xn--`) form; an IPv6 address
/// kept in its brackets.
pub fn url_hostname(server: &str) -> String {
    let t = server.trim();
    let rest = match t.find("://") { Some(i) => &t[i + 3..], None => t };
    let authority = rest.split(['/', '?', '#']).next().unwrap_or("");
    let host = authority.rsplit_once('@').map_or(authority, |(_, h)| h);
    if host.starts_with('[') {
        let inner = match host.find(']') { Some(i) => &host[1..i], None => &host[1..] };
        // the standard compressed form, as a URL parser writes it
        return match inner.parse::<std::net::Ipv6Addr>() { Ok(a) => format!("[{a}]"), Err(_) => format!("[{}]", inner.to_lowercase()) };
    }
    let host = host.split(':').next().unwrap_or("").to_lowercase();
    idna::domain_to_ascii(&host).ok().filter(|h| !h.is_empty()).unwrap_or(host)
}

fn comparable(address: &str) -> String {
    let a = address.trim().to_lowercase();
    match a.rsplit_once('@') {
        Some((l, d)) => format!("{l}@{}", idna::domain_to_ascii(d).ok().filter(|x| !x.is_empty()).unwrap_or_else(|| d.to_string())),
        None => a,
    }
}

/// emailMatchesLogin (identity-order.ts:20): the login's own address, regardless of case and
/// of the domain's Unicode or ASCII form; a bare login ("user") matches its local part.
pub fn email_matches_login(email: &str, username: &str) -> bool {
    let (address, login) = (comparable(email), comparable(username));
    if address == login { return true; }
    !login.contains('@') && address.split('@').next() == Some(login.as_str())
}

/// sortIdentities (identity-order.ts:31): the login's own identities first, the one that
/// cannot be deleted ahead of its aliases; the rest keeps the server's order.
pub fn sort_identities(ids: &[Value], username: &str) -> Vec<Value> {
    let mut v = ids.to_vec();
    // a stable sort on (not the login's, may be deleted) keeps the server's order otherwise
    v.sort_by_key(|i| {
        let mine = email_matches_login(i["email"].as_str().unwrap_or(""), username);
        (!mine, mine && may_delete(i))
    });
    v
}

/// Whether the server lets the identity be deleted (Bulwark checks `!identity.mayDelete`, so a
/// missing value is no).
pub fn may_delete(i: &Value) -> bool { i["mayDelete"] == true }

/// The order the composer and the settings offer: sorted, with the default sender (when it is
/// still one of them) in front. The first one is the default From.
pub fn ordered(ids: &[Value], username: &str, preferred: Option<&str>) -> Vec<Value> {
    let mut v = sort_identities(ids, username);
    if let Some(p) = preferred {
        if let Some(i) = v.iter().position(|x| x["id"].as_str() == Some(p)) {
            let x = v.remove(i);
            v.insert(0, x);
        }
    }
    v
}

/// The default sender of `account` in a `preferredIdentityIds` value (a map; anything else,
/// as Bulwark's settings store does, is no choice).
pub fn decode_preferred(v: Option<&Value>, account: &str) -> Option<String> {
    v.and_then(Value::as_object)?.get(account)?.as_str().filter(|s| !s.is_empty()).map(String::from)
}

/// `preferredIdentityIds` with `account`'s default set to `id`; the other accounts' entries stay.
pub fn encode_preferred(v: Option<&Value>, account: &str, id: &str) -> Value {
    let mut o: Map<String, Value> = v.and_then(Value::as_object).cloned().unwrap_or_default();
    o.insert(account.into(), json!(id));
    Value::Object(o)
}

/// The identities of a client in the order it offers them (reads prefs.json).
pub fn ordered_for(ids: &[Value], username: &str, server: &str) -> Vec<Value> {
    let p = crate::settings::Settings::load();
    ordered(ids, username, decode_preferred(p.prefs.get(KEY_PREFERRED), &account_key(username, server)).as_deref())
}

// ---------------- checks (lib/validation.ts) ----------------

/// isValidEmail (validation.ts:4); the domain may be written in Unicode.
pub fn is_valid_email(email: &str) -> bool {
    if email.is_empty() || email.len() > 254 || email.contains(['\r', '\n', '\0', '<', '>']) { return false; }
    let Some((local, domain)) = email.split_once('@') else { return false };
    if local.is_empty() || local.len() > 64 || domain.contains('@') { return false; }
    let atext = |c: char| c.is_ascii_alphanumeric() || ".!#$%&'*+/=?^_`{|}~-".contains(c);
    if !local.chars().all(atext) { return false; }
    let domain = if domain.is_ascii() { domain.to_string() } else { match idna::domain_to_ascii(domain) { Ok(d) => d, Err(_) => return false } };
    if domain.is_empty() || domain.len() > 255 || domain.starts_with('.') || domain.ends_with('.') { return false; }
    domain.split('.').all(|l| !l.is_empty() && l.len() <= 63 && !l.starts_with('-') && !l.ends_with('-') && l.chars().all(|c| c.is_ascii_alphanumeric() || c == '-'))
}

/// getEmailValidationError (validation.ts:54).
pub fn email_error(email: &str) -> Option<&'static str> {
    if email.trim().is_empty() { return Some("Email address is required"); }
    if email.len() > 254 { return Some("Email address is too long (max 254 characters)"); }
    if email.contains(['\r', '\n', '\0', '<', '>']) { return Some("Email address contains invalid characters"); }
    if !is_valid_email(email) { return Some("Please enter a valid email address"); }
    None
}

/// The addresses of a comma separated list that are not valid (validateEmailList).
pub fn invalid_in_list(csv: &str) -> Vec<String> {
    csv.split(',').map(str::trim).filter(|e| !e.is_empty() && !is_valid_email(e)).map(String::from).collect()
}

/// A comma separated list as JMAP EmailAddress[]; none is null (parseEmailList ?? null).
pub fn parse_list(csv: &str) -> Value {
    let v: Vec<Value> = csv.split(',').map(str::trim).filter(|e| !e.is_empty()).map(|e| json!({"email": e})).collect();
    if v.is_empty() { Value::Null } else { Value::Array(v) }
}

/// An EmailAddress[] as the form shows it: "a@x, b@y".
pub fn list_text(v: &Value) -> String {
    v.as_array().map(|a| a.iter().filter_map(|x| x["email"].as_str()).collect::<Vec<_>>().join(", ")).unwrap_or_default()
}

// ---------------- signatures (signature-editor.tsx, lib/signature-utils.ts) ----------------

/// A formatted signature as a save sends it: through the composer's own sanitiser.
pub fn clean_signature_html(html: &str) -> String {
    if html.trim().is_empty() { return String::new(); }
    crate::templates::clean_html(html)
}

/// textFromSignatureHtml: the plain-text part a save sends with a formatted signature.
pub fn text_from_html(html: &str) -> String {
    let clean = clean_signature_html(html);
    if clean.is_empty() { String::new() } else { crate::rte::html::html_text(&clean).trim().to_string() }
}

/// hasSignatureContent: text, or a picture.
pub fn has_html_content(html: &str) -> bool {
    let clean = clean_signature_html(html);
    !clean.is_empty() && (!crate::rte::html::html_text(&clean).trim().is_empty() || clean.to_lowercase().contains("<img"))
}

/// signatureBytes: the bytes the larger of the two fields takes as a save sends them.
pub fn signature_bytes(html: &str, text: &str, plain_only: bool) -> usize {
    if plain_only { return text.len(); }
    text.len().max(clean_signature_html(html).len())
}

/// getPlainTextSignature: the text signature, else the text of the formatted one.
pub fn plain_signature(identity: &Value) -> String {
    let t = identity["textSignature"].as_str().unwrap_or("");
    if !t.trim().is_empty() { return t.trim().to_string(); }
    text_from_html(identity["htmlSignature"].as_str().unwrap_or(""))
}

/// The size line under the signature (signature_size / signature_too_long), when it shows.
pub fn size_note(bytes: usize) -> Option<String> {
    if bytes > SIGNATURE_MAX_BYTES { Some(format!("Too long: {bytes} of {SIGNATURE_MAX_BYTES} bytes. Shorten it to save.")) }
    else if bytes >= SIGNATURE_SHOW_SIZE_FROM { Some(format!("{bytes} of {SIGNATURE_MAX_BYTES} bytes")) }
    else { None }
}

// ---------------- the form ----------------

/// The create / edit form (IdentityFormData). `html` says whether `signature` holds a formatted
/// (HTML) signature or plain text.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Form {
    pub name: String,
    pub email: String,
    pub reply_to: String,
    pub bcc: String,
    pub signature: String,
    pub html: bool,
}

/// The form for an identity as it is (or an empty one for a new address). With plain text only
/// (Composing) the signature is its text, and the formatted one is left as it is.
pub fn form_of(identity: Option<&Value>, plain_only: bool) -> Form {
    let Some(i) = identity else { return Form::default() };
    let html = i["htmlSignature"].as_str().unwrap_or("");
    let formatted = !plain_only && !html.trim().is_empty();
    Form {
        name: i["name"].as_str().unwrap_or("").into(),
        email: i["email"].as_str().unwrap_or("").into(),
        reply_to: list_text(&i["replyTo"]),
        bcc: list_text(&i["bcc"]),
        signature: if formatted { html.to_string() } else { i["textSignature"].as_str().unwrap_or("").to_string() },
        html: formatted,
    }
}

/// The signature after the "Formatted" switch: text becomes paragraphs, HTML its text.
pub fn switch_format(signature: &str, to_html: bool) -> String {
    if to_html {
        if signature.trim().is_empty() { return String::new(); }
        crate::rte::html::export(&crate::rte::model::Doc { blocks: crate::rte::html::import_text(signature) })
    } else {
        crate::rte::html::html_text(signature).trim().to_string()
    }
}

/// The two signature fields a save sends: (textSignature, htmlSignature), None for empty
/// (handleSubmit: blank is null). A formatted signature with nothing in it is empty
/// (signature-editor.tsx editHtml).
pub fn signature_fields(f: &Form) -> (Option<String>, Option<String>) {
    if f.html {
        if !has_html_content(&f.signature) { return (None, None); }
        let clean = clean_signature_html(&f.signature);
        let text = crate::rte::html::html_text(&clean).trim().to_string();
        (Some(text).filter(|t| !t.is_empty()), Some(clean))
    } else {
        (Some(f.signature.clone()).filter(|t| !t.trim().is_empty()), None)
    }
}

/// Whether the form's signature differs from the identity's as stored: its text, or the
/// Formatted switch. Only an edited signature is sent, checked and measured again.
pub fn signature_edited(old: &Value, f: &Form, plain_only: bool) -> bool {
    let was = form_of(Some(old), plain_only);
    f.signature != was.signature || f.html != was.html
}

/// The bytes the form's signature takes: as stored when it was not edited (it is not sent
/// again), else as a save would send it (a formatted one after the sanitiser, whose output
/// carries inline styling and so can be larger than what was typed).
pub fn form_signature_bytes(f: &Form, old: Option<&Value>, plain_only: bool) -> usize {
    match old {
        Some(o) if !signature_edited(o, f, plain_only) => {
            let text = o["textSignature"].as_str().unwrap_or("").len();
            if plain_only { text } else { text.max(o["htmlSignature"].as_str().unwrap_or("").len()) }
        }
        _ if f.html => signature_bytes(&f.signature, &text_from_html(&f.signature), false),
        _ => f.signature.len(),
    }
}

/// The form's errors (IdentityForm validate), joined; None when it can be saved. `old`: the
/// identity being edited (its address is fixed and not checked again, and a signature left as
/// it is is not measured: it is not sent).
pub fn validate(f: &Form, old: Option<&Value>, plain_only: bool) -> Option<String> {
    let mut errs: Vec<String> = vec![];
    if old.is_none() { if let Some(e) = email_error(f.email.trim()) { errs.push(e.into()); } }
    let bad = invalid_in_list(&f.reply_to);
    if !bad.is_empty() { errs.push(format!("Reply-to: these addresses are not valid: {}", bad.join(", "))); }
    let bad = invalid_in_list(&f.bcc);
    if !bad.is_empty() { errs.push(format!("Bcc: these addresses are not valid: {}", bad.join(", "))); }
    if old.is_none_or(|o| signature_edited(o, f, plain_only)) {
        let bytes = form_signature_bytes(f, None, plain_only);
        if bytes > SIGNATURE_MAX_BYTES {
            errs.push(if f.html {
                format!("The signature is too long ({bytes} of {SIGNATURE_MAX_BYTES} bytes as it would be saved; the formatting adds some size).")
            } else {
                format!("The signature is too long ({bytes} of {SIGNATURE_MAX_BYTES} bytes).")
            });
        }
    }
    if errs.is_empty() { None } else { Some(errs.join(" ")) }
}

/// The Identity/set create object (createIdentity): blank signatures are left out (they are
/// String, default ""), replyTo and bcc may be null.
pub fn create_object(f: &Form) -> Value {
    let (text, html) = signature_fields(f);
    let mut o = json!({"name": if f.name.trim().is_empty() { "" } else { f.name.as_str() }, "email": f.email.trim(), "replyTo": parse_list(&f.reply_to), "bcc": parse_list(&f.bcc)});
    if let Some(t) = text { o["textSignature"] = json!(t); }
    if let Some(h) = html { o["htmlSignature"] = json!(h); }
    o
}

/// The Identity/set update patch (updateIdentity) for an edited identity. The signature goes
/// out only when it was edited (its text or the Formatted switch): the stored HTML is not run
/// through the sanitiser again and a hand-written plain-text version is not rebuilt from it
/// (Bulwark makes the text part only when the HTML is edited, signature-editor.tsx editHtml).
/// A cleared signature is "" (the property is a String). With plain text only the formatted
/// signature is kept as it is; a plain signature otherwise clears the formatted one, so the
/// two cannot disagree.
pub fn update_patch(old: &Value, f: &Form, plain_only: bool) -> Value {
    let mut p = json!({"name": if f.name.trim().is_empty() { "" } else { f.name.as_str() }, "replyTo": parse_list(&f.reply_to), "bcc": parse_list(&f.bcc)});
    if signature_edited(old, f, plain_only) {
        let (text, html) = signature_fields(f);
        p["textSignature"] = json!(text.unwrap_or_default());
        if !plain_only || f.html { p["htmlSignature"] = json!(html.unwrap_or_default()); }
    }
    // only what changed goes out
    if let Some(o) = p.as_object_mut() {
        o.retain(|k, v| {
            let was = &old[k.as_str()];
            let blank = |x: &Value| x.is_null() || x.as_str() == Some("") || x.as_array().is_some_and(|a| a.is_empty());
            !(was == v || (blank(was) && blank(v)))
        });
    }
    p
}

/// The one-line summary of an identity in the list (the manager's additional info).
pub fn summary(i: &Value, primary: bool) -> String {
    let mut parts: Vec<String> = vec![];
    let email = i["email"].as_str().unwrap_or("");
    if i["name"].as_str().is_some_and(|n| !n.trim().is_empty()) { parts.push(email.to_string()); }
    if primary { parts.push("Primary".into()); }
    let rt = list_text(&i["replyTo"]);
    if !rt.is_empty() { parts.push(format!("Reply-to {rt}")); }
    let bcc = list_text(&i["bcc"]);
    if !bcc.is_empty() { parts.push(format!("Bcc {bcc}")); }
    let sig = plain_signature(i);
    if !sig.is_empty() {
        let line: String = sig.lines().next().unwrap_or("").chars().take(60).collect();
        parts.push(format!("Signature: {line}{}", if sig.lines().count() > 1 || sig.chars().count() > 60 { "…" } else { "" }));
    }
    if !may_delete(i) { parts.push("This identity cannot be deleted".into()); }
    parts.join(" · ")
}

// ---------------- the server (Identity/set) ----------------

/// The account identities live in.
pub fn account(c: &crate::jmap::Client) -> String { c.account_for("urn:ietf:params:jmap:submission") }

/// What a SetError means here (client.ts createIdentity / updateIdentity / deleteIdentity).
pub fn set_failure(err: &Value, what: &str) -> String {
    match (err["type"].as_str().unwrap_or(""), what) {
        ("forbidden", "create") | ("forbiddenFrom", "create") => "You are not authorized to send from this email address.".into(),
        ("forbidden", "update") => "You are not authorized to change this address.".into(),
        ("forbidden", "destroy") => "This address cannot be deleted.".into(),
        ("notFound", "update") => "This address is gone (it may have been deleted); reload.".into(),
        ("notFound", "destroy") => "This address is already gone.".into(),
        _ => crate::jmap::set_error(err),
    }
}

/// The outcome of one Identity/set call for the object `key` (a creation id or an identity id):
/// the created id for a create, an error message for a SetError, a method error or an answer
/// that does not say it was done.
pub fn set_outcome(resp: &[Value], what: &str, key: &str) -> Result<Option<String>, String> {
    let r = resp.first().ok_or("The server sent no answer.")?;
    if r[0] == "error" { return Err(crate::jmap::method_error(&r[1])); }
    if r[0] != "Identity/set" { return Err("The server sent an unexpected answer.".into()); }
    let (bad, good) = match what { "create" => ("notCreated", "created"), "update" => ("notUpdated", "updated"), _ => ("notDestroyed", "destroyed") };
    if let Some(e) = r[1][bad].get(key) { return Err(set_failure(e, what)); }
    match what {
        "create" => r[1][good][key]["id"].as_str().map(|s| Some(s.to_string())).ok_or_else(|| "The server did not say the address was added; reload.".into()),
        "update" => if r[1][good].get(key).is_some() { Ok(None) } else { Err("The server did not say the address was saved; reload.".into()) },
        _ => if r[1][good].as_array().is_some_and(|a| a.iter().any(|x| x.as_str() == Some(key))) { Ok(None) } else { Err("The server did not say the address was deleted; reload.".into()) },
    }
}

pub fn create(c: &crate::jmap::Client, f: &Form) -> Result<String, String> {
    let r = c.request(json!([["Identity/set", {"accountId": account(c), "create": {"new-identity": create_object(f)}}, "0"]]))?;
    set_outcome(&r, "create", "new-identity").map(|id| id.unwrap_or_default())
}

pub fn update(c: &crate::jmap::Client, id: &str, patch: Value) -> Result<(), String> {
    if patch.as_object().is_some_and(|o| o.is_empty()) { return Ok(()); }
    let r = c.request(json!([["Identity/set", {"accountId": account(c), "update": { id: patch }}, "0"]]))?;
    set_outcome(&r, "update", id).map(|_| ())
}

pub fn destroy(c: &crate::jmap::Client, id: &str) -> Result<(), String> {
    let r = c.request(json!([["Identity/set", {"accountId": account(c), "destroy": [id]}, "0"]]))?;
    set_outcome(&r, "destroy", id).map(|_| ())
}

// ---------------- the automatic Bcc (RFC 8621 Identity.bcc) ----------------

/// The identity's own Bcc addresses a send adds (email-composer.tsx:4245): each once, and none
/// already on the message. The server does not add them. Barbican puts them in the submission's
/// envelope only, never in the message, so no copy in Drafts (a saved draft, the draft an undone
/// send brings back, or one whose submission failed) carries them.
pub fn identity_bcc_extra(to: &str, cc: &str, bcc: &str, ident: &Value) -> Vec<String> {
    let have: Vec<String> = crate::jmap::parse_addresses(&format!("{to},{cc},{bcc}")).iter()
        .filter_map(|a| a["email"].as_str().map(|e| e.trim().to_lowercase())).collect();
    let mut add: Vec<String> = vec![];
    for e in ident["bcc"].as_array().into_iter().flatten().filter_map(|a| a["email"].as_str()).map(str::trim) {
        if e.contains('@') && !have.contains(&e.to_lowercase()) && !add.iter().any(|x| x.eq_ignore_ascii_case(e)) { add.push(e.to_string()); }
    }
    add
}

// ---------------- sub-addressing (lib/sub-addressing.ts) ----------------

pub const DEFAULT_DELIMITER: &str = "+";
pub const MAX_TAG_LENGTH: usize = 30;
const MAX_RECENT_TAGS: usize = 10;
const MAX_DOMAIN_SUGGESTIONS: usize = 5;

/// isValidSubAddressDelimiter: exactly one RFC 5321 atext special character.
pub fn valid_delimiter(d: &str) -> bool {
    let mut ch = d.chars();
    matches!((ch.next(), ch.next()), (Some(c), None) if "!#$%&'*+-./=?^_`{|}~".contains(c))
}

/// The delimiter setting (`subAddressDelimiter`), "+" when unset or not valid.
pub fn delimiter() -> String {
    let d = crate::settings::Settings::load().str("subAddressDelimiter", DEFAULT_DELIMITER);
    if valid_delimiter(&d) { d } else { DEFAULT_DELIMITER.into() }
}

/// generateSubAddress: `user{delimiter}tag@domain`, an existing tag replaced; the tag keeps
/// letters, digits and dashes, lower-cased. Unchanged when there is nothing to add.
pub fn generate_sub_address(base: &str, tag: &str, delimiter: &str) -> String {
    let mut parts = base.split('@');
    let (Some(local), Some(domain)) = (parts.next(), parts.next()) else { return base.into() };
    if local.is_empty() || domain.is_empty() || tag.is_empty() { return base.into(); }
    let clean_local = match local.find(delimiter) { Some(i) if !delimiter.is_empty() => &local[..i], _ => local };
    let clean_tag: String = tag.chars().filter(|c| c.is_ascii_alphanumeric() || *c == '-').collect::<String>().to_lowercase();
    if clean_tag.is_empty() { return base.into(); }
    format!("{clean_local}{delimiter}{clean_tag}@{domain}")
}

/// extractDomain: the domain of a recipient, lower-cased.
pub fn extract_domain(email: &str) -> Option<String> {
    let (_, d) = email.trim().rsplit_once('@')?;
    (!d.is_empty() && !d.contains('@')).then(|| d.to_lowercase())
}

/// suggestTagsForDomain.
pub fn suggest_tags(domain: &str) -> Vec<String> {
    let d = domain.to_lowercase();
    let known: &[(&[&str], &[&str])] = &[
        (&["amazon.com", "amazon.fr", "amazon.de", "amazon.co.uk"], &["amazon", "shopping", "orders"]),
        (&["ebay.com", "ebay.fr"], &["ebay", "shopping"]),
        (&["paypal.com"], &["paypal", "payments"]),
        (&["facebook.com"], &["facebook", "social"]),
        (&["twitter.com", "x.com"], &["twitter", "social"]),
        (&["linkedin.com"], &["linkedin", "professional"]),
        (&["github.com"], &["github", "dev", "notifications"]),
        (&["gitlab.com"], &["gitlab", "dev", "notifications"]),
        (&["stackoverflow.com"], &["stackoverflow", "dev"]),
        (&["reddit.com"], &["reddit", "social"]),
        (&["netflix.com"], &["netflix", "entertainment"]),
        (&["spotify.com"], &["spotify", "music"]),
        (&["steam.com"], &["steam", "gaming"]),
        (&["discord.com"], &["discord", "gaming"]),
    ];
    if let Some((_, tags)) = known.iter().find(|(ds, _)| ds.contains(&d.as_str())) { return tags.iter().map(|t| t.to_string()).collect(); }
    let parts: Vec<&str> = d.split('.').collect();
    let main = if parts.len() >= 2 { parts[parts.len() - 2] } else { parts[0] };
    vec![main.to_string(), "newsletter".into(), "registration".into()]
}

/// The helper's suggestions for the recipients: at most five, each once.
pub fn suggestions_for(recipients: &[String]) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    for t in recipients.iter().filter_map(|r| extract_domain(r)).flat_map(|d| suggest_tags(&d)) {
        if !out.contains(&t) { out.push(t); }
    }
    out.truncate(5);
    out
}

/// getTagValidationError, as the helper words it.
pub fn tag_error(tag: &str) -> Option<String> {
    if tag.is_empty() { return Some("Tag cannot be empty".into()); }
    if tag.chars().count() > MAX_TAG_LENGTH { return Some(format!("Tag must be {MAX_TAG_LENGTH} characters or less")); }
    if !tag.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') { return Some("Tag must contain only letters, numbers, and dashes".into()); }
    None
}

/// The recent tags in a `subAddressTags` value, newest first.
pub fn recent_tags(v: Option<&Value>) -> Vec<String> {
    v.and_then(|v| v["recentTags"].as_array()).map(|a| a.iter().filter_map(|t| t.as_str().map(String::from)).collect()).unwrap_or_default()
}

/// `subAddressTags` after using `tag` (addRecentTag, and addTagSuggestion for the first
/// recipient domain).
pub fn note_tag(v: Option<&Value>, tag: &str, domain: Option<&str>) -> Value {
    let mut o: Map<String, Value> = v.and_then(Value::as_object).cloned().unwrap_or_default();
    let mut recent: Vec<String> = vec![tag.to_string()];
    recent.extend(recent_tags(v).into_iter().filter(|t| t != tag));
    recent.truncate(MAX_RECENT_TAGS);
    o.insert("recentTags".into(), json!(recent));
    if let Some(d) = domain {
        let mut sugg: Map<String, Value> = o.get("tagSuggestions").and_then(Value::as_object).cloned().unwrap_or_default();
        let mut list: Vec<Value> = sugg.get(d).and_then(Value::as_array).cloned().unwrap_or_default();
        if !list.iter().any(|x| x.as_str() == Some(tag)) { list.push(json!(tag)); }
        list.truncate(MAX_DOMAIN_SUGGESTIONS);
        sugg.insert(d.into(), Value::Array(list));
        o.insert("tagSuggestions".into(), Value::Object(sugg));
    }
    Value::Object(o)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ident(id: &str, email: &str, may_delete: bool) -> Value { json!({"id": id, "email": email, "name": "", "mayDelete": may_delete}) }

    #[test]
    fn the_login_comes_first_then_the_server_order() {
        // Stalwart lists a group's identity before the member's own
        let ids = vec![ident("g", "team@example.test", false), ident("a", "ann+x@example.test", true), ident("alias", "ANN@example.test", true), ident("main", "ann@example.test", false), ident("o", "other@example.test", true)];
        let v: Vec<String> = sort_identities(&ids, "ann@example.test").iter().map(|i| i["id"].as_str().unwrap().to_string()).collect();
        let v: Vec<&str> = v.iter().map(String::as_str).collect();
        assert_eq!(v, ["main", "alias", "g", "a", "o"]);
        // a bare login matches the local part
        assert!(email_matches_login("ann@example.test", "ann"));
        assert!(!email_matches_login("anna@example.test", "ann"));
        // Unicode and ASCII forms of the domain are the same address
        assert!(email_matches_login("ann@bücher.example", "ann@xn--bcher-kva.example"));
    }

    #[test]
    fn the_default_sender_goes_in_front_and_is_kept_per_account() {
        let ids = vec![ident("main", "ann@example.test", false), ident("b", "sales@example.test", true)];
        assert_eq!(ordered(&ids, "ann@example.test", Some("b"))[0]["id"], "b");
        // a default that is gone changes nothing
        assert_eq!(ordered(&ids, "ann@example.test", Some("gone"))[0]["id"], "main");
        assert_eq!(account_key("ann@example.test", "https://mail.example.test/jmap"), "ann@example.test@mail.example.test");
        // as URL.hostname: lower case, no port or user, xn-- form, IPv6 in brackets
        assert_eq!(account_key("ann", "HTTPS://Mail.Example.test:8443/jmap/session"), "ann@mail.example.test");
        assert_eq!(url_hostname("https://user:pw@Mail.Example.test"), "mail.example.test");
        assert_eq!(url_hostname("https://Bücher.example/"), "xn--bcher-kva.example");
        assert_eq!(url_hostname("https://[2001:DB8::1]:443/jmap"), "[2001:db8::1]");
        // IPv6 in its standard compressed form
        assert_eq!(url_hostname("https://[2001:0DB8:0000:0000:0000:0000:0000:0001]/"), "[2001:db8::1]");
        assert_eq!(url_hostname("mail.example.test"), "mail.example.test");
        let v = encode_preferred(None, "a1", "b");
        let v = encode_preferred(Some(&v), "a2", "x");
        assert_eq!(decode_preferred(Some(&v), "a1").as_deref(), Some("b"));
        assert_eq!(decode_preferred(Some(&v), "a2").as_deref(), Some("x"));
        assert_eq!(decode_preferred(Some(&v), "a3"), None);
        // not a map: no choice
        assert_eq!(decode_preferred(Some(&json!(["b"])), "a1"), None);
    }

    #[test]
    fn addresses_are_checked_like_bulwark() {
        assert!(is_valid_email("ann@example.test"));
        assert!(is_valid_email("*@example.test"));
        assert!(is_valid_email("ann@bücher.example"));
        for bad in ["", "ann", "ann@", "@example.test", "a b@example.test", "ann@-x.test", "ann@x..test", "ann@x.test.", "<ann@example.test>", "ann@example.test\r\nBcc: x@y"] {
            assert!(!is_valid_email(bad), "{bad}");
        }
        assert_eq!(email_error(" "), Some("Email address is required"));
        assert_eq!(invalid_in_list("a@x.test, nope ,, b@y.test"), vec!["nope".to_string()]);
        assert_eq!(parse_list(" , "), Value::Null);
        assert_eq!(parse_list("a@x.test, b@y.test"), json!([{"email": "a@x.test"}, {"email": "b@y.test"}]));
        assert_eq!(list_text(&json!([{"email": "a@x.test"}, {"email": "b@y.test"}])), "a@x.test, b@y.test");
    }

    #[test]
    fn create_leaves_blank_signatures_out_and_sends_null_lists() {
        let f = Form { name: "  ".into(), email: " new@example.test ".into(), ..Default::default() };
        assert_eq!(create_object(&f), json!({"name": "", "email": "new@example.test", "replyTo": null, "bcc": null}));
        let f = Form { name: "Ann".into(), email: "new@example.test".into(), bcc: "archive@example.test".into(), signature: "Ann\nSales".into(), ..Default::default() };
        let o = create_object(&f);
        assert_eq!(o["bcc"], json!([{"email": "archive@example.test"}]));
        assert_eq!(o["textSignature"], "Ann\nSales");
        assert!(o.get("htmlSignature").is_none());
    }

    #[test]
    fn a_formatted_signature_is_sanitised_and_brings_its_text() {
        let f = Form { email: "a@example.test".into(), signature: "<p><b>Ann</b></p><script>alert(1)</script><p onclick=\"x()\">Sales</p>".into(), html: true, ..Default::default() };
        let (text, html) = signature_fields(&f);
        let html = html.unwrap();
        assert!(!html.contains("script") && !html.contains("onclick"), "{html}");
        assert!(html.contains("Ann") && html.contains("Sales"));
        let text = text.unwrap();
        assert!(text.contains("Ann") && text.contains("Sales") && !text.contains('<'), "{text}");
        // empty formatting is no signature
        let f = Form { signature: "<p><br></p>".into(), html: true, ..Default::default() };
        assert_eq!(signature_fields(&f), (None, None));
    }

    #[test]
    fn the_update_patch_sends_only_changes_and_keeps_both_fields_in_step() {
        let old = json!({"id": "i", "email": "a@example.test", "name": "Ann", "replyTo": null, "bcc": null, "textSignature": "Old", "htmlSignature": "<p>Old</p>"});
        // only the Bcc changed, and the signature was edited as plain text: the formatted one is cleared
        let f = Form { name: "Ann".into(), bcc: "copy@example.test".into(), signature: "New".into(), ..form_of(Some(&old), false) };
        let f = Form { html: false, ..f };
        let p = update_patch(&old, &f, false);
        assert_eq!(p, json!({"bcc": [{"email": "copy@example.test"}], "textSignature": "New", "htmlSignature": ""}));
        // plain text only: the formatted signature stays as it is
        let f = Form { signature: "New".into(), ..form_of(Some(&old), true) };
        assert!(!f.html);
        assert_eq!(update_patch(&old, &f, true), json!({"textSignature": "New"}));
        // nothing changed: an empty patch (an empty list and null are the same)
        let old2 = json!({"name": "Ann", "replyTo": [], "bcc": null, "textSignature": "", "htmlSignature": ""});
        assert_eq!(update_patch(&old2, &form_of(Some(&old2), false), false), json!({}));
    }

    #[test]
    fn editing_the_name_leaves_the_signatures_as_they_are() {
        // a formatted signature the editor model cannot hold (a picture in a table) and a
        // hand-written text version: a name change sends neither
        let old = json!({"id": "i", "email": "a@example.test", "name": "Ann", "replyTo": null, "bcc": null,
            "textSignature": "Ann - written by hand", "htmlSignature": "<table><tr><td><img src=\"https://example.test/logo.png\"></td><td>Ann</td></tr></table>"});
        for plain in [false, true] {
            let f = crate::identities::Form { name: "Ann B".into(), ..form_of(Some(&old), plain) };
            assert_eq!(update_patch(&old, &f, plain), json!({"name": "Ann B"}), "plain only: {plain}");
        }
        // switching the format is an edit: both fields go out
        let f = Form { html: false, ..form_of(Some(&old), false) };
        let p = update_patch(&old, &f, false);
        assert!(p.get("textSignature").is_some() && p["htmlSignature"] == "");
    }

    #[test]
    fn a_signature_left_as_it_is_is_not_measured_again() {
        // made in Bulwark: under the limit as stored, over it once the sanitiser has styled it
        let html: String = (0..190).map(|i| format!("<p>{}</p>", i % 10)).collect();
        assert!(html.len() < SIGNATURE_MAX_BYTES && clean_signature_html(&html).len() > SIGNATURE_MAX_BYTES, "{} / {}", html.len(), clean_signature_html(&html).len());
        let old = json!({"id": "i", "email": "a@example.test", "name": "Ann", "textSignature": "", "htmlSignature": html});
        // a name change is accepted, and the size shown is the stored one
        let f = Form { name: "Ann B".into(), ..form_of(Some(&old), false) };
        assert_eq!(validate(&f, Some(&old), false), None);
        assert_eq!(form_signature_bytes(&f, Some(&old), false), html.len());
        // an edited one is measured as it would be saved
        let f = Form { signature: format!("{html}<p>x</p>"), ..f };
        let e = validate(&f, Some(&old), false).unwrap();
        assert!(e.contains("too long") && e.contains("formatting adds"), "{e}");
        assert!(form_signature_bytes(&f, Some(&old), false) > SIGNATURE_MAX_BYTES);
        // the same for a new address
        let f = Form { email: "b@example.test".into(), signature: html.clone(), html: true, ..Default::default() };
        assert!(validate(&f, None, false).is_some());
    }

    #[test]
    fn a_missing_may_delete_is_not_deletable() {
        let a = json!({"id": "a", "email": "ann@example.test"});
        let b = json!({"id": "b", "email": "ann@example.test", "mayDelete": true});
        let c = json!({"id": "c", "email": "ann@example.test", "mayDelete": false});
        assert!(!may_delete(&a) && may_delete(&b) && !may_delete(&c));
        // it sorts with the ones that cannot be deleted, ahead of the aliases
        let v: Vec<String> = sort_identities(&[b.clone(), a.clone(), c.clone()], "ann@example.test").iter().map(|i| i["id"].as_str().unwrap().to_string()).collect();
        assert_eq!(v, ["a", "c", "b"]);
        assert!(summary(&a, false).contains("cannot be deleted"));
    }

    #[test]
    fn the_form_starts_from_the_identity() {
        let i = json!({"name": "Ann", "email": "a@example.test", "replyTo": [{"email": "r@example.test"}], "bcc": [{"email": "b@example.test"}], "textSignature": "T", "htmlSignature": "<p>H</p>"});
        let f = form_of(Some(&i), false);
        assert_eq!((f.reply_to.as_str(), f.bcc.as_str(), f.signature.as_str(), f.html), ("r@example.test", "b@example.test", "<p>H</p>", true));
        let f = form_of(Some(&i), true);
        assert_eq!((f.signature.as_str(), f.html), ("T", false));
        assert!(switch_format("Line one\nLine two", true).contains("Line two"));
        assert_eq!(switch_format("<p>Hi <b>there</b></p>", false), "Hi there");
    }

    #[test]
    fn validation_and_the_size_limit() {
        let ok = Form { email: "a@example.test".into(), ..Default::default() };
        assert_eq!(validate(&ok, None, false), None);
        let bad = Form { email: "nope".into(), bcc: "x".into(), ..Default::default() };
        let e = validate(&bad, None, false).unwrap();
        assert!(e.contains("valid email") && e.contains("Bcc"), "{e}");
        // editing: the fixed address is not checked
        assert_eq!(validate(&Form { email: "whatever".into(), ..Default::default() }, Some(&json!({"email": "whatever"})), false), None);
        let long = Form { email: "a@example.test".into(), signature: "é".repeat(1100), ..Default::default() };
        assert!(validate(&long, None, false).unwrap().contains("too long"));
        assert_eq!(size_note(10), None);
        assert!(size_note(1900).unwrap().starts_with("1900 of 2047"));
        assert!(size_note(2048).unwrap().starts_with("Too long"));
        assert_eq!(signature_bytes("<p>abc</p>", "abcdefghijklmnopqrstuvwxyz", true), 26);
    }

    #[test]
    fn identity_bcc_is_added_once_and_only_to_the_send() {
        let i = json!({"bcc": [{"email": "archive@example.test"}, {"email": "Boss@example.test"}, {"email": "archive@example.test"}]});
        assert_eq!(identity_bcc_extra("to@example.test", "", "", &i), ["archive@example.test", "Boss@example.test"]);
        // already a recipient: not again
        assert_eq!(identity_bcc_extra("", "boss@example.test", "x@example.test", &i), ["archive@example.test"]);
        assert!(identity_bcc_extra("", "", "x@example.test", &json!({"bcc": null})).is_empty());
    }

    #[test]
    fn set_answers_are_read_strictly() {
        let created = vec![json!(["Identity/set", {"created": {"new-identity": {"id": "I9"}}}, "0"])];
        assert_eq!(set_outcome(&created, "create", "new-identity"), Ok(Some("I9".into())));
        let refused = vec![json!(["Identity/set", {"notCreated": {"new-identity": {"type": "forbidden"}}}, "0"])];
        assert!(set_outcome(&refused, "create", "new-identity").unwrap_err().contains("not authorized"));
        let method = vec![json!(["error", {"type": "unknownMethod"}, "0"])];
        assert!(set_outcome(&method, "destroy", "I1").is_err());
        // no word of it being done is a failure, not a success
        assert!(set_outcome(&[json!(["Identity/set", {}, "0"])], "update", "I1").is_err());
        assert_eq!(set_outcome(&[json!(["Identity/set", {"destroyed": ["I1"]}, "0"])], "destroy", "I1"), Ok(None));
        assert!(set_outcome(&[json!(["Identity/set", {"notDestroyed": {"I1": {"type": "forbidden"}}}, "0"])], "destroy", "I1").unwrap_err().contains("cannot be deleted"));
    }

    #[test]
    fn sub_addresses() {
        assert_eq!(generate_sub_address("ann@example.test", "Shop_ping!", "+"), "ann+shopping@example.test");
        // an existing tag is replaced
        assert_eq!(generate_sub_address("ann+old@example.test", "new", "+"), "ann+new@example.test");
        assert_eq!(generate_sub_address("ann@example.test", "x", "-"), "ann-x@example.test");
        assert_eq!(generate_sub_address("ann@example.test", "!!", "+"), "ann@example.test");
        assert_eq!(generate_sub_address("ann", "x", "+"), "ann");
        assert!(valid_delimiter("+") && valid_delimiter("=") && !valid_delimiter("++") && !valid_delimiter("a") && !valid_delimiter(""));
        assert_eq!(suggest_tags("github.com"), ["github", "dev", "notifications"]);
        assert_eq!(suggest_tags("mail.shop.example"), ["shop", "newsletter", "registration"]);
        assert_eq!(suggestions_for(&["a@github.com".into(), "b@x.example".into()]), ["github", "dev", "notifications", "x", "newsletter"]);
        assert_eq!(tag_error(""), Some("Tag cannot be empty".into()));
        assert!(tag_error(&"a".repeat(31)).is_some());
        assert!(tag_error("a b").is_some());
        assert_eq!(tag_error("news-2026"), None);
        let v = note_tag(None, "a", Some("x.example"));
        let v = note_tag(Some(&v), "b", None);
        let v = note_tag(Some(&v), "a", Some("x.example"));
        assert_eq!(recent_tags(Some(&v)), ["a", "b"]);
        assert_eq!(v["tagSuggestions"]["x.example"], json!(["a"]));
    }

    #[test]
    fn the_list_summary() {
        let i = json!({"name": "Ann", "email": "a@example.test", "bcc": [{"email": "b@example.test"}], "textSignature": "Ann\nSales", "mayDelete": false});
        let s = summary(&i, true);
        assert_eq!(s, "a@example.test · Primary · Bcc b@example.test · Signature: Ann… · This identity cannot be deleted");
    }
}
