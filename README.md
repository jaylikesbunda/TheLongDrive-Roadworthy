# The Long Drive: Roadworthy

Roadworthy is a multiplayer and performance patch for **The Long Drive**. It adds
host-approved seats and ownership, ordered item sync, checked world handover,
and single-player stability and rendering fixes. See [CHANGELOG.md](CHANGELOG.md).

Status: **v0.1.0, experimental.** Supported game build: `2024.11.26b_test` only.
Known multiplayer issues include distant join spawns and missing collision terrain while driving.

## Install

1. Requirements: Windows 10/11 x64 with .NET Framework 4.x (built in on current Windows).
2. Close the game.
3. Run `TheLongDrive-Roadworthy.exe` from the release and click the button.
   The installer finds your Steam install, builds the patch from the embedded sources
   against your own `Assembly-CSharp.dll`, and verifies it before swapping it in.
4. The host and every joining player must run the same build.

Saves are not modified.

## Restore

- Run the EXE again and click *Restore original*, or run `Restore Original.cmd`
  (equivalent to `TheLongDrive-Roadworthy.exe --restore-all`).
- `--restore` removes only the newest pass (single-player v4, then multiplayer v3).
- Tampered backups or receipts are refused; nothing is overwritten.

## Build from source

Repository layout: root `runtime/*.cs` (runtime helper), `tools/` (IL patchers and
verifiers), `packaging/singleplayer/*.cs` (single-player pass), `Build-Patch.ps1`
and `Manage-Stability.ps1` (build and install), `tests/` (C# and Python tests).

```
powershell -NoProfile -ExecutionPolicy Bypass -File Test-Stability.ps1 [-GameDirectory "C:\path\to\The Long Drive"]
python -m unittest discover -s tests -v
python packaging/build_release.py
```

- `Test-Stability.ps1` is the development gate (builds, verifies and round-trips v3
  on a disposable copy of the game). It needs the game installed; `-GameDirectory`
  overrides Steam detection, and `TLD_GAME_DIR` works too.
- The Python tests skip game-dependent checks when no game is found.
- `build_release.py` needs Python 3.12 with `packaging/requirements.txt`. It downloads
  the pinned Mono.Cecil 0.11.6 package into `.tools/` on first run and checks its SHA-256.

## GitHub releases

GitHub Actions builds and tests the installer and ZIP. Pushing a version tag matching
`packaging/build_release.py` (for example, `v0.1.0`) creates a GitHub prerelease
with the ZIP and changelog after the build job succeeds. Gameplay tests requiring
the installed game are not run by GitHub Actions.
