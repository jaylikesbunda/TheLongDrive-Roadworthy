use crate::consts::{ORIGINAL_SHA256, V4_RECEIPT, VERSION};
use crate::ctx::{Ctx, Sink};
use crate::error::{err, InstallerError, Result};
use crate::ops::*;
use crate::payload::{extract_payload, payload_sha256};
use crate::state::{installation_info, installed_state};
use crate::steam::{discover_games, managed_dir, valid_game};
use crate::updates::check_updates;
use crate::util::{read_json, resolve, sha256_file, valid_hash};
use serde_json::Value;
use std::fs::OpenOptions;
use std::io::{IsTerminal, Write};
use std::path::{Path, PathBuf};

pub const APP_TITLE: &str = "The Long Drive: Roadworthy";

#[derive(Debug, Default, Clone, PartialEq, Eq)]
pub struct Args {
    pub game_dir: Option<String>,
    pub restore: bool,
    pub restore_all: bool,
    pub test_only: bool,
    pub status: bool,
    pub check_updates: bool,
    pub list: bool,
    pub update: bool,
    pub allow_downgrade: bool,
    pub no_pause: bool,
    pub cache_dir: Option<String>,
    pub help: bool,
}

const ACTIONS: [&str; 7] = ["--restore", "--restore-all", "--test-only", "--status", "--check-updates", "--list", "--update"];

pub fn usage() -> String {
    format!(
        "usage: roadworthy-cli [-h] [--game-dir GAME_DIR]\n                      [--restore | --restore-all | --test-only | --status | --check-updates | --list | --update]\n                      [--allow-downgrade] [--no-pause]\n\n{APP_TITLE} installer (multiplayer + single-player). No arguments opens the window.\n\noptions:\n  -h, --help           show this help message and exit\n  --game-dir GAME_DIR  Override Steam game detection\n  --restore            Undo the newest pass only (v4 -> v3, or v3 -> vanilla)\n  --restore-all        Undo every pass (v4 -> v3 -> vanilla)\n  --test-only          Build and verify the patch without changing the game\n  --status             Inspect installation without changing files\n  --check-updates      Check GitHub for a newer published release (read-only)\n  --list               List detected Steam installations\n  --update             Update an older recognized install to this bundled build\n  --allow-downgrade    Permit replacing a newer recognized Roadworthy release\n  --no-pause           Exit without waiting for Enter\n"
    )
}

pub fn parse_args(argv: &[String]) -> std::result::Result<Args, String> {
    let mut args = Args::default();
    let mut action: Option<String> = None;
    let mut i = 0;
    while i < argv.len() {
        let arg = argv[i].as_str();
        let (name, inline) = match arg.split_once('=') {
            Some((n, v)) if n.starts_with("--") => (n, Some(v.to_string())),
            _ => (arg, None),
        };
        match name {
            "-h" | "--help" => args.help = true,
            "--game-dir" | "--cache-dir" => {
                let value = match inline {
                    Some(v) => v,
                    None => {
                        i += 1;
                        argv.get(i).cloned().ok_or_else(|| format!("argument {name}: expected one argument"))?
                    }
                };
                if name == "--game-dir" { args.game_dir = Some(value) } else { args.cache_dir = Some(value) }
            }
            "--allow-downgrade" => args.allow_downgrade = true,
            "--no-pause" => args.no_pause = true,
            n if ACTIONS.contains(&n) && inline.is_none() => {
                if let Some(previous) = &action {
                    return Err(format!("argument {n}: not allowed with argument {previous}"));
                }
                action = Some(n.to_string());
                match n {
                    "--restore" => args.restore = true,
                    "--restore-all" => args.restore_all = true,
                    "--test-only" => args.test_only = true,
                    "--status" => args.status = true,
                    "--check-updates" => args.check_updates = true,
                    "--list" => args.list = true,
                    _ => args.update = true,
                }
            }
            _ => return Err(format!("unrecognized arguments: {arg}")),
        }
        i += 1;
    }
    Ok(args)
}

