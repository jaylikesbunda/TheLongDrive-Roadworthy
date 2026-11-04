use crate::consts::{ORIGINAL_SHA256, V4_BACKUP, V4_RECEIPT, VERSION};
use crate::error::{err, Result};
use crate::steam::{game_assembly, managed_dir};
use crate::util::{read_json, receipt_hash, sha256_file, valid_hash};
use crate::version::version_tuple;
use serde_json::Value;
use std::path::Path;

fn version_is(receipt: &Value, expected: f64) -> bool {
    receipt.get("version").and_then(Value::as_f64) == Some(expected)
}

/// The multiplayer receipt, validated together with the installed helper.
pub fn verified_v3(game: &Path) -> Result<Value> {
    let managed = managed_dir(game);
    let receipt = read_json(&managed.join("tldmp-v3-receipt.json"))?;
    if !version_is(&receipt, 3.0) {
        return err("tldmp-v3-receipt.json is not a multiplayer v3 receipt.");
    }
    if receipt_hash(&receipt, "originalSha256") != ORIGINAL_SHA256 {
        return err("The multiplayer receipt does not describe the supported original build.");
    }
    if !valid_hash(&receipt_hash(&receipt, "patchedSha256")) || !valid_hash(&receipt_hash(&receipt, "runtimeSha256")) {
        return err("The multiplayer receipt is missing its verified hashes.");
    }
    let helper = managed.join("Tld.Multiplayer.dll");
    if !helper.is_file() || sha256_file(&helper)? != receipt_hash(&receipt, "runtimeSha256") {
        return err("The installed multiplayer helper is missing or changed. Restore and reinstall the multiplayer patch.");
    }
    Ok(receipt)
}

pub fn installed_state(game: &Path) -> Result<String> {
    let managed = managed_dir(game);
    let current = sha256_file(&game_assembly(game))?;
    if current == ORIGINAL_SHA256 {
        return Ok("original".into());
    }
    let v4_path = managed.join(V4_RECEIPT);
    if v4_path.is_file() {
        let receipt = read_json(&v4_path)?;
        let patched = receipt_hash(&receipt, "patchedSha256");
        let original = receipt_hash(&receipt, "originalSha256");
        if !version_is(&receipt, 4.0) || !valid_hash(&patched) || !valid_hash(&original) {
            return err("tldmp-v4-receipt.json is not a recognised single-player receipt.");
        }
        let v3 = verified_v3(game)?;
        if receipt_hash(&v3, "patchedSha256") != original || receipt_hash(&v3, "runtimeSha256") != receipt_hash(&receipt, "runtimeSha256") {
            return err("The single-player receipt does not match the installed multiplayer build.");
        }
        if current == patched {
            let backup = managed.join(V4_BACKUP);
            if !backup.is_file() || sha256_file(&backup)? != original {
                return err("The single-player backup is missing or changed. Refusing to touch an unknown installation.");
            }
            return Ok("v4".into());
        }
        if current == original {
            return Ok("v3".into());
        }
        return err("tldmp-v4-receipt.json does not match the installed assembly.");
    }
    for version in [3, 1] {
        let receipt_path = managed.join(format!("tldmp-v{version}-receipt.json"));
        if !receipt_path.is_file() {
            continue;
        }
        let receipt = read_json(&receipt_path)?;
        if receipt_hash(&receipt, "patchedSha256") != current {
            continue;
        }
        let backup = managed.join(format!("Assembly-CSharp.tldmp-v{version}-original.bin"));
        if receipt.get("originalSha256").and_then(Value::as_str) != Some(ORIGINAL_SHA256) || !backup.is_file() || sha256_file(&backup)? != ORIGINAL_SHA256 {
            return err("The original backup is missing or changed. Game files were not modified.");
        }
        if version == 3 {
            let helper = managed.join("Tld.Multiplayer.dll");
            if !helper.is_file() || sha256_file(&helper)? != receipt_hash(&receipt, "runtimeSha256") {
                return err("The installed helper is missing or changed. Refusing to overwrite an unknown installation.");
            }
        }
        return Ok(format!("v{version}"));
    }
    err("Unsupported or modified game build. This patch supports 2024.11.26b_test only. It will not overwrite another mod or switch your Steam beta branch.")
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InstallationInfo {
    pub state: String,
    pub version: Option<String>,
    pub build: Option<String>,
    pub source_signature: Option<String>,
    pub relation: String,
}

/// Same integrity chain first; missing metadata only downgrades the reported version.
pub fn installation_info(game: &Path) -> Result<InstallationInfo> {
    installation_info_for(game, VERSION, crate::payload::payload_sha256())
}

pub fn installation_info_for(game: &Path, bundled_version: &str, bundled_payload: &str) -> Result<InstallationInfo> {
    let managed = managed_dir(game);
    let state = installed_state(game)?;
    let mut info = InstallationInfo { state: state.clone(), version: None, build: None, source_signature: None, relation: "original".into() };
    if state == "original" {
        return Ok(info);
    }
    let receipt = match state.as_str() {
        "v3" | "v4" => Some(read_json(&managed.join("tldmp-v3-receipt.json"))?),
        "v1" => {
            let legacy = managed.join("tldmp-v1-receipt.json");
            if legacy.is_file() {
                Some(read_json(&legacy)?)
            } else {
                None
            }
        }
        _ => None,
    };
    if let Some(receipt) = &receipt {
        if let Some(v) = receipt.get("roadworthyVersion").and_then(Value::as_str).filter(|v| version_tuple(v).is_some()) {
            info.version = Some(v.to_string());
        }
        info.build = receipt.get("installerPayloadSha256").and_then(Value::as_str).filter(|b| valid_hash(b)).map(str::to_string);
        if let Some(sig) = receipt.get("sourceSignature").and_then(Value::as_str) {
            info.source_signature = Some(sig.to_string());
        }
    }
    let installed = info.version.as_deref().and_then(version_tuple);
    let bundled = version_tuple(bundled_version);
    info.relation = if matches!((installed, bundled), (Some(i), Some(b)) if i > b) {
        "newer"
    } else if state == "v4" && info.build.as_deref() == Some(bundled_payload) {
        "current"
    } else if state == "v1" {
        "legacy"
    } else {
        "update"
    }
    .into();
    Ok(info)
}
