//! In-memory cache of opened (and prefetched) message bodies, so switching between messages
//! shows the next one at once instead of waiting for a round trip.
//!
//! Keyed by account (the client's `state_key`) and the listed message id. Bounded by entry
//! count and by bytes, least recently used out first. A message body never changes in JMAP,
//! only its keywords and mailboxes do: list syncs patch those in (`patch`), and drop what was
//! removed (`remove`). Conversations (the thread strip) are cached per thread id and dropped
//! whenever a sync touches that thread.
use serde_json::Value;
use std::collections::{HashMap, VecDeque};
use std::sync::{Arc, Mutex};

const MAX_ENTRIES: usize = 120;
const MAX_BYTES: usize = 48 * 1024 * 1024;
const MAX_THREADS: usize = 120;

type Key = (String, String);

pub struct BodyCache {
    max_entries: usize,
    max_bytes: usize,
    bodies: HashMap<Key, (Arc<Value>, usize)>,
    order: VecDeque<Key>,
    bytes: usize,
    threads: HashMap<Key, Arc<Vec<Value>>>,
    thread_order: VecDeque<Key>,
}

impl Default for BodyCache {
    fn default() -> Self { Self::new(MAX_ENTRIES, MAX_BYTES) }
}

/// Rough size of a message in memory (its body values dominate).
pub fn weight(m: &Value) -> usize {
    let mut n = 512;
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
        Self { max_entries, max_bytes, bodies: HashMap::new(), order: VecDeque::new(), bytes: 0, threads: HashMap::new(), thread_order: VecDeque::new() }
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

    pub fn remove(&mut self, account: &str, id: &str) {
        let k = (account.to_string(), id.to_string());
        if let Some((_, w)) = self.bodies.remove(&k) { self.bytes -= w; }
        if let Some(i) = self.order.iter().position(|x| *x == k) { self.order.remove(i); }
    }

    pub fn thread(&mut self, account: &str, thread: &str) -> Option<Arc<Vec<Value>>> {
        let k = (account.to_string(), thread.to_string());
        let v = self.threads.get(&k)?.clone();
        Self::touch(&mut self.thread_order, &k);
        Some(v)
    }

    pub fn put_thread(&mut self, account: &str, thread: &str, rows: Vec<Value>) -> Arc<Vec<Value>> {
        let k = (account.to_string(), thread.to_string());
        let v = Arc::new(rows);
        self.threads.insert(k.clone(), v.clone());
        Self::touch(&mut self.thread_order, &k);
        while self.threads.len() > MAX_THREADS {
            let Some(old) = self.thread_order.pop_front() else { break };
            self.threads.remove(&old);
        }
        v
    }

    pub fn drop_thread(&mut self, account: &str, thread: &str) {
        let k = (account.to_string(), thread.to_string());
        self.threads.remove(&k);
        if let Some(i) = self.thread_order.iter().position(|x| *x == k) { self.thread_order.remove(i); }
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
        *self = Self::new(self.max_entries, self.max_bytes);
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
    fn weight_counts_body_values() {
        assert!(weight(&msg("1", &"x".repeat(1000))) >= 1000);
    }
}
