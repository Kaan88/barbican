//! A small ZIP writer for "Download all" attachments (Bulwark handleDownloadAllAttachments packs
//! them with JSZip, whose default is STORE). Entries are stored uncompressed (attachments are
//! mostly compressed already), names are UTF-8 (general purpose bit 11) and made unique the
//! way Bulwark's createUniqueNamer does ("name (1).ext"). No ZIP64: an archive must stay under
//! 4 GB and 65535 entries.

/// One file in the archive.
pub struct Entry {
    pub name: String,
    pub data: Vec<u8>,
}

/// DOS date and time of a moment (2-second resolution, years 1980..2107).
pub fn dos_time(t: chrono::NaiveDateTime) -> (u16, u16) {
    use chrono::{Datelike, Timelike};
    let y = t.year().clamp(1980, 2107) as u16;
    let date = ((y - 1980) << 9) | ((t.month() as u16) << 5) | t.day() as u16;
    let time = ((t.hour() as u16) << 11) | ((t.minute() as u16) << 5) | (t.second() as u16 / 2);
    (time, date)
}

/// Names made unique in order: the second "a.pdf" becomes "a (1).pdf".
pub fn unique_names(names: &[String], fallback: &str) -> Vec<String> {
    let mut used = std::collections::HashSet::new();
    names.iter().map(|raw| {
        let base = if raw.trim().is_empty() { fallback.to_string() } else { raw.clone() };
        let (stem, ext) = match base.rfind('.') { Some(i) if i > 0 => (base[..i].to_string(), base[i..].to_string()), _ => (base.clone(), String::new()) };
        let mut n = 0;
        let mut name = base.clone();
        while used.contains(&name.to_lowercase()) { n += 1; name = format!("{stem} ({n}){ext}"); }
        used.insert(name.to_lowercase());
        name
    }).collect()
}

/// The archive's bytes.
pub fn build(entries: &[Entry], when: chrono::NaiveDateTime) -> Result<Vec<u8>, String> {
    if entries.len() > 0xFFFF { return Err("Too many files for one archive.".into()); }
    let (time, date) = dos_time(when);
    let mut out: Vec<u8> = Vec::new();
    let mut central: Vec<u8> = Vec::new();
    let u16le = |v: &mut Vec<u8>, x: u16| v.extend_from_slice(&x.to_le_bytes());
    let u32le = |v: &mut Vec<u8>, x: u32| v.extend_from_slice(&x.to_le_bytes());
    for e in entries {
        let name = e.name.replace('\\', "/");
        let name = name.trim_start_matches('/');
        let size = u32::try_from(e.data.len()).map_err(|_| "A file is too large for the archive.")?;
        let offset = u32::try_from(out.len()).map_err(|_| "The archive would be larger than 4 GB.")?;
        let crc = crc32fast::hash(&e.data);
        // local file header
        u32le(&mut out, 0x0403_4b50);
        u16le(&mut out, 20);
        u16le(&mut out, 0x0800);
        u16le(&mut out, 0);
        u16le(&mut out, time);
        u16le(&mut out, date);
        u32le(&mut out, crc);
        u32le(&mut out, size);
        u32le(&mut out, size);
        u16le(&mut out, name.len() as u16);
        u16le(&mut out, 0);
        out.extend_from_slice(name.as_bytes());
        out.extend_from_slice(&e.data);
        // central directory record
        u32le(&mut central, 0x0201_4b50);
        u16le(&mut central, 20);
        u16le(&mut central, 20);
        u16le(&mut central, 0x0800);
        u16le(&mut central, 0);
        u16le(&mut central, time);
        u16le(&mut central, date);
        u32le(&mut central, crc);
        u32le(&mut central, size);
        u32le(&mut central, size);
        u16le(&mut central, name.len() as u16);
        u16le(&mut central, 0);
        u16le(&mut central, 0);
        u16le(&mut central, 0);
        u16le(&mut central, 0);
        u32le(&mut central, 0);
        u32le(&mut central, offset);
        central.extend_from_slice(name.as_bytes());
    }
    let cd_offset = u32::try_from(out.len()).map_err(|_| "The archive would be larger than 4 GB.")?;
    let cd_size = central.len() as u32;
    out.extend_from_slice(&central);
    u32le(&mut out, 0x0605_4b50);
    u16le(&mut out, 0);
    u16le(&mut out, 0);
    u16le(&mut out, entries.len() as u16);
    u16le(&mut out, entries.len() as u16);
    u32le(&mut out, cd_size);
    u32le(&mut out, cd_offset);
    u16le(&mut out, 0);
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rd16(b: &[u8], i: usize) -> u16 { u16::from_le_bytes([b[i], b[i + 1]]) }
    fn rd32(b: &[u8], i: usize) -> u32 { u32::from_le_bytes([b[i], b[i + 1], b[i + 2], b[i + 3]]) }

    #[test]
    fn names_are_unique() {
        let n = unique_names(&["a.pdf".into(), "A.pdf".into(), "a.pdf".into(), "".into(), "notes".into(), "notes".into()], "attachment");
        assert_eq!(n, vec!["a.pdf", "A (1).pdf", "a (2).pdf", "attachment", "notes", "notes (1)"]);
    }

    #[test]
    fn archive_layout_reads_back() {
        let when = chrono::NaiveDate::from_ymd_opt(2026, 11, 16).unwrap().and_hms_opt(13, 45, 31).unwrap();
        let z = build(&[Entry { name: "hello.txt".into(), data: b"hello world".to_vec() }, Entry { name: "ünï.bin".into(), data: vec![0, 1, 2] }], when).unwrap();
        // end of central directory
        let eocd = z.len() - 22;
        assert_eq!(rd32(&z, eocd), 0x0605_4b50);
        assert_eq!(rd16(&z, eocd + 10), 2);
        let (cd_size, cd_off) = (rd32(&z, eocd + 12) as usize, rd32(&z, eocd + 16) as usize);
        assert_eq!(cd_off + cd_size, eocd);
        // walk the central directory and check each local entry against it
        let mut p = cd_off;
        let mut names = vec![];
        while p < eocd {
            assert_eq!(rd32(&z, p), 0x0201_4b50);
            assert_eq!(rd16(&z, p + 8) & 0x0800, 0x0800);
            let crc = rd32(&z, p + 16);
            let size = rd32(&z, p + 24) as usize;
            let nlen = rd16(&z, p + 28) as usize;
            let local = rd32(&z, p + 42) as usize;
            let name = String::from_utf8(z[p + 46..p + 46 + nlen].to_vec()).unwrap();
            assert_eq!(rd32(&z, local), 0x0403_4b50);
            let data = &z[local + 30 + nlen..local + 30 + nlen + size];
            assert_eq!(crc32fast::hash(data), crc);
            names.push((name, data.to_vec()));
            p += 46 + nlen;
        }
        assert_eq!(names[0], ("hello.txt".to_string(), b"hello world".to_vec()));
        assert_eq!(names[1].0, "ünï.bin");
        // CRC-32 of "hello world"
        assert_eq!(crc32fast::hash(b"hello world"), 0x0d4a_1185);
        let (t, d) = dos_time(when);
        assert_eq!(d, (46 << 9) | (11 << 5) | 16);
        assert_eq!(t, (13 << 11) | (45 << 5) | 15);
    }
}
