use crate::error::{err, InstallerError, Result};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::fs::File;
use std::io::Read;
use std::path::{Path, PathBuf};

pub fn hex_upper(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{:02X}", b)).collect()
}

pub fn sha256_bytes(data: &[u8]) -> String {
    hex_upper(&Sha256::digest(data))
}

pub fn sha256_file(path: &Path) -> Result<String> {
    let mut file = File::open(path).map_err(|e| InstallerError(format!("Cannot read {}: {}", path.display(), e)))?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 1024 * 1024];
    loop {
        let n = file.read(&mut buffer).map_err(|e| InstallerError(format!("Cannot read {}: {}", path.display(), e)))?;
        if n == 0 {
            break;
        }
        hasher.update(&buffer[..n]);
    }
    Ok(hex_upper(&hasher.finalize()))
}

/// utf-8-sig tolerant JSON object reader; anything unreadable is the receipt error.
pub fn read_json(path: &Path) -> Result<Value> {
    let fail = || InstallerError(format!("Cannot read installation receipt: {}", path.display()));
    let bytes = std::fs::read(path).map_err(|_| fail())?;
    let text = std::str::from_utf8(&bytes).map_err(|_| fail())?;
    let text = text.strip_prefix('\u{feff}').unwrap_or(text);
    let value: Value = serde_json::from_str(text).map_err(|_| fail())?;
    if !value.is_object() {
        return Err(fail());
    }
    Ok(value)
}

pub fn write_json(path: &Path, data: &Value) -> Result<()> {
    let mut name = path.file_name().map(|n| n.to_os_string()).unwrap_or_default();
    name.push(".tmp");
    let temporary = path.with_file_name(name);
    let mut text = serde_json::to_string_pretty(data).map_err(|e| InstallerError(e.to_string()))?;
    text.push('\n');
    std::fs::write(&temporary, text).map_err(|e| InstallerError(format!("Cannot write {}: {}", temporary.display(), e)))?;
    std::fs::rename(&temporary, path).map_err(|e| InstallerError(format!("Cannot replace {}: {}", path.display(), e)))
}

pub fn valid_hash(value: &str) -> bool {
    value.len() == 64 && value.chars().all(|c| "0123456789ABCDEF".contains(c))
}

pub fn receipt_hash(receipt: &Value, key: &str) -> String {
    receipt.get(key).and_then(Value::as_str).map(|s| s.to_uppercase()).unwrap_or_default()
}

/// Mirrors Path(...).expanduser().resolve(): absolute, normalised, no \\?\ prefix, works for missing paths.
pub fn resolve(path: &Path) -> PathBuf {
    let candidate = std::fs::canonicalize(path).or_else(|_| std::path::absolute(path)).unwrap_or_else(|_| path.to_path_buf());
    strip_verbatim(candidate)
}

fn strip_verbatim(path: PathBuf) -> PathBuf {
    let text = path.to_string_lossy();
    if let Some(rest) = text.strip_prefix(r"\\?\UNC\") {
        return PathBuf::from(format!(r"\\{}", rest));
    }
    if let Some(rest) = text.strip_prefix(r"\\?\") {
        return PathBuf::from(rest);
    }
    path
}

pub fn unique_paths<I: IntoIterator<Item = PathBuf>>(paths: I) -> Vec<PathBuf> {
    let mut found: Vec<(String, PathBuf)> = Vec::new();
    for path in paths {
        let resolved = resolve(&path);
        let key = resolved.to_string_lossy().to_lowercase();
        if !found.iter().any(|(k, _)| *k == key) {
            found.push((key, resolved));
        }
    }
    found.into_iter().map(|(_, p)| p).collect()
}

/// Case-insensitive component-wise Path.is_relative_to for Windows paths.
pub fn is_relative_to(path: &Path, base: &Path) -> bool {
    let norm = |p: &Path| -> Vec<String> { p.components().map(|c| c.as_os_str().to_string_lossy().to_lowercase()).collect() };
    let (a, b) = (norm(path), norm(base));
    a.len() >= b.len() && a[..b.len()] == b[..]
}

pub fn require<T>(condition: bool, message: &str, value: T) -> Result<T> {
    if condition {
        Ok(value)
    } else {
        err(message)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hashes_are_upper_hex() {
        assert_eq!(sha256_bytes(b""), "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
        assert!(valid_hash(&sha256_bytes(b"x")));
        assert!(!valid_hash(&sha256_bytes(b"x").to_lowercase()));
        assert!(!valid_hash("ABC"));
    }

    #[test]
    fn receipt_hash_uppercases_strings_only() {
        let v: Value = serde_json::json!({"a": "abc", "b": 5});
        assert_eq!(receipt_hash(&v, "a"), "ABC");
        assert_eq!(receipt_hash(&v, "b"), "");
        assert_eq!(receipt_hash(&v, "c"), "");
    }

    #[test]
    fn read_json_accepts_bom_and_rejects_non_objects() {
        let dir = tempfile::tempdir().unwrap();
        let ok = dir.path().join("ok.json");
        std::fs::write(&ok, b"\xef\xbb\xbf{\"version\": 3}").unwrap();
        assert_eq!(read_json(&ok).unwrap()["version"], 3);
        let list = dir.path().join("list.json");
        std::fs::write(&list, b"[1]").unwrap();
        assert!(read_json(&list).is_err());
        assert!(read_json(&dir.path().join("missing.json")).unwrap_err().0.starts_with("Cannot read installation receipt:"));
    }

    #[test]
    fn write_json_replaces_atomically() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("r.json");
        write_json(&path, &serde_json::json!({"a": 1})).unwrap();
        write_json(&path, &serde_json::json!({"a": 2})).unwrap();
        assert_eq!(read_json(&path).unwrap()["a"], 2);
        assert!(!dir.path().join("r.json.tmp").exists());
    }

    #[test]
    fn unique_paths_ignore_case_and_keep_first() {
        let dir = tempfile::tempdir().unwrap();
        let a = dir.path().join("Steam");
        let b = dir.path().join("STEAM");
        let found = unique_paths(vec![a.clone(), b]);
        assert_eq!(found.len(), 1);
        assert_eq!(found[0], resolve(&a));
    }

    #[test]
    fn relative_check_is_case_insensitive() {
        assert!(is_relative_to(Path::new(r"C:\Steam\Common\Game"), Path::new(r"c:\steam\common")));
        assert!(!is_relative_to(Path::new(r"C:\Steam\Other"), Path::new(r"C:\Steam\Common")));
        assert!(!is_relative_to(Path::new(r"C:\Steam\CommonX"), Path::new(r"C:\Steam\Common")));
    }
}
