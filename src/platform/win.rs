//! Windows pieces of the platform layer: registry reads/writes in HKCU, the app id that
//! groups toasts, and the taskbar's unread overlay.
use windows::core::{HSTRING, PCWSTR};
use windows::Win32::Foundation::{ERROR_FILE_NOT_FOUND, HWND};
use windows::Win32::System::Registry::*;

const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const PERSONALIZE: &str = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

/// The app id toasts, the taskbar and the Start menu shortcut share. The MSI writes the
/// shortcut with this id and registers it under HKCU\Software\Classes\AppUserModelId.
pub const APP_ID: &str = "Barbican.Mail";

/// Hold the per-session "Barbican is running" mutex for the life of the process. False when
/// another Barbican already holds it (the update installer must not run under it).
pub fn first_instance() -> bool {
    use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};
    use windows::Win32::System::Threading::CreateMutexW;
    // one per profile: a test or automation run must not count as the user's Barbican
    let shared = wide(r"Local\Barbican.Instance");
    let name = match crate::profile::current() {
        p if crate::profile::is_default(p) => shared.clone(),
        p => wide(&format!(r"Local\Barbican.Instance.{p}")),
    };
    // Any profile also holds the shared name, so the installer never replaces a running Barbican.
    // And when the shared name was already held (the user's Barbican, or another profile's, is
    // running), a non-default profile is not "first" either: the update in the shared data_dir()
    // must never be installed from under a running Barbican by a test, automation or dev run.
    let mut other_running = false;
    if !crate::profile::is_default(crate::profile::current()) {
        if unsafe { CreateMutexW(None, false, PCWSTR(shared.as_ptr())) }.is_ok() {
            other_running = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        }
    }
    // the per-profile "a Barbican of this profile runs" mutex (see `other_of_profile_running`)
    PROFILE_FIRST.store(hold_profile_mutex(), std::sync::atomic::Ordering::SeqCst);
    // the handles are leaked on purpose: Windows releases them when the process ends
    let mine = match unsafe { CreateMutexW(None, false, PCWSTR(name.as_ptr())) } {
        Ok(_) => { let e = unsafe { GetLastError() }; e != ERROR_ALREADY_EXISTS }
        Err(_) => true,
    };
    mine && !other_running
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

// ---- who else uses this profile's folders (the WebView's profile folder above all) ----
//
// Every process holds a named mutex per profile from its start (`Local\Barbican.Profile.<name>`,
// default profile `...default`). The WebView folder, the temp folder and the config folder
// are the profile's, so another process holding the same name may have them open. A
// process cannot tell itself from another by opening the name (its own handle counts), so the
// check lets go of its own handle for a moment, opens the name, and takes it again.

/// The raw handle this process holds on its profile's mutex (0: none).
static PROFILE_MUTEX: std::sync::Mutex<isize> = std::sync::Mutex::new(0);
/// Whether this process created the profile's mutex (no other process of the profile at start).
static PROFILE_FIRST: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(true);

fn profile_mutex_name(profile: &str) -> Vec<u16> { wide(&format!(r"Local\Barbican.Profile.{profile}")) }

/// Take this process's hold on the profile mutex; true when nobody held it before.
fn hold_profile_mutex() -> bool {
    let (mut held, first) = hold(&profile_mutex_name(crate::profile::current()));
    let mut slot = PROFILE_MUTEX.lock().unwrap_or_else(|e| e.into_inner());
    std::mem::swap(&mut *slot, &mut held);
    first
}

/// Create (or open) the named mutex: its raw handle and whether this call created it.
fn hold(name: &[u16]) -> (isize, bool) {
    use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};
    use windows::Win32::System::Threading::CreateMutexW;
    match unsafe { CreateMutexW(None, false, PCWSTR(name.as_ptr())) } {
        Ok(h) => { let first = unsafe { GetLastError() } != ERROR_ALREADY_EXISTS; (h.0 as isize, first) }
        Err(_) => (0, true),
    }
}

/// Whether any other process has the named mutex, given this process's own handle in `own`
/// (released while looking and taken again after).
fn held_by_another(name: &[u16], own: &mut isize) -> bool {
    use windows::Win32::Foundation::{CloseHandle, HANDLE};
    use windows::Win32::System::Threading::OpenMutexW;
    const SYNCHRONIZE: u32 = 0x0010_0000;
    if *own != 0 { unsafe { let _ = CloseHandle(HANDLE(*own as *mut _)); } *own = 0; }
    let other = match unsafe { OpenMutexW(windows::Win32::System::Threading::SYNCHRONIZATION_ACCESS_RIGHTS(SYNCHRONIZE), false, PCWSTR(name.as_ptr())) } {
        Ok(h) => { unsafe { let _ = CloseHandle(h); } true }
        Err(_) => false,
    };
    *own = hold(name).0;
    other
}

