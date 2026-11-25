//! Plugin packages (PLUGINS.md "Consent and storage", ROADMAP Phase 5 B2): a folder or a ZIP holding `manifest.json`
//! (API 2) and one library per platform, checked and unpacked into `plugins\<id>\`. Nothing is loaded: an installed
//! plugin is listed as off and runs only after the consent dialog.
//!
//! Checks, before anything is written: the archive size, the number of entries, each entry's size and the total
//! unpacked size ([`Limits`]); every path (no absolute path, no `..`, no drive letter or `:`, no Windows device name,
//! no trailing dot or space, no two names that differ only in case, no file that is also a folder); no symbolic link
//! or junction (a Unix symlink in the ZIP's file attributes, or a link among the folder's own entries; other reparse
//! points, such as OneDrive placeholders, are ordinary files); no encrypted, ZIP64 or multi-disk
//! archive; only stored and deflated entries, whose CRC must match. The manifest is validated with
//! [`crate::plugins::validate_manifest`] and must name a library for this platform that the package contains.
//! Bulwark's own package checks are `lib/extension-package.ts:31-90` (200 entries, 20 MB per entry).
use crate::plugins;
use std::path::{Path, PathBuf};

/// Size and count caps for one package.
#[derive(Clone, Copy, Debug)]
pub struct Limits {
    /// Files and folders in the package.
    pub max_entries: usize,
    /// One unpacked file.
    pub max_entry_bytes: u64,
    /// All unpacked files together.
    pub max_total_bytes: u64,
    /// The ZIP file itself.
    pub max_archive_bytes: u64,
}

/// The caps used for installs: Bulwark's 200 entries and 20 MB per entry, and 32 MB in all. Bulwark caps a plugin at
/// 5 MB (`lib/plugin-types.ts:1742`), but that is a JavaScript bundle; a native library for two platforms is larger.
pub const LIMITS: Limits = Limits { max_entries: 200, max_entry_bytes: 20 * 1024 * 1024, max_total_bytes: 32 * 1024 * 1024,
    max_archive_bytes: 32 * 1024 * 1024 };

/// One file of a package: its path inside the package (`/`-separated, checked) and its bytes.
#[derive(Debug)]
pub struct PackageFile {
    pub path: String,
    pub data: Vec<u8>,
}

/// What an install did.
#[derive(Debug, PartialEq)]
pub struct Installed {
    pub id: String,
    pub name: String,
    pub version: String,
    /// A package with this id was already installed and has been replaced.
    pub updated: bool,
}

/// Check one path inside a package and split it into its parts. `\` counts as a separator (a Windows tool may write
/// it), so `..\x` is caught like `../x`.
pub fn check_path(raw: &str) -> Result<Vec<String>, String> {
    let bad = |why: &str| Err(format!("\"{}\": {why}", raw.escape_debug()));
    if raw.is_empty() { return bad("empty name"); }
    if raw.chars().any(char::is_control) { return bad("control characters in the name"); }
    let name = raw.replace('\\', "/");
    if name.starts_with('/') { return bad("absolute path"); }
    let b = name.as_bytes();
    if b.len() >= 2 && b[0].is_ascii_alphabetic() && b[1] == b':' { return bad("drive letter"); }
    if name.contains(':') { return bad("':' is not allowed (drive letter or alternate data stream)"); }
    if name.len() > 240 { return bad("path too long"); }
    let trimmed = name.strip_suffix('/').unwrap_or(&name);
    let mut parts = vec![];
    for part in trimmed.split('/') {
        if part.is_empty() { return bad("empty folder name"); }
        if part == ".." { return bad("'..' leaves the package"); }
        if part == "." { return bad("'.' is not allowed"); }
        if part.ends_with('.') || part.ends_with(' ') { return bad("a name may not end in a dot or a space"); }
        if part.contains(['<', '>', '"', '|', '?', '*']) { return bad("characters Windows does not allow in names"); }
        if plugins::is_reserved_windows_name(part) { return bad("a Windows device name"); }
        parts.push(part.to_string());
    }
    Ok(parts)
}

/// No two entries that differ only in case, and no file that is also used as a folder.
fn check_names(entries: &[(Vec<String>, bool)]) -> Result<(), String> {
    use std::collections::HashSet;
    let mut seen: HashSet<String> = HashSet::new();
    let mut folders: HashSet<String> = HashSet::new();
    let mut files: Vec<String> = vec![];
    for (parts, is_dir) in entries {
        let key = parts.join("/").to_lowercase();
        if !seen.insert(key.clone()) && !*is_dir {
            return Err(format!("\"{}\": two entries with this name (names may not differ only in case)", parts.join("/")));
        }
        for i in 1..parts.len() { folders.insert(parts[..i].join("/").to_lowercase()); }
        if *is_dir { folders.insert(key); } else { files.push(key); }
    }
    // a folder entry may repeat (some tools write one per file), a file may not be a folder too
    let mut file_set = HashSet::new();
    for f in &files {
        if !file_set.insert(f) { return Err(format!("\"{f}\": two entries with this name (names may not differ only in case)")); }
        if folders.contains(f) { return Err(format!("\"{f}\" is both a file and a folder")); }
    }
    Ok(())
}

fn u16_at(b: &[u8], i: usize) -> Result<u16, String> {
    b.get(i..i + 2).map(|x| u16::from_le_bytes([x[0], x[1]])).ok_or_else(|| "the ZIP file is cut short".to_string())
}
fn u32_at(b: &[u8], i: usize) -> Result<u32, String> {
    b.get(i..i + 4).map(|x| u32::from_le_bytes([x[0], x[1], x[2], x[3]])).ok_or_else(|| "the ZIP file is cut short".to_string())
}

/// Unix file type bits in a ZIP entry's external attributes (made on a Unix host): a symbolic link.
const S_IFMT: u32 = 0o170000;
const S_IFLNK: u32 = 0o120000;

