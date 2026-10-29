import contextlib
import hashlib
import io
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parent))

import support
from support import ORIGINAL_SHA256, installer, sha256

ROOT = support.ROOT
VANILLA = b"vanilla fixture bytes"
V3 = b"multiplayer v3 fixture bytes"
HELPER = b"multiplayer helper fixture bytes"
V4 = b"single-player v4 fixture bytes"


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def write_json(path: Path, data: dict) -> None:
    path.write_text(json.dumps(data), encoding="utf-8")


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def create_game(root: Path) -> Path:
    game = root / "The Long Drive"
    (game / "TheLongDrive_Data" / "Managed").mkdir(parents=True)
    (game / "TheLongDrive.exe").write_text("Disposable detection marker. Never executed.")
    return game


def managed_of(game: Path) -> Path:
    return game / "TheLongDrive_Data" / "Managed"


class InstallerTests(unittest.TestCase):
    def test_vdf_nested_unicode_and_escaped_paths(self):
        data = installer.parse_vdf('''// Steam libraries
"libraryfolders" { "0" { "path" "D:\\\\Games With Spaces\\\\Steam" "label" "héllo" } }
''')
        self.assertEqual(data["libraryfolders"]["0"]["path"], "D:\\Games With Spaces\\Steam")
        self.assertEqual(data["libraryfolders"]["0"]["label"], "héllo")

    def test_vdf_rejects_truncated_input(self):
        with self.assertRaises(installer.InstallerError):
            installer.parse_vdf('"libraryfolders" { "0" {')

    def test_discovery_uses_external_library_and_manifest_install_name(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            steam, library = root / "Steam", root / "Extra Library ü"
            (steam / "steamapps").mkdir(parents=True)
            (library / "steamapps" / "common" / "Renamed Install" / "TheLongDrive_Data" / "Managed").mkdir(parents=True)
            value = str(library).replace("\\", "\\\\")
            (steam / "steamapps" / "libraryfolders.vdf").write_text('"libraryfolders" { "1" { "path" "' + value + '" } }', encoding="utf-8")
            manifest = library / "steamapps" / "appmanifest_1017180.acf"
            manifest.write_text('"AppState" { "appid" "1017180" "installdir" "Renamed Install" }', encoding="utf-8")
            game = library / "steamapps" / "common" / "Renamed Install"
            (game / "TheLongDrive.exe").write_bytes(b"not executed")
            installer.game_assembly(game).write_bytes(b"fixture")
            self.assertEqual(installer.discover_games([steam]), [game.resolve()])
            manifest.write_text('"AppState" { "appid" "1017180" "installdir" "../../escape" }', encoding="utf-8")
            self.assertEqual(installer.discover_games([steam]), [])

    def test_unknown_game_state_refused_without_writes(self):
        with tempfile.TemporaryDirectory() as tmp:
            game = Path(tmp)
            target = installer.game_assembly(game)
            target.parent.mkdir(parents=True)
            target.write_bytes(b"different game build")
            with self.assertRaises(installer.InstallerError):
                installer.installed_state(game)
            self.assertEqual(target.read_bytes(), b"different game build")

    def test_unsafe_embedded_archive_is_refused(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("../outside.txt", b"bad")
        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(installer, "payload_bytes", return_value=stream.getvalue()):
            with self.assertRaises(installer.InstallerError):
                installer.extract_payload(Path(tmp) / "cache")
            self.assertFalse((Path(tmp) / "outside.txt").exists())


class V4StateTests(unittest.TestCase):
    """Receipt/backup state machine, using disposable bytes only."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.patch = mock.patch.object(installer, "ORIGINAL_SHA256", digest(VANILLA))
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.game = create_game(Path(self.tmp.name))
        self.managed = managed_of(self.game)

    def install_v3(self, v3: bytes = V3, helper: bytes = HELPER) -> None:
        (self.managed / "Assembly-CSharp.dll").write_bytes(v3)
        (self.managed / "Tld.Multiplayer.dll").write_bytes(helper)
        (self.managed / "Assembly-CSharp.tldmp-v3-original.bin").write_bytes(VANILLA)
        write_json(self.managed / "tldmp-v3-receipt.json", {
            "version": 3,
            "originalSha256": digest(VANILLA),
            "patchedSha256": digest(v3),
            "runtimeSha256": digest(helper),
        })

    def install_v4(self, v3: bytes = V3, v4: bytes = V4, helper: bytes = HELPER) -> None:
        self.install_v3(v3, helper)
        (self.managed / "Assembly-CSharp.dll").write_bytes(v4)
        (self.managed / "Assembly-CSharp.tldmp-v4-original.bin").write_bytes(v3)
        write_json(self.managed / "tldmp-v4-receipt.json", {
            "version": 4,
            "originalSha256": digest(v3),
            "patchedSha256": digest(v4),
            "runtimeSha256": digest(helper),
        })

    def test_states_progress_from_vanilla_to_v4(self):
        (self.managed / "Assembly-CSharp.dll").write_bytes(VANILLA)
        self.assertEqual(installer.installed_state(self.game), "original")
        self.install_v3()
        self.assertEqual(installer.installed_state(self.game), "v3")
        self.install_v4()
        self.assertEqual(installer.installed_state(self.game), "v4")

    def test_restore_singleplayer_unwinds_to_v3(self):
        self.install_v4()
        installer.restore_singleplayer(self.game, io.StringIO())
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), V3)
        self.assertFalse((self.managed / "tldmp-v4-receipt.json").exists())
        self.assertFalse((self.managed / "Assembly-CSharp.tldmp-v4-original.bin").exists())
        self.assertTrue((self.managed / "Tld.Multiplayer.dll").is_file())
        self.assertEqual(installer.installed_state(self.game), "v3")

    def test_tampered_v4_backup_refuses_restore(self):
        self.install_v4()
        backup = self.managed / "Assembly-CSharp.tldmp-v4-original.bin"
        backup.write_bytes(backup.read_bytes() + b"tampered")
        with self.assertRaises(installer.InstallerError):
            installer.restore_singleplayer(self.game, io.StringIO())
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), V4)
        self.assertTrue((self.managed / "tldmp-v4-receipt.json").is_file())

    def test_v4_receipt_must_match_multiplayer_lineage(self):
        self.install_v4()
        receipt = read_json(self.managed / "tldmp-v4-receipt.json")
        receipt["originalSha256"] = "A" * 64
        write_json(self.managed / "tldmp-v4-receipt.json", receipt)
        with self.assertRaises(installer.InstallerError):
            installer.installed_state(self.game)

    def test_v4_state_requires_the_multiplayer_helper(self):
        self.install_v4()
        (self.managed / "Tld.Multiplayer.dll").write_bytes(b"different helper")
        with self.assertRaises(installer.InstallerError):
            installer.installed_state(self.game)

    def test_v4_state_requires_the_multiplayer_receipt(self):
        self.install_v4()
        (self.managed / "tldmp-v3-receipt.json").unlink()
        with self.assertRaises(installer.InstallerError):
            installer.installed_state(self.game)

    def test_interrupted_restore_reads_as_v3(self):
        self.install_v4()
        (self.managed / "Assembly-CSharp.dll").write_bytes(V3)
        self.assertEqual(installer.installed_state(self.game), "v3")

    def test_unrelated_live_build_with_v4_receipt_is_refused(self):
        self.install_v4()
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"another mod")
        with self.assertRaises(installer.InstallerError):
            installer.installed_state(self.game)


def test_payload() -> bytes:
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted((ROOT / "packaging" / "singleplayer").glob("*.cs")):
            archive.writestr("packaging/singleplayer/" + path.name, path.read_bytes())
        archive.writestr(".tools/cecil.zip", (ROOT / ".tools" / "cecil.zip").read_bytes())
    return stream.getvalue()


class SandboxPatchTests(unittest.TestCase):
    """End-to-end install on a disposable copy of the real managed folder."""

    @classmethod
    def setUpClass(cls):
        cls.local = support.find_v3_install()
        if cls.local is None:
            raise unittest.SkipTest("no verified multiplayer v3 install available for a sandbox fixture")
        if not (ROOT / ".tools" / "cecil.zip").is_file():
            raise unittest.SkipTest("packaging dependency .tools/cecil.zip is missing")
        cls.payload = test_payload()
        cls.cache = Path(tempfile.mkdtemp(prefix="tld-cache-"))

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.cache, ignore_errors=True)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.game = create_game(Path(self.tmp.name))
        self.managed = managed_of(self.game)
        support.copy_reference_assemblies(self.local["managed"], self.managed)
        self.v3_bytes = self.local["v3"].read_bytes()
        helper = self.local["helper"].read_bytes()
        (self.managed / "Assembly-CSharp.dll").write_bytes(self.v3_bytes)
        (self.managed / "Tld.Multiplayer.dll").write_bytes(helper)
        (self.managed / "Assembly-CSharp.tldmp-v3-original.bin").write_bytes(self.local["vanilla"].read_bytes())
        write_json(self.managed / "tldmp-v3-receipt.json", {
            "version": 3,
            "originalSha256": ORIGINAL_SHA256,
            "patchedSha256": digest(self.v3_bytes),
            "runtimeSha256": digest(helper),
        })

    def cli(self, *arguments, preflight=True, run_ps=None):
        argv = ["--no-pause", "--game-dir", str(self.game), "--cache-dir", str(self.cache), *arguments]
        patches = [mock.patch.object(installer, "payload_bytes", return_value=self.payload)]
        if preflight:
            patches.append(mock.patch.object(installer, "preflight"))
        if run_ps is not None:
            patches.append(mock.patch.object(installer, "run_ps", side_effect=run_ps))
        with contextlib.ExitStack() as stack:
            for patch in patches:
                stack.enter_context(patch)
            return installer.main_cli(argv)

    def fake_multiplayer_restore(self, calls):
        def run_ps(work, log, script, game, *arguments):
            calls.append((script, arguments))
            if arguments != ("-Action", "Restore"):
                raise AssertionError("unexpected PowerShell call")
            # What Manage-Stability.ps1 does: put the original back, drop the helper.
            shutil.copyfile(self.local["vanilla"], self.managed / "Assembly-CSharp.dll")
            (self.managed / "Tld.Multiplayer.dll").unlink()
        return run_ps

    def test_install_applies_singleplayer_after_multiplayer(self):
        self.assertEqual(self.cli(), 0)
        live = (self.managed / "Assembly-CSharp.dll").read_bytes()
        self.assertNotEqual(live, self.v3_bytes)
        self.assertEqual(installer.installed_state(self.game), "v4")
        receipt = read_json(self.managed / "tldmp-v4-receipt.json")
        self.assertEqual(receipt["input"]["sha256"], digest(self.v3_bytes))
        self.assertEqual(receipt["output"]["sha256"], digest(live))
        self.assertEqual(receipt["originalSha256"], digest(self.v3_bytes))
        self.assertEqual(receipt["patchedSha256"], digest(live))
        self.assertGreaterEqual(len(receipt["edits"]), 9)
        self.assertIn("TldStable", json.dumps(receipt["edits"]))
        self.assertEqual((self.managed / "Assembly-CSharp.tldmp-v4-original.bin").read_bytes(), self.v3_bytes)
        self.assertEqual(sha256(self.managed / "Tld.Multiplayer.dll"), receipt["runtimeSha256"])

    def test_install_is_idempotent(self):
        self.assertEqual(self.cli(), 0)
        first = (self.managed / "Assembly-CSharp.dll").read_bytes()
        with mock.patch("sys.stdout", new_callable=io.StringIO) as output:
            self.assertEqual(self.cli(run_ps=lambda *a: self.fail("no build on a patched install")), 0)
            self.assertIn("already installed", output.getvalue())
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), first)
        self.assertEqual(installer.installed_state(self.game), "v4")

    def test_restore_returns_to_multiplayer_only(self):
        self.assertEqual(self.cli(), 0)
        self.assertEqual(self.cli("--restore"), 0)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), self.v3_bytes)
        self.assertEqual(installer.installed_state(self.game), "v3")
        self.assertFalse((self.managed / "tldmp-v4-receipt.json").exists())
        self.assertFalse((self.managed / "Assembly-CSharp.tldmp-v4-original.bin").exists())
        self.assertTrue((self.managed / "Tld.Multiplayer.dll").is_file())

    def test_restore_all_returns_to_vanilla(self):
        self.assertEqual(self.cli(), 0)
        calls = []
        self.assertEqual(self.cli("--restore-all", run_ps=self.fake_multiplayer_restore(calls)), 0)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), self.local["vanilla"].read_bytes())
        self.assertEqual(installer.installed_state(self.game), "original")
        self.assertEqual([script for script, _ in calls], ["Manage-Stability.ps1"])
        for name in ("Tld.Multiplayer.dll", "tldmp-v3-receipt.json", "tldmp-v4-receipt.json",
                     "Assembly-CSharp.tldmp-v3-original.bin", "Assembly-CSharp.tldmp-v4-original.bin"):
            self.assertFalse((self.managed / name).exists(), name)

    def test_tampered_v4_backup_makes_cli_restore_refuse(self):
        self.assertEqual(self.cli(), 0)
        backup = self.managed / "Assembly-CSharp.tldmp-v4-original.bin"
        backup.write_bytes(backup.read_bytes() + b"tamper fixture")
        live = (self.managed / "Assembly-CSharp.dll").read_bytes()
        self.assertEqual(self.cli("--restore"), 1)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), live)
        self.assertTrue((self.managed / "tldmp-v4-receipt.json").is_file())

    def test_changed_multiplayer_helper_refuses_before_patching(self):
        (self.managed / "Tld.Multiplayer.dll").write_bytes(b"changed helper")
        live = (self.managed / "Assembly-CSharp.dll").read_bytes()
        self.assertEqual(self.cli(), 1)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), live)
        self.assertFalse((self.managed / "tldmp-v4-receipt.json").exists())

    def test_missing_multiplayer_receipt_refuses(self):
        (self.managed / "tldmp-v3-receipt.json").unlink()
        live = (self.managed / "Assembly-CSharp.dll").read_bytes()
        self.assertEqual(self.cli(), 1)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), live)


if __name__ == "__main__":
    unittest.main()
