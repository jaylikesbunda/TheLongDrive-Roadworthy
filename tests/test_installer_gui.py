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

NO_RELEASES = {"version": None, "tag": None, "newer": False, "url": None}


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
    return lambda game: {"state": state, "version": version,
                         "build": app.PAYLOAD_SHA256 if relation == "current" else None,
                         "relation": relation}


class GuiTests(unittest.TestCase):
    def pump_until(self, root, predicate, timeout=3):
        deadline = time.monotonic() + timeout
        while not predicate() and time.monotonic() < deadline:
            root.update()
            time.sleep(.01)
        self.assertTrue(predicate())

    def open_gui(self, root, info, worker=None, updates=None):
        import contextlib
        stack = contextlib.ExitStack()
        self.addCleanup(stack.close)
        stack.enter_context(mock.patch.object(app, "check_updates",
                                              return_value=updates or NO_RELEASES))
        stack.enter_context(mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)))
        stack.enter_context(mock.patch.object(app, "installed_state", side_effect=lambda g: info["state"]))
        stack.enter_context(mock.patch.object(app, "installation_info",
                                              side_effect=fake_info(info["state"], info["relation"], info.get("version"))))
        cli = stack.enter_context(mock.patch.object(app, "main_cli",
                                                    side_effect=worker or (lambda argv, progress=None: 0)))
        ui = app.build_gui(root)
        ui["path"].set(r"C:\Fixture Game")
        return ui, cli

    def test_dark_clam_theme_and_layout(self):
        root = tk.Tk()
        root.withdraw()
        try:
            from tkinter import ttk
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"})
            self.assertEqual(ttk.Style(root).theme_use(), "clam")
            self.assertEqual(root.cget("bg"), "#141819")
            self.assertEqual(ui["bundled"].get(), f"Roadworthy v{app.VERSION}")
            self.assertEqual(root.minsize(), (620, 404))
            self.assertEqual(str(ui["progress"]["mode"]), "determinate")
        finally:
            root.destroy()

    def test_unknown_installation_disables_both_buttons(self):
        root = tk.Tk()
        root.withdraw()
        try:
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", return_value=NO_RELEASES), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", side_effect=app.InstallerError("Unknown")), \
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
        root = tk.Tk()
        root.withdraw()
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
                self.assertTrue(ui["installed"].get())
        finally:
            root.destroy()

    def test_progress_follows_worker_callback_and_failure_retains(self):
        root = tk.Tk()
        root.withdraw()
        try:
            def worker(argv, progress=None):
                progress(0, "checking")
                progress(40, "v3 candidate verified")
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
        root = tk.Tk()
        root.withdraw()
        try:
            calls = []
            fixture = {"state": "original", "relation": "original"}
            def worker(argv, progress=None):
                calls.append(argv)
                if progress:
                    progress(100, "done")
                fixture["state"], fixture["relation"] = "v4", "current"
                return 0
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", return_value=NO_RELEASES), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", side_effect=lambda g: fixture["state"]), \
                 mock.patch.object(app, "installation_info",
                                   side_effect=lambda g: {"state": fixture["state"],
                                                          "version": app.VERSION if fixture["state"] == "v4" else None,
                                                          "build": app.PAYLOAD_SHA256 if fixture["state"] == "v4" else None,
                                                          "relation": fixture["relation"]}), \
                 mock.patch.object(app, "main_cli", side_effect=worker):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: ui["info"][0] is not None)
                ui["run"]()
                self.pump_until(root, lambda: not ui["busy"][0] and ui["info"][0] is not None
                                and ui["info"][0]["relation"] == "current")
                self.assertNotIn("--restore-all", calls[-1])
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                ui["run"]("restore")
                self.pump_until(root, lambda: not ui["busy"][0])
                self.assertIn("--restore-all", calls[-1])
        finally:
            root.destroy()

    def test_busy_blocks_browse_and_path_change(self):
        root = tk.Tk()
        root.withdraw()
        try:
            release = threading.Event()
            def worker(argv, progress=None):
                release.wait(3)
                return 0
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"}, worker)
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["run"]()
            self.pump_until(root, lambda: ui["busy"][0])
            for control in ui["controls"]:
                self.assertEqual(str(control["state"]), "disabled")
            release.set()
            self.pump_until(root, lambda: not ui["busy"][0])
        finally:
            release.set()
            root.destroy()

    def test_failed_operation_rechecks_real_state(self):
        root = tk.Tk()
        root.withdraw()
        try:
            fixture = {"state": "original", "relation": "original"}
            def worker(argv, progress=None):
                if progress:
                    progress(55, "stage")
                fixture["state"], fixture["relation"] = "v4", "current"
                return 1
            with mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "check_updates", return_value=NO_RELEASES), \
                 mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "installed_state", side_effect=lambda g: fixture["state"]), \
                 mock.patch.object(app, "installation_info",
                                   side_effect=lambda g: {"state": fixture["state"],
                                                          "version": app.VERSION if fixture["state"] == "v4" else None,
                                                          "build": app.PAYLOAD_SHA256 if fixture["state"] == "v4" else None,
                                                          "relation": fixture["relation"]}), \
                 mock.patch.object(app, "main_cli", side_effect=worker):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: ui["info"][0] is not None)
                ui["run"]()
                self.pump_until(root, lambda: (not ui["busy"][0]) and ui["info"][0] is not None
                                and ui["info"][0]["relation"] == "current")
                self.assertIn("failed at 55%", ui["phase"].get())
                self.assertEqual(ui["progress_value"].get(), 55)
                self.assertEqual(str(ui["primary"]["state"]), "disabled")
                self.assertEqual(str(ui["restore"]["state"]), "normal")
        finally:
            root.destroy()

    def test_stale_detection_cannot_overwrite_new_path(self):
        root = tk.Tk()
        root.withdraw()
        try:
            hold = threading.Event()
            seen = []
            def inspect(game):
                seen.append(str(game))
                if len(seen) == 1:
                    hold.wait(3)
                    return "v4"
                return "original"
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", return_value=NO_RELEASES), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", side_effect=inspect), \
                 mock.patch.object(app, "installation_info",
                                   side_effect=lambda g: {"state": "v4" if len(seen) == 1 else "original",
                                                          "version": None, "build": None,
                                                          "relation": "update" if len(seen) == 1 else "original"}):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Slow Game")
                self.pump_until(root, lambda: len(seen) >= 1)
                ui["path"].set(r"C:\Fast Game")
                hold.set()
                self.pump_until(root, lambda: ui["state"][0] == "original")
        finally:
            hold.set()
            root.destroy()

    def test_details_collapsed_toggle_and_error_expand(self):
        root = tk.Tk()
        root.withdraw()
        try:
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"})
            self.assertFalse(ui["log"].winfo_manager())
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["details"].invoke()
            root.update()
            self.assertTrue(ui["log"].winfo_manager())
            self.assertEqual(root.minsize(), (620, 544))
            ui["details"].invoke()
            root.update()
            self.assertFalse(ui["log"].winfo_manager())
        finally:
            root.destroy()
        root = tk.Tk()
        root.withdraw()
        try:
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", return_value=NO_RELEASES), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", side_effect=app.InstallerError("bad files")), \
                 mock.patch.object(app, "main_cli") as worker:
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Bad Game")
                self.pump_until(root, lambda: "Cannot use" in ui["status"].get())
                self.assertTrue(ui["log"].winfo_manager())
                self.assertIn("bad files", ui["log"].get("1.0", "end"))
        finally:
            root.destroy()

    def test_details_toggle_stays_responsive_while_busy(self):
        root = tk.Tk()
        root.withdraw()
        try:
            release = threading.Event()
            def worker(argv, progress=None):
                release.wait(3)
                return 0
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"}, worker)
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["run"]()
            self.pump_until(root, lambda: ui["busy"][0])
            ui["details"].invoke()
            root.update()
            self.assertTrue(ui["log"].winfo_manager())
            release.set()
            self.pump_until(root, lambda: not ui["busy"][0])
        finally:
            release.set()
            root.destroy()

    def test_update_banner_newer_offline_and_retry(self):
        root = tk.Tk()
        root.withdraw()
        try:
            calls = []
            result = [{"version": "9.9.9", "tag": "v9.9.9", "newer": True,
                       "url": app.RELEASE_PAGE + "v9.9.9"}]
            def fake_check():
                calls.append(1)
                if isinstance(result[0], Exception):
                    raise result[0]
                return result[0]
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", side_effect=fake_check), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", return_value="original"), \
                 mock.patch.object(app, "installation_info", side_effect=fake_info("original", "original")), \
                 mock.patch.object(app, "main_cli", return_value=0):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: "available" in ui["update_status"].get())
                self.assertIn("v9.9.9", ui["update_status"].get())
                self.assertTrue(ui["download_button"].winfo_manager())
                with mock.patch("webbrowser.open") as wb:
                    ui["download_button"].invoke()
                    wb.assert_called_once_with(app.RELEASE_PAGE + "v9.9.9")
                result[0] = app.InstallerError("offline")
                self.pump_until(root, lambda: ui["info"][0] is not None)  # offline must not block local install
                ui["check_updates_button"].invoke()
                self.pump_until(root, lambda: "Could not check" in ui["update_status"].get())
                self.assertFalse(ui["log"].winfo_manager())
                self.assertEqual(str(ui["primary"]["state"]), "normal")
                self.assertGreaterEqual(len(calls), 2)
        finally:
            root.destroy()

    def test_details_geometry_restores_and_manual_resize_kept(self):
        root = tk.Tk()
        try:
            ui, _ = self.open_gui(root, {"state": "original", "relation": "original"})
            self.pump_until(root, lambda: ui["info"][0] is not None)
            ui["details"].invoke()
            self.pump_until(root, lambda: root.winfo_height() >= 580)
            self.assertEqual(root.winfo_height(), 584)
            ui["details"].invoke()
            self.pump_until(root, lambda: root.winfo_height() <= 430)
            self.assertEqual(root.winfo_height(), 424)
            ui["details"].invoke()
            root.geometry("720x600")  # user-chosen size must not be overwritten
            root.update()
            ui["details"].invoke()
            root.update()
            self.assertNotEqual(root.winfo_height(), 424)
        finally:
            root.destroy()

    def test_blocked_check_keeps_gui_responsive_and_single_flight(self):
        root = tk.Tk()
        root.withdraw()
        try:
            hold = threading.Event()
            calls = []
            def slow_check():
                calls.append(1)
                hold.wait(3)
                return {"version": "0.1.3", "tag": "v0.1.3", "newer": False,
                        "url": app.RELEASE_PAGE + "v0.1.3"}
            with mock.patch.object(app, "discover_games", return_value=[]), \
                 mock.patch.object(app, "check_updates", side_effect=slow_check), \
                 mock.patch.object(app, "choose_game", side_effect=lambda p_: Path(p_)), \
                 mock.patch.object(app, "installed_state", return_value="original"), \
                 mock.patch.object(app, "installation_info", side_effect=fake_info("original", "original")), \
                 mock.patch.object(app, "main_cli", return_value=0):
                ui = app.build_gui(root)
                ui["path"].set(r"C:\Fixture Game")
                self.pump_until(root, lambda: ui["check_busy"][0])
                responsive = []
                root.after(1, lambda: responsive.append(True))
                deadline = time.monotonic() + 2
                while not responsive and time.monotonic() < deadline:
                    root.update()
                    time.sleep(.01)
                self.assertTrue(responsive)                                   # heartbeat while network blocks
                self.pump_until(root, lambda: ui["info"][0] is not None)
                self.assertEqual(str(ui["primary"]["state"]), "normal")       # local install stays enabled
                ui["check_updates_button"].invoke()                           # no overlap while in flight
                self.assertEqual(len(calls), 1)
                hold.set()
                self.pump_until(root, lambda: not ui["check_busy"][0])
                self.assertIn("No newer published version", ui["update_status"].get())
        finally:
            hold.set()
            root.destroy()

    def test_updater_footer_has_entrypoint_and_no_preemptive_restore(self):
        source = (ROOT / "packaging" / "updater_gui.py").read_text(encoding="utf-8")
        self.assertIn('if __name__ ==', source)
        self.assertIn('gui_self_test', source)
        self.assertIn('verify_update_payload', source)
        self.assertNotIn('["--restore-all"], []', source)
        compile(source, 'updater_gui.py', 'exec')

    def test_gui_self_test_entrypoint_runs(self):
        self.assertEqual(app.gui_self_test(), 0)


if __name__ == "__main__":
    unittest.main()
