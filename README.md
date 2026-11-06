<img width="3840" height="1868" alt="thelongdrive-roadworthy" src="https://github.com/user-attachments/assets/4fe0204a-8c2b-46d4-88a0-f50422778538" />

A multiplayer and performance patch for **The Long Drive**.

**v0.2.0-pre2 experimental** | Windows | Game build `2024.11.26b_test`

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
- The version label reads v0.2.0-pre2 Roadworthy.
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

- Vehicle updates better tolerate packet loss.
- Smoother remote cars and faster updates after impacts.
- Car movement updates work in tests on the same PC.
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

- Improved syncing of cars and parts spawned by guests.
- Failed spawn deliveries retry without duplicating confirmed spawns.
- New guest items and saved items are protected from early deletion.
- Part and color updates stay in order while missing items load.
- Moved parts update their mounts correctly.
- Missing-item recovery uses less memory and spreads work across frames.
- Missing-item repairs reach other players more reliably.
- Items held by other players no longer disappear.
- Items are no longer pulled out of your hands or off mounts by delayed updates.
- Quitting no longer deletes held or worn items.
- Box pieces and loose items no longer get stuck in a guest's car or teleport.
- Cargo ghosting for the driving client is available with TLD_MP_CARGO_GHOST=1.
- Unpainted car parts no longer show pure black for other players.
- Loose items in a moving car (glove box, cab, bed) stay in place for passengers and no longer fall out behind the car.
- Wheels with oversized tyres can now be picked up through the tyre.

### Performance and streaming

- Heavy collisions play fewer overlapping impact sounds.
- Loose debris takes less processing time.
- Debris pieces pass through each other.
- Shattered pieces use simpler collisions.
- Item weight checks use less memory.
- Vehicle wheels use less processing time.
- Stored items use less processing time.
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
- Breaking objects takes less repeated processing.
- Spawning items uses less memory.
- Choosing random items uses less memory.
- Reduced the work needed to spawn starting items.
- Objects finish spawning before saving or sharing the world.
- Large objects spawned by guests transfer more reliably.
- Cheaper physics checks.
- Large construction saves no longer slow down as they grow.
- Distant terrain loads without stuttering.
- Less CPU spent on rocks, trees and other props when nothing near you changes.
- Parked cars use less CPU.
- Idle tank caps use less CPU.
- Performance logs save without waiting for disk writes.
- Optional logs help track down stutters.
- Added an optional setting to test debris collisions.

### Terrain and gameplay

- New worlds stay in your selected biome.
- Grass thins out more gradually between biomes.
- Press `;` to switch which way you pour fluids.
- You can now drink your own piss
- Collect and mix piss as its own yellow liquid.
- Drinking piss hydrates less than water and slightly damages your health.
- Peeing no longer adds alcohol to containers.
- You get thirsty faster.
- Sprinting uses more food and water.
- Drinking fuel or oil can temporarily make you thirstier.
- Your water bar pulses yellow while you are sick.
- Cars can now damage enemy NPCs.
- Hard car hits can now kill and ragdoll rabbits.
- Multiplayer rabbits spawn around the host and guests, including in Steam sessions.
- Fixed rabbits losing forward momentum while moving.
- Reduced sand covering roads.
- Large mountains are less likely to overlap roads.
- Mountains that block roads try to move nearby instead of disappearing.
- Random buildings check for steep ground before spawning.
- Roadside buildings try nearby flatter ground.
- New worlds have smoother ground around buildings.
- New worlds keep more mountains beside roads without blocking them.
- Reduced background work while driving and using fluid containers.
- Existing saves keep their original biome setting.

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

- Multiplayer logs make missing parts, delays and color differences easier to investigate.
- Local test logs show whether spawned items reached the host.
- Press F11 to save a feedback bundle of your logs.
- Store and unstore logs name the item and its position.
- Safer saving with a `.bak` backup.
- Fixed a world-stopping error from duplicate physics bodies on load.
- Fixed an error flood when a worn item is destroyed.

</details>

<details>
<summary>Performance diagnostics</summary>

Performance records write in the background to
`%LOCALAPPDATA%\TheLongDrive-Roadworthy\diagnostics`. F11 feedback bundles include
the current runtime log. Busy or stalled disk writes drop excess records instead
of blocking gameplay; dropped-record counts are written to the log.

Performance diagnostics are opt-in. Set `TLD_MP_DIAGNOSTICS=1` before launching,
or create an empty `tldmp-diagnostics.on` beside `TheLongDrive.exe`, then restart.
Remove the flag/unset the variable and restart to disable them. No physics,
collision, rendering-quality or network-rate settings are changed by profiling.

Debris fragments pass through other debris while remaining solid against cars,
terrain and ordinary objects. This reduces collision work when several objects
break together, but loose fragments no longer form solid piles with each other.
Host and guests must use the same build. Performance and fragment behavior need
in-game testing, including pickup, cargo and save/reload.

