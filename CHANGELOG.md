# Changelog

Current build: `20261029-loopback-backpressure` (29 October 2026)

Experimental build for game version `2024.11.26b_test`. Host and guests need the same build.

## Menus

- Graphics settings have Shadow quality and Performance preset options.
- Menus fit the screen at any resolution and aspect ratio.
- Menu backgrounds fill ultrawide screens.
- Consistent spacing and alignment across all menus.
- Menu elements no longer overlap or touch the screen edge.
- Settings tabs share one panel size.
- Save/Load buttons and confirmations line up in fixed spots.
- Steering wheel setup no longer shows the settings tab behind it.
- Multiplayer window, lobby list and dialogs stay inside the window.

## Multiplayer

- Joining checks that the host's map and build match.
- Loopback test clients use the host’s map and starting position.
- Loopback network bursts no longer disconnect solely because the receive queue fills.
- Joining players wait for collision ground before being released.
- Joining players use clearance checks around props, cars, walls and other players.
- Large world transfers no longer delay movement updates.
- Busy sessions no longer freeze from network update floods.
- Reconnecting no longer duplicates Steam event handlers.
- A player leaving no longer freezes other players.
- Smoother passenger view of moving cars.
- Other players move more smoothly.
- Passengers no longer drift out of seats.
- Wheels and attached parts no longer drift away from moving cars.
- Items held by other players no longer disappear.
- Items are no longer pulled out of your hands or off mounts by delayed updates.
- Quitting no longer deletes held or worn items.
- Box pieces and loose items no longer get stuck in a guest's car or teleport.
- Faster road updates with two players nearby.
- Press F11 to save a feedback bundle of your logs.
- Guests generate distant props with less stutter.
- Clients join faster and keep the car and cargo above the ground while the world loads.
- Car smoothing can be turned on or off in Settings > Misc.
- Cargo ghosting for the driving client is available with TLD_MP_CARGO_GHOST=1.
- Passengers receive car position updates up to 60 times a second instead of about 5.
- Other players' movement updates 30 times a second instead of about 3.
- High update rate can be turned off in Settings > Misc.
- Passengers no longer see the car pause for up to a second every few seconds.
- Received item and car updates cost less CPU.
- Outdated position updates no longer queue behind newer ones.
- Late position updates are ignored, so cars and players no longer jump back to old positions.
- Passengers no longer take over the car after a short network hitch.
- Cars and players no longer jump back and forth after a hitch.
- Abandoned cars and other world items show all their parts for joining players.
- A passenger no longer takes over the car while the host is nearby; whoever drives owns it.
- Unpainted car parts no longer show pure black for other players.
- Doors, windows, mirrors and seats move smoothly for other players.
- Loose items in a moving car (glove box, cab, bed) stay in place for passengers and no longer fall out behind the car.
- Faster position updates over Steam.
- Passengers no longer flash to the wrong terrain on long drives.
- Hosting removes rabbits from the world, so guests no longer freeze or get flung by them.

## Single-player and multiplayer

- The version label reads v0.1.0 Roadworthy.
- Roads draw as static meshes at any scale.
- Store and unstore logs name the item and its position.
- Faster road updates while driving.
- Roads cost less to draw.
- Mirrors that are off screen no longer render.
- Road chunks at the map edge are no longer rebuilt every frame.
- Roadside buildings sit on the ground instead of the road centre line.
- Changing view distance no longer removes the ground under you.
- Fixed terrain corruption when several pieces generate at once.
- Less stutter from item sync, spawning and prop cleanup in large worlds.
- Lower memory use for road and terrain generation.
- Content checks start earlier to reduce first-join pauses.
- Terrain and prop checks do less repeated work.
- Large prop requests are split up to reduce frame spikes.
- The starting-area bus stop is placed on the ground once terrain loads.
- Fewer stutters when items load in.
- Cheaper physics checks.
- Removed warning spam from flying animals.
- Rabbits spawn in single-player again (toggle in Settings > Misc).
- Sandstorms occur again in single-player (toggle in Settings > Misc).
- Removed warning spam from frozen far-away creatures.
- Large construction saves no longer slow down as they grow.
- Safer saving with a `.bak` backup.
- Fixed a world-stopping error from duplicate physics bodies on load.
- Cars no longer vanish when one of their parts is far away.
- Items no longer fall through unloaded ground.
- New graphics preset: 2x MSAA, four pixel lights, 80 m shadows with two cascades, realtime reflection probes off.
- Distant terrain loads without stuttering.
- Less CPU spent on rocks, trees and other props when nothing near you changes.
- Parked cars use less CPU.
- Idle tank caps use less CPU.
- Realtime reflection probes stay off after loading a save.
- Fixed an error flood when a worn item is destroyed.
- Car radiators explode properly again.
