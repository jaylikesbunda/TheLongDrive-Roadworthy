THE LONG DRIVE: ROADWORTHY v0.1.3 (EXPERIMENTAL)
Multiplayer and performance patch for The Long Drive, beta 2024.11.26b_test only.
See CHANGELOG.txt for what changed.

INSTALL
- Needs Windows 10/11 x64 and .NET Framework 4.x. No Python needed.
- Close the game.
- Run TheLongDrive-Roadworthy.exe and click the button. It finds your Steam
  folder, builds the patch from the embedded sources for your game files, verifies
  it, and installs it. If detection misses the game, paste the game folder path.
- The host and every joining player must run this same build.
- Saves are not changed.

RESTORE
- Run the EXE again and click Restore original, or run "Restore Original.cmd".
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
