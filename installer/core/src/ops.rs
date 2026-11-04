use crate::consts::{CECIL_DLL_SHA256, ORIGINAL_SHA256, SINGLEPLAYER_SOURCES, V4_BACKUP, V4_RECEIPT, VERSION};
use crate::ctx::Ctx;
use crate::error::{err, InstallerError, Result};
use crate::payload::payload_sha256;
use crate::state::{installation_info, installed_state, verified_v3};
use crate::steam::{game_assembly, managed_dir};
use crate::util::{read_json, receipt_hash, sha256_bytes, sha256_file, valid_hash, write_json};
use serde_json::{json, Value};
use std::io::{BufRead, BufReader, Read};
use std::path::{Path, PathBuf};
use std::process::Command;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

fn io_err(what: &str, path: &Path, e: std::io::Error) -> InstallerError {
    InstallerError(format!("{what} {}: {e}", path.display()))
}

fn system_root() -> PathBuf {
    PathBuf::from(std::env::var_os("SystemRoot").unwrap_or_else(|| r"C:\Windows".into()))
}

pub fn powershell() -> PathBuf {
    system_root().join("System32").join("WindowsPowerShell").join("v1.0").join("powershell.exe")
}

pub fn compiler() -> PathBuf {
    system_root().join("Microsoft.NET").join("Framework64").join("v4.0.30319").join("csc.exe")
}

fn command(program: &Path) -> Command {
    let mut cmd = Command::new(program);
    #[cfg(windows)]
    std::os::windows::process::CommandExt::creation_flags(&mut cmd, CREATE_NO_WINDOW);
    cmd
}

fn system_command(program: &Path) -> Command {
    let mut cmd = command(program);
    // Use Windows PowerShell's own modules, even when started from PowerShell 7.
    let program_files = std::env::var("ProgramFiles").unwrap_or_else(|_| r"C:\Program Files".into());
    let modules = format!(
        "{};{}",
        powershell().parent().unwrap().join("Modules").display(),
        Path::new(&program_files).join("WindowsPowerShell").join("Modules").display()
    );
    cmd.env("PSModulePath", modules);
    cmd
}

/// Run a helper with no console window, echoing merged stdout+stderr into the console and log.
fn run_command(mut cmd: Command, cwd: &Path, ctx: &mut Ctx, failure: &str) -> Result<()> {
    let (reader, writer) = std::io::pipe().map_err(|e| InstallerError(format!("Cannot create a pipe: {e}")))?;
    let writer2 = writer.try_clone().map_err(|e| InstallerError(format!("Cannot create a pipe: {e}")))?;
    cmd.current_dir(cwd).stdout(writer).stderr(writer2);
    let mut child = cmd.spawn().map_err(|e| InstallerError(format!("Cannot start {}: {e}", cmd.get_program().to_string_lossy())))?;
    drop(cmd);
    let mut reader = BufReader::new(reader);
    let mut buffer = Vec::new();
    loop {
        buffer.clear();
        match reader.read_until(b'\n', &mut buffer) {
            Ok(0) | Err(_) => break,
            Ok(_) => ctx.tee(&String::from_utf8_lossy(&buffer)),
        }
    }
    let status = child.wait().map_err(|e| InstallerError(format!("Cannot wait for helper: {e}")))?;
    if !status.success() {
        return err(failure);
    }
    Ok(())
}

fn run_process(program: &Path, args: &[String], cwd: &Path, ctx: &mut Ctx, label: &str) -> Result<()> {
    let mut cmd = command(program);
    cmd.args(args);
    run_command(cmd, cwd, ctx, &format!("{label} failed. Installation was stopped."))
}

pub fn run_ps(work: &Path, ctx: &mut Ctx, script: &str, game: &Path, extra: &[&str]) -> Result<()> {
    let mut cmd = system_command(&powershell());
    cmd.args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
        .arg(work.join(script))
        .arg("-GameDirectory")
        .arg(game)
        .args(extra);
    run_command(cmd, work, ctx, &format!("{script} failed. See the log above; installation was stopped."))
}

pub fn preflight(game: &Path, need_compiler: bool) -> Result<()> {
    let _ = game;
    if !powershell().is_file() {
        return err("Windows PowerShell is unavailable. This installer targets Windows 10/11 x64.");
    }
    if need_compiler && !compiler().is_file() {
        return err("The built-in .NET Framework compiler is missing. Enable/repair Windows .NET Framework 4.x, then rerun.");
    }
    let mut cmd = system_command(&powershell());
    cmd.args(["-NoProfile", "-NonInteractive", "-Command", "if (Get-Process -Name TheLongDrive -ErrorAction SilentlyContinue) { exit 9 }"]);
    let output = cmd.output().map_err(|e| InstallerError(format!("Could not check whether the game is running: {e}")))?;
    match output.status.code() {
        Some(9) => err("Close The Long Drive, then rerun this installer."),
        Some(0) => Ok(()),
        _ => err("Could not check whether the game is running."),
    }
}

