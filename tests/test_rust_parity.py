"""Differential test: the Python installer (spec) and the Rust installer must behave the same.

Both run the same scenarios against separate disposable fake games built from
read-only copies of the real Managed DLLs. The real game and saves are never
written. csc output is not deterministic, so compiled files are compared by
structure (receipts, hash chains, states), not by bytes.

Environment:
  ROADWORTHY_RUST_CLI   path to roadworthy-cli.exe (default installer/target/release/roadworthy-cli.exe)
  TLD_GAME_DIR          real game folder to copy Managed DLLs from (read-only)
  ROADWORTHY_PARITY_TMP scratch folder (default: system temp)
"""
from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
import support  # noqa: E402

RUST_CLI = Path(os.environ.get("ROADWORTHY_RUST_CLI", ROOT / "installer" / "target" / "release" / "roadworthy-cli.exe"))
PAYLOAD = ROOT / "installer" / "payload" / "payload.zip"
ORIGINAL_SHA256 = "CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000"
KEEP = re.compile(r"^(\[ *\d+%\]|Game:|Current installation:|Bundled release:|Done\.|Original game|This bundled build|"
                  r"Build and verification passed|Single-player pass removed|Stopped:|No published|Latest published)")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def build_python_installer(target: Path) -> Path:
    """The Python spec installer with exactly the payload the Rust build embeds."""
    data = PAYLOAD.read_bytes()
    template = (ROOT / "packaging" / "installer_template.py").read_text(encoding="utf-8")
    source = template.replace("__PAYLOAD_SHA256__", hashlib.sha256(data).hexdigest().upper()).replace(
        "__PAYLOAD_B64__", base64.b64encode(data).decode("ascii"))
    target.write_text(source, encoding="utf-8", newline="\n")
    return target


class Impl:
    def __init__(self, name, command, root):
        self.name, self.command, self.root = name, command, root
        self.fake = root / "game"
        self.managed = self.fake / "TheLongDrive_Data" / "Managed"
        self.cache = root / "cache"

    def prepare(self, real_managed: Path, vanilla: Path):
        self.managed.mkdir(parents=True)
        for path in real_managed.glob("*.dll"):
            if path.name not in ("Assembly-CSharp.dll", "Tld.Multiplayer.dll"):
                shutil.copy2(path, self.managed / path.name)
        shutil.copy2(vanilla, self.managed / "Assembly-CSharp.dll")
        (self.fake / "TheLongDrive.exe").write_text("Disposable detection marker. Never executed.")

    def run(self, *args, expected=0):
        env = os.environ.copy()
        for name in ("PYTHONHOME", "PYTHONPATH", "VIRTUAL_ENV"):
            env.pop(name, None)
        result = subprocess.run([*self.command, *args, "--no-pause", "--game-dir", str(self.fake), "--cache-dir", str(self.cache)],
                                cwd=self.root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                encoding="utf-8", errors="replace", timeout=1800)
        with (self.root / "transcript.log").open("a", encoding="utf-8") as transcript:
            transcript.write(f"$ {args}\n{result.stdout}\n")
        return result.returncode, self.normalise(result.stdout)

    def normalise(self, text):
        lines = []
        for line in text.splitlines():
            if not KEEP.match(line):
                continue
            line = line.replace(str(self.fake), "<GAME>").replace(str(self.cache), "<CACHE>")
            line = re.sub(r"build [0-9A-F]{12}", "build <B>", line)
            line = re.sub(r"\(release [^,]+,", "(release <R>,", line)
            lines.append(line.rstrip())
        return lines

    def receipt(self, name):
        path = self.managed / name
        return json.loads(path.read_text(encoding="utf-8-sig")) if path.is_file() else None

    def state(self):
        v4, v3 = self.receipt("tldmp-v4-receipt.json"), self.receipt("tldmp-v3-receipt.json")
        return {"assembly": digest(self.managed / "Assembly-CSharp.dll"), "v3": v3, "v4": v4,
                "files": sorted(p.name for p in self.managed.iterdir() if p.name.startswith(("tldmp-", "Tld.Multiplayer"))
                                or p.name.startswith("Assembly-CSharp.tldmp"))}