pub fn choose_game(explicit: Option<&str>) -> Result<PathBuf> {
    if let Some(path) = explicit {
        let game = resolve(Path::new(path));
        if !valid_game(&game) {
            return err(format!("Not a The Long Drive installation: {}", game.display()));
        }
        return Ok(game);
    }
    let games = discover_games(None)?;
    if games.is_empty() {
        return err("Steam game not found. Run with --game-dir \"FULL PATH TO THE GAME\".");
    }
    let supported: Vec<PathBuf> = games.iter().filter(|g| installed_state(g).is_ok()).cloned().collect();
    let choices = if supported.is_empty() { games } else { supported };
    if choices.len() != 1 {
        let lines: Vec<String> = choices.iter().map(|g| format!("  {}", g.display())).collect();
        return err(format!("Multiple installations found. Use --game-dir to select one:\n{}", lines.join("\n")));
    }
    Ok(choices[0].clone())
}

pub struct InstallerLock(std::fs::File);

pub fn installer_lock(cache: &Path) -> Result<InstallerLock> {
    std::fs::create_dir_all(cache).map_err(|e| InstallerError(format!("Cannot create {}: {e}", cache.display())))?;
    let path = cache.join("installer.lock");
    let mut file = OpenOptions::new().read(true).write(true).create(true).truncate(false).open(&path).map_err(|e| InstallerError(format!("Cannot open {}: {e}", path.display())))?;
    if file.metadata().map(|m| m.len() == 0).unwrap_or(false) {
        let _ = file.write_all(b"0");
        let _ = file.flush();
    }
    file.try_lock().map_err(|_| InstallerError("Another patch installer is already running. Close it first.".into()))?;
    Ok(InstallerLock(file))
}

impl Drop for InstallerLock {
    fn drop(&mut self) {
        let _ = self.0.unlock();
    }
}

fn default_cache() -> PathBuf {
    let local = std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(std::env::var_os("USERPROFILE").unwrap_or_default()).join("AppData").join("Local"));
    local.join("TheLongDrive-Roadworthy")
}

pub fn run(argv: &[String], sink: &mut dyn Sink) -> i32 {
    let args = match parse_args(argv) {
        Ok(a) => a,
        Err(message) => {
            sink.err(&format!("{}roadworthy-cli: error: {message}\n", usage().lines().take(3).collect::<Vec<_>>().join("\n") + "\n"));
            return 2;
        }
    };
    if args.help {
        sink.out(&usage());
        return 0;
    }
    let code = match run_inner(&args, sink) {
        Ok(code) => code,
        Err(e) => {
            sink.err(&format!("\nStopped: {e}\n"));
            1
        }
    };
    if !args.no_pause && std::io::stdin().is_terminal() {
        sink.out("\nPress Enter to close...");
        let mut line = String::new();
        let _ = std::io::stdin().read_line(&mut line);
    }
    code
}

fn blocked_downgrade(info: &crate::state::InstallationInfo, args: &Args) -> bool {
    info.relation == "newer" && !args.allow_downgrade
}