/// Copy source next to target, verify it, then swap it in one step.
pub fn stage_replace(source: &Path, target: &Path, expected: &str, suffix: &str) -> Result<()> {
    let expected = expected.to_uppercase();
    let mut name = target.file_name().unwrap().to_os_string();
    name.push(suffix);
    let temporary = target.with_file_name(name);
    let result = (|| {
        std::fs::copy(source, &temporary).map_err(|e| io_err("Cannot copy to", &temporary, e))?;
        if sha256_file(&temporary)? != expected {
            return err("Staged file failed its hash check. Nothing was replaced.");
        }
        std::fs::rename(&temporary, target).map_err(|e| io_err("Cannot replace", target, e))?;
        if sha256_file(target)? != expected {
            return err("Installed file failed its hash check. Restore the original game through Steam.");
        }
        Ok(())
    })();
    if temporary.exists() {
        let _ = std::fs::remove_file(&temporary);
    }
    result
}

fn utc_parts() -> (i64, u32, u32, u32, u32, u32) {
    let seconds = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs() as i64).unwrap_or(0);
    let days = seconds.div_euclid(86400);
    let rem = seconds.rem_euclid(86400) as u32;
    let z = days + 719468;
    let era = z.div_euclid(146097);
    let doe = z.rem_euclid(146097);
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d, rem / 3600, rem % 3600 / 60, rem % 60)
}

pub fn timestamp_compact() -> String {
    let (y, mo, d, h, mi, s) = utc_parts();
    format!("{y:04}{mo:02}{d:02}-{h:02}{mi:02}{s:02}")
}

pub fn created_utc() -> String {
    let (y, mo, d, h, mi, s) = utc_parts();
    format!("{y:04}-{mo:02}-{d:02}T{h:02}:{mi:02}:{s:02}Z")
}

fn stage_dir(work: &Path, kind: &str) -> PathBuf {
    work.join(kind).join(format!("{}-{}", timestamp_compact(), std::process::id()))
}

