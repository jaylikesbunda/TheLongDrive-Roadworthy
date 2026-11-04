# Installer dependencies

Direct crates (exact pins unless noted). Transitive versions are locked in Cargo.lock; those published
under 7 days before 2026-11-04 were pinned back, except wasm-only crates (js-sys, wasm-bindgen*, web-sys,
jiff*) that are not part of the Windows build graph.

| Crate | Why |
|---|---|
| serde, serde_json (preserve_order) | Receipts and update JSON, same key order as the Python installer |
| sha2 | SHA-256 for payload, receipts and backups |
| zip (deflate) | Embedded payload and Mono.Cecil package |
| winreg | Steam install lookup |
| ureq 2.10.1 + native-tls | GitHub release check over Windows schannel (no C toolchain) |
| tauri 2.12.1, tauri-build 2.7.1 | Window shell (WebView2) |
| rfd 0.17.2 | Folder picker |
| windows-sys 0.61.2 | Console probe, message boxes, open release page |
| tempfile (dev only) | Unit tests |
