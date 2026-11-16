//! Incremental model updates (design/MOTION.md, "Lists").
//!
//! Replacing a list property with a fresh `ModelRc::new(VecModel::from(rows))` makes every row
//! of the view rebuild: the list flashes, hover and press states are lost and the whole list
//! repaints. These helpers instead turn the model that is already shown into the new rows with
//! the fewest row notifications: rows are matched by a key (the message or folder id), a row
//! whose content is unchanged is not touched at all, a changed row gets `set_row_data`, a new
//! one `insert`, a gone one `remove`. When most of the list is new (another folder, a new
//! search) a single reset is cheaper and is used instead.
//!
//! ```ignore
//! listsync::sync_into(&u.get_folders(), rows, |f| f.id.clone(), |a, b| a == b, |m| u.set_folders(m));
//! ```
use slint::{Model, ModelRc, VecModel};
use std::collections::HashSet;
use std::hash::Hash;

/// What one sync did to the model.
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub struct SyncStats {
    /// rows replaced in place (`set_row_data`)
    pub set: usize,
    pub inserted: usize,
    pub removed: usize,
    /// rows moved (a remove plus an insert each, counted here and not in inserted / removed)
    pub moved: usize,
    /// the whole model was replaced in one go (`set_vec`, or a new model installed)
    pub reset: bool,
}

impl SyncStats {
    /// Nothing was notified: the view does not repaint.
    #[allow(dead_code)] // part of the helper API (tests, logging)
    pub fn untouched(&self) -> bool { *self == SyncStats::default() }
}

/// Moves allowed before a reorder is treated as a new list (one reset instead of many moves).
const MAX_MOVES: usize = 8;

/// Make `model` hold `new`, matching rows by `key` and comparing them with `same`.
/// Keys must be unique in `new`; duplicates (or a list that is mostly new) fall back to one reset.
pub fn sync_by<T, K>(model: &VecModel<T>, new: Vec<T>, key: impl Fn(&T) -> K, same: impl Fn(&T, &T) -> bool) -> SyncStats
where
    T: Clone + 'static,
    K: Eq + Hash + Clone,
{
    let mut st = SyncStats::default();
    let new_keys: Vec<K> = new.iter().map(&key).collect();
    let wanted: HashSet<&K> = new_keys.iter().collect();
    let reset = |model: &VecModel<T>, new: Vec<T>| {
        model.set_vec(new);
        SyncStats { reset: true, ..SyncStats::default() }
    };
    if wanted.len() != new_keys.len() { return reset(model, new); }
    // the keys now shown, kept in step with every remove / insert below
    let mut cur: Vec<K> = model.iter().map(|r| key(&r)).collect();
    if cur.is_empty() && new.is_empty() { return st; }
    let kept = cur.iter().filter(|k| wanted.contains(k)).count();
    // mostly new content (another folder, a new search, the first fill, a clear): one reset is
    // cheaper than a notification per row, and nothing on screen is worth keeping anyway
    if kept * 2 < new.len().max(1) { return reset(model, new); }
    // mostly gone (a filter that keeps a few rows): one reset instead of a remove per row
    let gone = cur.len() - kept;
    if gone > MAX_MOVES && gone > kept { return reset(model, new); }
    // 1. drop the rows that are gone, from the end so indices stay valid
    for i in (0..cur.len()).rev() {
        if !wanted.contains(&cur[i]) {
            model.remove(i);
            cur.remove(i);
            st.removed += 1;
        }
    }
    // 2. walk the new rows: same key here -> update if changed; key further down -> move; else insert
    let present: HashSet<K> = cur.iter().cloned().collect();
    for (i, row) in new.iter().enumerate() {
        let k = &new_keys[i];
        if i < cur.len() && cur[i] == *k {
            if let Some(old) = model.row_data(i) {
                if !same(&old, row) {
                    model.set_row_data(i, row.clone());
                    st.set += 1;
                }
            }
            continue;
        }
        if present.contains(k) {
            let Some(j) = (i + 1..cur.len()).find(|&j| cur[j] == *k) else { return reset(model, new) };
            st.moved += 1;
            if st.moved > MAX_MOVES { return reset(model, new); }
            model.remove(j);
            cur.remove(j);
            model.insert(i, row.clone());
            cur.insert(i, k.clone());
        } else {
            model.insert(i, row.clone());
            cur.insert(i, k.clone());
            st.inserted += 1;
        }
    }
    // every kept key was matched above, so nothing should be left over; be safe anyway
    while cur.len() > new.len() {
        model.remove(cur.len() - 1);
        cur.pop();
        st.removed += 1;
    }
    st
}

