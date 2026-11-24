//! In-memory cache of opened (and prefetched) message bodies, so switching between messages
//! shows the next one at once instead of waiting for a round trip.
//!
//! Keyed by account (the client's `state_key`) and the listed message id. Bounded by entry
//! count and by bytes, least recently used out first. A message body never changes in JMAP,
//! only its keywords and mailboxes do: list syncs patch those in (`patch`), and drop what was
//! removed (`remove`). Conversations (the thread strip) are cached per thread id and dropped
//! whenever a sync touches that thread. Local actions (read, flag, tag, move, delete) patch or
//! drop the cached copies too (`patch_local`, `remove_local`), in every account and thread.
//!
//! Signing out clears it; a fetch that started before that (`generation`) stores nothing.
use serde_json::Value;
use std::collections::{HashMap, VecDeque};
use std::sync::{Arc, Mutex};

const MAX_ENTRIES: usize = 120;
const MAX_BYTES: usize = 48 * 1024 * 1024;
const MAX_THREADS: usize = 120;
const MAX_THREAD_BYTES: usize = 8 * 1024 * 1024;

type Key = (String, String);

pub struct BodyCache {
    max_entries: usize,
    max_bytes: usize,
    bodies: HashMap<Key, (Arc<Value>, usize)>,
    order: VecDeque<Key>,
    bytes: usize,
    threads: HashMap<Key, (Arc<Vec<Value>>, usize)>,
    thread_order: VecDeque<Key>,
    thread_bytes: usize,
    generation: u64,
}

impl Default for BodyCache {
    fn default() -> Self { Self::new(MAX_ENTRIES, MAX_BYTES) }
}

/// Rough size of a conversation's rows (list entries: headers, preview, keywords).
pub fn thread_weight(rows: &[Value]) -> usize {
    rows.iter().map(|r| 640 + r["preview"].as_str().map_or(0, str::len) + r["subject"].as_str().map_or(0, str::len)).sum()
}

/// Rough size of a message in memory: its body values dominate, then headers and parts.
pub fn weight(m: &Value) -> usize {
    let mut n = 512;
    if let Some(h) = m["headers"].as_array() {
        n += h.iter().map(|x| 48 + x["name"].as_str().map_or(0, str::len) + x["value"].as_str().map_or(0, str::len)).sum::<usize>();
    }
    for k in ["attachments", "htmlBody", "textBody", "to", "cc", "bcc", "from", "replyTo"] {
        n += m[k].as_array().map_or(0, |a| a.len() * 256);
    }
    n += m["preview"].as_str().map_or(0, str::len) + m["subject"].as_str().map_or(0, str::len);
    if let Some(o) = m["bodyValues"].as_object() {
        n += o.values().map(|v| v["value"].as_str().map_or(0, str::len)).sum::<usize>();
    }
    if let Some(o) = m["__cidBodyValues"].as_object() {
        n += o.values().map(|v| v.as_str().map_or(0, str::len)).sum::<usize>();
    }
    n
}

impl BodyCache {
    pub fn new(max_entries: usize, max_bytes: usize) -> Self {
        Self { max_entries, max_bytes, bodies: HashMap::new(), order: VecDeque::new(), bytes: 0, threads: HashMap::new(), thread_order: VecDeque::new(), thread_bytes: 0, generation: 0 }
    }

    /// Changes whenever the cache is cleared (sign-out): fetches started before store nothing.
    pub fn generation(&self) -> u64 { self.generation }

    /// `put`, unless the cache was cleared since the fetch began (then only the value).
    pub fn put_gen(&mut self, generation: u64, account: &str, id: &str, m: Value) -> Arc<Value> {
        if generation != self.generation { return Arc::new(m); }
        self.put(account, id, m)
    }

    pub fn put_thread_gen(&mut self, generation: u64, account: &str, thread: &str, rows: Vec<Value>) -> Arc<Vec<Value>> {
        if generation != self.generation { return Arc::new(rows); }
        self.put_thread(account, thread, rows)
    }

    fn touch(order: &mut VecDeque<Key>, k: &Key) {
        if let Some(i) = order.iter().position(|x| x == k) { order.remove(i); }
        order.push_back(k.clone());
    }

    pub fn get(&mut self, account: &str, id: &str) -> Option<Arc<Value>> {
        let k = (account.to_string(), id.to_string());
        let v = self.bodies.get(&k)?.0.clone();
        Self::touch(&mut self.order, &k);
        Some(v)
    }

    pub fn contains(&self, account: &str, id: &str) -> bool {
        self.bodies.contains_key(&(account.to_string(), id.to_string()))
    }

