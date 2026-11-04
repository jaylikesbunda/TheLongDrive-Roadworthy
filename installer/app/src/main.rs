#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod cli;
mod gui;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.is_empty() || args == ["--gui"] {
        gui::run();
    } else {
        std::process::exit(cli::run(&args));
    }
}
