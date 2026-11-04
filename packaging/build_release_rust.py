"""Build the Rust/Tauri installer release into a NEW folder (never deletes an existing release).

Usage: python packaging/build_release_rust.py OUTPUT_DIR
The EXE embeds the source payload and needs no Python. Mono.Cecil comes from the hash-pinned
package cached in .tools/cecil.zip (see build_release.cecil_package).
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "packaging"))
import build_release  # noqa: E402

NAME = build_release.NAME
VERSION = build_release.VERSION
PAYLOAD = ROOT / "installer" / "payload" / "payload.zip"
EXE_SOURCE = ROOT / "installer" / "target" / "release" / f"{NAME}.exe"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    out = Path(sys.argv[1]).resolve()
    if out.exists() and any(out.iterdir()):
        raise SystemExit(f"Refusing to use a non-empty output folder: {out}")
    out.mkdir(parents=True, exist_ok=True)
    payload_hash = build_release.write_payload(PAYLOAD)
    subprocess.run(["cargo", "build", "--release", "-p", "roadworthy-app"], cwd=ROOT / "installer", check=True)
    release = out / NAME
    release.mkdir()
    shutil.copyfile(EXE_SOURCE, release / f"{NAME}.exe")
    shutil.copyfile(ROOT / "packaging" / "README.txt", release / "README.txt")
    shutil.copyfile(ROOT / "CHANGELOG.md", release / "CHANGELOG.txt")
    (release / "Restore Original.cmd").write_text(f'@echo off\r\n"%~dp0{NAME}.exe" --restore-all\r\n', encoding="utf-8", newline="")
    (release / "SHA256SUMS.txt").write_text("".join(
        sha256(path) + "  " + path.name + "\n" for path in sorted(release.iterdir()) if path.is_file() and path.name != "SHA256SUMS.txt"
    ), encoding="utf-8")
    archive = out / f"{NAME}-v{VERSION}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(release.iterdir()):
            bundle.write(path, f"{NAME}/{path.name}")
    print(f"Payload SHA-256: {payload_hash}")
    print(f"EXE: {release / (NAME + '.exe')} ({(release / (NAME + '.exe')).stat().st_size:,} bytes)")
    print(f"EXE SHA-256: {sha256(release / (NAME + '.exe'))}")
    print(f"ZIP: {archive}")


if __name__ == "__main__":
    main()