fn run_inner(args: &Args, sink: &mut dyn Sink) -> Result<i32> {
    let mut ctx = Ctx::new(sink);
    ctx.println(&format!("{APP_TITLE} v{VERSION} - multiplayer + single-player"));
    ctx.println("The host and every joining player must use this same patch build.");
    if args.list {
        for game in discover_games(None)? {
            ctx.println(&game.display().to_string());
        }
        return Ok(0);
    }
    if args.check_updates {
        let result = check_updates()?;
        match result.version {
            None => ctx.println(&format!("Bundled release: {VERSION}\nNo published releases found.")),
            Some(_) => {
                ctx.println(&format!(
                    "Bundled release: {VERSION}\nLatest published: {} ({}; prereleases included)",
                    result.tag.as_deref().unwrap_or(""),
                    if result.newer { "newer" } else { "not newer" }
                ));
                if result.newer {
                    ctx.println(result.url.as_deref().unwrap_or(""));
                }
            }
        }
        return Ok(0);
    }
    let game = choose_game(args.game_dir.as_deref())?;
    ctx.emit(0, "Checking the current installation");
    let mut state = installed_state(&game)?;
    let info = installation_info(&game)?;
    ctx.println(&info_line(&game)?);
    if args.status {
        ctx.emit(100, "Inspection complete");
        return Ok(0);
    }
    let restoring = args.restore || args.restore_all;
    if restoring && state == "original" {
        ctx.println("Original game already installed.");
        ctx.emit(100, "Nothing to restore");
        return Ok(0);
    }
    if !restoring && !args.test_only {
        if info.relation == "current" {
            ctx.println("This bundled build is already installed (multiplayer v3 + single-player v4).");
            ctx.emit(100, "Already up to date");
            return Ok(0);
        }
        if blocked_downgrade(&info, args) {
            return err(format!(
                "A newer Roadworthy release ({}) is installed. It will not be replaced automatically; rerun with --allow-downgrade only if you mean it.",
                info.version.as_deref().unwrap_or("None")
            ));
        }
    }
    preflight(&game, !restoring)?;
    ctx.emit(5, "Preflight checks complete");
    let cache = args.cache_dir.as_deref().map(|c| resolve(Path::new(c))).unwrap_or_else(default_cache);
    let work = cache.join(format!("src-{}", payload_sha256()[..16].to_lowercase()));
    let _lock = installer_lock(&cache)?;
    extract_payload(&work)?;
    ctx.emit(10, "Bundled sources extracted");
    let logs = cache.join("logs");
    std::fs::create_dir_all(&logs).map_err(|e| InstallerError(format!("Cannot create {}: {e}", logs.display())))?;
    let log_path = logs.join(format!("{}-{}.log", timestamp_compact(), std::process::id()));
    ctx.println(&format!("Sources and builds: {}\nLog: {}", work.display(), log_path.display()));
    ctx.log = Some(std::fs::File::create(&log_path).map_err(|e| InstallerError(format!("Cannot create {}: {e}", log_path.display())))?);
    state = installed_state(&game)?;
    let info = installation_info(&game)?;
    if !restoring && !args.test_only && blocked_downgrade(&info, args) {
        return err("A newer Roadworthy release is installed; refusing to downgrade.");
    }
    let managed = managed_dir(&game);
    if restoring {
        let entered = state.clone();
        if entered == "v4" {
            restore_singleplayer(&game, &mut ctx)?;
            state = "v3".into();
            ctx.emit(55, "Single-player pass removed");
        } else {
            ctx.emit(55, "No single-player pass present");
        }
        let want_vanilla = args.restore_all || entered != "v4";
        if want_vanilla && (state == "v1" || state == "v3") {
            restore_multiplayer(&work, &mut ctx, &state, &game)?;
            ctx.emit(90, "Multiplayer patch removed");
            ctx.println("Original game restored. Saves unchanged.");
        } else {
            ctx.emit(90, if state != "original" { "Multiplayer patch kept" } else { "Original game already installed" });
        }
        let expected = if want_vanilla { "original" } else { "v3" };
        let now = installed_state(&game)?;
        if now != expected {
            return err(format!("Restore finished in the '{now}' state instead of '{expected}'."));
        }
        ctx.emit(100, "Verified installation restored");
    } else if args.test_only {
        let manifest = read_json(&build_multiplayer(&work, &mut ctx, &game)?)?;
        ctx.emit(40, "v3 candidate built and verified");
        let patched = manifest_path(&manifest, "patched")?;
        build_singleplayer(&patched, &manifest_hash(&manifest, "patchedSha256"), &managed, &work, &mut ctx)?;
        ctx.emit(90, "v4 candidate built and verified");
        ctx.emit(100, "All build and verification checks passed");
        ctx.println("Build and verification passed. The game installation and saves were not changed.");
    } else {
        let manifest_file = build_multiplayer(&work, &mut ctx, &game)?;
        let manifest = read_json(&manifest_file)?;
        let candidate_v3 = manifest_path(&manifest, "patched")?;
        let candidate_v3_hash = sha256_file(&candidate_v3)?;
        let candidate_runtime = manifest_path(&manifest, "runtime")?;
        let baseline_raw = manifest.get("patchedSha256").and_then(Value::as_str);
        let runtime_raw = manifest.get("runtimeSha256").and_then(Value::as_str).unwrap_or("");
        let baseline_v3 = baseline_raw.map(str::to_uppercase).unwrap_or_default();
        let runtime_hash = runtime_raw.to_uppercase();
        if baseline_raw.is_none()
            || baseline_v3 != candidate_v3_hash
            || !valid_hash(runtime_raw)
            || sha256_file(&candidate_runtime)? != runtime_hash
            || manifest.get("originalSha256").and_then(Value::as_str) != Some(ORIGINAL_SHA256)
        {
            return err("The v3 build manifest failed its integrity check.");
        }
        ctx.emit(40, "v3 candidate built and verified");
        let (v4_output, v4_edits) = build_singleplayer(&candidate_v3, &baseline_v3, &managed, &work, &mut ctx)?;
        let prepared = Prepared { sha256: sha256_file(&v4_output)?, output: v4_output.clone(), edits: v4_edits, baseline: baseline_v3.clone() };
        ctx.emit(65, "v4 candidate built and verified");
        if sha256_file(&candidate_v3)? != candidate_v3_hash || sha256_file(&candidate_runtime)? != runtime_hash || sha256_file(&v4_output)? != prepared.sha256 {
            return err("A verified candidate changed on disk. Refusing to touch the installed chain.");
        }
        state = installed_state(&game)?;
        let info = installation_info(&game)?;
        if blocked_downgrade(&info, args) {
            return err("A newer Roadworthy release is installed; refusing to downgrade.");
        }
        let rollback = snapshot_managed(&game, &cache)?;
        ctx.println(&format!("Recovery copies of the previous chain: {}", rollback.display()));
        preflight(&game, false)?;
        if state == "v4" {
            restore_singleplayer(&game, &mut ctx)?;
            state = "v3".into();
        }
        if state == "v1" || state == "v3" {
            restore_multiplayer(&work, &mut ctx, &state, &game)?;
            state = "original".into();
        }
        ctx.emit(70, if state == "original" { "Previous patch restored to the original game" } else { "Original game already installed" });
        run_ps(&work, &mut ctx, "Manage-Stability.ps1", &game, &["-Action", "Install", "-ManifestPath", &manifest_file.to_string_lossy()])?;
        ctx.emit(85, "Candidate multiplayer build installed");
        apply_singleplayer(&game, &work, &mut ctx, Some(prepared))?;
        ctx.emit(95, "Candidate single-player pass installed");
        if installed_state(&game)? != "v4" {
            return err("Post-install verification did not find the installed v4 chain.");
        }
        stamp_metadata(&managed.join("tldmp-v3-receipt.json"))?;
        stamp_metadata(&managed.join(V4_RECEIPT))?;
        ctx.emit(100, "Installed chain verified and stamped");
        ctx.println("Done. Multiplayer v3 and the single-player pass are installed with verified backups. Saves unchanged.");
    }
    Ok(0)
}

