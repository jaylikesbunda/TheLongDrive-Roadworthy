use roadworthy_core::ctx::Sink;
use roadworthy_core::run as core_run;
use roadworthy_core::run::APP_TITLE;
use std::io::Write;
use windows_sys::Win32::Foundation::INVALID_HANDLE_VALUE;
use windows_sys::Win32::System::Console::{GetStdHandle, STD_OUTPUT_HANDLE};
use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_ICONINFORMATION, MB_OK};

fn console_available() -> bool {
    let handle = unsafe { GetStdHandle(STD_OUTPUT_HANDLE) };
    !handle.is_null() && handle != INVALID_HANDLE_VALUE
}

struct Collected(String);

impl Sink for Collected {
    fn out(&mut self, text: &str) {
        self.0.push_str(text);
    }
    fn err(&mut self, text: &str) {
        self.0.push_str(text);
    }
}

struct Console;

impl Sink for Console {
    fn out(&mut self, text: &str) {
        let mut out = std::io::stdout();
        let _ = out.write_all(text.as_bytes());
        let _ = out.flush();
    }
    fn err(&mut self, text: &str) {
        let mut err = std::io::stderr();
        let _ = err.write_all(text.as_bytes());
        let _ = err.flush();
    }
}

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Command-line mode. A windowed build may have no console, so results fall back to a message box.
pub fn run(args: &[String]) -> i32 {
    if console_available() {
        return run_with(args, &mut Console);
    }
    let mut sink = Collected(String::new());
    let mut args = args.to_vec();
    if !args.iter().any(|a| a == "--no-pause") {
        args.push("--no-pause".into());
    }
    let code = run_with(&args, &mut sink);
    let mut text = sink.0.trim().to_string();
    if text.is_empty() {
        text = format!("Exited with code {code}.");
    }
    let text: String = text.chars().take(2000).collect();
    unsafe {
        MessageBoxW(std::ptr::null_mut(), wide(&text).as_ptr(), wide(APP_TITLE).as_ptr(), MB_OK | if code != 0 { MB_ICONERROR } else { MB_ICONINFORMATION });
    }
    code
}

fn run_with(args: &[String], sink: &mut dyn Sink) -> i32 {
    core_run::run(args, sink)
}
