# Changelog

Experimental build for game version `2024.11.26b_test`. Host and guests need the same build.

## v0.1.3

- Preserve reliable item-data repair replies through the multiplayer relay.
- Send combined vehicle snapshots with bounded repeats for packet loss.
- Add velocity-aware car smoothing and short, rate-limited impact updates.
- Bound missing-item state by messages, bytes, and replay work per frame, with bounded recovery requests.
- Cancel queued state on item deletion and reconcile changed attachment slots through native mounting routines.
- Record item recovery and transport health on both host and clients every five seconds.
- Update the game, lobby, installer, and release labels to v0.1.3 Roadworthy.

## v0.1.2

### Items and starting area

- Bus stops are placed beside the road instead of on it.
- Wheels with oversized tyres can now be picked up through the tyre.

### Multiplayer joining

- Steam multiplayer joining is potentially fixed

### Menus and interface

- In-game and lobby labels now show v0.1.2 Roadworthy.

## v0.1.1

### Menus and interface

- Custom settings stay available after loading a world or returning to the main menu.
- The in-game version label now reads v0.1.1 Roadworthy.
- Roadworthy lobbies show their patch version in the lobby browser.

### Multiplayer joining

- Fixes a password exchange that could leave Steam players stuck when joining a lobby.
- Joining players wait for a clear spawn before their position is sent to other players.
- Missing cars can be requested when their driving state arrives early.

### Performance

- Multiplayer startup can continue while game files are checked.
- Reuses mirror visibility calculations when the camera has not moved.
- The graphics preset only changes the game's shadow settings when Performance is selected.

### World streaming

- Refreshes streaming positions immediately after the world recenters.
- Ground diagnostics keep recording during longer drives.

## v0.1.0

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

- Joining refuses to start if your map or build differs from the host's.
- Test clients running on the same PC use the host's map and starting position.
- A joining player is not released until the ground under them has loaded.
- A joining player is moved to a clear spot on solid ground instead of dropping inside a car, prop or wall.
- Sending the world to a joining player no longer holds up everyone else's movement.
- Reconnecting no longer makes the game handle the same Steam event several times.
- When a player leaves, the remaining players no longer freeze.
- Joining is faster, and the car and cargo stay above the ground while the world loads.
- Abandoned cars and other world items show all their parts for joining players.
- Game-file checks start earlier, so the first join stalls less.

### Multiplayer players and vehicles

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
