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
        self.helper_bytes = helper
        self.vanilla_bytes = self.local["vanilla"].read_bytes()
        self.candidate_v3 = Path(self.tmp.name) / "cand-v3.dll"
        self.candidate_v3.write_bytes(self.v3_bytes)
        self.candidate_helper = Path(self.tmp.name) / "cand-helper.dll"
        self.candidate_helper.write_bytes(helper)
        self.candidate_manifest = Path(self.tmp.name) / "manifest.json"
        write_json(self.candidate_manifest, {"version": 3, "originalSha256": ORIGINAL_SHA256,
                                             "patched": str(self.candidate_v3), "patchedSha256": digest(self.v3_bytes),
                                             "runtime": str(self.candidate_helper), "runtimeSha256": digest(helper)})

    def fake_ps(self, work, log, script, game, *arguments):
        managed = managed_of(game)
        if arguments == ("-Action", "Restore"):
            (managed / "Assembly-CSharp.dll").write_bytes(self.vanilla_bytes)
            helper = managed / "Tld.Multiplayer.dll"
            if helper.exists():
                helper.unlink()
            return
        if arguments[:2] == ("-Action", "Install"):
            assert (managed / "Assembly-CSharp.dll").read_bytes() == self.vanilla_bytes
            (managed / "Assembly-CSharp.dll").write_bytes(self.v3_bytes)
            (managed / "Tld.Multiplayer.dll").write_bytes(self.helper_bytes)
            (managed / "Assembly-CSharp.tldmp-v3-original.bin").write_bytes(self.vanilla_bytes)
            write_json(managed / "tldmp-v3-receipt.json", {
                "version": 3, "originalSha256": ORIGINAL_SHA256,
                "patchedSha256": digest(self.v3_bytes), "runtimeSha256": digest(self.helper_bytes)})
            return
        raise AssertionError(f"unexpected PowerShell call {script} {arguments}")

    def cli(self, *arguments, preflight=True, run_ps=None):
        argv = ["--no-pause", "--game-dir", str(self.game), "--cache-dir", str(self.cache), *arguments]
        patches = [mock.patch.object(installer, "payload_bytes", return_value=self.payload),
                   mock.patch.object(installer, "PAYLOAD_SHA256", "F" * 64),
                   mock.patch.object(installer, "build_multiplayer", return_value=self.candidate_manifest),
                   mock.patch.object(installer, "run_ps", side_effect=run_ps or self.fake_ps)]
        if preflight:
            patches.append(mock.patch.object(installer, "preflight"))
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


