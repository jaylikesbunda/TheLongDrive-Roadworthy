THE LONG DRIVE: ROADWORTHY v0.2.0-pre1 (EXPERIMENTAL)
Multiplayer and performance patch for The Long Drive, beta 2024.11.26b_test only.
See CHANGELOG.txt for what changed.

INSTALL
- Needs Windows 10/11 x64 and .NET Framework 4.x. No Python needed.
- Close the game.
- Run TheLongDrive-Roadworthy.exe and follow the page. It finds your Steam
  folder, builds the patch from the embedded sources for your game files, verifies
  it, and installs it. If detection misses the game, the first page asks for the
  game folder. The window needs Microsoft Edge WebView2, included with Windows 10/11.
- The host and every joining player must run this same build.
- Saves are not changed.

RESTORE
- Run the EXE again and choose Restore original, or run "Restore Original.cmd".
- The window is a short set of pages: the first page offers only what applies
  (Install, Update to v0.2.0-pre1, Close when up to date or when a newer version is
  installed, plus Restore original when a patch is installed). Recognized older
  installs (the legacy release version may be unknown) update automatically: the new build is
  fully built and verified before any previous patch is touched, and recovery
  copies of the replaced files are kept under the installer cache.
- The progress page reports real install stages; Show details opens the live log.
- The window checks GitHub releases once on open and mentions a newer
  published release on the first page; it never downloads or
  installs anything by itself, and works fully offline.
- TheLongDrive-Roadworthy.exe --check-updates checks for a newer release
- TheLongDrive-Roadworthy.exe --status       reports installed vs bundled build
- TheLongDrive-Roadworthy.exe --update       update an older install offline
- TheLongDrive-Roadworthy.exe --restore      removes only the newest pass
- TheLongDrive-Roadworthy.exe --restore-all  returns to the original game
- Tampered backups or receipts are refused; nothing is overwritten.

CLI
  TheLongDrive-Roadworthy.exe --status --no-pause
  TheLongDrive-Roadworthy.exe --test-only --no-pause
  TheLongDrive-Roadworthy.exe --list --no-pause
  TheLongDrive-Roadworthy.exe --game-dir "D:\path\to\The Long Drive" --no-pause
  Add --restore or --restore-all to undo. Run with no arguments for the window.

FILES
- Originals are kept in the game's Managed folder while patched:
    Assembly-CSharp.tldmp-v3-original.bin   (vanilla)
    Assembly-CSharp.tldmp-v4-original.bin   (multiplayer v3)
- Logs and build files are cached in %LOCALAPPDATA%\TheLongDrive-Roadworthy.
- No game binaries are included.
