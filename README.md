# The Long Drive: Roadworthy

A multiplayer and performance patch for **The Long Drive**.

**v0.1.0 experimental** | Windows | Game build `2024.11.26b_test`

## At a glance

- Smoother multiplayer movement, vehicles and item syncing.
- Faster world streaming and fewer performance spikes.
- Graphics presets and shadow controls.
- Fixes for terrain, saves and gameplay.

<details>
<summary>Full list of changes from the base game</summary>

### Menus and interface

- Graphics settings have Shadow quality and Performance preset options.
- Menus fit the screen at any resolution and aspect ratio.
- Menu backgrounds fill ultrawide screens.
- Consistent spacing and alignment across all menus.
- Menu elements no longer overlap or touch the screen edge.
- Settings tabs share one panel size.
- Save/Load buttons and confirmations line up in fixed spots.
- Steering wheel setup no longer shows the settings tab behind it.
- Multiplayer window, lobby list and dialogs stay inside the window.
- The version label reads v0.1.0 Roadworthy.

### Joining and world loading

- Joining checks that the host's map and build match.
- Loopback test clients use the host’s map and starting position.
- Joining players wait for collision ground before being released.
- Joining players use clearance checks around props, cars, walls and other players.
- Large world transfers no longer delay movement updates.
- Reconnecting no longer duplicates Steam event handlers.
- A player leaving no longer freezes other players.
- Clients join faster and keep the car and cargo above the ground while the world loads.
- Abandoned cars and other world items show all their parts for joining players.
- Content checks start earlier to reduce first-join pauses.

### Multiplayer players and vehicles

- Smoother passenger view of moving cars.
- Other players move more smoothly.
- Passengers no longer drift out of seats.
- Wheels and attached parts no longer drift away from moving cars.
- Car smoothing can be turned on or off in Settings > Misc.
- Passengers receive car position updates up to 60 times a second instead of about 5.
- Other players' movement updates 30 times a second instead of about 3.
- High update rate can be turned off in Settings > Misc.
- Passengers no longer see the car pause for up to a second every few seconds.
- Outdated position updates no longer queue behind newer ones.
- Late position updates are ignored, so cars and players no longer jump back to old positions.
- Passengers no longer take over the car after a short network hitch.
- Cars and players no longer jump back and forth after a hitch.
- A passenger no longer takes over the car while the host is nearby; whoever drives owns it.
- Doors, windows, mirrors and seats move smoothly for other players.
- Faster position updates over Steam.
- Passengers no longer flash to the wrong terrain on long drives.

### Multiplayer items and parts

- Items held by other players no longer disappear.
- Items are no longer pulled out of your hands or off mounts by delayed updates.
- Quitting no longer deletes held or worn items.
- Box pieces and loose items no longer get stuck in a guest's car or teleport.
- Cargo ghosting for the driving client is available with TLD_MP_CARGO_GHOST=1.
- Unpainted car parts no longer show pure black for other players.
- Loose items in a moving car (glove box, cab, bed) stay in place for passengers and no longer fall out behind the car.

### Performance and streaming

- Loopback network bursts no longer disconnect solely because the receive queue fills.
- Busy sessions no longer freeze from network update floods.
- Faster road updates with two players nearby.
- Guests generate distant props with less stutter.
- Received item and car updates cost less CPU.
- Roads draw as static meshes at any scale.
- Faster road updates while driving.
- Roads cost less to draw.
- Mirrors that are off screen no longer render.
- Road chunks at the map edge are no longer rebuilt every frame.
- Less stutter from item sync, spawning and prop cleanup in large worlds.
- Lower memory use for road and terrain generation.
- Terrain and prop checks do less repeated work.
- Large prop requests are split up to reduce frame spikes.
- Fewer stutters when items load in.
- Cheaper physics checks.
- Large construction saves no longer slow down as they grow.
- Distant terrain loads without stuttering.
- Less CPU spent on rocks, trees and other props when nothing near you changes.
- Parked cars use less CPU.
- Idle tank caps use less CPU.

### Terrain and gameplay

- Roadside buildings sit on the ground instead of the road centre line.
- Changing view distance no longer removes the ground under you.
- Fixed terrain corruption when several pieces generate at once.
- The starting-area bus stop is placed on the ground once terrain loads.
- Cars no longer vanish when one of their parts is far away.
- Items no longer fall through unloaded ground.
- Car radiators explode properly again.

### Graphics settings

- New graphics preset: 2x MSAA, four pixel lights, 80 m shadows with two cascades, realtime reflection probes off.
- Realtime reflection probes stay off after loading a save.

### Single-player and multiplayer wildlife

- Hosting removes rabbits from the world, so guests no longer freeze or get flung by them.
- Removed warning spam from flying animals.
- Rabbits spawn in single-player again (toggle in Settings > Misc).
- Sandstorms occur again in single-player (toggle in Settings > Misc).
- Removed warning spam from frozen far-away creatures.

### Saving and diagnostics

- Press F11 to save a feedback bundle of your logs.
- Store and unstore logs name the item and its position.
- Safer saving with a `.bak` backup.
- Fixed a world-stopping error from duplicate physics bodies on load.
- Fixed an error flood when a worn item is destroyed.

</details>

## Install

1. Requirements: Windows 10/11 x64 with .NET Framework 4.x (built in on current Windows).
2. Close the game.
3. Run `TheLongDrive-Roadworthy.exe` from the release and click the button.
   The installer finds your Steam install, builds the patch from the embedded sources
   against your own `Assembly-CSharp.dll`, and verifies it before swapping it in.
4. The host and every joining player must run the same build.

The installer does not modify your saves.

## Restore

- Run the EXE again and click *Restore original*, or run `Restore Original.cmd`
  (equivalent to `TheLongDrive-Roadworthy.exe --restore-all`).
- `--restore` removes only the newest pass (single-player v4, then multiplayer v3).
- Tampered backups or receipts are refused; nothing is overwritten.

<details>
<summary>Build from source</summary>


Repository layout: root `runtime/*.cs` (runtime helper), `tools/` (IL patchers and
verifiers), `packaging/singleplayer/*.cs` (single-player pass), `Build-Patch.ps1`
and `Manage-Stability.ps1` (build and install), `tests/` (C# and Python tests).

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/scripts/Test-Stability.ps1 [-GameDirectory "C:\path\to\The Long Drive"]
python -m unittest discover -s tests -v
python packaging/build_release.py
```

- `Test-Stability.ps1` is the development gate (builds, verifies and round-trips v3
  on a disposable copy of the game). It needs the game installed; `-GameDirectory`
  overrides Steam detection, and `TLD_GAME_DIR` works too.
- The Python tests skip game-dependent checks when no game is found.
- `build_release.py` needs Python 3.12 with `packaging/requirements.txt`. It downloads
  the pinned Mono.Cecil 0.11.6 package into `.tools/` on first run and checks its SHA-256.

</details>

<details>
<summary>GitHub releases</summary>


GitHub Actions builds and tests the installer and ZIP on pushes. To create a GitHub
prerelease, manually run the release workflow and enter the version tag matching
`packaging/build_release.py` (for example, `v0.1.0`). The workflow attaches the ZIP
and changelog after the build job succeeds. Gameplay tests requiring
the installed game are not run by GitHub Actions.

</details>
