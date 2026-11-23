//! Where the sign-in secrets live (remembered sessions, the admin API key, the AI key).
//!
//! * One set per profile (`profile.rs`): the entry is `Barbican/<profile>/<name>`, so two
//!   profiles never share a secret.
//! * Windows: a generic credential written with `CRED_PERSIST_LOCAL_MACHINE` (C# port ADR 0008:
//!   refresh tokens must not roam with the Windows profile; the `keyring` crate can only write
//!   `CRED_PERSIST_ENTERPRISE`), the value as UTF-8. Linux: Secret Service through `keyring`.
//! * Upgrade from 0.1.0, which wrote `session.Barbican` and friends (service `Barbican`, user
//!   `<name>`) through `keyring`: the default profile adopts such an entry the first time it is
//!   read (copy, read back, then remove the old one). Other profiles never look at the old names.
//!   A downgrade to 0.1.0 after that signs in again.
use crate::profile;

/// The entry for a secret of a profile.
pub fn target(profile: &str, name: &str) -> String {
    format!("Barbican/{profile}/{name}")
}

/// A place secrets can be kept: the real store, or a fake in the tests.
pub trait Backend {
    fn read(&self, target: &str) -> Option<String>;
    fn write(&self, target: &str, value: &str) -> bool;
    fn remove(&self, target: &str);
    /// The 0.1.0 entry (service `Barbican`, user `name`).
    fn read_legacy(&self, name: &str) -> Option<String>;
    fn remove_legacy(&self, name: &str);
}

/// A secret of `profile`; the default profile adopts a 0.1.0 entry when it has none of its own.
pub fn load(b: &dyn Backend, profile: &str, name: &str) -> Option<String> {
    let t = target(profile, name);
    if let Some(v) = b.read(&t) { return Some(v); }
    if !profile::is_default(profile) { return None; }
    let Some(old) = b.read_legacy(name) else {
        // another instance may have migrated between the two reads
        return b.read(&t);
    };
    if b.write(&t, &old) && b.read(&t).as_deref() == Some(old.as_str()) {
        b.remove_legacy(name);
    }
    Some(old)
}

/// Store a secret; the default profile also drops a stale 0.1.0 copy. False when it could not
/// be written (the old copy is then kept).
pub fn save(b: &dyn Backend, profile: &str, name: &str, value: &str) -> bool {
    if !b.write(&target(profile, name), value) { return false; }
    if profile::is_default(profile) { b.remove_legacy(name); }
    true
}

/// Remove a secret (sign-out): the profile's own entry, and for the default profile the 0.1.0 one.
pub fn forget(b: &dyn Backend, profile: &str, name: &str) {
    b.remove(&target(profile, name));
    if profile::is_default(profile) { b.remove_legacy(name); }
}

// ---- the real store for this process ----

pub fn get(name: &str) -> Option<String> { load(&Os, profile::current(), name) }
pub fn set(name: &str, value: &str) -> bool { save(&Os, profile::current(), name, value) }
pub fn delete(name: &str) { forget(&Os, profile::current(), name) }

struct Os;

impl Backend for Os {
    fn read(&self, target: &str) -> Option<String> { os::read(target) }
    fn write(&self, target: &str, value: &str) -> bool { os::write(target, value) }
    fn remove(&self, target: &str) { os::remove(target) }
    fn read_legacy(&self, name: &str) -> Option<String> {
        keyring::Entry::new("Barbican", name).ok()?.get_password().ok()
    }
    fn remove_legacy(&self, name: &str) {
        if let Ok(e) = keyring::Entry::new("Barbican", name) { let _ = e.delete_credential(); }
    }
}

#[cfg(windows)]
mod os {
    use windows_sys::Win32::Security::Credentials::{
        CredDeleteW, CredFree, CredReadW, CredWriteW, CREDENTIALW, CRED_PERSIST_LOCAL_MACHINE, CRED_TYPE_GENERIC,
    };

    fn wide(s: &str) -> Vec<u16> { s.encode_utf16().chain(std::iter::once(0)).collect() }

    pub fn read(target: &str) -> Option<String> {
        let t = wide(target);
        let mut p: *mut CREDENTIALW = std::ptr::null_mut();
        if unsafe { CredReadW(t.as_ptr(), CRED_TYPE_GENERIC, 0, &mut p) } == 0 || p.is_null() { return None; }
        let out = unsafe {
            let c = &*p;
            let bytes = if c.CredentialBlob.is_null() { &[][..] } else { std::slice::from_raw_parts(c.CredentialBlob, c.CredentialBlobSize as usize) };
            String::from_utf8(bytes.to_vec()).ok()
        };
        unsafe { CredFree(p as *const _) };
        out
    }

    pub fn write(target: &str, value: &str) -> bool {
        let mut t = wide(target);
        // the entry's user name is its last path part (`session`, `accounts`, ...)
        let mut user = wide(target.rsplit('/').next().unwrap_or(target));
        let mut blob = value.as_bytes().to_vec();
        let cred = CREDENTIALW {
            Flags: 0,
            Type: CRED_TYPE_GENERIC,
            TargetName: t.as_mut_ptr(),
            Comment: std::ptr::null_mut(),
            LastWritten: unsafe { std::mem::zeroed() },
            CredentialBlobSize: blob.len() as u32,
            CredentialBlob: blob.as_mut_ptr(),
            Persist: CRED_PERSIST_LOCAL_MACHINE,
            AttributeCount: 0,
            Attributes: std::ptr::null_mut(),
            TargetAlias: std::ptr::null_mut(),
            UserName: user.as_mut_ptr(),
        };
        let ok = unsafe { CredWriteW(&cred, 0) } != 0;
        blob.iter_mut().for_each(|b| *b = 0);
        ok
    }