    pub fn put(&mut self, account: &str, id: &str, m: Value) -> Arc<Value> {
        let k = (account.to_string(), id.to_string());
        let w = weight(&m);
        let v = Arc::new(m);
        if let Some((_, old)) = self.bodies.insert(k.clone(), (v.clone(), w)) { self.bytes -= old; }
        self.bytes += w;
        Self::touch(&mut self.order, &k);
        // evict the least recently used, but never the entry just added
        while (self.bodies.len() > self.max_entries || self.bytes > self.max_bytes) && self.order.len() > 1 {
            let Some(old) = self.order.pop_front() else { break };
            if let Some((_, w)) = self.bodies.remove(&old) { self.bytes -= w; }
        }
        v
    }

    /// A sync brought a fresh copy of a listed message: take over its keywords and mailboxes.
    pub fn patch(&mut self, account: &str, id: &str, fresh: &Value) {
        let k = (account.to_string(), id.to_string());
        let Some((v, _)) = self.bodies.get_mut(&k) else { return };
        let m = Arc::make_mut(v);
        for key in ["keywords", "mailboxIds"] {
            if !fresh[key].is_null() { m[key] = fresh[key].clone(); }
        }
    }

    /// The reader fetched the whole body of a cached message: keep it (and its fetch limit) so a
    /// reopen does not show the cut one again. Nothing is added for a message that is not cached.
    pub fn set_bodies(&mut self, account: &str, id: &str, full: &Value) {
        let k = (account.to_string(), id.to_string());
        let Some((v, old)) = self.bodies.get_mut(&k) else { return };
        let m = Arc::make_mut(v);
        for key in ["bodyValues", "__cidBodyValues", "__bodyLimit"] {
            if !full[key].is_null() { m[key] = full[key].clone(); }
        }
        let w = weight(m);
        self.bytes = self.bytes + w - *old;
        *old = w;
    }

    pub fn remove(&mut self, account: &str, id: &str) {
        let k = (account.to_string(), id.to_string());
        if let Some((_, w)) = self.bodies.remove(&k) { self.bytes -= w; }
        if let Some(i) = self.order.iter().position(|x| *x == k) { self.order.remove(i); }
    }

    pub fn thread(&mut self, account: &str, thread: &str) -> Option<Arc<Vec<Value>>> {
        let k = (account.to_string(), thread.to_string());
        let v = self.threads.get(&k)?.0.clone();
        Self::touch(&mut self.thread_order, &k);
        Some(v)
    }

    pub fn put_thread(&mut self, account: &str, thread: &str, rows: Vec<Value>) -> Arc<Vec<Value>> {
        let k = (account.to_string(), thread.to_string());
        let w = thread_weight(&rows);
        let v = Arc::new(rows);
        if let Some((_, old)) = self.threads.insert(k.clone(), (v.clone(), w)) { self.thread_bytes -= old; }
        self.thread_bytes += w;
        Self::touch(&mut self.thread_order, &k);
        while (self.threads.len() > MAX_THREADS || self.thread_bytes > MAX_THREAD_BYTES) && self.thread_order.len() > 1 {
            let Some(old) = self.thread_order.pop_front() else { break };
            if let Some((_, w)) = self.threads.remove(&old) { self.thread_bytes -= w; }
        }
        v
    }

    pub fn drop_thread(&mut self, account: &str, thread: &str) {
        let k = (account.to_string(), thread.to_string());
        if let Some((_, w)) = self.threads.remove(&k) { self.thread_bytes -= w; }
        if let Some(i) = self.thread_order.iter().position(|x| *x == k) { self.thread_order.remove(i); }
    }

    /// A local action changed messages (read, flag, tag): the cached bodies and the rows of
    /// cached conversations take the change too, in whichever account holds them. `ids` are
    /// list ids; `real` maps one to the server's id (conversation rows use those).
    pub fn patch_local(&mut self, ids: &[String], real: impl Fn(&str) -> String, f: &dyn Fn(&mut Value)) {
        for ((_, id), (v, _)) in self.bodies.iter_mut() {
            if ids.iter().any(|i| i == id) { f(Arc::make_mut(v)); }
        }
        let real: Vec<String> = ids.iter().map(|i| real(i)).collect();
        for (rows, _) in self.threads.values_mut() {
            if rows.iter().any(|r| r["id"].as_str().is_some_and(|x| real.iter().any(|i| i == x))) {
                for r in Arc::make_mut(rows).iter_mut() {
                    if r["id"].as_str().is_some_and(|x| real.iter().any(|i| i == x)) { f(r); }
                }
            }
        }
    }