def shape(value):
    if isinstance(value, dict):
        return {k: shape(v) for k, v in sorted(value.items())}
    if isinstance(value, list):
        return [shape(value[0])] if value else []
    return type(value).__name__


@unittest.skipUnless(RUST_CLI.is_file() and PAYLOAD.is_file(), "build the Rust CLI and installer/payload/payload.zip first")
class RustParity(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        games = support.game_dirs()
        if len(games) != 1:
            raise unittest.SkipTest("one real game install is required as a read-only DLL source")
        real = games[0] / "TheLongDrive_Data" / "Managed"
        vanilla = next((c for c in [real / "Assembly-CSharp.dll", *sorted(real.glob("Assembly-CSharp.tldmp-v*-original.bin"))]
                        if c.is_file() and digest(c) == ORIGINAL_SHA256), None)
        if vanilla is None:
            raise unittest.SkipTest("a verified original assembly is required")
        cls.real_assembly = real / "Assembly-CSharp.dll"
        cls.real_before = digest(cls.real_assembly)
        base = os.environ.get("ROADWORTHY_PARITY_TMP")
        cls.tmp = Path(tempfile.mkdtemp(prefix="roadworthy-parity-", dir=base))
        python_script = build_python_installer(cls.tmp / "installer.py")
        cls.py = Impl("python", [sys.executable, str(python_script)], cls.tmp / "py")
        cls.rs = Impl("rust", [str(RUST_CLI)], cls.tmp / "rs")
        for impl in (cls.py, cls.rs):
            impl.root.mkdir()
            impl.prepare(real, vanilla)
        cls.vanilla = vanilla
        cls.log = []

    @classmethod
    def tearDownClass(cls):
        assert digest(cls.real_assembly) == cls.real_before, "real game assembly changed during parity testing"
        print("Parity scratch:", cls.tmp)

    def both(self, *args, expected=0):
        py, rs = self.py.run(*args), self.rs.run(*args)
        self.assertEqual(py[0], expected, f"python exit {args}: {py}")
        self.assertEqual(rs[0], expected, f"rust exit {args}: {rs}")
        self.assertEqual(py[1], rs[1], f"normalised output differs for {args}")
        return py, rs

    def assert_chain(self, impl, expect_v4=True):
        state = impl.state()
        v3, v4 = state["v3"], state["v4"]
        self.assertEqual(v3["version"], 3)
        self.assertEqual(v3["originalSha256"], ORIGINAL_SHA256)
        self.assertEqual(digest(impl.managed / "Assembly-CSharp.tldmp-v4-original.bin"), v3["patchedSha256"])
        self.assertEqual(digest(impl.managed / "Tld.Multiplayer.dll"), v3["runtimeSha256"])
        if expect_v4:
            self.assertEqual(v4["version"], 4)
            self.assertEqual(state["assembly"], v4["patchedSha256"])
            self.assertEqual(v4["originalSha256"], v3["patchedSha256"])
            self.assertEqual(v4["runtimeSha256"], v3["runtimeSha256"])
            self.assertEqual(v4["verify"], "PASS")
            self.assertTrue(v4["edits"])
        for receipt in (v3, v4):
            self.assertEqual(receipt["roadworthyVersion"], support.installer.VERSION)
            self.assertEqual(receipt["installerPayloadSha256"], hashlib.sha256(PAYLOAD.read_bytes()).hexdigest().upper())

    def test_00_status_list_and_usage(self):
        self.both("--status")
        self.assertEqual(self.py.state()["assembly"], ORIGINAL_SHA256)
        code, _ = self.rs.run("--restore", "--status", expected=2)
        self.assertEqual(code, 2)
        self.assertEqual(self.py.run("--restore", "--status")[0], 2)

    def test_10_test_only_leaves_game_unchanged(self):
        before = (self.py.state()["assembly"], self.rs.state()["assembly"])
        self.both("--test-only")
        self.assertEqual((self.py.state()["assembly"], self.rs.state()["assembly"]), before)
        self.assertEqual(self.rs.state()["files"], [])

    def test_20_install_chain_and_receipt_shapes_match(self):
        self.both("--update")
        for impl in (self.py, self.rs):
            self.assert_chain(impl)
        a, b = self.py.state(), self.rs.state()
        self.assertEqual(a["files"], b["files"])
        self.assertEqual(shape(a["v3"]), shape(b["v3"]))
        self.assertEqual(shape(a["v4"]), shape(b["v4"]))
        for key in ("sourceSignature", "roadworthyVersion", "installerPayloadSha256", "originalSha256"):
            self.assertEqual(a["v3"][key], b["v3"][key], key)
        self.both("--status")

    def test_30_repeat_install_is_a_noop(self):
        before = (self.py.state()["assembly"], self.rs.state()["assembly"])
        py, rs = self.both("--update")
        self.assertTrue(any("already installed" in line for line in rs[1] + py[1]))
        self.assertEqual((self.py.state()["assembly"], self.rs.state()["assembly"]), before)

    def test_40_metadata_less_install_updates(self):
        for impl in (self.py, self.rs):
            for name in ("tldmp-v3-receipt.json", "tldmp-v4-receipt.json"):
                path = impl.managed / name
                data = json.loads(path.read_text(encoding="utf-8-sig"))
                data.pop("roadworthyVersion", None)
                data.pop("installerPayloadSha256", None)
                path.write_text(json.dumps(data), encoding="utf-8")
        self.both("--status")
        self.both("--update")
        for impl in (self.py, self.rs):
            self.assert_chain(impl)

    def test_50_different_payload_same_release_updates(self):
        for impl in (self.py, self.rs):
            path = impl.managed / "tldmp-v3-receipt.json"
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            data["installerPayloadSha256"] = "A" * 64
            path.write_text(json.dumps(data), encoding="utf-8")
        self.both("--update")
        for impl in (self.py, self.rs):
            self.assert_chain(impl)

    def test_60_tampered_backup_refuses_restore(self):
        for impl in (self.py, self.rs):
            backup = impl.managed / "Assembly-CSharp.tldmp-v4-original.bin"
            good = backup.read_bytes()
            before = impl.state()["assembly"]
            backup.write_bytes(good + b"tamper fixture")
            code, _ = impl.run("--restore")
            self.assertEqual(code, 1, impl.name)
            self.assertEqual(impl.state()["assembly"], before, impl.name)
            backup.write_bytes(good)

    def test_70_restore_then_restore_all(self):
        self.both("--restore")
        for impl in (self.py, self.rs):
            self.assertIsNone(impl.state()["v4"])
            self.assertTrue((impl.managed / "Tld.Multiplayer.dll").is_file())
            self.assertEqual(impl.state()["assembly"], impl.state()["v3"]["patchedSha256"])
        self.both("--restore-all")
        for impl in (self.py, self.rs):
            state = impl.state()
            self.assertEqual(state["assembly"], ORIGINAL_SHA256)
            self.assertEqual(state["files"], [])
            self.assertFalse((impl.managed / "Tld.Multiplayer.dll").exists())
        self.both("--restore-all")

    def test_80_unsupported_assembly_is_refused_untouched(self):
        for impl in (self.py, self.rs):
            with (impl.managed / "Assembly-CSharp.dll").open("ab") as handle:
                handle.write(b"unsupported build fixture")
        changed = (self.py.state()["assembly"], self.rs.state()["assembly"])
        self.both("--update", expected=1)
        self.assertEqual((self.py.state()["assembly"], self.rs.state()["assembly"]), changed)
        for impl in (self.py, self.rs):
            shutil.copy2(self.vanilla, impl.managed / "Assembly-CSharp.dll")

    def test_90_not_a_game_folder_is_refused(self):
        for impl in (self.py, self.rs):
            result = subprocess.run([*impl.command, "--status", "--no-pause", "--game-dir", str(self.tmp)], stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=120)
            self.assertEqual(result.returncode, 1, impl.name)
            self.assertIn("Not a The Long Drive installation:", result.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=2)