/// `sync_by` for rows that compare with `==` (structs without nested models).
#[allow(dead_code)] // helper API for the lists still to convert (design/MOTION.md)
pub fn sync_model<T, K>(model: &VecModel<T>, new: Vec<T>, key: impl Fn(&T) -> K) -> SyncStats
where
    T: Clone + PartialEq + 'static,
    K: Eq + Hash + Clone,
{
    sync_by(model, new, key, |a, b| a == b)
}

/// Sync the model behind a list property in place. `current` is the property's value
/// (`u.get_mails()`); when it is not a `VecModel` yet (the default empty model) a new one is
/// installed through `install` (`|m| u.set_mails(m)`), once.
pub fn sync_into<T, K>(
    current: &ModelRc<T>,
    new: Vec<T>,
    key: impl Fn(&T) -> K,
    same: impl Fn(&T, &T) -> bool,
    install: impl FnOnce(ModelRc<T>),
) -> SyncStats
where
    T: Clone + 'static,
    K: Eq + Hash + Clone,
{
    match current.as_any().downcast_ref::<VecModel<T>>() {
        Some(vm) => sync_by(vm, new, key, same),
        None => {
            install(ModelRc::new(VecModel::from(new)));
            SyncStats { reset: true, ..SyncStats::default() }
        }
    }
}

