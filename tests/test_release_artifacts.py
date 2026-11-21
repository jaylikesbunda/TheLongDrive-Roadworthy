"""Checks on the built release: GUI subsystem, icon resources, shipped docs."""
import base64
import importlib.util
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pe_utils
import support

ROOT = support.ROOT
RELEASE = support.RELEASE
EXE = RELEASE / "TheLongDrive-Roadworthy.exe"
TAURI_CONF = ROOT / "installer" / "app" / "tauri.conf.json"

EXPECTED_RELEASE_FILES = sorted([
    "CHANGELOG.txt",
    "README.txt",
    "Restore Original.cmd",
    "SHA256SUMS.txt",
    "TheLongDrive-Roadworthy.exe",
])
SOURCE_REQUIRED = [
    "packaging/singleplayer/PatchV4.cs",
    "Build-Patch.ps1",
    "tests/scripts/Test-Stability.ps1",
    "Manage-Stability.ps1",
    "tools/Resolve-GameDirectory.ps1",
    ".tools/cecil.zip",
    "LICENSE",
    "runtime/assets/RoadworthyPois.bin",
    "LICENSES/Mono.Cecil-LICENSE.txt",
]


@unittest.skipUnless(EXE.is_file(), "release EXE has not been built yet")
class ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = EXE.read_bytes()

    def test_exe_subsystem_is_gui(self):
        self.assertEqual(pe_utils.subsystem(self.data), 2, "EXE must use the Windows GUI subsystem")

    def test_exe_carries_icon_resources(self):
        types = pe_utils.resource_types(self.data)
        self.assertIn(pe_utils.RT_ICON, types)
        self.assertIn(pe_utils.RT_GROUP_ICON, types)

    def test_tauri_config_is_windowed_and_iconed(self):
        self.assertTrue(TAURI_CONF.is_file(), f"missing config: {TAURI_CONF}")
        config = json.loads(TAURI_CONF.read_text(encoding="utf-8"))
        self.assertEqual(config["$schema"], "https://schema.tauri.app/config/2")
        self.assertFalse(config["bundle"]["active"], "the installer ships as one embedded-payload EXE")
        self.assertEqual(config["app"]["windows"][0]["label"], "main")
        self.assertEqual(config["bundle"]["icon"], ["icons/icon.ico"])

    def test_build_script_uses_the_rust_installer(self):
        text = (ROOT / "packaging" / "build_release_rust.py").read_text(encoding="utf-8")
        self.assertIn('"cargo"', text)
        self.assertIn('"roadworthy-app"', text)
        self.assertIn('"payload"', text)
        self.assertIn('"payload.zip"', text)

    def test_release_ships_only_readme_and_changelog(self):
        files = sorted(path.name for path in RELEASE.iterdir() if path.is_file())
        self.assertEqual(files, EXPECTED_RELEASE_FILES)
        self.assertFalse(any(path.suffix.lower() == ".md" for path in RELEASE.iterdir()))
        for stale in ("STABILITY-V3.md", "FUNDAMENTAL-PATCH.md", "DRIVING-FINDINGS.md",
                      "AUDIT-FINDINGS.md", "LOOSE-ITEM-FIX.md"):
            self.assertFalse((RELEASE / stale).exists(), stale)
        readme = (RELEASE / "README.txt").read_text(encoding="utf-8")
        self.assertIn("TheLongDrive-Roadworthy.exe", readme)
        self.assertIn("--restore-all", readme)
        changelog = (RELEASE / "CHANGELOG.txt").read_text(encoding="utf-8")
        self.assertIn("Multiplayer", changelog)
        self.assertIn("Single-player", changelog)

    def test_payload_contains_sources_and_no_game_binaries(self):
        spec = importlib.util.spec_from_file_location("build_release_under_test", ROOT / "packaging" / "build_release.py")
        build_release = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(build_release)
        names = set(build_release.source_entries())
        for name in SOURCE_REQUIRED:
            self.assertIn(name, names)
        forbidden = {"Assembly-CSharp.dll", "TheLongDrive.exe"}
        for name in names:
            self.assertNotIn(name.rsplit("/", 1)[-1], forbidden, name)
            if name.lower().endswith(".md"):
                self.assertIn(name, ("README.md", "CHANGELOG.md"), name)

    def test_template_embeds_the_window_icon(self):
        text = (ROOT / "packaging" / "installer_template.py").read_text(encoding="utf-8")
        self.assertIn("iconphoto(True", text)
        self.assertIn("PhotoImage(data=APP_ICON_PNG_B64)", text)
        data = base64.b64decode(support.installer.APP_ICON_PNG_B64)
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertGreater(len(data), 500)


class IconAssetTests(unittest.TestCase):
    def test_app_ico_is_multi_size(self):
        data = (ROOT / "packaging" / "app.ico").read_bytes()
        self.assertEqual(data[:4], b"\x00\x00\x01\x00")
        count = int.from_bytes(data[4:6], "little")
        self.assertEqual(count, 7)
        sizes = []
        for index in range(count):
            entry = 6 + 16 * index
            width = data[entry] or 256
            sizes.append(width)
        self.assertEqual(sizes, [16, 24, 32, 48, 64, 128, 256])

    def test_window_icon_matches_the_ico_asset(self):
        import sys
        sys.path.insert(0, str(ROOT / "packaging"))
        import appicon
        self.assertEqual(support.installer.APP_ICON_PNG_B64, appicon.window_png_b64())