    pub fn remove(target: &str) {
        let t = wide(target);
        unsafe { CredDeleteW(t.as_ptr(), CRED_TYPE_GENERIC, 0) };
    }
}

#[cfg(not(windows))]
mod os {
    fn entry(target: &str) -> Option<keyring::Entry> { keyring::Entry::new("Barbican", target).ok() }
    pub fn read(target: &str) -> Option<String> { entry(target)?.get_password().ok() }
    pub fn write(target: &str, value: &str) -> bool { entry(target).is_some_and(|e| e.set_password(value).is_ok()) }
    pub fn remove(target: &str) { if let Some(e) = entry(target) { let _ = e.delete_credential(); } }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;
    use std::collections::HashMap;

    #[derive(Default)]
    struct Fake { new: RefCell<HashMap<String, String>>, old: RefCell<HashMap<String, String>>, fail_writes: bool }

    impl Backend for Fake {
        fn read(&self, t: &str) -> Option<String> { self.new.borrow().get(t).cloned() }
        fn write(&self, t: &str, v: &str) -> bool {
            if self.fail_writes { return false; }
            self.new.borrow_mut().insert(t.into(), v.into());
            true
        }
        fn remove(&self, t: &str) { self.new.borrow_mut().remove(t); }
        fn read_legacy(&self, n: &str) -> Option<String> { self.old.borrow().get(n).cloned() }
        fn remove_legacy(&self, n: &str) { self.old.borrow_mut().remove(n); }
    }

    #[test]
    fn entries_are_named_per_profile() {
        assert_eq!(target("default", "session"), "Barbican/default/session");
        assert_eq!(target("auto-8792", "accounts"), "Barbican/auto-8792/accounts");
        assert_ne!(target("dev", "session"), target("default", "session"));
        // the names of 0.1.0 (user `session` under service `Barbican`) are never produced
        assert!(target("default", "session").contains('/'));
    }

    #[test]
    fn two_profiles_never_share_a_secret() {
        let b = Fake::default();
        assert!(save(&b, "default", "session", "mine"));
        assert_eq!(load(&b, "dev", "session"), None);
        assert!(save(&b, "dev", "session", "other"));
        assert_eq!(load(&b, "default", "session").as_deref(), Some("mine"));
        assert_eq!(load(&b, "dev", "session").as_deref(), Some("other"));
        forget(&b, "dev", "session");
        assert_eq!(load(&b, "default", "session").as_deref(), Some("mine"));
    }

    #[test]
    fn default_profile_adopts_the_old_entry_without_losing_it() {
        let b = Fake::default();
        b.old.borrow_mut().insert("session".into(), "{\"kind\":\"password\"}".into());
        assert_eq!(load(&b, "default", "session").as_deref(), Some("{\"kind\":\"password\"}"));
        // copied, and the old one is gone
        assert_eq!(b.new.borrow().get("Barbican/default/session").map(String::as_str), Some("{\"kind\":\"password\"}"));
        assert!(b.old.borrow().is_empty());
        // and reads from now on come from the new entry
        assert_eq!(load(&b, "default", "session").as_deref(), Some("{\"kind\":\"password\"}"));
    }

    #[test]
    fn failed_copy_keeps_the_old_entry() {
        let b = Fake { fail_writes: true, ..Default::default() };
        b.old.borrow_mut().insert("accounts".into(), "[1]".into());
        // still readable, and still there for the next try
        assert_eq!(load(&b, "default", "accounts").as_deref(), Some("[1]"));
        assert_eq!(b.old.borrow().get("accounts").map(String::as_str), Some("[1]"));
        assert!(!save(&b, "default", "accounts", "[2]"));
        assert_eq!(b.old.borrow().get("accounts").map(String::as_str), Some("[1]"));
    }

    #[test]
    fn other_profiles_never_read_the_old_entries() {
        let b = Fake::default();
        b.old.borrow_mut().insert("session".into(), "mine".into());
        assert_eq!(load(&b, "dev", "session"), None);
        assert_eq!(load(&b, "auto-8792", "session"), None);
        // and a sign-out in another profile leaves them alone
        forget(&b, "dev", "session");
        assert!(b.old.borrow().contains_key("session"));
    }

    #[test]
    fn newer_entry_wins_over_the_old_one() {
        let b = Fake::default();
        b.old.borrow_mut().insert("session".into(), "old".into());
        assert!(save(&b, "default", "session", "new"));
        // saving drops the stale copy
        assert!(b.old.borrow().is_empty());
        assert_eq!(load(&b, "default", "session").as_deref(), Some("new"));
    }

    #[test]
    fn sign_out_of_the_default_profile_removes_both() {
        let b = Fake::default();
        b.old.borrow_mut().insert("session".into(), "old".into());
        assert!(save(&b, "default", "accounts", "[]"));
        b.old.borrow_mut().insert("accounts".into(), "stale".into());
        forget(&b, "default", "session");
        forget(&b, "default", "accounts");
        assert_eq!(load(&b, "default", "session"), None);
        assert_eq!(load(&b, "default", "accounts"), None);
        assert!(b.old.borrow().is_empty());
    }

    /// The real store, under a throwaway profile (never the user's entries).
    #[cfg(windows)]
    #[test]
    fn real_store_round_trip() {
        let p = format!("test-{}", std::process::id());
        let (t, v) = (target(&p, "unit"), "a\u{e9}\u{4e2d} secret");
        assert_eq!(os::read(&t), None);
        assert!(os::write(&t, v));
        assert_eq!(os::read(&t).as_deref(), Some(v));
        assert!(os::write(&t, "second"));
        assert_eq!(os::read(&t).as_deref(), Some("second"));
        os::remove(&t);
        assert_eq!(os::read(&t), None);
    }
}