A debris collision comparison is available for testing: an empty
`tldmp-debris-speculative.on` beside `TheLongDrive.exe` selects Continuous
Speculative for cactus/crate fragments and runtime slices. Remove the file and
restart to use native collision modes. Host and guests must select the same
mode; the compatibility check rejects different selections. Existing vehicles,
trailers, item identities and global physics settings retain their native setup.
Check fast impacts, ground penetration, pickup, cargo and save/reload before
judging this experimental mode safe for normal play.

The five-second reports cover 132 system timing labels, including player input
and interaction, collision callbacks, impact-audio creation, break/debris work,
storage scans, item spawning/loading/removal, part mounting, mass recalculation,
autosaves, headlights, fluids, world streaming and the existing vehicle/AI/render
systems. Inactive extended labels are omitted rather than printed as zero cost.

- `total`, `calls`, and `perFrame` cover completed outer calls in the window.
- `p50/p95` use the last 512 outer completions, not all calls or all frames.
- `maxCall` retains the worst call in the whole window, even after ring eviction.
- `peakFrame` and `peakFrameId` identify the heaviest frame for that label;
  `maxCallsPerFrame` independently identifies burst volume within the window.
- `fps-slow` reports the worst observed frames, their physics-step counts,
  `nativeTop` inclusive costs, and `nativeSelfTop` costs after subtracting only
  observed descendants. Never add nested inclusive totals together.
- Timers observe synchronous main-thread work, not the lifetime of a coroutine
  or worker job. `incomplete`/`incompleteScopes` indicate discarded/unbalanced
  observations, not measured zero-cost calls.

For a useful capture, wait until loading completes, idle for 15 seconds, then
reproduce one problem at a time (prop collisions, driving, storage, or streaming)
for 30-60 seconds. Note the approximate time and action, wait five seconds for
the report, and press F11 to create a feedback bundle. In multiplayer capture
both host and guest, identifying which machine stalled.

Diagnostics add CPU/logging overhead: use them to locate expensive paths, then
compare repeatable before/after runs with diagnostics disabled to judge FPS.

</details>

## Install

1. Requirements: Windows 10/11 x64 with .NET Framework 4.x (built in on current Windows).
2. Close the game.
3. Run `TheLongDrive-Roadworthy.exe` from the release and click the button.
   The installer finds your Steam install, shows the installed and bundled
   release/build, updates recognized older installs (the legacy release version may be unknown) automatically (the candidate
   is fully built and verified before the previous patch is touched, with
   recovery copies kept), and refuses to replace a newer release unless you pass
   `--allow-downgrade`. It also checks GitHub releases on open for a newer published
   version (read-only; offline works fine, `--check-updates` for the CLI). It
   builds the patch from the embedded sources
   against your own `Assembly-CSharp.dll`, and verifies it before swapping it in.
4. The host and every joining player must run the same build.

The installer does not modify your saves.

## Restore

- Run the EXE again and click *Restore original*, or run `Restore Original.cmd`
  (equivalent to `TheLongDrive-Roadworthy.exe --restore-all`).
- `--restore` removes only the newest pass (single-player v4, then multiplayer v3).
- Tampered backups or receipts are refused; nothing is overwritten.

## Experimental multiplayer (preview)

This build contains two mutually exclusive multiplayer backends. **Native is the
default and is unchanged.** The experimental backend reuses the proven native
world join, player replication and item/cargo sync, and adds host-issued
vehicle-motion ownership: the host admits car pose/velocity only from the peer
with the highest native car role, so each car has exactly one writer. The two
backends use different handshake signatures, so mixed-mode peers reject each
other before any world state is exchanged.

Select it in **Settings → Misc → Experimental multiplayer (preview)** before
creating or joining a session. The row stays interactive; if you change it before
any peer joins, the bridge recreates the session with the selected signature. Once
a peer is present the backend is fixed until the session ends. For development,
`TLD_MP_EXPERIMENTAL=1` selects it without the menu and overrides the row for that
process. Every peer must choose the same backend.

- Supported: session start/teardown, native world join, players, items/cargo,
  vehicle pose and velocity with host-issued ownership and epochs, per-car lease
  with stale-owner recovery, and atomic fitted-parts/stored-cargo groups.
- Selecting **Experimental multiplayer (preview)** in Misc enables the whole
  experimental path for the session: external snapshot/journal join (loopback),
  host item/state relay, player identity gate and breakable/prop authority. Native
  remains the untouched default. The env vars are kill switches:
  `TLD_MP_EXPERIMENTAL_JOIN=0`, `TLD_MP_EXPERIMENTAL_RELAY=0`,
  `TLD_MP_EXPERIMENTAL_PLAYERS=0`, `TLD_MP_EXPERIMENTAL_BREAKABLES=0`.