/// Read and check a ZIP package in memory. Returns its files (folders are checked, not returned).
pub fn read_zip(bytes: &[u8], limits: Limits) -> Result<Vec<PackageFile>, String> {
    if bytes.len() as u64 > limits.max_archive_bytes { return Err(format!("the package file is larger than {} MB", limits.max_archive_bytes >> 20)); }
    // end of central directory: the last signature within the final 64 KiB + 22 bytes
    let floor = bytes.len().saturating_sub(22 + 0xFFFF);
    let eocd = (floor..bytes.len().saturating_sub(21)).rev().find(|&i| bytes[i..i + 4] == [0x50, 0x4b, 0x05, 0x06])
        .ok_or("not a ZIP file")?;
    let (disk, cd_disk) = (u16_at(bytes, eocd + 4)?, u16_at(bytes, eocd + 6)?);
    let (on_disk, count) = (u16_at(bytes, eocd + 8)?, u16_at(bytes, eocd + 10)?);
    let (cd_size, cd_off) = (u32_at(bytes, eocd + 12)? as usize, u32_at(bytes, eocd + 16)? as usize);
    if disk != 0 || cd_disk != 0 || on_disk != count { return Err("split (multi-disk) ZIP files are not supported".into()); }
    if count == 0xFFFF || cd_off == 0xFFFF_FFFF || cd_size == 0xFFFF_FFFF { return Err("ZIP64 archives are not supported".into()); }
    if count as usize > limits.max_entries { return Err(format!("the package has {count} entries; at most {} are allowed", limits.max_entries)); }
    if cd_off.checked_add(cd_size).is_none_or(|end| end > eocd) { return Err("the ZIP directory is damaged".into()); }

    struct Raw { name: String, method: u16, crc: u32, clen: u64, ulen: u64, offset: usize, is_dir: bool }
    let mut raws = vec![];
    let mut names = vec![];
    let mut total: u64 = 0;
    let mut at = cd_off;
    for _ in 0..count {
        if u32_at(bytes, at)? != 0x0201_4b50 { return Err("the ZIP directory is damaged".into()); }
        let made_by = u16_at(bytes, at + 4)?;
        let flags = u16_at(bytes, at + 8)?;
        let method = u16_at(bytes, at + 10)?;
        let crc = u32_at(bytes, at + 16)?;
        let (clen, ulen) = (u32_at(bytes, at + 20)?, u32_at(bytes, at + 24)?);
        let (n, e, c) = (u16_at(bytes, at + 28)? as usize, u16_at(bytes, at + 30)? as usize, u16_at(bytes, at + 32)? as usize);
        let ext_attr = u32_at(bytes, at + 38)?;
        let offset = u32_at(bytes, at + 42)? as usize;
        let raw_name = bytes.get(at + 46..at + 46 + n).ok_or("the ZIP directory is damaged")?;
        at += 46 + n + e + c;
        let name = std::str::from_utf8(raw_name).map_err(|_| "a name in the package is not UTF-8".to_string())?.to_string();
        let parts = check_path(&name)?;
        if flags & 1 != 0 { return Err(format!("\"{name}\": encrypted entries are not supported")); }
        let unix = made_by >> 8 == 3;
        // the unpacker never creates links, so only a Unix symlink entry needs refusing
        if unix && (ext_attr >> 16) & S_IFMT == S_IFLNK {
            return Err(format!("\"{name}\": symbolic links are not allowed"));
        }
        if clen == 0xFFFF_FFFF || ulen == 0xFFFF_FFFF { return Err("ZIP64 archives are not supported".into()); }
        let is_dir = name.ends_with('/') || name.ends_with('\\');
        if is_dir && ulen != 0 { return Err(format!("\"{name}\": a folder entry with data")); }
        if !matches!(method, 0 | 8) { return Err(format!("\"{name}\": compression method {method} is not supported (only stored and deflate)")); }
        if ulen as u64 > limits.max_entry_bytes { return Err(format!("\"{name}\" is larger than {} MB", limits.max_entry_bytes >> 20)); }
        total += ulen as u64;
        if total > limits.max_total_bytes { return Err(format!("the package unpacks to more than {} MB", limits.max_total_bytes >> 20)); }
        names.push((parts, is_dir));
        raws.push(Raw { name, method, crc, clen: clen as u64, ulen: ulen as u64, offset, is_dir });
    }
    check_names(&names)?;

    let mut out = vec![];
    for r in raws.into_iter().filter(|r| !r.is_dir) {
        if u32_at(bytes, r.offset)? != 0x0403_4b50 { return Err(format!("\"{}\": damaged entry", r.name)); }
        let start = r.offset + 30 + u16_at(bytes, r.offset + 26)? as usize + u16_at(bytes, r.offset + 28)? as usize;
        let data = bytes.get(start..start + r.clen as usize).ok_or_else(|| format!("\"{}\": damaged entry", r.name))?;
        let data = if r.method == 0 {
            if r.clen != r.ulen { return Err(format!("\"{}\": damaged entry", r.name)); }
            data.to_vec()
        } else {
            use std::io::Read;
            // never trust the declared size: read at most one byte more than it says
            let mut out = Vec::with_capacity(r.ulen as usize);
            flate2::read::DeflateDecoder::new(data).take(r.ulen + 1).read_to_end(&mut out)
                .map_err(|e| format!("\"{}\": {e}", r.name))?;
            if out.len() as u64 != r.ulen { return Err(format!("\"{}\": the unpacked size does not match", r.name)); }
            out
        };
        if crc32fast::hash(&data) != r.crc { return Err(format!("\"{}\": the checksum does not match", r.name)); }
        out.push(PackageFile { path: check_path(&r.name)?.join("/"), data });
    }
    Ok(out)
}

/// Is this entry a symbolic link or junction (never followed)? Rust reports both on Windows; other reparse points
/// (OneDrive placeholders, compressed files) are plain files.
fn is_link(md: &std::fs::Metadata) -> bool {
    md.file_type().is_symlink()
}

/// Read and check a package folder (the same caps and path rules as a ZIP; links are refused, not followed).
pub fn read_folder(root: &Path, limits: Limits) -> Result<Vec<PackageFile>, String> {
    let md = std::fs::symlink_metadata(root).map_err(|e| format!("cannot read the folder: {e}"))?;
    if is_link(&md) { return Err("the folder is a symbolic link or junction".into()); }
    if !md.is_dir() { return Err("not a folder".into()); }
    let (mut out, mut names, mut total) = (vec![], vec![], 0u64);
    let mut stack: Vec<(PathBuf, String, usize)> = vec![(root.to_path_buf(), String::new(), 0)];
    while let Some((dir, rel, depth)) = stack.pop() {
        if depth > 16 { return Err(format!("\"{rel}\": folders nested too deep")); }
        let mut entries: Vec<_> = std::fs::read_dir(&dir).map_err(|e| format!("cannot read {}: {e}", dir.display()))?.flatten().collect();
        entries.sort_by_key(|e| e.file_name());
        for e in entries {
            let name = e.file_name().into_string().map_err(|_| "a name in the folder is not valid Unicode".to_string())?;
            let path = if rel.is_empty() { name.clone() } else { format!("{rel}/{name}") };
            let parts = check_path(&path)?;
            let md = std::fs::symlink_metadata(e.path()).map_err(|err| format!("\"{path}\": {err}"))?;
            if is_link(&md) { return Err(format!("\"{path}\": symbolic links and junctions are not allowed")); }
            if names.len() >= limits.max_entries { return Err(format!("the package has more than {} entries", limits.max_entries)); }
            if md.is_dir() {
                names.push((parts, true));
                stack.push((e.path(), path, depth + 1));
            } else if md.is_file() {
                if md.len() > limits.max_entry_bytes { return Err(format!("\"{path}\" is larger than {} MB", limits.max_entry_bytes >> 20)); }
                total += md.len();
                if total > limits.max_total_bytes { return Err(format!("the package is larger than {} MB", limits.max_total_bytes >> 20)); }
                let data = std::fs::read(e.path()).map_err(|err| format!("\"{path}\": {err}"))?;
                if data.len() as u64 != md.len() { return Err(format!("\"{path}\" changed while it was read")); }
                names.push((parts.clone(), false));
                out.push(PackageFile { path: parts.join("/"), data });
            } else {
                return Err(format!("\"{path}\": not a plain file"));
            }
        }
    }
    check_names(&names)?;
    Ok(out)
}

