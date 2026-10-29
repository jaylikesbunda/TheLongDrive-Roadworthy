"""Shared helpers for the packaging tests.

The real game folder is only ever read. Every test that patches anything builds
its own disposable game-shaped directory under a temporary path.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / "release" / "TheLongDrive-Roadworthy"
ORIGINAL_SHA256 = "CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000"


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


installer = _load("patch_installer_support", ROOT / "packaging" / "installer_template.py")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def game_dirs():
    override = os.environ.get("TLD_GAME_DIR")
    if override:
        return [Path(override).expanduser().resolve()]
    try:
        return installer.discover_games()
    except Exception:
        return []


def managed_dirs():
    return [game / "TheLongDrive_Data" / "Managed" for game in game_dirs()
            if (game / "TheLongDrive_Data" / "Managed").is_dir()]


def _receipt(managed: Path):
    path = managed / "tldmp-v3-receipt.json"
    if not path.is_file():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None
    if data.get("version") != 3 or not data.get("patchedSha256"):
        return None
    return data


def find_v3_install():
    """Locate a real multiplayer v3 assembly, its receipt and a vanilla original.

    Returns a dict with ``managed``, ``v3``, ``vanilla``, ``helper``, ``receipt``
    or ``None`` when this machine has no verified v3 build available.
    """
    for managed in managed_dirs():
        receipt = _receipt(managed)
        if receipt is None:
            continue
        wanted = receipt["patchedSha256"].upper()
        candidates = [managed / "Assembly-CSharp.dll"]
        candidates += sorted(managed.glob("Assembly-CSharp.tldmp-v*-original.bin"))
        runs = ROOT / ".local-analysis" / "runs"
        if runs.is_dir():
            candidates += sorted(runs.glob("*/stability-v3/Assembly-CSharp.dll"))
        v3 = next((path for path in candidates if path.is_file() and sha256(path) == wanted), None)
        if v3 is None:
            continue
        vanilla_candidates = [managed / "Assembly-CSharp.dll"] + sorted(managed.glob("Assembly-CSharp.tldmp-v*-original.bin"))
        vanilla = next((path for path in vanilla_candidates
                        if path.is_file() and sha256(path) == ORIGINAL_SHA256), None)
        helper = managed / "Tld.Multiplayer.dll"
        if vanilla is None or not helper.is_file():
            continue
        return {"managed": managed, "v3": v3, "vanilla": vanilla, "helper": helper, "receipt": receipt}
    return None


def copy_reference_assemblies(source: Path, destination: Path) -> None:
    """Give a sandbox Managed its reference assemblies, as a real install has."""
    for path in source.glob("*.dll"):
        if path.name in ("Assembly-CSharp.dll", "Tld.Multiplayer.dll"):
            continue
        target = destination / path.name
        if not target.exists():
            target.write_bytes(path.read_bytes())
