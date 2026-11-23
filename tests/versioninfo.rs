//! barbican.exe carries a VERSIONINFO resource whose ProductVersion is Cargo.toml's version
//! (Hub reads it first). The strings of a version resource are UTF-16 in the file, so this finds
//! them without a Windows API. Runs only when build.rs embedded the resource (Windows MSVC with a
//! resource compiler); the CI and release workflows check the same through Explorer's properties.

fn utf16(s: &str) -> Vec<u8> { s.encode_utf16().flat_map(|c| c.to_le_bytes()).collect() }

fn find(hay: &[u8], needle: &[u8]) -> Option<usize> { hay.windows(needle.len()).position(|w| w == needle) }

#[test]
fn exe_has_version_resource() {
    if option_env!("BARBICAN_VERSIONINFO").is_none() { return; }
    let bytes = std::fs::read(env!("CARGO_BIN_EXE_barbican")).unwrap();
    let version = env!("CARGO_PKG_VERSION");
    // "ProductVersion" NUL padding, then the value, then NUL
    for key in ["ProductVersion", "FileVersion"] {
        let at = find(&bytes, &utf16(key)).unwrap_or_else(|| panic!("no {key} in the exe"));
        let tail = &bytes[at..at + 2048.min(bytes.len() - at)];
        let mut value = utf16(version);
        value.extend_from_slice(&[0, 0]);
        assert!(find(tail, &value).is_some(), "{key} is not {version}");
    }
    assert!(find(&bytes, &utf16("barbican.exe")).is_some());
}