fn manifest_path(manifest: &Value, key: &str) -> Result<PathBuf> {
    manifest.get(key).and_then(Value::as_str).map(PathBuf::from).ok_or_else(|| InstallerError(format!("The v3 build manifest has no '{key}'.")))
}

fn manifest_hash(manifest: &Value, key: &str) -> String {
    manifest.get(key).and_then(Value::as_str).unwrap_or("").to_uppercase()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(items: &[&str]) -> std::result::Result<Args, String> {
        parse_args(&items.iter().map(|s| s.to_string()).collect::<Vec<_>>())
    }

    #[test]
    fn parses_every_flag() {
        let a = parse(&["--game-dir", "D:\\G", "--status", "--allow-downgrade", "--no-pause", "--cache-dir=C:\\c"]).unwrap();
        assert_eq!(a.game_dir.as_deref(), Some("D:\\G"));
        assert!(a.status && a.allow_downgrade && a.no_pause);
        assert_eq!(a.cache_dir.as_deref(), Some("C:\\c"));
        for flag in ACTIONS {
            assert!(parse(&[flag]).is_ok(), "{flag}");
        }
    }

    #[test]
    fn actions_are_mutually_exclusive_and_unknown_args_fail() {
        assert!(parse(&["--restore", "--status"]).unwrap_err().contains("not allowed with argument --restore"));
        assert!(parse(&["--bogus"]).unwrap_err().contains("unrecognized arguments"));
        assert!(parse(&["--game-dir"]).unwrap_err().contains("expected one argument"));
    }

    #[test]
    fn explicit_game_must_be_a_game_folder() {
        let dir = tempfile::tempdir().unwrap();
        let e = choose_game(Some(&dir.path().to_string_lossy())).unwrap_err();
        assert!(e.0.starts_with("Not a The Long Drive installation: "));
    }

    #[test]
    fn lock_is_exclusive_until_dropped() {
        let dir = tempfile::tempdir().unwrap();
        let first = installer_lock(dir.path()).unwrap();
        assert_eq!(installer_lock(dir.path()).err().unwrap().0, "Another patch installer is already running. Close it first.");
        drop(first);
        assert!(installer_lock(dir.path()).is_ok());
    }
}
