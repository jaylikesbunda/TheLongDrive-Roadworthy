"""Exercise the shipped one-file EXE alone, against a disposable managed-DLL copy."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
import support  # noqa: E402  (game discovery shared with the unit tests)

RELEASE = ROOT / "release" / "TheLongDrive-Roadworthy"
EXE_NAME = "TheLongDrive-Roadworthy.exe"
PY_NAME = "TheLongDrive-Roadworthy.py"
EXPECTED_RELEASE_FILES = sorted(["CHANGELOG.txt", EXE_NAME, PY_NAME, "README.txt",
                                 "Restore Original.cmd", "SHA256SUMS.txt"])
ORIGINAL_SHA256 = "CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000"
SIGNATURE_SOURCES = [name.strip().strip("'") for name in (ROOT / "tests" / "scripts" / "Test-Stability.ps1").read_text(
    encoding="utf-8-sig").split("$signatureInputs=@(", 1)[1].split(")", 1)[0].split(",")]


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def run(command, cwd, env, log, expected=0):
    print("Testing:", Path(command[0]).name, " ".join(command[1:3]), flush=True)
    result = subprocess.run(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=600)
    log.write(result.stdout)
    log.flush()
    if result.returncode != expected:
        print(result.stdout)
        raise AssertionError(f"Expected exit {expected}, got {result.returncode}")
    print(result.stdout[-650:], flush=True)
    return result.stdout


def find_vanilla(managed: Path) -> Path:
    for candidate in [managed / "Assembly-CSharp.dll", *sorted(managed.glob("Assembly-CSharp.tldmp-v*-original.bin"))]:
        if candidate.is_file() and digest(candidate) == ORIGINAL_SHA256:
            return candidate
    raise AssertionError("A verified original assembly is required for disposable testing.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-dir", default=None,
                        help="Game folder to copy from (default: TLD_GAME_DIR or the single detected Steam install)")
    parser.add_argument("--release-dir", default=None,
                        help="Release folder under test (default: release/TheLongDrive-Roadworthy)")
    args = parser.parse_args()
    release = Path(args.release_dir).resolve() if args.release_dir else RELEASE
    if args.game_dir:
        real = Path(args.game_dir).expanduser().resolve()
    else:
        games = support.game_dirs()
        if len(games) != 1:
            raise SystemExit(f"Found {len(games)} game installs; pass --game-dir or set TLD_GAME_DIR.")
        real = games[0]
    managed = real / "TheLongDrive_Data" / "Managed"
    installed_hash = digest(managed / "Assembly-CSharp.dll")
    managed_before = {p.name: digest(p) for p in managed.iterdir()
                      if p.is_file() and (p.name.startswith("Assembly-CSharp") or p.name == "Tld.Multiplayer.dll"
                                          or p.name.startswith("tldmp-"))}
    original_file = find_vanilla(managed)
    release_files = sorted(path.name for path in release.iterdir() if path.is_file())
    assert release_files == EXPECTED_RELEASE_FILES, release_files

    # Run copied EXEs and managed fixtures from a local temporary directory.
    # Windows can refuse to execute or load assemblies under redirected Documents.
    case = Path(tempfile.mkdtemp(prefix="roadworthy-release-smoke-"))
    isolated, fake = case / "exe-alone", case / "fake-game"
    isolated.mkdir(parents=True)
    fake_managed = fake / "TheLongDrive_Data" / "Managed"
    fake_managed.mkdir(parents=True)
    for path in managed.glob("*.dll"):
        if path.name in ("Assembly-CSharp.dll", "Tld.Multiplayer.dll"):
            continue
        shutil.copy2(path, fake_managed / path.name)
    # Always start from a pristine vanilla fixture, whatever the real install is.
    shutil.copy2(original_file, fake_managed / "Assembly-CSharp.dll")
    (fake / "TheLongDrive.exe").write_text("Disposable detection marker. Never executed.")

    exe = isolated / EXE_NAME
    shutil.copy2(release / EXE_NAME, exe)
    assert sorted(p.name for p in isolated.iterdir()) == [exe.name]
    env = os.environ.copy()
    for name in ("PYTHONHOME", "PYTHONPATH", "VIRTUAL_ENV"):
        env.pop(name, None)
    env["PATH"] = str(Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32")
    with (case / "test.log").open("w", encoding="utf-8") as log:
        run([str(exe), "--gui-self-test"], isolated, env, log)
        common = [str(exe), "--no-pause", "--game-dir", str(fake), "--cache-dir", str(case / "exe-cache")]
        run(common, isolated, env, log)
        v3_receipt = json.loads((fake_managed / "tldmp-v3-receipt.json").read_text(encoding="utf-8-sig"))
        v4_receipt = json.loads((fake_managed / "tldmp-v4-receipt.json").read_text(encoding="utf-8-sig"))
        patched_hash = digest(fake_managed / "Assembly-CSharp.dll")
        v3_hash = digest(fake_managed / "Assembly-CSharp.tldmp-v4-original.bin")
        assert v3_hash == v3_receipt["patchedSha256"]
        assert patched_hash == v4_receipt["patchedSha256"] != v3_hash
        assert v4_receipt["originalSha256"] == v3_hash
        assert v4_receipt["edits"], "v4 receipt must carry the edit list"
        assert (fake_managed / "Tld.Multiplayer.dll").is_file()
        signature_text = "\n".join(name + ":" + digest(ROOT / name.replace("\\", "/")) for name in SIGNATURE_SOURCES)
        expected_signature = hashlib.sha256(signature_text.encode("utf-8")).hexdigest().upper()
        assert v3_receipt["sourceSignature"] == expected_signature, "Smoke test must install THIS source build."

        output = run(common, isolated, env, log)
        assert "already installed" in output and digest(fake_managed / "Assembly-CSharp.dll") == patched_hash

        # A verified older install without release metadata upgrades, not refuses.
        for name in ("tldmp-v3-receipt.json", "tldmp-v4-receipt.json"):
            receipt_path = fake_managed / name
            receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
            receipt.pop("roadworthyVersion", None)
            receipt.pop("installerPayloadSha256", None)
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        run(common, isolated, env, log)
        v3_receipt = json.loads((fake_managed / "tldmp-v3-receipt.json").read_text(encoding="utf-8-sig"))
        v4_receipt = json.loads((fake_managed / "tldmp-v4-receipt.json").read_text(encoding="utf-8-sig"))
        assert v3_receipt["roadworthyVersion"] and v4_receipt["roadworthyVersion"]
        assert v3_receipt["installerPayloadSha256"] == v4_receipt["installerPayloadSha256"]
        assert v3_receipt["sourceSignature"] == expected_signature
        assert digest(fake_managed / "Assembly-CSharp.dll") == v4_receipt["patchedSha256"]

        # Same release but a different bundled build still runs the update.
        v3_receipt["installerPayloadSha256"] = "A" * 64
        (fake_managed / "tldmp-v3-receipt.json").write_text(json.dumps(v3_receipt), encoding="utf-8")
        run(common, isolated, env, log)
        v3_receipt = json.loads((fake_managed / "tldmp-v3-receipt.json").read_text(encoding="utf-8-sig"))
        v4_receipt = json.loads((fake_managed / "tldmp-v4-receipt.json").read_text(encoding="utf-8-sig"))
        assert v3_receipt["installerPayloadSha256"] != "A" * 64
        assert v3_receipt["installerPayloadSha256"] == v4_receipt["installerPayloadSha256"]
        assert v3_receipt["sourceSignature"] == expected_signature
        assert digest(fake_managed / "Assembly-CSharp.dll") == v4_receipt["patchedSha256"]

        backup = fake_managed / "Assembly-CSharp.tldmp-v4-original.bin"
        good_backup = backup.read_bytes()
        backup.write_bytes(good_backup + b"tamper fixture")
        run(common + ["--restore"], isolated, env, log, expected=1)
        assert digest(fake_managed / "Assembly-CSharp.dll") == patched_hash
        backup.write_bytes(good_backup)

        run(common + ["--restore"], isolated, env, log)
        assert digest(fake_managed / "Assembly-CSharp.dll") == v3_hash
        assert not (fake_managed / "tldmp-v4-receipt.json").exists()
        assert not backup.exists()
        assert (fake_managed / "Tld.Multiplayer.dll").is_file()

        run(common + ["--restore-all"], isolated, env, log)
        assert digest(fake_managed / "Assembly-CSharp.dll") == ORIGINAL_SHA256
        assert not (fake_managed / "Tld.Multiplayer.dll").exists()
        assert not (fake_managed / "tldmp-v3-receipt.json").exists()
        assert not (fake_managed / "Assembly-CSharp.tldmp-v3-original.bin").exists()
        output = run([str(exe), "--status", "--game-dir", str(fake), "--no-pause"], isolated, env, log)
        assert "original" in output

        with (fake_managed / "Assembly-CSharp.dll").open("ab") as handle:
            handle.write(b"unsupported build fixture")
        changed_hash = digest(fake_managed / "Assembly-CSharp.dll")
        run(common, isolated, env, log, expected=1)
        assert digest(fake_managed / "Assembly-CSharp.dll") == changed_hash
        shutil.copy2(original_file, fake_managed / "Assembly-CSharp.dll")

        py_alone = case / "py-alone"
        py_alone.mkdir()
        script = py_alone / PY_NAME
        shutil.copy2(release / PY_NAME, script)
        run([sys.executable, str(script), "--gui-self-test"], py_alone, env, log)
        run([sys.executable, str(script), "--test-only", "--no-pause", "--game-dir", str(fake),
             "--cache-dir", str(case / "py-cache")], py_alone, env, log)
        assert digest(fake_managed / "Assembly-CSharp.dll") == ORIGINAL_SHA256
    assert digest(managed / "Assembly-CSharp.dll") == installed_hash
    managed_after = {p.name: digest(p) for p in managed.iterdir()
                     if p.is_file() and (p.name.startswith("Assembly-CSharp") or p.name == "Tld.Multiplayer.dll"
                                         or p.name.startswith("tldmp-"))}
    assert managed_after == managed_before, "real Managed files changed during the disposable smoke test"
    for name in ("packaging/singleplayer/PatchV4.cs", "Build-Patch.ps1", "Manage-Stability.ps1", ".tools/cecil.zip"):
        assert (ROOT / name).is_file(), name
    assert "# // not a virus" in (release / PY_NAME).read_text(encoding="utf-8")
    print("PASS: standalone EXE install/v3+v4/repeat/restore/restore-all/refusal/detection, standalone Python test-only; real game unchanged.")
    print("Log:", case / "test.log")


if __name__ == "__main__":
    main()
