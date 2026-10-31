<img width="3840" height="1868" alt="thelongdrive-roadworthy" src="https://github.com/user-attachments/assets/4fe0204a-8c2b-46d4-88a0-f50422778538" />

A multiplayer and performance patch for **The Long Drive**.

**v0.1.3 experimental** | Windows | Game build `2024.11.26b_test`

## What does this patch do?

- Smoother multiplayer movement and better vehicle and item syncing.
- Faster world streaming and fewer performance spikes.
- Graphics presets and shadow controls.
- Lots of fixes to terrain, saves, multiplayer and gameplay.

<details>
<summary>Full list of changes from the base game</summary>

### Menus and interface

- Graphics settings have Shadow quality and Performance preset options.
- Custom settings stay available after loading a world or returning to the main menu.
- Menus fit the screen at any resolution and aspect ratio.
- Menu backgrounds fill ultrawide screens.
- Consistent spacing and alignment across all menus.
- Menu elements no longer overlap or touch the screen edge.
- Settings tabs share one panel size.
- Save/Load buttons and confirmations line up in fixed spots.
- Steering wheel setup no longer shows the settings tab behind it.
- Multiplayer window, lobby list and dialogs stay inside the window.
- The version label reads v0.1.3 Roadworthy.
- Roadworthy lobbies show their patch version in the lobby browser.

### Joining and world loading

- Joining refuses to start if your map or build differs from the host's.
- Test clients running on the same PC use the host's map and starting position.
- A joining player is not released until the ground under them has loaded.
- A joining player is moved to a clear spot on solid ground instead of dropping inside a car, prop or wall.
- Joining players wait for a clear spawn before their position is sent to other players.
- Missing cars can be requested when their driving state arrives early.
- Sending the world to a joining player no longer holds up everyone else's movement.
- Reconnecting no longer makes the game handle the same Steam event several times.
- When a player leaves, the remaining players no longer freeze.
- Joining is faster, and the car and cargo stay above the ground while the world loads.
- Abandoned cars and other world items show all their parts for joining players.
- Game-file checks start earlier, so the first join stalls less.
- Multiplayer startup can continue while game files are checked.
- Multiplayer joining since v0.1.1 is potentially fixed: joins are faster and cars and cargo stay above the ground.

### Multiplayer players and vehicles

- Vehicle snapshots include velocity and repeat one recent update to tolerate packet loss.
- Car smoothing uses velocity, with limited prediction and short impact update bursts.
- The car you are riding in no longer stutters on your screen.
- Other players no longer skip ahead when they walk.
- Passengers no longer drift out of seats.
- Wheels and attached parts no longer drift away from moving cars.
- A car being pushed by another player moves smoothly instead of jumping once a second.
- When a car crashes and gets bloody, everyone sees the blood, not just the driver.
- Car smoothing can be turned on or off in Settings > Misc.
- Passengers receive car position updates up to 60 times a second instead of about 5.
- Other players' movement updates 30 times a second instead of about 3.
- High update rate can be turned off in Settings > Misc.
- Passengers no longer see the car freeze for up to a second every few seconds.
- Outdated position updates no longer queue behind newer ones.
- Late position updates are ignored, so cars and players no longer jump back to old positions.
- A short network hitch no longer hands the car over to a passenger.
- Cars and players no longer snap back and forth after a network hitch.
- A passenger no longer takes over the car while the host is nearby; whoever drives owns it.
- Doors, windows, mirrors and seats move smoothly for other players.
- Position updates reach other players sooner over Steam.
- Passengers no longer flash to the wrong terrain on long drives.

### Multiplayer items and parts

- Items held by other players no longer disappear.
- Items are no longer pulled out of your hands or off mounts by delayed updates.
- Quitting no longer deletes held or worn items.
- Box pieces and loose items no longer get stuck in a guest's car or teleport.
- Cargo ghosting for the driving client is available with TLD_MP_CARGO_GHOST=1.
- Unpainted car parts no longer show pure black for other players.
- Loose items in a moving car (glove box, cab, bed) stay in place for passengers and no longer fall out behind the car.
- Wheels with oversized tyres can now be picked up through the tyre.

### Performance and streaming

- Test clients on the same PC no longer disconnect just because a burst of updates arrives faster than the game reads them.
- A flood of network updates no longer freezes the game.
- Roads update faster when two players are nearby.
- Guests generate distant props with less stutter.
- Received item and car updates cost less CPU.
- Roads draw as static meshes at any scale.
- Roads update faster while you drive.
- Roads cost less to draw.
- Mirrors that are off screen no longer render.
- Mirror visibility is checked once per camera per frame instead of again for every mirror.
- Road chunks at the map edge are no longer rebuilt every frame.
- Large worlds stutter less when items sync, props spawn and leftover objects are cleaned up.
- Lower memory use for road and terrain generation.
- Terrain and prop checks no longer redo work that has not changed.
- Large prop requests are split up to reduce frame spikes.
- Items loading in cause fewer visible stutters.
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
- Bus stops are placed beside the road instead of on it.
- Cars no longer vanish when one of their parts is far away.
- Items no longer fall through unloaded ground.
- Car radiators explode properly again.

### Graphics settings

- Graphics preset: 2x MSAA with realtime reflection probes off.
- The Performance preset also lowers shadow distance, cascades and pixel lights; every other preset leaves the game's own shadow settings alone.
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
- Local two-instance motion tests: run `Start-Loopback.ps1 -Role Host`, load a test save, then run `Start-Loopback.ps1 -Role Client -RttMs 200 -JitterMs 40 -LossPercent 5`.
- Loopback enables combined snapshots, repeats, impact bursts and car smoothing at a 60 Hz target. Use `-CarRate 20`, `-NativeVehicleMotion`, or `-NativeCarSmoothing` for comparisons; apply network impairment on one instance only.
- In the receiver's log, `vehicleReceive` should show rising `receivedTotal` and `processedTotal`. Sender `combinedTotal`/`repeatedTotal` and `impactTriggersTotal`/`impactSentTotal` show which motion features ran.
- `build_release.py` needs Python 3.12 with `packaging/requirements.txt`. It downloads
  the pinned Mono.Cecil 0.11.6 package into `.tools/` on first run and checks its SHA-256.

</details>

<details>
<summary>GitHub releases</summary>


GitHub Actions builds and tests the installer and ZIP on pushes. To create a GitHub
prerelease, manually run the release workflow and enter the version tag matching
`packaging/build_release.py` (for example, `v0.1.3`). The workflow attaches the ZIP
and changelog after the build job succeeds. Gameplay tests requiring
the installed game are not run by GitHub Actions.

</details>
