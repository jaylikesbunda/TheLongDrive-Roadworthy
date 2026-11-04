use crate::consts::CECIL_SHA256;
use crate::error::{err, InstallerError, Result};
use crate::util::{is_relative_to, resolve, sha256_bytes, sha256_file};
use std::io::{Cursor, Read};
use std::path::Path;

static PAYLOAD: &[u8] = include_bytes!(env!("ROADWORTHY_PAYLOAD_PATH"));

pub fn payload_sha256() -> &'static str {
    env!("ROADWORTHY_PAYLOAD_SHA256")
}

pub fn payload_bytes() -> Result<&'static [u8]> {
    if sha256_bytes(PAYLOAD) != payload_sha256() {
        return err("Embedded patch payload failed its integrity check.");
    }
    Ok(PAYLOAD)
}

pub fn extract_payload(destination: &Path) -> Result<()> {
    extract_payload_from(payload_bytes()?, destination)
}

fn unsafe_name(name: &str) -> bool {
    name.starts_with('/') || name.contains('\\') || name.contains(':') || name.split('/').any(|part| part == "..")
}

pub fn extract_payload_from(data: &[u8], destination: &Path) -> Result<()> {
    std::fs::create_dir_all(destination).map_err(|e| InstallerError(format!("Cannot create {}: {}", destination.display(), e)))?;
    let destination = resolve(destination);
    let mut archive = zip::ZipArchive::new(Cursor::new(data)).map_err(|e| InstallerError(format!("Embedded patch payload is unreadable: {e}")))?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(|e| InstallerError(format!("Embedded patch payload is unreadable: {e}")))?;
        let name = entry.name().to_string();
        if unsafe_name(&name) {
            return err("Unsafe embedded payload path.");
        }
        if name.ends_with('/') {
            continue;
        }
        let mut target = destination.clone();
        for part in name.split('/').filter(|p| !p.is_empty() && *p != ".") {
            target.push(part);
        }
        let target = resolve(&target);
        if !is_relative_to(&target, &destination) {
            return err("Embedded payload path escaped the cache directory.");
        }
        if let Some(parent) = target.parent() {
            std::fs::create_dir_all(parent).map_err(|e| InstallerError(format!("Cannot create {}: {}", parent.display(), e)))?;
        }
        let mut bytes = Vec::with_capacity(entry.size() as usize);
        entry.read_to_end(&mut bytes).map_err(|e| InstallerError(format!("Embedded patch payload is unreadable: {e}")))?;
        if !target.is_file() || std::fs::read(&target).map(|existing| existing != bytes).unwrap_or(true) {
            let mut temp_name = target.file_name().unwrap().to_os_string();
            temp_name.push(".installing");
            let temporary = target.with_file_name(temp_name);
            std::fs::write(&temporary, &bytes).map_err(|e| InstallerError(format!("Cannot write {}: {}", temporary.display(), e)))?;
            std::fs::rename(&temporary, &target).map_err(|e| InstallerError(format!("Cannot replace {}: {}", target.display(), e)))?;
        }
    }
    if sha256_file(&destination.join(".tools").join("cecil.zip"))? != CECIL_SHA256 {
        return err("Bundled Mono.Cecil dependency failed its integrity check.");
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn zip_of(entries: &[(&str, &[u8])]) -> Vec<u8> {
        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        for (name, data) in entries {
            writer.start_file(*name, zip::write::SimpleFileOptions::default()).unwrap();
            writer.write_all(data).unwrap();
        }
        writer.finish().unwrap().into_inner()
    }

    #[test]
    fn rejects_unsafe_paths_with_the_original_message() {
        for name in ["/abs.txt", "../escape.txt", "a/../../b.txt", "a\\b.txt", "C:evil.txt"] {
            let dir = tempfile::tempdir().unwrap();
            let e = extract_payload_from(&zip_of(&[(name, b"x")]), dir.path()).unwrap_err();
            assert_eq!(e.0, "Unsafe embedded payload path.", "{name}");
        }
    }

    #[test]
    fn wrong_cecil_package_is_refused_after_extraction() {
        let dir = tempfile::tempdir().unwrap();
        let e = extract_payload_from(&zip_of(&[(".tools/cecil.zip", b"not cecil"), ("a/b.txt", b"ok")]), dir.path()).unwrap_err();
        assert_eq!(e.0, "Bundled Mono.Cecil dependency failed its integrity check.");
        assert_eq!(std::fs::read(dir.path().join("a").join("b.txt")).unwrap(), b"ok");
    }

    #[test]
    fn missing_cecil_is_an_error() {
        let dir = tempfile::tempdir().unwrap();
        assert!(extract_payload_from(&zip_of(&[("a.txt", b"x")]), dir.path()).is_err());
    }

    #[test]
    fn embedded_payload_matches_its_build_time_hash() {
        assert_eq!(sha256_bytes(PAYLOAD), payload_sha256());
        assert!(payload_bytes().is_ok());
    }
}