/// A package may hold its files at the top or inside one folder (a zipped folder): find `manifest.json` and make the
/// paths relative to it.
fn rooted(files: Vec<PackageFile>) -> Result<Vec<PackageFile>, String> {
    if files.iter().any(|f| f.path == "manifest.json") { return Ok(files); }
    let top = files.first().and_then(|f| f.path.split_once('/')).map(|(t, _)| t.to_string())
        .ok_or("the package has no manifest.json at the top")?;
    let prefix = format!("{top}/");
    if !files.iter().all(|f| f.path.starts_with(&prefix)) || !files.iter().any(|f| f.path == format!("{prefix}manifest.json")) {
        return Err("the package has no manifest.json at the top".into());
    }
    Ok(files.into_iter().map(|f| PackageFile { path: f.path[prefix.len()..].to_string(), data: f.data }).collect())
}

fn random_tag() -> String {
    let mut b = [0u8; 6];
    let _ = getrandom::getrandom(&mut b);
    b.iter().map(|x| format!("{x:02x}")).collect()
}

/// A checked package, ready to be written: nothing is on disk yet.
pub struct Prepared {
    files: Vec<PackageFile>,
    pub id: String,
    pub name: String,
    pub version: String,
    pub author: String,
}

impl Prepared {
    /// The installed package this one would replace, as `(name, author)`; `None` for a first install.
    pub fn replaces(&self, plugins_dir: &Path) -> Option<(String, String)> {
        plugins::installed_identity(&plugins_dir.join(&self.id)).filter(|(id, _, _)| *id == self.id).map(|(_, n, a)| (n, a))
    }
}

/// Install a package (a `.zip` file or a folder) into `plugins_dir\<id>\`. A package with the same id is replaced.
pub fn install(src: &Path, plugins_dir: &Path) -> Result<Installed, String> {
    install_with(src, plugins_dir, LIMITS)
}

pub fn install_with(src: &Path, plugins_dir: &Path, limits: Limits) -> Result<Installed, String> {
    commit(prepare(src, limits)?, plugins_dir)
}

/// Read and check a package (a `.zip` file or a folder) and its manifest; writes nothing.
pub fn prepare(src: &Path, limits: Limits) -> Result<Prepared, String> {
    let files = if src.is_dir() {
        read_folder(src, limits)?
    } else {
        let len = std::fs::metadata(src).map_err(|e| format!("cannot read the file: {e}"))?.len();
        if len > limits.max_archive_bytes { return Err(format!("the package file is larger than {} MB", limits.max_archive_bytes >> 20)); }
        read_zip(&std::fs::read(src).map_err(|e| format!("cannot read the file: {e}"))?, limits)?
    };
    prepare_files(files)
}

pub fn prepare_files(files: Vec<PackageFile>) -> Result<Prepared, String> {
    let files = rooted(files)?;
    let mf = files.iter().find(|f| f.path == "manifest.json").ok_or("the package has no manifest.json")?;
    if mf.data.len() as u64 > plugins::MAX_MANIFEST_BYTES { return Err("manifest.json is too large".into()); }
    let v: serde_json::Value = serde_json::from_slice(&mf.data).map_err(|e| format!("bad manifest.json: {e}"))?;
    let m = plugins::validate_manifest(&v)?;
    let lib = m.library_here().ok_or_else(|| format!("this plugin has no library for {}", plugins::platform_key()))?;
    if !files.iter().any(|f| f.path == lib) { return Err(format!("the package does not contain its library {lib}")); }
    Ok(Prepared { files, id: m.id, name: m.name, version: m.version, author: m.author })
}

pub fn install_files(files: Vec<PackageFile>, plugins_dir: &Path) -> Result<Installed, String> {
    commit(prepare_files(files)?, plugins_dir)
}

/// Held around every change to the plugins folder ([`commit`], [`uninstall`], [`sweep_leftovers`]): a sweep must never
/// run while another install has its staging folder or its `.old-*` backup in the folder.
static FOLDER_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

fn folder_lock() -> std::sync::MutexGuard<'static, ()> {
    FOLDER_LOCK.lock().unwrap_or_else(|e| e.into_inner())
}

/// Remove staging and backup folders a crashed or interrupted install left in the plugins folder.
pub fn sweep_leftovers(plugins_dir: &Path) {
    let _held = folder_lock();
    sweep_locked(plugins_dir);
}

/// The id of a backup folder name, `.old-<id>-<tag>`.
fn backup_id(name: &str) -> Option<&str> {
    name.strip_prefix(".old-")?.rsplit_once('-').map(|(id, _)| id)
}

/// A staging folder is always removed, and so is a backup (`.old-<id>-<tag>`) while `<id>` exists. When `<id>` is
/// missing, an update stopped between its two renames: the newest backup that holds a readable manifest of that id is
/// the installed version and is put back, and the other backups of that id are removed (all of them are kept for the
/// next start if putting it back fails).
fn sweep_locked(plugins_dir: &Path) {
    let Ok(rd) = std::fs::read_dir(plugins_dir) else { return };
    let entries: Vec<(String, PathBuf)> = rd.flatten().map(|e| (e.file_name().to_string_lossy().to_string(), e.path())).collect();
    let mut orphans: std::collections::BTreeMap<String, Vec<(std::time::SystemTime, PathBuf)>> = Default::default();
    for (name, path) in entries {
        if !(name.starts_with(".install-") || name.starts_with(".old-")) { continue; }
        let Ok(md) = std::fs::symlink_metadata(&path) else { continue };
        if is_link(&md) { let _ = std::fs::remove_dir(&path).or_else(|_| std::fs::remove_file(&path)); continue; }
        if !md.is_dir() { continue; }
        if let Some(id) = backup_id(&name).filter(|id| plugins::valid_id(id)) {
            if std::fs::symlink_metadata(plugins_dir.join(id)).is_err() {
                orphans.entry(id.to_string()).or_default().push((md.modified().unwrap_or(std::time::UNIX_EPOCH), path));
                continue;
            }
        }
        let _ = std::fs::remove_dir_all(&path);
    }
    for (id, mut backups) in orphans {
        backups.sort_by(|a, b| b.0.cmp(&a.0));
        let keep = backups.iter().position(|(_, p)| plugins::installed_identity(p).is_some_and(|(i, _, _)| i == id));
        if let Some(i) = keep {
            if std::fs::rename(&backups[i].1, plugins_dir.join(&id)).is_err() { continue; }
        }
        for (k, (_, p)) in backups.iter().enumerate() {
            if Some(k) != keep { let _ = std::fs::remove_dir_all(p); }
        }
    }
}

