use sha2::{Digest, Sha256};
use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-env-changed=ROADWORTHY_PAYLOAD");
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    let configured = std::env::var_os("ROADWORTHY_PAYLOAD").map(PathBuf::from);
    let default = manifest.join("..").join("payload").join("payload.zip");
    let wanted = configured.unwrap_or(default);
    println!("cargo:rerun-if-changed={}", wanted.display());
    let path = if wanted.is_file() {
        std::fs::canonicalize(&wanted).unwrap()
    } else {
        println!("cargo:warning=No payload at {}; embedding an empty placeholder (unit tests only).", wanted.display());
        let placeholder = PathBuf::from(std::env::var("OUT_DIR").unwrap()).join("placeholder.zip");
        let mut empty = vec![0x50, 0x4b, 0x05, 0x06];
        empty.extend_from_slice(&[0u8; 18]);
        std::fs::write(&placeholder, empty).unwrap();
        placeholder
    };
    let data = std::fs::read(&path).unwrap();
    let digest: String = Sha256::digest(&data).iter().map(|b| format!("{:02X}", b)).collect();
    let text = path.to_string_lossy();
    let text = text.strip_prefix(r"\\?\").unwrap_or(&text);
    println!("cargo:rustc-env=ROADWORTHY_PAYLOAD_PATH={}", text);
    println!("cargo:rustc-env=ROADWORTHY_PAYLOAD_SHA256={}", digest);
}
