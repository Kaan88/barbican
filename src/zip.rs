//! Small ZIP writers: `build` for "Download all" attachments (Bulwark handleDownloadAllAttachments packs
//! them with JSZip, whose default is STORE). Entries are stored uncompressed (attachments are
//! mostly compressed already), names are UTF-8 (general purpose bit 11) and made unique the
//! way Bulwark's createUniqueNamer does ("name (1).ext"). No ZIP64: an archive must stay under
//! 4 GB and 65535 entries.
//! `StreamZip` writes the Files "Download as zip" archive to disk as it goes.

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

/// Counts what passes through, and stops with an error once `cancel` is set.
struct Counted<'a, W: std::io::Write> { inner: W, n: u64, cancel: Option<&'a std::sync::atomic::AtomicBool> }

impl<W: std::io::Write> std::io::Write for Counted<'_, W> {
    fn write(&mut self, b: &[u8]) -> std::io::Result<usize> {
        if self.cancel.is_some_and(|c| c.load(std::sync::atomic::Ordering::Relaxed)) {
            return Err(std::io::Error::new(std::io::ErrorKind::Other, CANCELLED));
        }
        let k = self.inner.write(b)?;
        self.n += k as u64;
        Ok(k)
    }
    fn flush(&mut self) -> std::io::Result<()> { self.inner.flush() }
}

/// Feeds the CRC of what it writes.
struct Tee<'a, 'b, X: std::io::Write> { w: &'a mut X, crc: &'b mut crc32fast::Hasher }

impl<X: std::io::Write> std::io::Write for Tee<'_, '_, X> {
    fn write(&mut self, b: &[u8]) -> std::io::Result<usize> { let k = self.w.write(b)?; self.crc.update(&b[..k]); Ok(k) }
    fn flush(&mut self) -> std::io::Result<()> { self.w.flush() }
}

/// The error text of a write stopped by a cancel.
pub const CANCELLED: &str = "The download was cancelled";

/// A zip written as it goes (Files "Download as zip", Bulwark lib/files-zip.ts): each file's
/// bytes are deflated (level 1, as Bulwark's JSZip call, files-zip.ts:107-111) straight into
/// `out`, with a data descriptor after them (general purpose bit 3), so nothing is held whole
/// in memory. Folders are "name/" entries. No ZIP64: offsets and sizes stay under 4 GB, 65535
/// entries at most.
pub struct StreamZip<W: std::io::Write> {
    out: W,
    offset: u64,
    central: Vec<u8>,
    count: usize,
}

const MAX32: u64 = u32::MAX as u64;
pub const TOO_BIG: &str = "The zip would be larger than 4 GB.";

impl<W: std::io::Write> StreamZip<W> {
    pub fn new(out: W) -> Self { StreamZip { out, offset: 0, central: vec![], count: 0 } }

    fn put(&mut self, b: &[u8]) -> Result<(), String> {
        self.out.write_all(b).map_err(|e| e.to_string())?;
        self.offset += b.len() as u64;
        Ok(())
    }

    fn local_header(name: &str, flags: u16, method: u16, time: u16, date: u16) -> Vec<u8> {
        let mut h = Vec::with_capacity(30 + name.len());
        h.extend_from_slice(&0x0403_4b50u32.to_le_bytes());
        h.extend_from_slice(&20u16.to_le_bytes());
        h.extend_from_slice(&flags.to_le_bytes());
        h.extend_from_slice(&method.to_le_bytes());
        h.extend_from_slice(&time.to_le_bytes());
        h.extend_from_slice(&date.to_le_bytes());
        // crc and sizes: in the data descriptor (a file) or zero (a folder)
        h.extend_from_slice(&[0; 12]);
        h.extend_from_slice(&(name.len() as u16).to_le_bytes());
        h.extend_from_slice(&0u16.to_le_bytes());
        h.extend_from_slice(name.as_bytes());
        h
    }

