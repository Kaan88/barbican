//! Release tool (run by .github/workflows/release.yml):
//!
//!   cargo run --release --example release_manifest -- <version> <dist-dir> <download-base-url> <notes-url>
//!       writes <dist-dir>/<channel>.json and, when BARBICAN_UPDATE_SIGNING_KEY (base64 32-byte
//!       Ed25519 seed) is set, <channel>.json.sig, plus SHA256SUMS for every file in the folder.
//!   cargo run --release --example release_manifest -- keygen
//!       prints a new seed (keep it secret) and its public key (BARBICAN_UPDATE_PUBKEY).
#[path = "../src/release_manifest.rs"]
mod release_manifest;

use release_manifest::*;
use sha2::{Digest, Sha256};

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.first().map(String::as_str) == Some("keygen") {
        use base64::Engine;
        let mut seed = [0u8; 32];
        getrandom::getrandom(&mut seed).expect("random");
        let s = base64::engine::general_purpose::STANDARD.encode(seed);
        println!("secret seed (BARBICAN_UPDATE_SIGNING_KEY): {s}");
        println!("public key (BARBICAN_UPDATE_PUBKEY):        {}", public_key(&s).unwrap());
        return;
    }
    if args.len() < 4 { eprintln!("usage: release_manifest <version> <dist-dir> <base-url> <notes-url> | keygen"); std::process::exit(2); }
    let (version, dir, base, notes) = (&args[0], std::path::Path::new(&args[1]), args[2].trim_end_matches('/'), &args[3]);
    let mut files: Vec<_> = std::fs::read_dir(dir).expect("dist dir").flatten().filter(|e| e.path().is_file()).map(|e| e.path()).collect();
    files.sort();
    let mut sums = String::new();
    let mut assets = vec![];
    for p in &files {
        let name = p.file_name().unwrap().to_string_lossy().to_string();
        if name == "SHA256SUMS" || name.ends_with(".json") || name.ends_with(".sig") { continue; }
        let bytes = std::fs::read(p).expect("read");
        let sha = format!("{:x}", Sha256::digest(&bytes));
        sums.push_str(&format!("{sha}  {name}\n"));
        if let Some(key) = asset_key(&name) {
            assets.push(ReleaseAsset { key: key.into(), url: format!("{base}/{name}"), sha256: sha, size: bytes.len() as u64 });
        }
    }
    std::fs::write(dir.join("SHA256SUMS"), &sums).expect("write sums");
    let channel = channel_for(version);
    let body = build(version, channel, notes, &assets);
    std::fs::write(dir.join(format!("{channel}.json")), &body).expect("write manifest");
    match std::env::var("BARBICAN_UPDATE_SIGNING_KEY").ok().filter(|k| !k.trim().is_empty()) {
        Some(k) => {
            std::fs::write(dir.join(format!("{channel}.json.sig")), sign(&k, body.as_bytes()).expect("sign")).expect("write sig");
            println!("signed {channel}.json with {} asset(s)", assets.len());
        }
        None => println!("BARBICAN_UPDATE_SIGNING_KEY not set: {channel}.json written unsigned (apps will not accept it)"),
    }
}
