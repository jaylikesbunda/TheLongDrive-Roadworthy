use roadworthy_core::consts::{BETA, RELEASE_PAGE, VERSION};
use roadworthy_core::ctx::Sink;
use roadworthy_core::payload::payload_sha256;
use roadworthy_core::run::{choose_game, run as core_run, APP_TITLE};
use roadworthy_core::state::{installation_info, installed_state};
use roadworthy_core::steam::discover_games;
use roadworthy_core::updates::check_updates as core_check_updates;
use serde::Serialize;
use serde_json::{json, Value};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tauri::{Emitter, Manager};

#[derive(Default)]
struct AppState {
    busy: Arc<AtomicBool>,
}

#[derive(Serialize)]
struct Bundled {
    title: &'static str,
    beta: &'static str,
    version: &'static str,
    build: String,
}

struct EventSink(tauri::AppHandle);

impl Sink for EventSink {
    fn out(&mut self, text: &str) {
        let _ = self.0.emit("rw-log", text);
    }
    fn err(&mut self, text: &str) {
        let _ = self.0.emit("rw-log", text);
    }
    fn progress(&mut self, percent: u8, phase: &str) {
        let _ = self.0.emit("rw-progress", json!({"percent": percent, "phase": phase}));
    }
}

#[tauri::command]
fn bundled() -> Bundled {
    Bundled { title: APP_TITLE, beta: BETA, version: VERSION, build: payload_sha256()[..12].to_string() }
}

#[tauri::command]
fn prefill() -> Option<String> {
    std::env::var("ROADWORTHY_GAME_DIR_PREFILL").ok().filter(|v| !v.is_empty())
}

#[tauri::command]
async fn detect() -> Vec<String> {
    tauri::async_runtime::spawn_blocking(|| discover_games(None).unwrap_or_default().iter().map(|g| g.display().to_string()).collect())
        .await
        .unwrap_or_default()
}

#[tauri::command]
async fn inspect(path: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let game = choose_game(Some(&path)).map_err(|e| e.to_string())?;
        let state = installed_state(&game).map_err(|e| e.to_string())?;
        let info = installation_info(&game).map_err(|e| e.to_string())?;
        Ok(json!({
            "state": state,
            "version": info.version,
            "build": info.build,
            "sourceSignature": info.source_signature,
            "relation": info.relation,
        }))
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn check_updates() -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let result = core_check_updates().map_err(|e| e.to_string())?;
        Ok(json!({"version": result.version, "tag": result.tag, "newer": result.newer, "url": result.url}))
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn browse() -> Option<String> {
    tauri::async_runtime::spawn_blocking(|| rfd::FileDialog::new().pick_folder().map(|p| p.display().to_string())).await.ok().flatten()
}

#[tauri::command]
fn open_release(url: String) -> Result<(), String> {
    use windows_sys::Win32::UI::Shell::ShellExecuteW;
    use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
    if !url.starts_with(RELEASE_PAGE) || url.chars().any(|c| c.is_control() || c == ' ') {
        return Err("Refusing to open an unexpected address.".into());
    }
    let wide = |t: &str| -> Vec<u16> { t.encode_utf16().chain(std::iter::once(0)).collect() };
    let result = unsafe { ShellExecuteW(std::ptr::null_mut(), wide("open").as_ptr(), wide(&url).as_ptr(), std::ptr::null(), std::ptr::null(), SW_SHOWNORMAL) };
    if (result as usize) <= 32 {
        return Err("Could not open the browser.".into());
    }
    Ok(())
}

#[tauri::command]
fn quit(app: tauri::AppHandle, state: tauri::State<AppState>) {
    if !state.busy.load(Ordering::SeqCst) {
        app.exit(0);
    }
}

#[tauri::command]
fn run_action(app: tauri::AppHandle, state: tauri::State<AppState>, action: String, path: String) -> Result<(), String> {
    if action != "install" && action != "restore" {
        return Err("Unknown action.".into());
    }
    if state.busy.swap(true, Ordering::SeqCst) {
        return Err("An operation is already running.".into());
    }
    let busy = state.busy.clone();
    let mut argv = vec!["--game-dir".to_string(), path, "--no-pause".to_string()];
    if action == "restore" {
        argv.push("--restore-all".into());
    }
    std::thread::spawn(move || {
        let mut sink = EventSink(app.clone());
        let code = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| core_run(&argv, &mut sink))).unwrap_or_else(|_| {
            sink.err("\nStopped: the installer hit an unexpected error.\n");
            1
        });
        busy.store(false, Ordering::SeqCst);
        let _ = app.emit("rw-done", json!({"action": action, "code": code}));
    });
    Ok(())
}

fn show_error(message: &str) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK};
    let text: Vec<u16> = message.encode_utf16().chain(std::iter::once(0)).collect();
    let title: Vec<u16> = APP_TITLE.encode_utf16().chain(std::iter::once(0)).collect();
    unsafe {
        MessageBoxW(std::ptr::null_mut(), text.as_ptr(), title.as_ptr(), MB_OK | MB_ICONERROR);
    }
}

pub fn run() {
    std::panic::set_hook(Box::new(|info| {
        show_error(&format!("The window could not start: {info}\nInstall the Microsoft Edge WebView2 Runtime and try again."));
        std::process::exit(1);
    }));
    let result = tauri::Builder::default()
        .manage(AppState::default())
        .setup(|app| {
            let folder = std::env::var_os("ROADWORTHY_WEBVIEW_DIR").map(std::path::PathBuf::from).unwrap_or_else(|| {
                std::env::var_os("LOCALAPPDATA").map(std::path::PathBuf::from).unwrap_or_else(std::env::temp_dir).join("TheLongDrive-Roadworthy").join("webview2")
            });
            std::fs::create_dir_all(&folder)?;
            let config = app.config().app.windows.first().cloned().ok_or("The window configuration is missing.")?;
            tauri::WebviewWindowBuilder::from_config(app.handle(), &config)?.data_directory(folder).build()?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![bundled, prefill, detect, inspect, check_updates, browse, open_release, run_action, quit])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                if window.state::<AppState>().busy.load(Ordering::SeqCst) {
                    api.prevent_close();
                    let _ = window.emit("rw-close-blocked", ());
                }
            }
        })
        .run(tauri::generate_context!());
    if let Err(error) = result {
        show_error(&format!("The window could not start: {error}\nInstall the Microsoft Edge WebView2 Runtime and try again."));
        std::process::exit(1);
    }
}