/// `rename`, tried again a few times with a short wait (50, 100, 200 and 400 ms): an antivirus scanner or the search
/// indexer can hold a file of the folder open for a moment, which Windows reports as a sharing violation.
fn rename_retry(from: &Path, to: &Path) -> std::io::Result<()> {
    let mut wait = 50;
    loop {
        match std::fs::rename(from, to) {
            Ok(()) => return Ok(()),
            Err(e) if wait > 400 || matches!(e.kind(), std::io::ErrorKind::NotFound | std::io::ErrorKind::AlreadyExists) => return Err(e),
            Err(_) => { std::thread::sleep(std::time::Duration::from_millis(wait)); wait *= 2; }
        }
    }
}

/// Unpack a checked package: first into a hidden staging folder, then moved into place in one rename. An existing
/// `plugins_dir\<id>\` is replaced only when it is a real folder holding a package with the same id; anything else
/// of that name (a dropped-in file, a link, another plugin's folder) is left alone and the install is refused.
pub fn commit(pkg: Prepared, plugins_dir: &Path) -> Result<Installed, String> {
    let Prepared { files, id, name, version, .. } = pkg;
    let _held = folder_lock();
    std::fs::create_dir_all(plugins_dir).map_err(|e| format!("cannot create the plugins folder: {e}"))?;
    // leftovers are swept only at start (and only by the first Barbican of the profile): another Barbican's install
    // may be running now, and the lock is per process
    let target = plugins_dir.join(&id);
    let updated = std::fs::symlink_metadata(&target).is_ok();
    if updated && !plugins::installed_identity(&target).is_some_and(|(old, _, _)| old == id) {
        return Err(format!("\"{id}\" in the plugins folder is not an installed package with this id, so it is not replaced"));
    }
    if plugins::dropped_in_ids(plugins_dir).contains(&id) {
        return Err(format!("a plugin dropped into the plugins folder already uses the id \"{id}\": remove that plugin first"));
    }
    let staging = plugins_dir.join(format!(".install-{id}-{}", random_tag()));
    let written = (|| -> Result<(), String> {
        std::fs::create_dir(&staging).map_err(|e| e.to_string())?;
        for f in &files {
            let path = f.path.split('/').fold(staging.clone(), |p, part| p.join(part));
            if let Some(parent) = path.parent() { std::fs::create_dir_all(parent).map_err(|e| e.to_string())?; }
            use std::io::Write;
            let mut out = std::fs::OpenOptions::new().write(true).create_new(true).open(&path).map_err(|e| format!("{}: {e}", f.path))?;
            out.write_all(&f.data).and_then(|_| out.sync_all()).map_err(|e| format!("{}: {e}", f.path))?;
        }
        Ok(())
    })();
    if let Err(e) = written {
        let _ = std::fs::remove_dir_all(&staging);
        return Err(format!("could not unpack the plugin: {e}"));
    }
    if updated {
        if plugins::loaded_under(&target) {
            let _ = std::fs::remove_dir_all(&staging);
            return Err("the installed version is loaded: restart Barbican, then install the update".into());
        }
        let old = plugins_dir.join(format!(".old-{id}-{}", random_tag()));
        if let Err(e) = rename_retry(&target, &old) {
            let _ = std::fs::remove_dir_all(&staging);
            return Err(format!("could not replace the installed version ({e}); restart Barbican and try again"));
        }
        if let Err(e) = rename_retry(&staging, &target) {
            let _ = std::fs::remove_dir_all(&staging);
            if let Err(back) = rename_retry(&old, &target) {
                // the backup is the installed version now: the sweep at the next start puts it back, never deletes it
                // that sentence first: the toast is cut to 300 characters
                return Err(format!("the installed version is kept in {} and is put back when Barbican starts again. \
                    Installing failed ({e}) and putting it back failed too ({back}).", old.display()));
            }
            return Err(format!("could not install the plugin ({e}); the installed version was kept"));
        }
        let _ = std::fs::remove_dir_all(&old);
    } else if let Err(e) = rename_retry(&staging, &target) {
        let _ = std::fs::remove_dir_all(&staging);
        return Err(format!("could not install the plugin: {e}"));
    }
    Ok(Installed { id, name, version, updated })
}