fn ensure_singleplayer_tools(work: &Path, ctx: &mut Ctx) -> Result<PathBuf> {
    let source_dir = work.join("packaging").join("singleplayer");
    let tools = work.join(".tools").join("singleplayer");
    std::fs::create_dir_all(&tools).map_err(|e| io_err("Cannot create", &tools, e))?;
    let (cecil, helper, patcher, verifier) = (tools.join("Mono.Cecil.dll"), tools.join("TldStableHelper.dll"), tools.join("PatchV4.exe"), tools.join("VerifyV4.exe"));
    let mut text = String::new();
    for name in SINGLEPLAYER_SOURCES {
        let bytes = std::fs::read(source_dir.join(name)).map_err(|e| io_err("Cannot read", &source_dir.join(name), e))?;
        text.push_str(&format!("{name}:{}\n", sha256_bytes(&bytes).to_lowercase()));
    }
    let signature = sha256_bytes(text.as_bytes());
    let marker = tools.join("build.txt");
    if marker.is_file()
        && std::fs::read_to_string(&marker).map(|m| m.trim() == signature).unwrap_or(false)
        && [&cecil, &helper, &patcher, &verifier].iter().all(|p| p.is_file())
    {
        return Ok(tools);
    }
    if !compiler().is_file() {
        return err("The built-in .NET Framework compiler is missing. Enable/repair Windows .NET Framework 4.x, then rerun.");
    }
    let cecil_zip = work.join(".tools").join("cecil.zip");
    let mut archive = zip::ZipArchive::new(std::fs::File::open(&cecil_zip).map_err(|e| io_err("Cannot read", &cecil_zip, e))?)
        .map_err(|e| InstallerError(format!("Bundled Mono.Cecil package is unreadable: {e}")))?;
    let mut cecil_bytes = Vec::new();
    archive
        .by_name("lib/net40/Mono.Cecil.dll")
        .map_err(|e| InstallerError(format!("Bundled Mono.Cecil package is unreadable: {e}")))?
        .read_to_end(&mut cecil_bytes)
        .map_err(|e| InstallerError(format!("Bundled Mono.Cecil package is unreadable: {e}")))?;
    if sha256_bytes(&cecil_bytes) != CECIL_DLL_SHA256 {
        return err("Bundled Mono.Cecil assembly failed its integrity check.");
    }
    std::fs::write(&cecil, &cecil_bytes).map_err(|e| io_err("Cannot write", &cecil, e))?;
    for path in [&helper, &patcher, &verifier] {
        if path.exists() {
            std::fs::remove_file(path).map_err(|e| io_err("Cannot remove", path, e))?;
        }
    }
    ctx.println("Building the single-player patcher from the bundled sources...");
    let csc = compiler();
    let s = |p: &Path| p.to_string_lossy().to_string();
    run_process(&csc, &["/nologo".into(), "/target:library".into(), "/platform:anycpu".into(), "/optimize+".into(), format!("/out:{}", s(&helper)), s(&source_dir.join("TldStable.cs"))], &tools, ctx, "Single-player helper compilation")?;
    let shared: Vec<String> = ["Checks.cs", "Expected.cs", "Invariants.cs"].iter().map(|n| s(&source_dir.join(n))).collect();
    let mut patch_args: Vec<String> = vec!["/nologo".into(), "/target:exe".into(), "/platform:anycpu".into(), "/optimize+".into(), format!("/out:{}", s(&patcher)), format!("/reference:{}", s(&cecil)), format!("/resource:{},TldStableHelper.dll", s(&helper))];
    patch_args.extend(shared.clone());
    patch_args.push(s(&source_dir.join("PatchV4.cs")));
    run_process(&csc, &patch_args, &tools, ctx, "PatchV4 compilation")?;
    let mut verify_args: Vec<String> = vec!["/nologo".into(), "/target:exe".into(), "/platform:anycpu".into(), "/optimize+".into(), format!("/out:{}", s(&verifier)), format!("/reference:{}", s(&cecil))];
    verify_args.extend(shared);
    verify_args.push(s(&source_dir.join("VerifyV4.cs")));
    run_process(&csc, &verify_args, &tools, ctx, "VerifyV4 compilation")?;
    if ![&cecil, &helper, &patcher, &verifier].iter().all(|p| p.is_file()) {
        return err("The single-player tools were not produced by the bundled compiler.");
    }
    std::fs::write(&marker, format!("{signature}\n")).map_err(|e| io_err("Cannot write", &marker, e))?;
    Ok(tools)
}

/// Patch a copy of a multiplayer v3 assembly with the v4 pass and verify it.
pub fn build_singleplayer(v3_file: &Path, baseline: &str, managed: &Path, work: &Path, ctx: &mut Ctx) -> Result<(PathBuf, Vec<Value>)> {
    let tools = ensure_singleplayer_tools(work, ctx)?;
    let stage = stage_dir(work, "v4-stage");
    std::fs::create_dir_all(&stage).map_err(|e| io_err("Cannot create", &stage, e))?;
    let source = stage.join("Assembly-CSharp.v3.dll");
    let output = stage.join("Assembly-CSharp.v4.dll");
    std::fs::copy(v3_file, &source).map_err(|e| io_err("Cannot copy to", &source, e))?;
    if sha256_file(&source)? != baseline {
        return err("The multiplayer assembly changed while copying. Nothing was installed.");
    }
    let s = |p: &Path| p.to_string_lossy().to_string();
    ctx.println("Applying the single-player pass to a copy...");
    run_process(&tools.join("PatchV4.exe"), &[s(&source), s(&output), s(managed), baseline.to_string()], &stage, ctx, "PatchV4")?;
    ctx.println("Verifying the patched copy...");
    run_process(&tools.join("VerifyV4.exe"), &[s(&source), s(&output), baseline.to_string()], &stage, ctx, "VerifyV4")?;
    if sha256_file(&output)? == baseline {
        return err("The single-player pass produced an unchanged assembly.");
    }
    let receipt = read_json(&stage.join("v4-receipt.json"))?;
    match receipt.get("edits") {
        Some(Value::Array(edits)) if !edits.is_empty() => Ok((output, edits.clone())),
        _ => err("The single-player pass did not report its edits."),
    }
}

/// Build and verify v3 from the embedded sources; returns its manifest path.
pub fn build_multiplayer(work: &Path, ctx: &mut Ctx, game: &Path) -> Result<PathBuf> {
    let output = stage_dir(work, "v3-build");
    std::fs::create_dir_all(output.parent().unwrap()).map_err(|e| io_err("Cannot create", output.parent().unwrap(), e))?;
    ctx.println("Building the patch from source for this game...");
    run_ps(work, ctx, "Build-Patch.ps1", game, &["-OutputDirectory", &output.to_string_lossy()])?;
    Ok(output.join("manifest.json"))
}