    /// Messages left the list by a local action (moved, deleted): their bodies go, and so do
    /// the conversations they were in (re-read on the next open).
    pub fn remove_local(&mut self, ids: &[String], real: impl Fn(&str) -> String) {
        let keys: Vec<Key> = self.bodies.keys().filter(|(_, id)| ids.iter().any(|i| i == id)).cloned().collect();
        for (a, id) in keys { self.remove(&a, &id); }
        let real: Vec<String> = ids.iter().map(|i| real(i)).collect();
        let gone: Vec<Key> = self.threads.iter()
            .filter(|(_, (rows, _))| rows.iter().any(|r| r["id"].as_str().is_some_and(|x| real.iter().any(|i| i == x))))
            .map(|(k, _)| k.clone()).collect();
        for (a, t) in gone { self.drop_thread(&a, &t); }
    }

    /// Apply a list sync: fresh copies of added/updated messages, ids that left the list and
    /// threads that changed.
    pub fn on_sync<'a>(&mut self, account: &str, fresh: impl IntoIterator<Item = (&'a String, &'a Value)>, removed: &[String], threads: &[String]) {
        for (id, m) in fresh {
            self.patch(account, id, m);
            if let Some(t) = m["threadId"].as_str() { self.drop_thread(account, t); }
        }
        for id in removed { self.remove(account, id); }
        for t in threads { self.drop_thread(account, t); }
    }

    pub fn clear(&mut self) {
        let generation = self.generation + 1;
        *self = Self::new(self.max_entries, self.max_bytes);
        self.generation = generation;
    }

    pub fn len(&self) -> usize { self.bodies.len() }
    pub fn bytes(&self) -> usize { self.bytes }
}

pub static CACHE: Mutex<Option<BodyCache>> = Mutex::new(None);

/// Run `f` on the process-wide cache.
pub fn with<R>(f: impl FnOnce(&mut BodyCache) -> R) -> R {
    let mut g = CACHE.lock().unwrap();
    f(g.get_or_insert_with(BodyCache::default))
}

