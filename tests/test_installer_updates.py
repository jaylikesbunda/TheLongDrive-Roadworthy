"""Update-check API behavior: mocked HTTP, strict versioning, no writes."""
import importlib.util
import io
import json
import os
from pathlib import Path
import unittest
from unittest import mock
import urllib.error

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("installer_updates_fixture", ROOT / "packaging" / "installer_template.py")
app = importlib.util.module_from_spec(spec)
spec.loader.exec_module(app)

LIVE_API = "https://api.github.com/repos/jaylikesbunda/TheLongDrive-Roadworthy/releases?per_page=100&page=1"


def release(tag, draft=False, published="2026-01-01T00:00:00Z", html_url="https://evil.example/x"):
    return {"tag_name": tag, "draft": draft, "prerelease": True, "published_at": published,
            "html_url": html_url, "assets": []}


class Response:
    def __init__(self, data):
        self.data = data if isinstance(data, bytes) else json.dumps(data).encode()
        self.status = 200
    def read(self, n=-1):
        return self.data if n < 0 else self.data[:n]
    def __enter__(self):
        return self
    def __exit__(self, *args):
        return False


def urlopen_of(*pages):
    calls = []
    def open_(request, timeout=None):
        calls.append(request.full_url)
        page = int(request.full_url.rsplit("page=", 1)[1])
        body = pages[page - 1] if page - 1 < len(pages) else []
        if isinstance(body, Exception):
            raise body
        return Response(body)
    return open_, calls


class CheckUpdatesTests(unittest.TestCase):
    def check(self, *pages):
        open_, calls = urlopen_of(*pages)
        with mock.patch.object(app.urllib.request, "urlopen", side_effect=open_):
            return app.check_updates(), calls

    def test_numeric_max_wins_including_prerelease(self):
        result, _ = self.check([release("v0.1.9"), release("v0.1.10"), release("0.1.2")])
        self.assertEqual(result["version"], "0.1.10")
        self.assertTrue(result["newer"])
        self.assertTrue(result["url"].startswith(app.RELEASE_PAGE))
        self.assertNotIn("evil.example", result["url"])
        self.assertEqual(result["url"], app.RELEASE_PAGE + "v0.1.10")

    def test_older_and_equal_are_not_newer(self):
        result, _ = self.check([release("v0.1.2"), release("v0.0.9")])
        self.assertFalse(result["newer"])
        result, _ = self.check([release(f"v{app.VERSION}")])
        self.assertFalse(result["newer"])

    def test_drafts_and_unpublished_are_skipped(self):
        result, _ = self.check([release("v9.9.9", draft=True),
                                release("v8.8.8", published=None), release("v0.1.0")])
        self.assertEqual(result["version"], "0.1.0")
        self.assertFalse(result["newer"])

    def test_bad_tags_and_bad_entries_are_skipped(self):
        result, _ = self.check([release("nightly"), release("v1.2"), "junk", 42, release("v0.1.1")])
        self.assertEqual(result["version"], "0.1.1")

    def test_empty_list_means_no_published_release(self):
        result, _ = self.check([])
        self.assertIsNone(result["version"])
        self.assertFalse(result["newer"])
        self.assertIsNone(result["url"])

    def test_pagination_stops_after_full_page_and_caps_at_three(self):
        page1 = [release("v0.1.1")] * 100
        page2 = [release("v0.1.2")] * 100
        page3 = [release("v0.1.3")]
        result, calls = self.check(page1, page2, page3)
        self.assertEqual(len(calls), 3)
        self.assertTrue(all("per_page=100" in url and "page=" in url for url in calls))
        open_, calls = urlopen_of(page1, page1, page1, [release("v9.9.9")])
        with mock.patch.object(app.urllib.request, "urlopen", side_effect=open_):
            app.check_updates()
        self.assertEqual(len(calls), 3)  # fourth page never requested

    def test_malformed_payload_errors(self):
        for body in ({"not": "a list"}, b"<html>not json"):
            with self.assertRaises(app.InstallerError):
                self.check(body if isinstance(body, dict) else body)
        big = json.dumps([release("v0.1.0")] * 40000).encode()
        with self.assertRaises(app.InstallerError):
            self._oversize(big)

    def _oversize(self, big):
        with mock.patch.object(app.urllib.request, "urlopen", return_value=Response(big)):
            return app.check_updates()

    def test_http_and_network_errors_raise_installer_error(self):
        for exc in (urllib.error.HTTPError("u", 403, "Forbidden", {}, None),
                    urllib.error.URLError("dns"), TimeoutError("slow")):
            with self.assertRaises(app.InstallerError):
                self.check(exc)

    def test_timeout_parameter_and_headers(self):
        seen = {}
        def open_(request, timeout=None):
            seen["timeout"] = timeout
            seen["ua"] = request.get_header("User-agent")
            seen["accept"] = request.get_header("Accept")
            return Response([])
        with mock.patch.object(app.urllib.request, "urlopen", side_effect=open_):
            app.check_updates()
        self.assertEqual(seen["timeout"], 5)
        self.assertIn(app.VERSION, seen["ua"])
        self.assertEqual(seen["accept"], "application/vnd.github+json")


class CheckUpdatesCliTests(unittest.TestCase):
    def cli(self, result=None, error=None):
        def fake():
            if error:
                raise error
            return result
        with mock.patch.object(app, "check_updates", side_effect=fake), \
             mock.patch.object(app, "choose_game", side_effect=AssertionError("must not run")), \
             mock.patch.object(app, "preflight", side_effect=AssertionError("must not run")):
            return app.main_cli(["--no-pause", "--check-updates"])

    def test_newer_prints_url_and_returns_zero(self):
        self.assertEqual(self.cli({"version": "9.9.9", "tag": "v9.9.9", "newer": True,
                                   "url": app.RELEASE_PAGE + "v9.9.9"}), 0)

    def test_current_and_no_releases_return_zero(self):
        self.assertEqual(self.cli({"version": "0.1.0", "tag": "v0.1.0", "newer": False,
                                   "url": app.RELEASE_PAGE + "v0.1.0"}), 0)
        self.assertEqual(self.cli({"version": None, "tag": None, "newer": False, "url": None}), 0)

    def test_connection_error_returns_one(self):
        self.assertEqual(self.cli(error=app.InstallerError("offline")), 1)


class LiveApiTest(unittest.TestCase):
    @unittest.skipUnless(os.environ.get("TLD_LIVE_API") == "1", "approved live check only")
    def test_live_releases_api(self):
        out = ROOT / ".local-analysis" / "live-releases.json"
        out.parent.mkdir(exist_ok=True)
        request = app.urllib.request.Request(LIVE_API, headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": f"Roadworthy-Installer/{app.VERSION}"})
        with app.urllib.request.urlopen(request, timeout=5) as response:
            data = response.read(2 * 1024 * 1024 + 1)
            status = response.status
        out.write_bytes(data)
        print(f"live status={status} bytes={len(data)} saved={out}")
        releases = json.loads(data)
        self.assertIsInstance(releases, list)


if __name__ == "__main__":
    unittest.main()