/// Whether this process was the first of its profile to start (decided once, atomically).
pub fn first_of_profile() -> bool { PROFILE_FIRST.load(std::sync::atomic::Ordering::SeqCst) }

/// Live: whether another Barbican process of this profile is running now.
pub fn other_of_profile_running() -> bool {
    let mut slot = PROFILE_MUTEX.lock().unwrap_or_else(|e| e.into_inner());
    held_by_another(&profile_mutex_name(crate::profile::current()), &mut slot)
}


fn read_dword(key: &str, value: &str) -> Option<u32> {
    let (k, v) = (wide(key), wide(value));
    let mut data = 0u32;
    let mut size = 4u32;
    let r = unsafe {
        RegGetValueW(HKEY_CURRENT_USER, PCWSTR(k.as_ptr()), PCWSTR(v.as_ptr()), RRF_RT_REG_DWORD, None,
            Some(&mut data as *mut u32 as *mut _), Some(&mut size))
    };
    r.is_ok().then_some(data)
}

fn read_string(key: &str, value: Option<&str>) -> Option<String> {
    let k = wide(key);
    let v = value.map(wide);
    let vp = v.as_ref().map(|v| PCWSTR(v.as_ptr())).unwrap_or(PCWSTR::null());
    let mut size = 0u32;
    unsafe {
        if RegGetValueW(HKEY_CURRENT_USER, PCWSTR(k.as_ptr()), vp, RRF_RT_REG_SZ, None, None, Some(&mut size)).is_err() { return None; }
        let mut buf = vec![0u16; (size as usize).div_ceil(2) + 1];
        let mut size = (buf.len() * 2) as u32;
        RegGetValueW(HKEY_CURRENT_USER, PCWSTR(k.as_ptr()), vp, RRF_RT_REG_SZ, None, Some(buf.as_mut_ptr() as *mut _), Some(&mut size)).ok().ok()?;
        let end = buf.iter().position(|c| *c == 0).unwrap_or(buf.len());
        Some(String::from_utf16_lossy(&buf[..end]))
    }
}

/// HKCU\...\Themes\Personalize AppsUseLightTheme (missing on old Windows: None).
pub fn apps_use_light_theme() -> Option<bool> {
    read_dword(PERSONALIZE, "AppsUseLightTheme").map(|v| v != 0)
}

/// The Run value Barbican starts at sign-in with.
#[cfg_attr(not(test), allow(dead_code))]
pub fn run_value(name: &str) -> Option<String> {
    read_string(RUN_KEY, Some(name))
}

/// Write (Some) or remove (None) the Run value. Only from the user's own toggle.
pub fn set_run_value(name: &str, command: Option<&str>) -> Result<(), String> {
    let (k, n) = (wide(RUN_KEY), wide(name));
    unsafe {
        match command {
            Some(cmd) => {
                let data: Vec<u8> = wide(cmd).iter().flat_map(|c| c.to_le_bytes()).collect();
                RegSetKeyValueW(HKEY_CURRENT_USER, PCWSTR(k.as_ptr()), PCWSTR(n.as_ptr()), REG_SZ.0, Some(data.as_ptr() as *const _), data.len() as u32)
                    .ok().map_err(|e| e.to_string())
            }
            None => {
                let r = RegDeleteKeyValueW(HKEY_CURRENT_USER, PCWSTR(k.as_ptr()), PCWSTR(n.as_ptr()));
                if r.is_ok() || r == ERROR_FILE_NOT_FOUND { Ok(()) } else { Err(r.to_hresult().message().to_string()) }
            }
        }
    }
}

/// Whether the installer registered APP_ID (read-only). Toasts under an unregistered id are
/// dropped by Windows, so without it they go out under PowerShell's id like before.
pub fn app_id_registered() -> bool {
    read_string(&format!(r"Software\Classes\AppUserModelId\{APP_ID}"), Some("DisplayName")).is_some()
}