pub fn generation() -> u64 { with(|b| b.generation()) }

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn msg(id: &str, body: &str) -> Value {
        json!({"id": id, "threadId": format!("t{id}"), "keywords": {}, "bodyValues": {"1": {"value": body}}})
    }

    #[test]
    fn get_put_and_lru_by_count() {
        let mut c = BodyCache::new(2, usize::MAX);
        c.put("a", "1", msg("1", "x"));
        c.put("a", "2", msg("2", "y"));
        assert!(c.get("a", "1").is_some()); // 1 is now the most recent
        c.put("a", "3", msg("3", "z"));
        assert!(c.contains("a", "1") && c.contains("a", "3"));
        assert!(!c.contains("a", "2"), "least recently used goes first");
        assert_eq!(c.len(), 2);
    }

    #[test]
    fn whole_body_replaces_the_cut_one_and_keeps_the_byte_count_right() {
        let mut c = BodyCache::default();
        c.put("a", "1", json!({"id": "1", "keywords": {"$seen": true}, "__bodyLimit": 2_000_000,
            "bodyValues": {"1": {"value": "cut", "isTruncated": true}}}));
        let before = c.bytes();
        let whole = json!({"bodyValues": {"1": {"value": "cut and a very much longer rest of the message", "isTruncated": false}}, "__bodyLimit": 8_000_000});
        c.set_bodies("a", "1", &whole);
        let m = c.get("a", "1").unwrap();
        assert_eq!(m["bodyValues"]["1"]["isTruncated"], false);
        assert_eq!(m["__bodyLimit"], 8_000_000);
        assert_eq!(m["keywords"]["$seen"], true, "what the full body does not carry is kept");
        assert!(c.bytes() > before);
        assert_eq!(c.bytes(), weight(&m));
        // a message that is not cached is not added
        c.set_bodies("a", "2", &whole);
        assert!(!c.contains("a", "2"));
    }

    #[test]
    fn per_account() {
        let mut c = BodyCache::default();
        c.put("a", "1", msg("1", "x"));
        assert!(c.get("b", "1").is_none());
        assert!(c.get("a", "1").is_some());
    }

    #[test]
    fn bounded_by_bytes_keeps_newest() {
        let big = "x".repeat(10_000);
        let mut c = BodyCache::new(100, 25_000);
        c.put("a", "1", msg("1", &big));
        c.put("a", "2", msg("2", &big));
        c.put("a", "3", msg("3", &big));
        assert!(c.bytes() <= 25_000);
        assert!(c.contains("a", "3") && !c.contains("a", "1"));
        // one entry larger than the budget is still kept (it is the one on screen)
        c.put("a", "4", msg("4", &"y".repeat(40_000)));
        assert!(c.contains("a", "4"));
        assert_eq!(c.len(), 1);
    }

    #[test]
    fn replacing_an_entry_keeps_the_byte_count() {
        let mut c = BodyCache::default();
        c.put("a", "1", msg("1", "aaaa"));
        let b = c.bytes();
        c.put("a", "1", msg("1", "bbbb"));
        assert_eq!(c.bytes(), b);
        c.remove("a", "1");
        assert_eq!(c.bytes(), 0);
        assert_eq!(c.len(), 0);
    }

    #[test]
    fn sync_patches_keywords_and_drops_removed_and_threads() {
        let mut c = BodyCache::default();
        c.put("a", "1", msg("1", "x"));
        c.put("a", "2", msg("2", "y"));
        c.put_thread("a", "t1", vec![json!({"id": "1"})]);
        c.put_thread("a", "t9", vec![json!({"id": "9"})]);
        let fresh = HashMap::from([("1".to_string(), json!({"id": "1", "threadId": "t1", "keywords": {"$seen": true}, "mailboxIds": {"m2": true}}))]);
        c.on_sync("a", &fresh, &["2".to_string()], &["t9".to_string()]);
        let m = c.get("a", "1").unwrap();
        assert_eq!(m["keywords"]["$seen"], true);
        assert_eq!(m["mailboxIds"]["m2"], true);
        assert_eq!(m["bodyValues"]["1"]["value"], "x", "the body stays");
        assert!(!c.contains("a", "2"));
        assert!(c.thread("a", "t1").is_none(), "a changed message's thread is re-read");
        assert!(c.thread("a", "t9").is_none());
    }

    #[test]
    fn local_actions_reach_bodies_and_conversations() {
        let mut c = BodyCache::default();
        c.put("a", "u:1", msg("1", "x"));
        c.put("b", "2", msg("2", "y"));
        c.put_thread("a", "t1", vec![json!({"id": "1", "keywords": {}}), json!({"id": "3", "keywords": {}})]);
        let real = |i: &str| i.trim_start_matches("u:").to_string();
        c.patch_local(&["u:1".to_string()], real, &|m: &mut Value| m["keywords"]["$seen"] = json!(true));
        assert_eq!(c.get("a", "u:1").unwrap()["keywords"]["$seen"], true);
        let t = c.thread("a", "t1").unwrap();
        assert_eq!(t[0]["keywords"]["$seen"], true, "the strip's row follows");
        assert!(t[1]["keywords"]["$seen"].is_null());
        c.remove_local(&["3".to_string()], real);
        assert!(c.thread("a", "t1").is_none(), "a conversation that lost a message is re-read");
        assert!(c.contains("a", "u:1") && c.contains("b", "2"));
        c.remove_local(&["2".to_string()], real);
        assert!(!c.contains("b", "2"));
    }

    #[test]
    fn a_fetch_from_before_a_clear_is_not_stored() {
        let mut c = BodyCache::default();
        let g = c.generation();
        c.clear();
        c.put_gen(g, "a", "1", msg("1", "x"));
        c.put_thread_gen(g, "a", "t1", vec![json!({"id": "1"})]);
        assert!(!c.contains("a", "1"));
        assert!(c.thread("a", "t1").is_none());
        c.put_gen(c.generation(), "a", "1", msg("1", "x"));
        assert!(c.contains("a", "1"));
    }

    #[test]
    fn conversations_are_bounded_by_bytes() {
        let mut c = BodyCache::default();
        let row = json!({"id": "1", "preview": "p".repeat(200_000)});
        for t in 0..60 { c.put_thread("a", &format!("t{t}"), vec![row.clone(); 1]); }
        assert!(c.thread_bytes <= MAX_THREAD_BYTES);
        assert!(c.thread("a", "t59").is_some() && c.thread("a", "t0").is_none());
    }

    #[test]
    fn weight_counts_headers() {
        let mut m = msg("1", "x");
        let w = weight(&m);
        m["headers"] = json!([{"name": "Received", "value": "r".repeat(4000)}]);
        assert!(weight(&m) >= w + 4000);
    }

    #[test]
    fn weight_counts_body_values() {
        assert!(weight(&msg("1", &"x".repeat(1000))) >= 1000);
    }
}