    #[allow(clippy::too_many_arguments)]
    fn central_record(&mut self, name: &str, flags: u16, method: u16, time: u16, date: u16, crc: u32, csize: u32, size: u32, attrs: u32, offset: u32) {
        let c = &mut self.central;
        c.extend_from_slice(&0x0201_4b50u32.to_le_bytes());
        c.extend_from_slice(&20u16.to_le_bytes());
        c.extend_from_slice(&20u16.to_le_bytes());
        c.extend_from_slice(&flags.to_le_bytes());
        c.extend_from_slice(&method.to_le_bytes());
        c.extend_from_slice(&time.to_le_bytes());
        c.extend_from_slice(&date.to_le_bytes());
        c.extend_from_slice(&crc.to_le_bytes());
        c.extend_from_slice(&csize.to_le_bytes());
        c.extend_from_slice(&size.to_le_bytes());
        c.extend_from_slice(&(name.len() as u16).to_le_bytes());
        // extra and comment lengths, disk number, internal attributes
        c.extend_from_slice(&[0; 8]);
        c.extend_from_slice(&attrs.to_le_bytes());
        c.extend_from_slice(&offset.to_le_bytes());
        c.extend_from_slice(name.as_bytes());
        self.count += 1;
    }

    fn check_entry(&self, name: &str) -> Result<u32, String> {
        if self.count >= 0xFFFF { return Err("Too many files for one zip.".into()); }
        if name.len() > 0xFFFF { return Err("A name is too long for the zip.".into()); }
        u32::try_from(self.offset).map_err(|_| TOO_BIG.to_string())
    }

    /// An empty folder ("path/").
    pub fn add_dir(&mut self, path: &str, when: chrono::NaiveDateTime) -> Result<(), String> {
        let name = format!("{}/", path.trim_end_matches('/'));
        let offset = self.check_entry(&name)?;
        let (time, date) = dos_time(when);
        self.put(&Self::local_header(&name, 0x0800, 0, time, date))?;
        // the MS-DOS directory attribute
        self.central_record(&name, 0x0800, 0, time, date, 0, 0, 0, 0x10, offset);
        Ok(())
    }

    /// A file whose bytes `fill` writes; `cancel` stops it half way (the zip is unusable then).
    pub fn add_file(&mut self, path: &str, when: chrono::NaiveDateTime, cancel: Option<&std::sync::atomic::AtomicBool>,
        fill: &mut dyn FnMut(&mut dyn std::io::Write) -> Result<(), String>) -> Result<(), String> {
        let offset = self.check_entry(path)?;
        let (time, date) = dos_time(when);
        let flags = 0x0800 | 0x0008;
        self.put(&Self::local_header(path, flags, 8, time, date))?;
        let mut crc = crc32fast::Hasher::new();
        let sink = Counted { inner: &mut self.out, n: 0, cancel: None };
        let mut enc = flate2::write::DeflateEncoder::new(sink, flate2::Compression::new(1));
        let raw = {
            let mut src = Counted { inner: Tee { w: &mut enc, crc: &mut crc }, n: 0, cancel };
            fill(&mut src)?;
            src.n
        };
        let packed = enc.finish().map_err(|e| e.to_string())?.n;
        self.offset += packed;
        let (Ok(raw32), Ok(packed32)) = (u32::try_from(raw), u32::try_from(packed)) else { return Err(TOO_BIG.into()) };
        let crc = crc.finalize();
        let mut d = Vec::with_capacity(16);
        d.extend_from_slice(&0x0807_4b50u32.to_le_bytes());
        d.extend_from_slice(&crc.to_le_bytes());
        d.extend_from_slice(&packed32.to_le_bytes());
        d.extend_from_slice(&raw32.to_le_bytes());
        self.put(&d)?;
        self.central_record(path, flags, 8, time, date, crc, packed32, raw32, 0, offset);
        Ok(())
    }

    /// The central directory and its end record; returns the writer, flushed.
    pub fn finish(mut self) -> Result<W, String> {
        let cd_offset = u32::try_from(self.offset).map_err(|_| TOO_BIG.to_string())?;
        let central = std::mem::take(&mut self.central);
        self.put(&central)?;
        u32::try_from(self.offset).map_err(|_| TOO_BIG.to_string())?;
        let mut e = Vec::with_capacity(22);
        e.extend_from_slice(&0x0605_4b50u32.to_le_bytes());
        e.extend_from_slice(&[0; 4]);
        e.extend_from_slice(&(self.count as u16).to_le_bytes());
        e.extend_from_slice(&(self.count as u16).to_le_bytes());
        e.extend_from_slice(&(central.len() as u32).to_le_bytes());
        e.extend_from_slice(&cd_offset.to_le_bytes());
        e.extend_from_slice(&0u16.to_le_bytes());
        self.put(&e)?;
        self.out.flush().map_err(|e| e.to_string())?;
        Ok(self.out)
    }
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

