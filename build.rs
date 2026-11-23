//! Compiles the Slint UI and, on Windows (MSVC), embeds the application icon and a VERSIONINFO
//! resource into barbican.exe. Hub (the owner's launcher) reads the exe's own version first, so
//! FILEVERSION and ProductVersion follow Cargo.toml's version (the release workflow sets it
//! from the tag). The resource compiler is the SDK's `rc.exe` (PATH, else the newest x64 one under
//! the Windows 10/11 SDK) or, when cross-compiling, LLVM's `llvm-rc` (`BARBICAN_LLVM_RC` or PATH).
//! Without one the build still succeeds, without icon and version, and says so; a build that did
//! embed them sets `BARBICAN_VERSIONINFO` for tests/versioninfo.rs.
use std::path::PathBuf;
use std::process::Command;

fn main() {
    let cfg = slint_build::CompilerConfiguration::new().with_style("fluent".into());
    slint_build::compile_with_config("ui/app.slint", cfg).unwrap();
    println!("cargo:rerun-if-changed=packaging/icons/barbican.ico");
    println!("cargo:rerun-if-env-changed=BARBICAN_LLVM_RC");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") || std::env::var("CARGO_CFG_TARGET_ENV").as_deref() != Ok("msvc") {
        return;
    }
    embed_resources();
}

fn embed_resources() {
    let Some((rc, llvm)) = find_rc() else {
        println!("cargo:warning=rc.exe and llvm-rc not found; barbican.exe is built without its icon and version resource");
        return;
    };
    let out = PathBuf::from(std::env::var("OUT_DIR").unwrap());
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    let icon = manifest.join("packaging/icons/barbican.ico");
    let script = out.join("barbican.rc");
    let res = out.join("barbican.res");
    let part = |name: &str| std::env::var(name).unwrap_or_default();
    let numeric = format!("{},{},{},0", part("CARGO_PKG_VERSION_MAJOR"), part("CARGO_PKG_VERSION_MINOR"), part("CARGO_PKG_VERSION_PATCH"));
    let version = part("CARGO_PKG_VERSION");
    // Numbers only and no #include, so llvm-rc takes the script without a preprocessor.
    let text = format!(
        "1 ICON \"{}\"\n\
         1 VERSIONINFO\nFILEVERSION {numeric}\nPRODUCTVERSION {numeric}\nFILEOS 0x40004\nFILETYPE 0x1\n{{\n\
         BLOCK \"StringFileInfo\" {{ BLOCK \"040904B0\" {{\n\
         VALUE \"CompanyName\", \"Kaan88\"\nVALUE \"FileDescription\", \"Barbican\"\n\
         VALUE \"FileVersion\", \"{version}\"\nVALUE \"ProductVersion\", \"{version}\"\n\
         VALUE \"InternalName\", \"barbican\"\nVALUE \"OriginalFilename\", \"barbican.exe\"\nVALUE \"ProductName\", \"Barbican\"\n\
         }} }}\nBLOCK \"VarFileInfo\" {{ VALUE \"Translation\", 0x0409, 0x04B0 }}\n}}\n",
        icon.display().to_string().replace('\\', "/")
    );
    if std::fs::write(&script, text).is_err() { return; }
    let mut command = Command::new(rc);
    if llvm {
        command.arg("/FO").arg(&res).arg("-no-preprocess").arg("--").arg(&script);
    } else {
        command.arg("/nologo").arg("/fo").arg(&res).arg(&script);
    }
    if command.status().map(|s| s.success()).unwrap_or(false) {
        println!("cargo:rustc-link-arg-bins={}", res.display());
        println!("cargo:rustc-env=BARBICAN_VERSIONINFO=1");
    } else {
        println!("cargo:warning=the resource compiler failed; barbican.exe is built without its icon and version resource");
    }
}

fn find_rc() -> Option<(PathBuf, bool)> {
    let on_path = |name: &str| {
        let paths = std::env::var_os("PATH")?;
        std::env::split_paths(&paths).map(|dir| dir.join(name)).find(|p| p.is_file())
    };
    if let Some(rc) = on_path("rc.exe") { return Some((rc, false)); }
    if let Some(base) = std::env::var_os("ProgramFiles(x86)") {
        let base = PathBuf::from(base).join("Windows Kits/10/bin");
        let mut found: Vec<PathBuf> = std::fs::read_dir(base).into_iter().flatten().flatten()
            .map(|e| e.path().join("x64/rc.exe")).filter(|p| p.is_file()).collect();
        found.sort();
        if let Some(rc) = found.pop() { return Some((rc, false)); }
    }
    if let Some(rc) = std::env::var_os("BARBICAN_LLVM_RC").map(PathBuf::from) { return rc.is_file().then_some((rc, true)); }
    on_path("llvm-rc").map(|rc| (rc, true))
}