/// Give this process the installed app id, so the taskbar button, its overlay and the
/// toasts belong together. Process-wide and not stored anywhere.
pub fn set_process_app_id() {
    if app_id_registered() {
        unsafe { let _ = windows::Win32::UI::Shell::SetCurrentProcessExplicitAppUserModelID(&HSTRING::from(APP_ID)); }
    }
}

/// Work areas (screen minus the taskbar) of the connected monitors as (x, y, width, height)
/// in physical virtual-desktop pixels, in the system's order.
pub fn monitor_work_areas() -> Vec<(i32, i32, i32, i32)> {
    use windows::Win32::Foundation::{LPARAM, RECT};
    use windows::Win32::Graphics::Gdi::{EnumDisplayMonitors, GetMonitorInfoW, HDC, HMONITOR, MONITORINFO};
    unsafe extern "system" fn each(m: HMONITOR, _: HDC, _: *mut RECT, out: LPARAM) -> windows::core::BOOL {
        let out = unsafe { &mut *(out.0 as *mut Vec<(i32, i32, i32, i32)>) };
        let mut info = MONITORINFO { cbSize: std::mem::size_of::<MONITORINFO>() as u32, ..Default::default() };
        if unsafe { GetMonitorInfoW(m, &mut info) }.as_bool() {
            let r = info.rcWork;
            out.push((r.left, r.top, r.right - r.left, r.bottom - r.top));
        }
        true.into()
    }
    let mut out: Vec<(i32, i32, i32, i32)> = Vec::new();
    unsafe { let _ = EnumDisplayMonitors(None, None, Some(each), LPARAM(&mut out as *mut _ as isize)); }
    out
}

/// The unread overlay on the taskbar button (None clears it). `rgba` is a square icon.
pub fn set_overlay(hwnd: isize, rgba: Option<(&[u8], u32)>, label: &str) {
    use windows::Win32::System::Com::*;
    use windows::Win32::UI::Shell::{ITaskbarList3, TaskbarList};
    use windows::Win32::UI::WindowsAndMessaging::{CreateIcon, DestroyIcon, HICON};
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        let Ok(tb) = CoCreateInstance::<_, ITaskbarList3>(&TaskbarList, None, CLSCTX_INPROC_SERVER) else { return };
        if tb.HrInit().is_err() { return; }
        let hwnd = HWND(hwnd as *mut _);
        match rgba {
            Some((px, size)) => {
                // BGRA plus an AND mask from the inverted alpha, like winit and tray-icon do
                let mut bgra = px.to_vec();
                let mut mask = Vec::with_capacity(px.len() / 4);
                for p in bgra.chunks_exact_mut(4) {
                    p.swap(0, 2);
                    mask.push(p[3].wrapping_sub(u8::MAX));
                }
                let Ok(icon): Result<HICON, _> = CreateIcon(None, size as i32, size as i32, 1, 32, mask.as_ptr(), bgra.as_ptr()) else { return };
                let _ = tb.SetOverlayIcon(hwnd, icon, &HSTRING::from(label));
                let _ = DestroyIcon(icon);
            }
            None => { let _ = tb.SetOverlayIcon(hwnd, HICON::default(), PCWSTR::null()); }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn another_holder_is_told_from_my_own_hold() {
        let name = wide(&format!(r"Local\Barbican.Test.{}", std::process::id()));
        // nobody holds it: my own hold does not count as another process
        let (mut own, first) = hold(&name);
        assert!(first && own != 0);
        assert!(!held_by_another(&name, &mut own));
        assert!(own != 0, "my hold is taken again after the look");
        // a second holder (a handle apart from mine, standing for another process) does count
        let (other, first) = hold(&name);
        assert!(!first);
        assert!(held_by_another(&name, &mut own));
        // once it is gone, only mine is left
        unsafe { let _ = windows::Win32::Foundation::CloseHandle(windows::Win32::Foundation::HANDLE(other as *mut _)); }
        assert!(!held_by_another(&name, &mut own));
        unsafe { let _ = windows::Win32::Foundation::CloseHandle(windows::Win32::Foundation::HANDLE(own as *mut _)); }
    }

    #[test]
    fn monitor_work_areas_are_real_rectangles() {
        // a session without a display may list none; any listed monitor has a size
        assert!(super::monitor_work_areas().iter().all(|&(_, _, w, h)| w > 0 && h > 0));
    }

    #[test]
    fn reads_without_writing() {
        // present on every Windows 10/11 desktop; reading never changes anything
        let _ = super::apps_use_light_theme();
        assert!(super::run_value("Barbican-test-value-that-does-not-exist").is_none());
    }
}