pub struct Prepared {
    pub output: PathBuf,
    pub edits: Vec<Value>,
    pub sha256: String,
    pub baseline: String,
}

fn version_is(receipt: &Value, expected: f64) -> bool {
    receipt.get("version").and_then(Value::as_f64) == Some(expected)
}

/// Apply v4 to a copy of the installed multiplayer assembly, verify, then swap.
pub fn apply_singleplayer(game: &Path, work: &Path, ctx: &mut Ctx, prepared: Option<Prepared>) -> Result<String> {
    let managed = managed_dir(game);
    let assembly = game_assembly(game);
    let v3 = verified_v3(game)?;
    let baseline = receipt_hash(&v3, "patchedSha256");
    let runtime = receipt_hash(&v3, "runtimeSha256");
    if sha256_file(&assembly)? != baseline {
        return err("The installed multiplayer assembly does not match tldmp-v3-receipt.json. Restore and reinstall the multiplayer patch.");
    }
    let (output, edits) = match prepared {
        Some(p) => {
            if !p.output.is_file() || p.edits.is_empty() {
                return err("The prepared single-player candidate is missing.");
            }
            if sha256_file(&p.output)? != p.sha256 {
                return err("The prepared single-player candidate changed after verification.");
            }
            if p.baseline != baseline {
                return err("The prepared single-player candidate does not match the installed multiplayer build.");
            }
            (p.output, p.edits)
        }
        None => build_singleplayer(&assembly, &baseline, &managed, work, ctx)?,
    };
    let patched = sha256_file(&output)?;
    let backup = managed.join(V4_BACKUP);
    if backup.is_file() {
        if sha256_file(&backup)? != baseline {
            return err("An existing single-player backup is not the installed multiplayer build.");
        }
    } else {
        stage_replace(&assembly, &backup, &baseline, ".v4backup")?;
    }
    stage_replace(&output, &assembly, &patched, ".v4new")?;
    let size = std::fs::metadata(&output).map_err(|e| io_err("Cannot read", &output, e))?.len();
    write_json(
        &managed.join(V4_RECEIPT),
        &json!({
            "version": 4,
            "patch": "single-player",
            "createdUtc": created_utc(),
            "input": {"file": "Assembly-CSharp.dll (multiplayer v3)", "sha256": baseline},
            "output": {"file": "Assembly-CSharp.dll", "sha256": patched, "bytes": size},
            "originalSha256": baseline,
            "patchedSha256": patched,
            "runtimeSha256": runtime,
            "backup": V4_BACKUP,
            "verify": "PASS",
            "edits": edits,
        }),
    )?;
    Ok(patched)
}

/// Swap the verified v4 backup back in and remove the v4 receipt.
pub fn restore_singleplayer(game: &Path, ctx: &mut Ctx) -> Result<String> {
    let managed = managed_dir(game);
    let assembly = game_assembly(game);
    let receipt = read_json(&managed.join(V4_RECEIPT))?;
    if !version_is(&receipt, 4.0) {
        return err("tldmp-v4-receipt.json is not a recognised single-player receipt.");
    }
    let patched = receipt_hash(&receipt, "patchedSha256");
    let baseline = receipt_hash(&receipt, "originalSha256");
    if !valid_hash(&patched) || !valid_hash(&baseline) {
        return err("The single-player receipt is missing its verified hashes.");
    }
    if sha256_file(&assembly)? != patched {
        return err("The installed assembly is not the build in tldmp-v4-receipt.json. Refusing to overwrite it.");
    }
    let v3 = verified_v3(game)?;
    if receipt_hash(&v3, "patchedSha256") != baseline || receipt_hash(&v3, "runtimeSha256") != receipt_hash(&receipt, "runtimeSha256") {
        return err("The multiplayer receipt no longer matches the single-player backup. Refusing.");
    }
    let backup = managed.join(V4_BACKUP);
    if !backup.is_file() {
        return err("The single-player backup is missing. Verify the game files in Steam to recover.");
    }
    if sha256_file(&backup)? != baseline {
        return err("The single-player backup changed. Refusing to overwrite the installed assembly.");
    }
    stage_replace(&backup, &assembly, &baseline, ".v4restore")?;
    for path in [managed.join(V4_RECEIPT), backup] {
        let _ = std::fs::remove_file(path);
    }
    ctx.println("Single-player pass removed; the verified multiplayer build is back.");
    Ok(baseline)
}