/// Remove an installed package and everything it stored: `plugins_dir\<id>\` and `data_dir\<id>\`.
pub fn uninstall(id: &str, plugins_dir: &Path, data_dir: &Path) -> Result<(), String> {
    if !plugins::valid_id(id) { return Err(format!("{id} is not an installed package")); }
    let _held = folder_lock();
    let target = plugins_dir.join(id);
    if plugins::loaded_under(&target) { return Err("this plugin is loaded: switch it off, restart Barbican, then uninstall it".into()); }
    // backups of this package go too: one left over from an update (removing it failed then) would otherwise be put
    // back by the sweep at the next start, bringing the uninstalled plugin back
    let backups: Vec<PathBuf> = std::fs::read_dir(plugins_dir).map(|rd| rd.flatten()
        .filter(|e| backup_id(&e.file_name().to_string_lossy()) == Some(id)).map(|e| e.path()).collect()).unwrap_or_default();
    // backups first: one that cannot be removed then leaves the package in place, rather than a package gone and an
    // old version the sweep would put back
    for dir in backups.into_iter().chain([target, data_dir.join(id)]) {
        match std::fs::symlink_metadata(&dir) {
            Err(_) => {}
            // a link is removed itself; what it points to is not touched
            Ok(md) if is_link(&md) => std::fs::remove_dir(&dir).or_else(|_| std::fs::remove_file(&dir)).map_err(|e| format!("could not remove {}: {e}", dir.display()))?,
            Ok(_) => std::fs::remove_dir_all(&dir).map_err(|e| format!("could not remove {}: {e}", dir.display()))?,
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    /// One entry of a hand-made ZIP: name, data, "made by" host, external attributes, deflate or store.
    struct E<'a> { name: &'a str, data: &'a [u8], made_by: u16, attr: u32, deflate: bool }
    fn f<'a>(name: &'a str, data: &'a [u8]) -> E<'a> { E { name, data, made_by: 20, attr: 0, deflate: false } }

    /// A ZIP built byte by byte, so the tests can write what no good tool would.
    fn mkzip(entries: &[E]) -> Vec<u8> {
        let (mut out, mut cd) = (vec![], vec![]);
        for e in entries {
            let packed = if e.deflate {
                use std::io::Write;
                let mut z = flate2::write::DeflateEncoder::new(vec![], flate2::Compression::default());
                z.write_all(e.data).unwrap();
                z.finish().unwrap()
            } else { e.data.to_vec() };
            let crc = crc32fast::hash(e.data);
            let method: u16 = if e.deflate { 8 } else { 0 };
            let off = out.len() as u32;
            out.extend_from_slice(&0x0403_4b50u32.to_le_bytes());
            for v in [20u16, 0x0800, method, 0, 0] { out.extend_from_slice(&v.to_le_bytes()); }
            for v in [crc, packed.len() as u32, e.data.len() as u32] { out.extend_from_slice(&v.to_le_bytes()); }
            out.extend_from_slice(&(e.name.len() as u16).to_le_bytes());
            out.extend_from_slice(&0u16.to_le_bytes());
            out.extend_from_slice(e.name.as_bytes());
            out.extend_from_slice(&packed);
            cd.extend_from_slice(&0x0201_4b50u32.to_le_bytes());
            for v in [e.made_by, 20, 0x0800, method, 0, 0] { cd.extend_from_slice(&v.to_le_bytes()); }
            for v in [crc, packed.len() as u32, e.data.len() as u32] { cd.extend_from_slice(&v.to_le_bytes()); }
            for v in [e.name.len() as u16, 0, 0, 0, 0] { cd.extend_from_slice(&v.to_le_bytes()); }
            cd.extend_from_slice(&e.attr.to_le_bytes());
            cd.extend_from_slice(&off.to_le_bytes());
            cd.extend_from_slice(e.name.as_bytes());
        }
        let cd_off = out.len() as u32;
        out.extend_from_slice(&cd);
        out.extend_from_slice(&0x0605_4b50u32.to_le_bytes());
        for v in [0u16, 0, entries.len() as u16, entries.len() as u16] { out.extend_from_slice(&v.to_le_bytes()); }
        out.extend_from_slice(&(cd.len() as u32).to_le_bytes());
        out.extend_from_slice(&cd_off.to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes());
        out
    }

    fn lib_file() -> String { format!("demo.{}", std::env::consts::DLL_EXTENSION) }
    fn manifest(id: &str, version: &str) -> Vec<u8> {
        json!({"api": 2, "id": id, "name": "Demo", "version": version, "author": "Example",
            "library": {plugins::platform_key(): lib_file()},
            "permissions": ["email:read", "ui:email-details"], "hooks": ["onEmailOpen"], "slots": ["email-details-section"]})
            .to_string().into_bytes()
    }
    fn temp(tag: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("barbican-pkg-test-{tag}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&d);
        std::fs::create_dir_all(&d).unwrap();
        d
    }
    const SMALL: Limits = Limits { max_entries: 4, max_entry_bytes: 64, max_total_bytes: 200, max_archive_bytes: 2000 };

    fn zip_err(entries: &[E], limits: Limits) -> String {
        match read_zip(&mkzip(entries), limits) { Err(e) => e, Ok(_) => panic!("accepted") }
    }

    #[test]
    fn a_good_zip_reads_stored_and_deflated_entries() {
        let files = read_zip(&mkzip(&[f("manifest.json", b"{}"), E { deflate: true, ..f("lib/x.bin", b"hello hello hello hello") }, f("lib/", b"")]), LIMITS).unwrap();
        assert_eq!(files.len(), 2, "folder entries are checked, not returned");
        assert_eq!(files[1].path, "lib/x.bin");
        assert_eq!(files[1].data, b"hello hello hello hello");
    }

    #[test]
    fn zip_slip_is_refused() {
        for name in ["../evil.dll", "a/../../evil.dll", "..\\evil.dll", "a\\..\\..\\x", "./x", "a//b"] {
            let e = zip_err(&[f(name, b"x")], LIMITS);
            assert!(e.contains("leaves the package") || e.contains("'.'") || e.contains("empty folder"), "{name}: {e}");
        }
    }

    #[test]
    fn absolute_paths_are_refused() {
        for name in ["/etc/passwd", "\\Windows\\x.dll", "//server/share/x", "\\\\server\\share\\x"] {
            assert!(zip_err(&[f(name, b"x")], LIMITS).contains("absolute path"), "{name}");
        }
    }

    #[test]
    fn drive_letters_and_streams_are_refused() {
        for name in ["C:/Windows/x.dll", "c:x.dll", "D:\\x"] {
            assert!(zip_err(&[f(name, b"x")], LIMITS).contains("drive letter"), "{name}");
        }
        assert!(zip_err(&[f("lib.dll:hidden", b"x")], LIMITS).contains("alternate data stream"));
    }

    #[test]
    fn windows_traps_in_names_are_refused() {
        for name in ["con", "lib/NUL.txt", "com1.dll", "lpt\u{b9}", "a.", "a ", "a?b", "x\u{0}y"] {
            assert!(read_zip(&mkzip(&[f(name, b"x")]), LIMITS).is_err(), "{name:?}");
        }
        assert!(check_path("console.dll").is_ok() && check_path("com10.txt").is_ok());
    }

    #[test]
    fn names_differing_only_in_case_are_refused() {
        let e = zip_err(&[f("Demo.dll", b"x"), f("demo.dll", b"y")], LIMITS);
        assert!(e.contains("differ only in case"), "{e}");
        let e = zip_err(&[f("Lib/a", b"x"), f("lib/A", b"y")], LIMITS);
        assert!(e.contains("differ only in case"), "{e}");
        assert!(zip_err(&[f("a", b"x"), f("a/b", b"y")], LIMITS).contains("both a file and a folder"));
        // a folder entry next to its files is fine
        assert!(read_zip(&mkzip(&[f("lib/", b""), f("Lib/", b""), f("lib/a", b"x")]), LIMITS).is_ok());
    }

    #[test]
    fn symlinks_and_reparse_points_are_refused() {
        let link = E { made_by: 3 << 8, attr: (0o120777u32) << 16, ..f("link.dll", b"/etc/passwd") };
        assert!(zip_err(&[link], LIMITS).contains("symbolic links"));
        // a Windows reparse attribute alone is not a link (OneDrive placeholders carry it)
        let reparse = E { attr: 0x400, ..f("placeholder", b"x") };
        assert!(read_zip(&mkzip(&[reparse]), LIMITS).is_ok());
        // a plain Unix file is fine
        assert!(read_zip(&mkzip(&[E { made_by: 3 << 8, attr: 0o100644 << 16, ..f("ok", b"x") }]), LIMITS).is_ok());
    }

    #[test]
    fn oversize_packages_are_refused() {
        let big = [7u8; 65];
        assert!(zip_err(&[f("big", &big)], SMALL).contains("larger than"), "one entry over the cap");
        let part = [7u8; 60];
        assert!(zip_err(&[f("a", &part), f("b", &part), f("c", &part), f("d", &part)], SMALL).contains("more than"), "the total over the cap");
        let mut huge = mkzip(&[f("a", b"x")]);
        huge.resize(SMALL.max_archive_bytes as usize + 1, 0);
        assert!(read_zip(&huge, SMALL).unwrap_err().contains("larger than"), "the archive itself over the cap");
        // a deflate bomb that lies about its size stops at the declared size
        let mut bomb = mkzip(&[E { deflate: true, ..f("bomb", &[0u8; 60]) }]);
        let cd = bomb.windows(4).rposition(|w| w == [0x50, 0x4b, 0x01, 0x02]).unwrap();
        bomb[cd + 24..cd + 28].copy_from_slice(&10u32.to_le_bytes());
        assert!(read_zip(&bomb, SMALL).unwrap_err().contains("size does not match"));
    }

    #[test]
    fn too_many_entries_are_refused() {
        let e = zip_err(&[f("a", b""), f("b", b""), f("c", b""), f("d", b""), f("e", b"")], SMALL);
        assert!(e.contains("5 entries") && e.contains("at most 4"), "{e}");
    }

    #[test]
    fn damaged_or_unsupported_zips_are_refused() {
        assert!(read_zip(b"not a zip at all", LIMITS).unwrap_err().contains("not a ZIP"));
        let mut z = mkzip(&[f("a", b"abc")]);
        let at = z.windows(3).position(|w| w == b"abc").unwrap();
        z[at] = b'x';
        assert!(read_zip(&z, LIMITS).unwrap_err().contains("checksum"));
        let mut enc = mkzip(&[f("a", b"abc")]);
        let cd = enc.windows(4).rposition(|w| w == [0x50, 0x4b, 0x01, 0x02]).unwrap();
        enc[cd + 8] |= 1;
        assert!(read_zip(&enc, LIMITS).unwrap_err().contains("encrypted"));
    }

    #[test]
    fn install_update_and_uninstall() {
        let root = temp("install");
        let (plugins_dir, data_dir) = (root.join("plugins"), root.join("plugin-data"));
        let lib = lib_file();
        let m1 = manifest("dev.example.demo", "1.0.0");
        let zip = mkzip(&[f("demo/manifest.json", &m1), f(&format!("demo/{lib}"), b"v1"), f("demo/LICENSE", b"MIT")]);
        let src = root.join("demo.zip");
        std::fs::write(&src, &zip).unwrap();
        let r = install(&src, &plugins_dir).unwrap();
        assert_eq!(r, Installed { id: "dev.example.demo".into(), name: "Demo".into(), version: "1.0.0".into(), updated: false });
        let dir = plugins_dir.join("dev.example.demo");
        assert_eq!(std::fs::read(dir.join(&lib)).unwrap(), b"v1");
        assert!(dir.join("manifest.json").is_file() && dir.join("LICENSE").is_file());

        // listed, off and not loaded
        let (list, errors) = plugins::discover_in(&plugins_dir);
        assert!(errors.is_empty(), "{errors:?}");
        assert_eq!(list.len(), 1);
        assert_eq!(list[0].key, "dev.example.demo");
        assert_eq!(list[0].decl.api, 2);
        assert_eq!(list[0].decl.package.as_deref(), Some(dir.as_path()));
        assert!(plugins::granted_enabled(&list, &[], &[]).is_empty());

        // an update from a folder replaces it in place
        let folder = root.join("demo-src");
        std::fs::create_dir_all(&folder).unwrap();
        std::fs::write(folder.join("manifest.json"), manifest("dev.example.demo", "1.1.0")).unwrap();
        std::fs::write(folder.join(&lib), b"v2").unwrap();
        let r = install(&folder, &plugins_dir).unwrap();
        assert!(r.updated && r.version == "1.1.0");
        assert_eq!(std::fs::read(dir.join(&lib)).unwrap(), b"v2");
        assert!(!dir.join("LICENSE").exists(), "the old files are gone");
        assert_eq!(std::fs::read_dir(&plugins_dir).unwrap().count(), 1, "no staging folders left behind");

        // uninstall removes the code and the data
        std::fs::create_dir_all(data_dir.join("dev.example.demo")).unwrap();
        std::fs::write(data_dir.join("dev.example.demo").join("abc.json"), b"{}").unwrap();
        std::fs::create_dir_all(data_dir.join("dev.example.other")).unwrap();
        uninstall("dev.example.demo", &plugins_dir, &data_dir).unwrap();
        assert!(!dir.exists() && !data_dir.join("dev.example.demo").exists());
        assert!(data_dir.join("dev.example.other").exists(), "other plugins' data stays");
        assert!(uninstall("../plugins", &plugins_dir, &data_dir).is_err(), "only a valid id names a folder");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn install_refuses_bad_packages_and_writes_nothing() {
        let root = temp("refuse");
        let plugins_dir = root.join("plugins");
        let lib = lib_file();
        let cases: Vec<(Vec<u8>, &str)> = vec![
            (mkzip(&[f(&lib, b"x")]), "no manifest.json"),
            (mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0"))]), "does not contain its library"),
            (mkzip(&[f("manifest.json", &manifest("Bad Id", "1.0.0")), f(&lib, b"x")]), "\"id\""),
            (mkzip(&[f("manifest.json", b"{not json"), f(&lib, b"x")]), "bad manifest.json"),
            (mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0")), f(&lib, b"x"), f("../x", b"y")]), "leaves the package"),
        ];
        for (zip, want) in cases {
            let src = root.join("p.zip");
            std::fs::write(&src, &zip).unwrap();
            let e = install(&src, &plugins_dir).unwrap_err();
            assert!(e.contains(want), "{want}: {e}");
        }
        assert!(std::fs::read_dir(&plugins_dir).map(|d| d.count()).unwrap_or(0) == 0, "nothing was unpacked");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn folder_packages_get_the_same_checks() {
        let root = temp("folder");
        let src = root.join("src");
        std::fs::create_dir_all(src.join("sub")).unwrap();
        for i in 0..4 { std::fs::write(src.join("sub").join(format!("f{i}")), b"x").unwrap(); }
        assert!(read_folder(&src, SMALL).unwrap_err().contains("more than 4 entries"));
        std::fs::remove_dir_all(src.join("sub")).unwrap();
        std::fs::write(src.join("big"), [1u8; 65]).unwrap();
        assert!(read_folder(&src, SMALL).unwrap_err().contains("larger than"));
        std::fs::remove_file(src.join("big")).unwrap();
        std::fs::write(src.join("ok"), b"x").unwrap();
        // a link inside the folder (needs the right to make links; skipped without it)
        #[cfg(windows)]
        let made = std::os::windows::fs::symlink_file(src.join("ok"), src.join("link")).is_ok();
        #[cfg(unix)]
        let made = std::os::unix::fs::symlink(src.join("ok"), src.join("link")).is_ok();
        if made {
            assert!(read_folder(&src, LIMITS).unwrap_err().contains("symbolic links"));
        } else {
            eprintln!("cannot create symbolic links here; link check not exercised");
        }
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn path_checks_accept_ordinary_names() {
        assert_eq!(check_path("lib/demo.dll").unwrap(), ["lib", "demo.dll"]);
        assert_eq!(check_path("locales\\de.json").unwrap(), ["locales", "de.json"]);
        assert_eq!(check_path("folder/").unwrap(), ["folder"]);
    }

    #[test]
    fn an_installed_package_is_recorded_with_path_and_hash_and_starts_off() {
        let root = temp("record");
        let plugins_dir = root.join("plugins");
        let lib = lib_file();
        let zip = mkzip(&[f("manifest.json", &manifest("dev.example.rec", "1.0.0")), f(&lib, b"library bytes")]);
        let src = root.join("p.zip");
        std::fs::write(&src, &zip).unwrap();
        install(&src, &plugins_dir).unwrap();
        let (list, errors) = plugins::discover_in(&plugins_dir);
        assert!(errors.is_empty(), "{errors:?}");
        let at = "2026-01-01T00:00:00Z";
        let g = list[0].grant(vec![], at);
        use sha2::{Digest, Sha256};
        assert_eq!(g.sha256, plugins::package_fingerprint(&plugins_dir.join("dev.example.rec"), false).unwrap());
        assert_ne!(g.sha256, Sha256::digest(b"library bytes").iter().map(|b| format!("{b:02x}")).collect::<String>(), "the pin covers the whole package, not only the library");
        assert!(g.path.contains("dev.example.rec"), "{}", g.path);
        assert!(g.granted.contains(&"email:read".to_string()));
        // off until a grant exists, on with it, off again when the file changes
        assert!(plugins::granted_enabled(&list, &["dev.example.rec".into()], &[]).is_empty());
        std::fs::write(plugins_dir.join("dev.example.rec").join(&lib), b"tampered").unwrap();
        let (list2, _) = plugins::discover_in(&plugins_dir);
        assert!(plugins::granted_enabled(&list2, &["dev.example.rec".into()], &[g.clone()]).is_empty(), "a changed file needs consent again");
        // a grant still holds when nothing changed, and any other file of the package counts too
        std::fs::write(plugins_dir.join("dev.example.rec").join(&lib), b"library bytes").unwrap();
        let (list3, _) = plugins::discover_in(&plugins_dir);
        assert_eq!(plugins::granted_enabled(&list3, &["dev.example.rec".into()], &[g.clone()]), ["dev.example.rec"]);
        std::fs::write(plugins_dir.join("dev.example.rec").join("extra.txt"), b"new").unwrap();
        let (list4, _) = plugins::discover_in(&plugins_dir);
        assert!(plugins::granted_enabled(&list4, &["dev.example.rec".into()], &[g]).is_empty(), "an added file changes the package hash");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn uninstall_of_nothing_is_ok_and_ids_cannot_escape() {
        let root = temp("unins");
        let (p, d) = (root.join("plugins"), root.join("plugin-data"));
        std::fs::create_dir_all(&p).unwrap();
        assert!(uninstall("dev.example.none", &p, &d).is_ok());
        for bad in ["", "..", "a/b", "a\\b", "C:"] { assert!(uninstall(bad, &p, &d).is_err(), "{bad}"); }
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn ids_that_look_like_libraries_or_sidecars_are_refused() {
        for id in ["spam_score.dll", "tool.so", "tool.dylib", "tool.manifest.json", "x.json"] {
            assert!(!plugins::valid_id(id), "{id}");
            let root = temp("badid");
            let zip = mkzip(&[f("manifest.json", &manifest(id, "1.0.0")), f(&lib_file(), b"x")]);
            std::fs::write(root.join("p.zip"), &zip).unwrap();
            assert!(install(&root.join("p.zip"), &root.join("plugins")).unwrap_err().contains("id"), "{id}");
            let _ = std::fs::remove_dir_all(&root);
        }
        assert!(plugins::valid_id("dev.example.spam-score") && plugins::valid_id("jsonlint") && plugins::valid_id("my.dll-tool"));
    }

    #[test]
    fn only_a_package_with_the_same_id_is_replaced() {
        let root = temp("replace");
        let plugins_dir = root.join("plugins");
        let lib = lib_file();
        std::fs::create_dir_all(&plugins_dir).unwrap();
        let zip = mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0")), f(&lib, b"new")]);
        std::fs::write(root.join("p.zip"), &zip).unwrap();
        // a plain file, a folder of something else and a folder with another id are all left alone
        let target = plugins_dir.join("dev.example.demo");
        std::fs::write(&target, b"own file").unwrap();
        assert!(install(&root.join("p.zip"), &plugins_dir).unwrap_err().contains("not replaced"));
        assert_eq!(std::fs::read(&target).unwrap(), b"own file");
        std::fs::remove_file(&target).unwrap();
        std::fs::create_dir_all(&target).unwrap();
        std::fs::write(target.join("keep.txt"), b"k").unwrap();
        assert!(install(&root.join("p.zip"), &plugins_dir).unwrap_err().contains("not replaced"));
        std::fs::write(target.join("manifest.json"), br#"{"id":"dev.example.other"}"#).unwrap();
        assert!(install(&root.join("p.zip"), &plugins_dir).unwrap_err().contains("not replaced"));
        assert!(target.join("keep.txt").exists());
        // same id: replaced, and the name and author of what is replaced are reported
        std::fs::write(target.join("manifest.json"), br#"{"id":"dev.example.demo","name":"Old","author":"Someone Else"}"#).unwrap();
        let pkg = prepare(&root.join("p.zip"), LIMITS).unwrap();
        assert_eq!(pkg.replaces(&plugins_dir), Some(("Old".to_string(), "Someone Else".to_string())));
        assert_ne!(pkg.author, "Someone Else");
        assert!(commit(pkg, &plugins_dir).unwrap().updated);
        assert!(!target.join("keep.txt").exists());
        // a first install replaces nothing
        let pkg = prepare(&root.join("p.zip"), LIMITS).unwrap();
        assert!(pkg.replaces(&root.join("elsewhere")).is_none());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn leftover_staging_folders_are_swept() {
        let root = temp("sweep");
        let plugins_dir = root.join("plugins");
        for n in [".install-a-1", ".old-dev.example.b-2a3f"] {
            std::fs::create_dir_all(plugins_dir.join(n)).unwrap();
            std::fs::write(plugins_dir.join(n).join("x"), b"x").unwrap();
        }
        std::fs::create_dir_all(plugins_dir.join("keepme")).unwrap();
        // the backup of a package that is still installed goes
        std::fs::create_dir_all(plugins_dir.join("dev.example.b")).unwrap();
        sweep_leftovers(&plugins_dir);
        std::fs::remove_dir_all(plugins_dir.join("dev.example.b")).unwrap();
        let zip = mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0")), f(&lib_file(), b"x")]);
        std::fs::write(root.join("p.zip"), &zip).unwrap();
        install(&root.join("p.zip"), &plugins_dir).unwrap();
        let mut names: Vec<String> = std::fs::read_dir(&plugins_dir).unwrap().flatten().map(|e| e.file_name().to_string_lossy().to_string()).collect();
        names.sort();
        assert_eq!(names, ["dev.example.demo", "keepme"]);
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn a_sweep_waits_for_a_running_install() {
        let root = temp("sweep-wait");
        let plugins_dir = root.join("plugins");
        let staging = plugins_dir.join(".install-dev.example.demo-1");
        std::fs::create_dir_all(&staging).unwrap();
        // the lock as a running commit or uninstall holds it, with its staging folder in place
        let held = folder_lock();
        let pd = plugins_dir.clone();
        let sweeper = std::thread::spawn(move || sweep_leftovers(&pd));
        std::thread::sleep(std::time::Duration::from_millis(150));
        assert!(staging.is_dir(), "the staging folder of a running install is not swept");
        drop(held);
        sweeper.join().unwrap();
        assert!(!staging.exists(), "swept once the install ended");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn a_backup_whose_package_is_missing_is_put_back_not_swept() {
        let root = temp("sweep-restore");
        let plugins_dir = root.join("plugins");
        let old = plugins_dir.join(".old-dev.example.demo-0a1b2c");
        std::fs::create_dir_all(&old).unwrap();
        std::fs::write(old.join("manifest.json"), manifest("dev.example.demo", "1.0.0")).unwrap();
        // a backup with a name that is no plugin id cannot be one: it goes
        std::fs::create_dir_all(plugins_dir.join(".old-X-1")).unwrap();
        sweep_leftovers(&plugins_dir);
        assert!(!old.exists());
        assert!(plugins_dir.join("dev.example.demo").join("manifest.json").is_file(), "the update stopped between its renames: put back");
        assert!(!plugins_dir.join(".old-X-1").exists());
        // with the package back in place, a second backup of it is removed
        std::fs::create_dir_all(plugins_dir.join(".old-dev.example.demo-ffff")).unwrap();
        sweep_leftovers(&plugins_dir);
        assert!(!plugins_dir.join(".old-dev.example.demo-ffff").exists());
        assert!(plugins_dir.join("dev.example.demo").is_dir());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn only_the_newest_readable_backup_is_put_back() {
        let root = temp("sweep-newest");
        let plugins_dir = root.join("plugins");
        let mk = |tag: &str, version: Option<&str>| {
            let p = plugins_dir.join(format!(".old-dev.example.demo-{tag}"));
            std::fs::create_dir_all(&p).unwrap();
            match version {
                Some(v) => std::fs::write(p.join("manifest.json"), manifest("dev.example.demo", v)).unwrap(),
                None => std::fs::write(p.join("half-deleted.bin"), b"x").unwrap(),
            }
            // folder times must differ
            std::thread::sleep(std::time::Duration::from_millis(30));
        };
        mk("aa", Some("1.0.0"));
        mk("bb", Some("2.0.0"));
        mk("cc", None); // the newest, but without a manifest: not a package
        sweep_leftovers(&plugins_dir);
        let names: Vec<String> = std::fs::read_dir(&plugins_dir).unwrap().flatten().map(|e| e.file_name().to_string_lossy().to_string()).collect();
        assert_eq!(names, ["dev.example.demo"], "the others are removed");
        let v: serde_json::Value = serde_json::from_slice(&std::fs::read(plugins_dir.join("dev.example.demo").join("manifest.json")).unwrap()).unwrap();
        assert_eq!(v["version"], "2.0.0");
        // a backup holding another id's manifest is not put back
        let p = plugins_dir.join(".old-dev.example.other-dd");
        std::fs::create_dir_all(&p).unwrap();
        std::fs::write(p.join("manifest.json"), manifest("dev.example.demo", "3.0.0")).unwrap();
        sweep_leftovers(&plugins_dir);
        assert!(!p.exists() && !plugins_dir.join("dev.example.other").exists());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn uninstall_removes_the_packages_backups() {
        let root = temp("uninstall-backups");
        let plugins_dir = root.join("plugins");
        let data_dir = root.join("plugin-data");
        std::fs::create_dir_all(plugins_dir.join("dev.example.demo")).unwrap();
        let stale = plugins_dir.join(".old-dev.example.demo-0123");
        std::fs::create_dir_all(&stale).unwrap();
        std::fs::write(stale.join("manifest.json"), manifest("dev.example.demo", "1.0.0")).unwrap();
        let other = plugins_dir.join(".old-dev.example.demo2-0123");
        std::fs::create_dir_all(&other).unwrap();
        uninstall("dev.example.demo", &plugins_dir, &data_dir).unwrap();
        assert!(!stale.exists(), "it would come back at the next start");
        assert!(other.exists(), "another id's backup is left alone");
        sweep_leftovers(&plugins_dir);
        assert!(!plugins_dir.join("dev.example.demo").exists(), "the uninstalled plugin does not come back");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn an_install_does_not_sweep() {
        let root = temp("no-sweep");
        let plugins_dir = root.join("plugins");
        // another Barbican's install in progress, as far as this process can tell
        std::fs::create_dir_all(plugins_dir.join(".install-dev.example.other-1")).unwrap();
        let zip = mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0")), f(&lib_file(), b"x")]);
        std::fs::write(root.join("p.zip"), &zip).unwrap();
        install(&root.join("p.zip"), &plugins_dir).unwrap();
        assert!(plugins_dir.join(".install-dev.example.other-1").is_dir());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn an_id_a_dropped_in_plugin_uses_is_refused() {
        let root = temp("shadow");
        let plugins_dir = root.join("plugins");
        std::fs::create_dir_all(&plugins_dir).unwrap();
        std::fs::write(plugins_dir.join(format!("spam.{}", std::env::consts::DLL_EXTENSION)), b"x").unwrap();
        std::fs::write(plugins_dir.join("spam.manifest.json"), json!({"id": "dev.example.demo"}).to_string()).unwrap();
        let zip = mkzip(&[f("manifest.json", &manifest("dev.example.demo", "1.0.0")), f(&lib_file(), b"x")]);
        std::fs::write(root.join("p.zip"), &zip).unwrap();
        let e = install(&root.join("p.zip"), &plugins_dir).unwrap_err();
        assert!(e.contains("dropped into the plugins folder already uses the id"), "{e}");
        assert!(!plugins_dir.join("dev.example.demo").exists());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn too_many_http_origins_are_refused() {
        let mut v: serde_json::Value = serde_json::from_slice(&manifest("dev.example.demo", "1.0.0")).unwrap();
        v["permissions"] = json!(["http:fetch"]);
        v["hooks"] = json!([]);
        v["slots"] = json!([]);
        v["httpOrigins"] = json!((0..plugins::MAX_HTTP_ORIGINS + 1).map(|i| format!("https://h{i}.example.com")).collect::<Vec<_>>());
        assert!(plugins::validate_manifest(&v).unwrap_err().contains("at most"));
        v["httpOrigins"] = json!((0..plugins::MAX_HTTP_ORIGINS).map(|i| format!("https://h{i}.example.com")).collect::<Vec<_>>());
        assert!(plugins::validate_manifest(&v).is_ok());
    }
}
