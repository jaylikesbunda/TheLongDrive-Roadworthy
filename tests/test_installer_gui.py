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
            root.update()
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


def fake_info(state, relation, version=None):
    return lambda game: {"state": state, "version": version, "build": app.PAYLOAD_SHA256 if relation == "current" else None,
                         "relation": relation}


class GuiTests(unittest.TestCase):
    def pump_until(self, root, predicate, timeout=3):
        deadline = time.monotonic() + timeout
        while not predicate() and time.monotonic() < deadline:
            root.update()
            time.sleep(.01)
        self.assertTrue(predicate())

    def open_gui(self, root, info, worker=None):
        import contextlib
        stack = contextlib.ExitStack()
        self.addCleanup(stack.close)
        stack.enter_context(mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)))
        stack.enter_context(mock.patch.object(app, "installed_state", side_effect=lambda g: info["state"]))
        stack.enter_context(mock.patch.object(app, "installation_info", side_effect=fake_info(info["state"], info["relation"], info.get("version"))))
        cli = stack.enter_context(mock.patch.object(app, "main_cli", side_effect=worker or (lambda argv, progress=None: 0)))
        ui = app.build_gui(root)
        ui["path"].set(r"C:\Fixture Game")
        return ui, cli

    def test_dark_clam_theme_and_layout(self):
        root = tk.Tk(); root.withdraw()
        try:
            from tkinter import ttk
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"})
            self.assertEqual(ttk.Style(root).theme_use(), "clam")
            self.assertEqual(root.cget("bg"), "#141819")
            self.assertEqual(ui["bundled"].get(), f"Roadworthy v{app.VERSION}")
            self.assertEqual(root.minsize(), (620, 380))
            self.assertEqual(str(ui["progress"]["mode"]), "determinate")
            root.destroy()
        except tk.TclError:
            root.destroy()

    def test_unknown_installation_disables_both_buttons(self):
        root = tk.Tk(); root.withdraw()
        try:
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)),              mock.patch.object(app, "installed_state", side_effect=app.InstallerError("Unknown")), \
                 mock.patch.object(app, "main_cli") as worker:
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Unknown Game")
                self.pump_until(root, lambda: "Cannot use" in ui["status"].get())
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                self.assertEqual(str(ui["restore"]["state"]), "disabled")
                ui["run"]()
                worker.assert_not_called()
        finally:
            root.destroy()

    def test_action_labels_follow_relation(self):
        root = tk.Tk(); root.withdraw()
        cases = [("original", "original", "Install Roadworthy", "normal", "disabled"),
                 ("v4", "update", f"Update to v{app.VERSION}", "normal", "normal"),
                 ("v4", "current", "Up to date", "disabled", "normal"),
                 ("v4", "newer", "Newer version installed", "disabled", "normal")]
        try:
            for st, rel, text, primary_state, restore_state in cases:
                ui, _ = self.open_gui(root, {"state": st, "relation": rel})
                self.pump_until(root, lambda u=ui: u["info"][0] is not None)
                self.assertEqual(ui["primary"]["text"], text, rel)
                self.assertEqual(str(ui["primary"]["state"]), primary_state, rel)
                self.assertEqual(str(ui["restore"]["state"]), restore_state, rel)
                expected_card = "Original game" if st == "original" else ("Roadworthy" in ui["installed"].get() or "Legacy" in ui["installed"].get())
                self.assertTrue(expected_card if st == "original" else expected_card)
        finally:
            root.destroy()

    def test_progress_follows_worker_callback_and_failure_retains(self):
        root = tk.Tk(); root.withdraw()
        try:
            def worker(argv, progress=None):
                progress(0, "checking"); progress(40, "v3 candidate verified")
                return 1
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"}, worker)
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["run"]()
            self.pump_until(root, lambda: not ui["busy"][0])
            self.assertEqual(ui["progress_value"].get(), 40)
            self.assertIn("failed", ui["phase"].get())
        finally:
            root.destroy()

    def test_success_rechecks_and_restore_action(self):
        root = tk.Tk(); root.withdraw()
        try:
            calls = []
            fixture = {"state": "original", "relation": "original"}
            def worker(argv, progress=None):
                calls.append(argv)
                if progress: progress(100, "done")
                fixture["state"], fixture["relation"] = "v4", "current"
                return 0
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)),              mock.patch.object(app, "installed_state", side_effect=lambda g: fixture["state"]), \
                 mock.patch.object(app, "installation_info", side_effect=fake_info("x", "x")) as _n, \
                 mock.patch.object(app, "main_cli", side_effect=worker):
                app.installation_info = lambda g: {"state": fixture["state"], "version": app.VERSION if fixture["state"] == "v4" else None,
                                                   "build": app.PAYLOAD_SHA256 if fixture["state"] == "v4" else None,
                                                   "relation": fixture["relation"]}
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: ui["info"][0] is not None)
                ui["run"]()
                self.pump_until(root, lambda: not ui["busy"][0] and ui["info"][0] is not None and ui["info"][0]["relation"] == "current")
                self.assertNotIn("--restore-all", calls[-1])
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                ui["run"]("restore")
                self.pump_until(root, lambda: not ui["busy"][0])
                self.assertIn("--restore-all", calls[-1])
        finally:
            root.destroy()

    def test_busy_blocks_browse_and_path_change(self):
        root = tk.Tk(); root.withdraw()
        try:
            release = threading.Event()
            def worker(argv, progress=None):
                release.wait(3); return 0
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"}, worker)
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["run"]()
            self.pump_until(root, lambda: ui["busy"][0])
            for control in ui["controls"]:
                self.assertEqual(str(control["state"]), "disabled")
            release.set()
            self.pump_until(root, lambda: not ui["busy"][0])
        finally:
            release.set(); root.destroy()

    def test_failed_operation_rechecks_real_state(self):
        root = tk.Tk(); root.withdraw()
        try:
            fixture = {"state": "original", "relation": "original"}
            def worker(argv, progress=None):
                if progress: progress(55, "stage")
                fixture["state"], fixture["relation"] = "v4", "current"
                return 1
            with mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)),                  mock.patch.object(app, "installed_state", side_effect=lambda g: fixture["state"]),                  mock.patch.object(app, "installation_info", side_effect=lambda g: {"state": fixture["state"], "version": app.VERSION if fixture["state"] == "v4" else None, "build": app.PAYLOAD_SHA256 if fixture["state"] == "v4" else None, "relation": fixture["relation"]}),                  mock.patch.object(app, "main_cli", side_effect=worker),                  mock.patch.object(app, "discover_games", return_value=[]):
                ui = app.build_gui(root); ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: ui["info"][0] is not None)
                ui["run"]()
                self.pump_until(root, lambda: (not ui["busy"][0]) and ui["info"][0] is not None and ui["info"][0]["relation"] == "current")
                self.assertIn("failed at 55%", ui["phase"].get())
                self.assertEqual(ui["progress_value"].get(), 55)
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                self.assertEqual(str(ui["restore"]["state"]), "normal")
        finally:
            root.destroy()

    def test_stale_detection_cannot_overwrite_new_path(self):
        root = tk.Tk(); root.withdraw()
        try:
            hold = threading.Event()
            seen = []
            def inspect(game):
                seen.append(str(game))
                if len(seen) == 1:
                    hold.wait(3)
                    return "v4"
                return "original"
            with mock.patch.object(app, "discover_games", return_value=[]),                  mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)),                  mock.patch.object(app, "installed_state", side_effect=inspect),                  mock.patch.object(app, "installation_info", side_effect=lambda g: {"state": "v4" if len(seen) == 1 else "original", "version": None, "build": None, "relation": "update" if len(seen) == 1 else "original"}):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Slow Game")
                self.pump_until(root, lambda: len(seen) >= 1)   # first inspect started
                ui["path"].set(r"C:\Fast Game")                 # supersedes it (new token)
                hold.set()
                self.pump_until(root, lambda: ui["state"][0] == "original")
        finally:
            hold.set(); root.destroy()

    def test_updater_footer_has_entrypoint_and_no_preemptive_restore(self):
        source = (ROOT / "packaging" / "updater_gui.py").read_text(encoding="utf-8")
        self.assertIn('if __name__ ==', source)
        self.assertIn('gui_self_test', source)
        self.assertIn('verify_update_payload', source)
        self.assertNotIn('["--restore-all"], []', source)
        compile(source, 'updater_gui.py', 'exec')

    def test_details_collapsed_toggle_and_error_expand(self):
        root = tk.Tk(); root.withdraw()
        try:
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"})
            self.assertFalse(ui["log"].winfo_manager())          # collapsed by default
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["details"].invoke()
            root.update()
            self.assertTrue(ui["log"].winfo_manager())           # manual toggle expands
            self.assertEqual(root.minsize(), (620, 520))
            ui["details"].invoke()
            root.update()
            self.assertFalse(ui["log"].winfo_manager())
        finally:
            root.destroy()
        root = tk.Tk(); root.withdraw()
        try:
            with mock.patch.object(app, "discover_games", return_value=[]),                  mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)),                  mock.patch.object(app, "installed_state", side_effect=app.InstallerError("bad files")),                  mock.patch.object(app, "main_cli") as worker:
                ui = app.build_gui(root); ui["path"].set(r"C:\Bad Game")
                self.pump_until(root, lambda: "Cannot use" in ui["status"].get())
                self.assertTrue(ui["log"].winfo_manager())       # invalid auto-expands
                self.assertIn("bad files", ui["log"].get("1.0", "end"))
        finally:
            root.destroy()

    def test_details_toggle_stays_responsive_while_busy(self):
        root = tk.Tk(); root.withdraw()
        try:
            release = threading.Event()
            def worker(argv, progress=None):
                release.wait(3); return 0
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"}, worker)
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["run"]()
            self.pump_until(root, lambda: ui["busy"][0])
            ui["details"].invoke(); root.update()
            self.assertTrue(ui["log"].winfo_manager())
            release.set(); self.pump_until(root, lambda: not ui["busy"][0])
        finally:
            release.set(); root.destroy()

    def test_gui_self_test_entrypoint_runs(self):
        self.assertEqual(app.gui_self_test(), 0)


if __name__ == "__main__":
    unittest.main()