fn clear_singleplayer_files(game: &Path) {
    let managed = managed_dir(game);
    let (receipt_path, backup) = (managed.join(V4_RECEIPT), managed.join(V4_BACKUP));
    let Ok(receipt) = read_json(&receipt_path) else { return };
    if version_is(&receipt, 4.0) && backup.is_file() && sha256_file(&backup).map(|h| h == receipt_hash(&receipt, "originalSha256")).unwrap_or(false) {
        for path in [receipt_path, backup] {
            let _ = std::fs::remove_file(path);
        }
    }
}

fn clear_multiplayer_files(game: &Path) {
    let assembly = game_assembly(game);
    if !assembly.is_file() || sha256_file(&assembly).map(|h| h != ORIGINAL_SHA256).unwrap_or(true) {
        return;
    }
    let managed = assembly.parent().unwrap().to_path_buf();
    let (receipt_path, backup) = (managed.join("tldmp-v3-receipt.json"), managed.join("Assembly-CSharp.tldmp-v3-original.bin"));
    let Ok(receipt) = read_json(&receipt_path) else { return };
    if receipt_hash(&receipt, "originalSha256") != ORIGINAL_SHA256 || !backup.is_file() || sha256_file(&backup).map(|h| h != ORIGINAL_SHA256).unwrap_or(true) {
        return;
    }
    for path in [receipt_path, backup] {
        let _ = std::fs::remove_file(path);
    }
}

pub fn restore_multiplayer(work: &Path, ctx: &mut Ctx, state: &str, game: &Path) -> Result<()> {
    let script = if state == "v3" { "Manage-Stability.ps1" } else { "Manage-Patch.ps1" };
    run_ps(work, ctx, script, game, &["-Action", "Restore"])?;
    clear_singleplayer_files(game);
    clear_multiplayer_files(game);
    Ok(())
}

/// Keep the verified installed chain so a failed update can be recovered by hand.
pub fn snapshot_managed(game: &Path, cache: &Path) -> Result<PathBuf> {
    let managed = managed_dir(game);
    std::fs::create_dir_all(cache).map_err(|e| io_err("Cannot create", cache, e))?;
    let mut folder = None;
    for attempt in 0..100u32 {
        let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.subsec_nanos()).unwrap_or(0);
        let candidate = cache.join(format!("rollback-{:08x}{:02x}", nanos ^ std::process::id().rotate_left(13), attempt));
        if std::fs::create_dir(&candidate).is_ok() {
            folder = Some(candidate);
            break;
        }
    }
    let folder = folder.ok_or_else(|| InstallerError("Cannot create a rollback folder.".into()))?;
    let mut files = serde_json::Map::new();
    for name in [
        "Assembly-CSharp.dll", "Tld.Multiplayer.dll", "tldmp-v1-receipt.json", "tldmp-v3-receipt.json", "tldmp-v4-receipt.json",
        "Assembly-CSharp.tldmp-v1-original.bin", "Assembly-CSharp.tldmp-v3-original.bin", V4_BACKUP,
    ] {
        let source = managed.join(name);
        if source.is_file() {
            std::fs::copy(&source, folder.join(name)).map_err(|e| io_err("Cannot copy", &source, e))?;
            files.insert(name.to_string(), Value::String(sha256_file(&folder.join(name))?));
        }
    }
    write_json(&folder.join("MANIFEST.json"), &json!({"createdUtc": created_utc(), "game": game.to_string_lossy(), "files": files}))?;
    Ok(folder)
}

/// Record this installer's release/build identity on a receipt we own, preserving every field.
pub fn stamp_metadata(receipt_path: &Path) -> Result<()> {
    let mut receipt = read_json(receipt_path)?;
    let map = receipt.as_object_mut().unwrap();
    map.insert("roadworthyVersion".into(), Value::String(VERSION.into()));
    map.insert("installerPayloadSha256".into(), Value::String(payload_sha256().into()));
    write_json(receipt_path, &receipt)
}

pub fn info_line(game: &Path) -> Result<String> {
    let state = installed_state(game)?;
    let info = installation_info(game)?;
    let shown = info.build.clone().or(info.source_signature.clone()).unwrap_or_else(|| "unknown".into());
    Ok(format!(
        "Game: {}\nCurrent installation: {} (release {}, build {})\nBundled release: {} (payload {})",
        game.display(),
        state,
        info.version.as_deref().unwrap_or("unknown"),
        shown.chars().take(12).collect::<String>(),
        VERSION,
        &payload_sha256()[..12]
    ))
}