class UpdateTests(V4StateTests):
    """Update lifecycle on synthetic byte fixtures: real state/restore/apply
    helpers; candidate builds and PowerShell calls stubbed."""

    NEWV3, NEWHELPER, NEWV4 = b"newv3", b"newhelper", b"newv4"
    FAKE_PAYLOAD = "F" * 64

    def setUp(self):
        super().setUp()
        self.cache = Path(tempfile.mkdtemp(prefix="tld-upd-"))
        self.addCleanup(shutil.rmtree, self.cache, True)
        self.payload_patch = mock.patch.object(installer, "PAYLOAD_SHA256", self.FAKE_PAYLOAD)
        self.payload_patch.start(); self.addCleanup(self.payload_patch.stop)
        self.cecil_bytes = b"synthetic Cecil fixture; never loaded"
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as archive:
            archive.writestr(".tools/cecil.zip", self.cecil_bytes)
        self.payload = buf.getvalue()
        self.cecil_patch = mock.patch.object(installer, "CECIL_SHA256", digest(self.cecil_bytes))
        self.cecil_patch.start(); self.addCleanup(self.cecil_patch.stop)
        self.calls = []
        self.candidate_v3 = self.cache / "cand-v3.dll"
        self.candidate_v3.write_bytes(self.NEWV3)
        self.candidate_helper = self.cache / "cand-helper.dll"
        self.candidate_helper.write_bytes(self.NEWHELPER)
        self.manifest = self.cache / "manifest.json"
        write_json(self.manifest, {"version": 3, "originalSha256": digest(VANILLA),
                                   "patched": str(self.candidate_v3), "patchedSha256": digest(self.NEWV3),
                                   "runtime": str(self.candidate_helper), "runtimeSha256": digest(self.NEWHELPER)})

    def stamp(self, receipt, version=None, build=None):
        if version is not None:
            receipt["roadworthyVersion"] = version
        if build is not None:
            receipt["installerPayloadSha256"] = build
        return receipt

    def fake_ps(self, work, log, script, game, *arguments):
        self.calls.append((script, arguments))
        managed = managed_of(game)
        if arguments == ("-Action", "Restore"):
            (managed / "Assembly-CSharp.dll").write_bytes(VANILLA)
            helper = managed / "Tld.Multiplayer.dll"
            if helper.exists():
                helper.unlink()
            return
        if arguments[:2] == ("-Action", "Install"):
            assert (managed / "Assembly-CSharp.dll").read_bytes() == VANILLA
            (managed / "Assembly-CSharp.dll").write_bytes(self.NEWV3)
            (managed / "Tld.Multiplayer.dll").write_bytes(self.NEWHELPER)
            (managed / "Assembly-CSharp.tldmp-v3-original.bin").write_bytes(VANILLA)
            write_json(managed / "tldmp-v3-receipt.json", {
                "version": 3, "originalSha256": digest(VANILLA),
                "patchedSha256": digest(self.NEWV3), "runtimeSha256": digest(self.NEWHELPER)})
            return
        raise AssertionError(f"unexpected PowerShell call {script} {arguments}")

    def cli(self, *arguments, build_mp=None, build_sp=None, run_ps=None, progress=None):
        argv = ["--no-pause", "--game-dir", str(self.game), "--cache-dir", str(self.cache), *arguments]
        patches = [mock.patch.object(installer, "payload_bytes", return_value=self.payload),
                   mock.patch.object(installer, "preflight"),
                   mock.patch.object(installer, "build_multiplayer",
                                     side_effect=build_mp or (lambda work, log, game: self.manifest)),
                   mock.patch.object(installer, "build_singleplayer",
                                     side_effect=build_sp or self.fake_v4_build),
                   mock.patch.object(installer, "run_ps", side_effect=run_ps or self.fake_ps)]
        with contextlib.ExitStack() as stack:
            for patch in patches:
                stack.enter_context(patch)
            return installer.main_cli(argv, progress=progress)

    def fake_v4_build(self, v3_file, baseline, managed, work, log):
        output = Path(work) / "cand-v4.dll"
        output.write_bytes(self.NEWV4)
        return output, [{"member": "fake"}]

    def managed_hashes(self):
        return {p.name: digest(p.read_bytes()) for p in self.managed.iterdir() if p.is_file()}

    def test_oldv4_upgrades_restoring_only_after_both_builds(self):
        self.install_v4()
        built = []
        self.assertEqual(self.cli(build_mp=lambda *a: (built.append("v3"), self.manifest)[1],
                                  build_sp=lambda *a: (built.append("v4"), self.fake_v4_build(*a))[1]), 0)
        self.assertEqual(built, ["v3", "v4"])                                # BOTH verified before first restore
        self.assertEqual(self.calls[0][1], ("-Action", "Restore"))
        self.assertEqual(self.calls[1][1][:2], ("-Action", "Install"))
        self.assertEqual(installer.installed_state(self.game), "v4")
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), self.NEWV4)
        receipt = read_json(self.managed / "tldmp-v3-receipt.json")
        self.assertEqual(receipt["roadworthyVersion"], installer.VERSION)
        self.assertEqual(receipt["installerPayloadSha256"], self.FAKE_PAYLOAD)

    def test_oldv3_and_v1_upgrade(self):
        self.install_v3()
        self.assertEqual(self.cli(), 0)
        self.assertEqual((self.managed / "Assembly-CSharp.dll").read_bytes(), self.NEWV4)
        # v1 fixture: legacy receipt format
        self.install_v3()  # reset managed then convert to v1
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"oldv1")
        (self.managed / "Assembly-CSharp.tldmp-v1-original.bin").write_bytes(VANILLA)
        for f in ("tldmp-v3-receipt.json", "Tld.Multiplayer.dll", "Assembly-CSharp.tldmp-v3-original.bin",
                  "tldmp-v4-receipt.json", "Assembly-CSharp.tldmp-v4-original.bin"):
            (self.managed / f).unlink()
        write_json(self.managed / "tldmp-v1-receipt.json", {"version": 1, "originalSha256": digest(VANILLA),
                                                            "patchedSha256": digest(b"oldv1")})
        self.calls.clear()
        self.assertEqual(self.cli(), 0)
        self.assertEqual(installer.installed_state(self.game), "v4")

    def test_current_is_noop_after_integrity(self):
        self.install_v4()
        for name in ("tldmp-v3-receipt.json", "tldmp-v4-receipt.json"):
            write_json(self.managed / name, self.stamp(read_json(self.managed / name), installer.VERSION, self.FAKE_PAYLOAD))
        before = self.managed_hashes()
        self.assertEqual(self.cli(run_ps=lambda *a: self.fail("no PowerShell on a current install")), 0)
        self.assertEqual(self.managed_hashes(), before)

    def test_newer_refused_unless_opted_in(self):
        self.install_v4()
        write_json(self.managed / "tldmp-v3-receipt.json",
                   self.stamp(read_json(self.managed / "tldmp-v3-receipt.json"), "9.9.9", self.FAKE_PAYLOAD))
        before = self.managed_hashes()
        self.assertEqual(self.cli(run_ps=lambda *a: self.fail("no PowerShell on refused downgrade")), 1)
        self.assertEqual(self.managed_hashes(), before)
        self.assertEqual(self.cli("--allow-downgrade"), 0)
        self.assertEqual(installer.installed_state(self.game), "v4")

    def test_same_version_different_payload_updates(self):
        self.install_v4()
        write_json(self.managed / "tldmp-v3-receipt.json",
                   self.stamp(read_json(self.managed / "tldmp-v3-receipt.json"), installer.VERSION, "A" * 64))
        self.assertEqual(self.cli(), 0)

    def test_malformed_version_is_unknown_metadata(self):
        self.install_v4()
        write_json(self.managed / "tldmp-v3-receipt.json",
                   self.stamp(read_json(self.managed / "tldmp-v3-receipt.json"), "0.1.x", self.FAKE_PAYLOAD))
        info = installer.installation_info(self.game)
        self.assertIsNone(info["version"])
        self.assertEqual(info["relation"], "current")                       # payload still proves the build
        self.assertEqual(self.cli(run_ps=lambda *a: self.fail("current build needs no update")), 0)

    def test_candidate_v4_build_failure_preserves_old_chain(self):
        self.install_v4()
        before = self.managed_hashes()
        self.assertEqual(self.cli(build_sp=lambda *a: (_ for _ in ()).throw(installer.InstallerError("v4 build"))), 1)
        self.assertEqual(self.managed_hashes(), before)

    def test_current_metadata_but_tampered_backup_refuses(self):
        self.install_v4()
        for name in ("tldmp-v3-receipt.json", "tldmp-v4-receipt.json"):
            write_json(self.managed / name, self.stamp(read_json(self.managed / name), installer.VERSION, self.FAKE_PAYLOAD))
        (self.managed / "Assembly-CSharp.tldmp-v4-original.bin").write_bytes(b"tampered")
        before = self.managed_hashes()
        self.assertEqual(self.cli(run_ps=lambda *a: self.fail("tamper must refuse")), 1)
        self.assertEqual(self.managed_hashes(), before)

    def test_candidate_build_failure_preserves_everything(self):
        self.install_v4()
        before = self.managed_hashes()
        self.assertEqual(self.cli(build_mp=lambda *a: (_ for _ in ()).throw(installer.InstallerError("build failed"))), 1)
        self.assertEqual(self.managed_hashes(), before)

    def test_staged_candidate_tamper_before_restore_refused(self):
        self.install_v3()
        before = self.managed_hashes()
        def tamper(v3_file, baseline, managed, work, log):
            self.candidate_v3.write_bytes(b"corrupted")
            return self.fake_v4_build(v3_file, baseline, managed, work, log)
        self.assertEqual(self.cli(build_sp=tamper), 1)
        self.assertEqual(self.managed_hashes(), before)

    def test_prepared_baseline_mismatch_rejected(self):
        self.install_v3()
        def wrong_install(work, log, script, game, *arguments):
            if arguments[:2] == ("-Action", "Install"):
                managed = managed_of(game)
                (managed / "Assembly-CSharp.dll").write_bytes(b"differentv3")
                (managed / "Tld.Multiplayer.dll").write_bytes(self.NEWHELPER)
                (managed / "Assembly-CSharp.tldmp-v3-original.bin").write_bytes(VANILLA)
                write_json(managed / "tldmp-v3-receipt.json", {"version": 3, "originalSha256": digest(VANILLA),
                                                               "patchedSha256": digest(b"differentv3"),
                                                               "runtimeSha256": digest(self.NEWHELPER)})
                return
            self.fake_ps(work, log, script, game, *arguments)
        self.assertEqual(self.cli(run_ps=wrong_install), 1)

    def test_restore_of_v3_returns_to_vanilla(self):
        self.install_v3()
        self.assertEqual(self.cli("--restore"), 0)
        self.assertEqual(installer.installed_state(self.game), "original")
        self.assertFalse((self.managed / "Tld.Multiplayer.dll").exists())

    def test_status_is_read_only(self):
        self.install_v4()
        before = self.managed_hashes()
        self.assertEqual(self.cli("--status", run_ps=lambda *a: self.fail("status touched PowerShell")), 0)
        self.assertEqual(self.managed_hashes(), before)

    def test_upgrade_never_reads_the_cached_cecil_package(self):
        self.install_v4()
        real_read = Path.read_bytes
        forbidden = (ROOT / ".tools" / "cecil.zip").resolve()
        def guard(path):
            assert path.resolve() != forbidden, "update must not depend on the developer's .tools cache"
            return real_read(path)
        with mock.patch.object(Path, "read_bytes", guard):
            self.assertEqual(self.cli(), 0)
        with zipfile.ZipFile(io.BytesIO(self.payload)) as archive:
            self.assertEqual(archive.read(".tools/cecil.zip"), self.cecil_bytes)

    def test_progress_monotonic_and_failure_stays_below_100(self):
        self.install_v4()
        seen = []
        self.assertEqual(self.cli(progress=lambda p, s: seen.append(p)), 0)
        self.assertEqual(seen, sorted(seen))
        self.assertEqual(seen[-1], 100)
        self.install_v4()
        seen.clear()
        self.assertEqual(self.cli(build_mp=lambda *a: (_ for _ in ()).throw(installer.InstallerError("boom")),
                                  progress=lambda p, s: seen.append(p)), 1)
        self.assertLess(seen[-1], 100)
        rollback = list(self.cache.glob("rollback-*"))
        self.assertTrue(rollback and (rollback[0] / "MANIFEST.json").is_file())


if __name__ == "__main__":
    unittest.main()
