"""Build the installer (.py and single-file windowed EXE) and the release ZIP.

No game binaries are included: the installer compiles the patch from the
embedded sources against the player's own game. Mono.Cecil comes from the
hash-pinned NuGet package (cached in .tools/cecil.zip).
"""
from __future__ import annotations

import base64
import hashlib
import io
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NAME = "TheLongDrive-Roadworthy"
VERSION = "0.1.1"
RELEASE = ROOT / "release" / NAME
ARCHIVE = ROOT / "release" / f"{NAME}-v{VERSION}.zip"
CECIL = ROOT / ".tools" / "cecil.zip"
CECIL_URL = "https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg"
CECIL_HASH = "D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5"
APP_ICON = ROOT / "packaging" / "app.ico"


def source_entries() -> dict[str, bytes]:
    """Payload: the repository's patch sources and scripts, at their repo paths."""
    patterns = ["*.ps1", "README.md", "CHANGELOG.md", "runtime/*.cs", "tools/*.cs", "tools/*.ps1",
                "tests/*.cs", "tests/scripts/*.ps1", "packaging/singleplayer/*.cs"]
    entries = {}
    for pattern in patterns:
        for path in sorted(ROOT.glob(pattern)):
            entries[path.relative_to(ROOT).as_posix()] = path.read_bytes()
    for path in sorted((ROOT / "packaging" / "licenses").glob("*.txt")):
        entries["LICENSES/" + path.name] = path.read_bytes()
    entries[".tools/cecil.zip"] = cecil_package()
    return entries


def cecil_package() -> bytes:
    if not CECIL.is_file():
        CECIL.parent.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(CECIL_URL, timeout=60) as response:
            CECIL.write_bytes(response.read())
    data = CECIL.read_bytes()
    if hashlib.sha256(data).hexdigest().upper() != CECIL_HASH:
        raise RuntimeError("Mono.Cecil package checksum mismatch")
    return data


def make_zip(entries: dict[str, bytes]) -> bytes:
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 10, 22, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    return stream.getvalue()


def build_icon() -> str:
    """Regenerate app.ico and return the window-icon PNG as base64."""
    sys.path.insert(0, str(ROOT / "packaging"))
    import appicon
    appicon.write_ico(APP_ICON)
    return appicon.window_png_b64()


def embedded_icon(text: str) -> str:
    """Read APP_ICON_PNG_B64 out of the installer template source."""
    import ast
    for node in ast.parse(text).body:
        if isinstance(node, ast.Assign) and any(getattr(target, "id", None) == "APP_ICON_PNG_B64"
                                                for target in node.targets):
            return ast.literal_eval(node.value)
    raise RuntimeError("installer_template.py has no APP_ICON_PNG_B64 constant")


def main():
    if RELEASE.exists():
        shutil.rmtree(RELEASE)
    RELEASE.mkdir(parents=True)

    icon_b64 = build_icon()
    payload = make_zip(source_entries())
    template = (ROOT / "packaging" / "installer_template.py").read_text(encoding="utf-8")
    if embedded_icon(template) != icon_b64:
        raise RuntimeError("installer_template.py does not embed the current window icon; regenerate it with packaging/appicon.py")
    source = template.replace("__PAYLOAD_SHA256__", hashlib.sha256(payload).hexdigest().upper()).replace("__PAYLOAD_B64__", base64.b64encode(payload).decode("ascii"))
    installer = RELEASE / f"{NAME}.py"
    installer.write_text(source, encoding="utf-8", newline="\n")
    shutil.copyfile(ROOT / "packaging" / "README.txt", RELEASE / "README.txt")
    shutil.copyfile(ROOT / "CHANGELOG.md", RELEASE / "CHANGELOG.txt")
    (RELEASE / "Restore Original.cmd").write_text(f'@echo off\r\n"%~dp0{NAME}.exe" --restore-all\r\n', encoding="utf-8", newline="")
    subprocess.run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--onefile", "--windowed", "--noupx",
                    "--icon", str(APP_ICON), "--name", NAME, "--distpath", str(RELEASE),
                    "--workpath", str(ROOT / ".tools" / "pyinstaller-work"),
                    "--specpath", str(ROOT / ".tools" / "pyinstaller-spec"), str(installer)], check=True)
    executable = RELEASE / f"{NAME}.exe"
    (RELEASE / "SHA256SUMS.txt").write_text("".join(
        hashlib.sha256(path.read_bytes()).hexdigest().upper() + "  " + path.name + "\n"
        for path in sorted(RELEASE.iterdir()) if path.is_file() and path.name != "SHA256SUMS.txt"
    ), encoding="utf-8")
    with zipfile.ZipFile(ARCHIVE, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(RELEASE.iterdir()):
            if path.is_file():
                bundle.write(path, RELEASE.name + "/" + path.name)
    print(f"Release ZIP: {ARCHIVE}")
    print(f"Standalone EXE: {executable} ({executable.stat().st_size:,} bytes)")
    print(f"EXE SHA-256: {hashlib.sha256(executable.read_bytes()).hexdigest().upper()}")


if __name__ == "__main__":
    main()