/// Two nested models (a row's `[color]` or `[string]`) hold the same rows. Slint's own `==` on
/// `ModelRc` only compares identity, so rows rebuilt from data would always look changed.
pub fn rows_eq<T: PartialEq + Clone + 'static>(a: &ModelRc<T>, b: &ModelRc<T>) -> bool {
    a.row_count() == b.row_count() && a.iter().zip(b.iter()).all(|(x, y)| x == y)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Clone, Debug, PartialEq)]
    struct R { id: u32, v: &'static str }
    fn r(id: u32, v: &'static str) -> R { R { id, v } }
    fn rows(m: &VecModel<R>) -> Vec<R> { m.iter().collect() }
    fn sync(m: &VecModel<R>, new: Vec<R>) -> SyncStats { sync_model(m, new, |x| x.id) }

    #[test]
    fn unchanged_rows_are_not_touched() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c")]);
        let st = sync(&m, vec![r(1, "a"), r(2, "b"), r(3, "c")]);
        assert!(st.untouched(), "{st:?}");
    }

    #[test]
    fn changed_row_is_set_in_place() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c")]);
        let st = sync(&m, vec![r(1, "a"), r(2, "B"), r(3, "c")]);
        assert_eq!(st, SyncStats { set: 1, ..Default::default() });
        assert_eq!(rows(&m), vec![r(1, "a"), r(2, "B"), r(3, "c")]);
    }

    #[test]
    fn new_mail_on_top_is_one_insert() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c")]);
        let st = sync(&m, vec![r(9, "new"), r(1, "a"), r(2, "b"), r(3, "c")]);
        assert_eq!(st, SyncStats { inserted: 1, ..Default::default() });
        assert_eq!(rows(&m)[0], r(9, "new"));
    }

    #[test]
    fn deleted_row_is_one_remove() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c"), r(4, "d")]);
        let st = sync(&m, vec![r(1, "a"), r(3, "c"), r(4, "d")]);
        assert_eq!(st, SyncStats { removed: 1, ..Default::default() });
        assert_eq!(rows(&m), vec![r(1, "a"), r(3, "c"), r(4, "d")]);
    }

    #[test]
    fn load_more_appends() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b")]);
        let st = sync(&m, vec![r(1, "a"), r(2, "b"), r(3, "c"), r(4, "d")]);
        assert_eq!(st, SyncStats { inserted: 2, ..Default::default() });
        assert_eq!(rows(&m).len(), 4);
    }

    #[test]
    fn mixed_insert_remove_update_and_move() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c"), r(4, "d"), r(5, "e")]);
        // 2 deleted, 6 new on top, 4 moved above 3, 5 changed in place
        let new = vec![r(6, "f"), r(1, "a"), r(4, "d"), r(3, "c"), r(5, "E")];
        let st = sync(&m, new.clone());
        assert_eq!(rows(&m), new);
        assert_eq!(st.removed, 1);
        assert_eq!(st.inserted, 1);
        assert_eq!(st.moved, 1);
        assert_eq!(st.set, 1);
        assert!(!st.reset);
    }

    #[test]
    fn mostly_new_list_resets_once() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b"), r(3, "c")]);
        let new = vec![r(7, "x"), r(8, "y"), r(9, "z"), r(1, "a")];
        let st = sync(&m, new.clone());
        assert!(st.reset);
        assert_eq!(rows(&m), new);
    }

    #[test]
    fn big_reorder_resets() {
        let old: Vec<R> = (0..40).map(|i| r(i, "x")).collect();
        let m = VecModel::from(old.clone());
        let mut new = old;
        new.reverse();
        let st = sync(&m, new.clone());
        assert!(st.reset);
        assert_eq!(rows(&m), new);
    }

    #[test]
    fn filter_keeping_few_rows_resets() {
        let m = VecModel::from((0..40).map(|i| r(i, "x")).collect::<Vec<_>>());
        let new: Vec<R> = (0..5).map(|i| r(i * 8, "x")).collect();
        let st = sync(&m, new.clone());
        assert!(st.reset);
        assert_eq!(rows(&m), new);
    }

    #[test]
    fn duplicate_keys_reset() {
        let m = VecModel::from(vec![r(1, "a"), r(2, "b")]);
        let st = sync(&m, vec![r(1, "a"), r(1, "b")]);
        assert!(st.reset);
        assert_eq!(rows(&m), vec![r(1, "a"), r(1, "b")]);
    }

    #[test]
    fn empty_and_clear() {
        let m = VecModel::from(Vec::<R>::new());
        assert!(sync(&m, vec![]).untouched());
        let st = sync(&m, vec![r(1, "a")]);
        assert_eq!(rows(&m), vec![r(1, "a")]);
        assert!(st.reset);
        let st = sync(&m, vec![]);
        assert!(st.reset || st.removed == 1);
        assert!(rows(&m).is_empty());
    }

    #[test]
    fn sync_into_installs_once_then_syncs_in_place() {
        let mut installed: Option<ModelRc<R>> = None;
        let st = sync_into(&ModelRc::default(), vec![r(1, "a")], |x| x.id, |a, b| a == b, |m| installed = Some(m));
        assert!(st.reset);
        let cur = installed.expect("installed");
        let st = sync_into(&cur, vec![r(1, "a"), r(2, "b")], |x| x.id, |a, b| a == b, |_| panic!("must sync in place"));
        assert_eq!(st, SyncStats { inserted: 1, ..Default::default() });
        assert_eq!(cur.row_count(), 2);
    }

    #[test]
    fn nested_rows_compare_by_content() {
        let a: ModelRc<i32> = ModelRc::new(VecModel::from(vec![1, 2]));
        let b: ModelRc<i32> = ModelRc::new(VecModel::from(vec![1, 2]));
        let c: ModelRc<i32> = ModelRc::new(VecModel::from(vec![1, 3]));
        assert!(rows_eq(&a, &b));
        assert!(!rows_eq(&a, &c));
    }
}
