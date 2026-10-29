"""Exercise the real Tk GUI with a fake worker; never install or launch the game."""
import base64
import importlib.util
from pathlib import Path
import threading
import time
import tkinter as tk
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("installer_gui_fixture", ROOT / "packaging" / "installer_template.py")
app = importlib.util.module_from_spec(spec)
spec.loader.exec_module(app)


class IconTests(unittest.TestCase):
    def test_embedded_icon_is_a_png_and_can_be_applied(self):
        data = base64.b64decode(app.APP_ICON_PNG_B64)
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        root = tk.Tk()
        root.withdraw()
        try:
            app.set_window_icon(root)
            self.assertIsNotNone(app._WINDOW_ICON)
        finally:
            root.destroy()

    def test_icon_failure_never_blocks_the_gui(self):
        root = tk.Tk()
        root.withdraw()
        try:
            with mock.patch("tkinter.PhotoImage", side_effect=tk.TclError("unsupported image")):
                app.set_window_icon(root)
            self.assertIsNone(app._WINDOW_ICON)
        finally:
            root.destroy()

    def test_window_icon_uses_iconphoto_and_embedded_data(self):
        text = (ROOT / "packaging" / "installer_template.py").read_text(encoding="utf-8")
        self.assertIn("root.iconphoto(True, _WINDOW_ICON)", text)
        self.assertIn("tk.PhotoImage(data=APP_ICON_PNG_B64)", text)


class GuiTests(unittest.TestCase):
    def pump_until(self, root, predicate, timeout=3):
        deadline = time.monotonic() + timeout
        while not predicate() and time.monotonic() < deadline:
            root.update()
            time.sleep(.01)
        self.assertTrue(predicate())

    def test_unknown_installation_never_enables_patch(self):
        root = tk.Tk()
        root.withdraw()
        try:
            with mock.patch.object(app, "discover_games", return_value=[]), mock.patch.object(app, "installed_state", side_effect=app.InstallerError("Unknown game files")), mock.patch.object(app, "main_cli") as worker:
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Unknown Game")
                self.pump_until(root, lambda: "Cannot use" in ui["status"].get())
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                ui["run"]()
                worker.assert_not_called()
        finally:
            root.destroy()

    def test_old_detection_result_cannot_replace_new_path(self):
        root = tk.Tk()
        root.withdraw()
        started, release = threading.Event(), threading.Event()
        def inspect(game):
            if "slow" in str(game).lower():
                started.set()
                release.wait(4)
                return "v3"
            return "original"
        try:
            with mock.patch.object(app, "discover_games", return_value=[]), mock.patch.object(app, "installed_state", side_effect=inspect):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Slow Game")
                self.pump_until(root, started.is_set)
                ui["path"].set(r"C:\Fast Game")
                self.pump_until(root, lambda: ui["state"][0] == "original")
                release.set()
                # Let the old thread's result reach the GUI queue.
                deadline = time.monotonic() + .3
                while time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertEqual(ui["state"][0], "original")
                self.assertEqual(ui["primary"]["text"], "Patch game")
        finally:
            release.set()
            root.destroy()

    def test_gui_worker_remains_responsive_and_restores_buttons(self):
        root = tk.Tk()
        root.withdraw()
        release = threading.Event()
        calls = []
        fixture_state = ["original"]

        def worker(args):
            calls.append(args)
            print("Fake offline check passed.")
            release.wait(5)
            fixture_state[0] = "original" if "--restore-all" in args else "v4"
            return 0

        try:
            with mock.patch.object(app, "discover_games", return_value=[]), mock.patch.object(app, "installed_state", side_effect=lambda game: fixture_state[0]), mock.patch.object(app, "main_cli", side_effect=worker):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Disposable Fixture")
                deadline = time.monotonic() + 3
                while ui["state"][0] is None and time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertEqual(root.title(), app.APP_TITLE)
                self.assertIn("2024.11.26b_test", ui["subtitle"]["text"])
                self.assertEqual(ui["primary"]["text"], "Patch game")
                self.assertEqual(len(ui["controls"]), 2)  # folder and one action
                ui["run"]()
                self.assertTrue(ui["busy"][0])
                self.assertEqual(str(ui["controls"][-1]["state"]), "disabled")
                # An event can still run while the worker is held.
                responsive = []
                root.after(1, lambda: responsive.append(True))
                deadline = time.monotonic() + 2
                while not responsive and time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertTrue(responsive)
                release.set()
                deadline = time.monotonic() + 3
                while (ui["busy"][0] or ui["state"][0] != "v4") and time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertFalse(ui["busy"][0])
                self.assertNotIn("--test-only", calls[0])
                self.assertNotIn("--restore", calls[0])
                self.assertNotIn("--restore-all", calls[0])
                self.assertEqual(ui["primary"]["text"], "Restore original")
                self.assertIn("Fake offline check passed", ui["log"].get("1.0", "end"))
                self.assertEqual(str(ui["controls"][-1]["state"]), "normal")
                ui["run"]()
                deadline = time.monotonic() + 3
                while (ui["busy"][0] or ui["state"][0] != "original") and time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertIn("--restore-all", calls[1])
                self.assertEqual(ui["primary"]["text"], "Patch game")
        finally:
            release.set()
            root.destroy()


if __name__ == "__main__":
    unittest.main()