    /// Read a zip back the way an unzip tool does (central directory, then each entry).
    fn read_back(z: &[u8]) -> Vec<(String, Vec<u8>)> {
        let eocd = z.len() - 22;
        assert_eq!(rd32(z, eocd), 0x0605_4b50);
        let n = rd16(z, eocd + 10) as usize;
        let (cd_size, cd_off) = (rd32(z, eocd + 12) as usize, rd32(z, eocd + 16) as usize);
        assert_eq!(cd_off + cd_size, eocd);
        let mut p = cd_off;
        let mut out = vec![];
        for _ in 0..n {
            assert_eq!(rd32(z, p), 0x0201_4b50);
            let method = rd16(z, p + 10);
            let crc = rd32(z, p + 16);
            let csize = rd32(z, p + 20) as usize;
            let size = rd32(z, p + 24) as usize;
            let nlen = rd16(z, p + 28) as usize;
            let local = rd32(z, p + 42) as usize;
            let name = String::from_utf8(z[p + 46..p + 46 + nlen].to_vec()).unwrap();
            assert_eq!(rd32(z, local), 0x0403_4b50);
            let lnlen = rd16(z, local + 26) as usize;
            let start = local + 30 + lnlen;
            let data = if method == 8 {
                use std::io::Read;
                let mut d = vec![];
                flate2::read::DeflateDecoder::new(&z[start..start + csize]).read_to_end(&mut d).unwrap();
                // the data descriptor follows the packed bytes
                assert_eq!(rd32(z, start + csize), 0x0807_4b50);
                assert_eq!(rd32(z, start + csize + 4), crc);
                d
            } else { z[start..start + csize].to_vec() };
            assert_eq!(data.len(), size);
            assert_eq!(crc32fast::hash(&data), crc);
            out.push((name, data));
            p += 46 + nlen;
        }
        out
    }

    #[test]
    fn streamed_zip_reads_back() {
        let when = chrono::NaiveDate::from_ymd_opt(2026, 11, 25).unwrap().and_hms_opt(9, 0, 0).unwrap();
        let mut z = StreamZip::new(Vec::new());
        z.add_dir("Docs/Empty", when).unwrap();
        let big: Vec<u8> = (0..200_000u32).map(|i| (i % 251) as u8).collect();
        let b2 = big.clone();
        z.add_file("Docs/big.bin", when, None, &mut |w| { for c in b2.chunks(7000) { w.write_all(c).map_err(|e| e.to_string())?; } Ok(()) }).unwrap();
        z.add_file("Docs/ünï.txt", when, None, &mut |w| w.write_all(b"hello").map_err(|e| e.to_string())).unwrap();
        z.add_file("empty.txt", when, None, &mut |_| Ok(())).unwrap();
        let bytes = z.finish().unwrap();
        let got = read_back(&bytes);
        assert_eq!(got.iter().map(|g| g.0.as_str()).collect::<Vec<_>>(), ["Docs/Empty/", "Docs/big.bin", "Docs/ünï.txt", "empty.txt"]);
        assert_eq!(got[1].1, big);
        assert_eq!(got[2].1, b"hello");
        assert!(got[3].1.is_empty());
    }

    #[test]
    fn a_cancel_stops_the_file() {
        use std::sync::atomic::{AtomicBool, Ordering};
        let when = chrono::NaiveDate::from_ymd_opt(2026, 11, 25).unwrap().and_hms_opt(9, 0, 0).unwrap();
        let cancel = AtomicBool::new(false);
        let mut z = StreamZip::new(Vec::new());
        let r = z.add_file("a.bin", when, Some(&cancel), &mut |w| {
            w.write_all(b"first").map_err(|e| e.to_string())?;
            cancel.store(true, Ordering::Relaxed);
            w.write_all(b"second").map_err(|e| e.to_string())
        });
        assert!(r.unwrap_err().contains(CANCELLED));
    }
}