- The item-owner gate is **opt-in** (`TLD_MP_EXPERIMENTAL_ITEMS=1`) until
  interaction handoff exists. By default a guest's door/part/car interaction is
  not rejected by the host's car-group ownership; that was the cause of the host
  seeing neither the interaction nor the car moving.
- Host item/state interactions (doors, parts, car values, paint, damage, storage,
  attach/detach, usables, fluids) are relayed through the item event stream: the
  host assigns an event id and the guest applies each once, in order, with
  dependency buffering. Poses and guest-originated actions stay native. Vehicle
  ownership compares the host's actual role with the incoming driver's role, so a
  host fallback owner cannot reject the first guest-driver update.
- The build also contains the next-layer cores: a generic entity replication
  policy/registry, interest sets + far-store with duplicate reconciliation, and
  player/seat replication. The host seat ledger is wired to the native seat
  observer (observational; native sitting is not blocked yet).
- Blocked / not implemented: interest filtering and generic-registry delivery,
  guest-originated item events between guests, seat enforcement, vehicle handoff,
  experimental world/creature/POI ownership, experimental world-snapshot on Steam,
  persistence/save-cut protocol, Steam↔loopback mixing. Unsupported combinations
  are logged; `TLD_MP_VEHICLE_SNAPSHOTS=0` is refused in experimental mode and the
  session stays native.
- Diagnostics: `[TLD MP backend]` in the Unity log, `[TLD MP experimental]` and
  `experimental{cars= grants= rejected= applied=}` in the async diagnostic log.

### Loopback with protected test data

`Start-Loopback.ps1` gives each instance a stable private game-data root through
`TLD_MP_SAVE_ROOT` (`.local-analysis/loopback-data/host` and `.../client`). The
patched `pathscript.path()` redirects saves, settings, options, maps and logs under
that root, and bridge diagnostics go to `<root>/diagnostics`. Documents is never
used by a test instance. Add `-FreshData` to clear a role's root first.

```
powershell -NoProfile -ExecutionPolicy Bypass -File Start-Loopback.ps1 -Role Host -Experimental -FreshData
powershell -NoProfile -ExecutionPolicy Bypass -File Start-Loopback.ps1 -Role Client -Experimental -RttMs 200 -LossPercent 5 -JitterMs 40
```

You can also omit `-Experimental` and enable **Experimental multiplayer (preview)**
in Misc on both instances before joining. Load a save on the host and wait until
the world is visible before starting the client. Logs:
`.local-analysis/loopback-logs/` (Unity) and `.local-analysis/loopback-data/<role>/diagnostics/`
(bridge). Prove the redirect against the staged assembly before launching a world:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tests/scripts/Test-SaveIsolation.ps1 -StageDirectory <stage>
```

### Restoring the previous build

Two exact rollback sets are kept under `.local-analysis/staged/`. Close the game
before either operation.

**Previous Roadworthy build (installed before the experimental preview):** the
first install's `installed-backup` is the exact five-file chain. Copy it back:

```
$m = 'D:\Program Files (x86)\Steam\steamapps\common\The Long Drive\TheLongDrive_Data\Managed'
$b = '.local-analysis\staged\experimental-mp-20261105-final\installed-backup'
Copy-Item "$b\Tld.Multiplayer.dll" $m -Force
Copy-Item "$b\Assembly-CSharp.tldmp-v4-original.bin" $m -Force
Copy-Item "$b\Assembly-CSharp.dll" $m -Force
Copy-Item "$b\tldmp-v3-receipt.json" $m -Force
Copy-Item "$b\tldmp-v4-receipt.json" $m -Force
```

Expected result: `Assembly-CSharp.dll` = `C96C9C74…`, `Tld.Multiplayer.dll` =
`987DC2FA…`.

**Vanilla original game:** restore the pristine v3 original and remove the helper:

```
$m = 'D:\Program Files (x86)\Steam\steamapps\common\The Long Drive\TheLongDrive_Data\Managed'
Copy-Item "$m\Assembly-CSharp.tldmp-v3-original.bin" "$m\Assembly-CSharp.dll" -Force
Remove-Item "$m\Tld.Multiplayer.dll" -Force
```

Expected result: `Assembly-CSharp.dll` = `CF2C2D0E…`. The release EXE's *Restore
original* performs the equivalent v4-then-v3 removal. `Manage-Stability.ps1
-Action Restore` is v3-only and refuses a v3+v4 install, so use the copies above.


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
`packaging/build_release.py` (for example, `v0.2.0-pre2`). The workflow attaches the ZIP
and changelog after the build job succeeds. Gameplay tests requiring
the installed game are not run by GitHub Actions.

</details>

