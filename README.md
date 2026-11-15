<img width="3840" height="1868" alt="thelongdrive-roadworthy" src="https://github.com/user-attachments/assets/4fe0204a-8c2b-46d4-88a0-f50422778538" />

A multiplayer and performance patch for **The Long Drive**.

**v0.3.0 experimental** | Windows | Game build `2024.11.26b_test`

## What does this patch do?

- Smoother multiplayer movement and better vehicle and item syncing.
- Faster world streaming and fewer performance spikes.
- Graphics presets and shadow controls.
- Join friends over Steam even if your mods or maps differ.
- New survival touches: beer, drunk effects and visible pee.
- Lots of fixes to terrain, saves, multiplayer and gameplay.

<details>
<summary>Full list of changes from the base game</summary>

### Settings and menus

#### Roadworthy settings

- A Roadworthy tab in the settings menu holds all Roadworthy options in one place. Its Reset button puts every option back to its default.
- Some options have clearer names: "Faster multiplayer" (was High multiplayer update rate) and "New Buggy multiplayer" (was Experimental multiplayer).
- Custom settings stay available after loading a world or returning to the main menu.

#### Menus

- Menus fit the screen at any resolution and aspect ratio, and backgrounds fill ultrawide screens.
- Consistent spacing and alignment across all menus; nothing overlaps or touches the screen edge.
- Settings tabs share one panel size.
- Save/Load buttons and confirmations line up in fixed spots.
- Steering wheel setup no longer shows the settings tab behind it.
- The multiplayer menu matches the rest of the game: plank buttons, a paper panel, and native checkboxes, sliders and text boxes.
- The multiplayer window, lobby list and dialogs stay inside the window.
- Roadworthy lobbies show their patch version in the lobby browser.

### Multiplayer

#### Joining

- Guests can join even if their mods, extra game files or custom maps differ from the host's. Only the game version and the Roadworthy version must match.
- Different local physics settings no longer block joining.
- Joining is faster, and the car and cargo stay above the ground while the world loads.
- Steam joins start the connection sooner and the host accepts it right away, so guests are less likely to sit in the lobby without the world loading.
- Guests keep retrying the Steam connection for up to a minute, and retry once if the first connection drops.
- Hosts no longer turn away guests who joined the lobby a moment ago.
- If a join fails, a pop-up says why, such as which version each player has, and you go back to the server list instead of being stuck at the main menu.
- A joining player waits until the ground under them has loaded, then is placed on a clear spot instead of inside a car, prop or wall.
- Abandoned cars and other world items show all their parts for joining players.
- Sending the world to a joining player no longer holds up everyone else's movement.
- When a player leaves, the remaining players no longer freeze.

#### Player and car movement

- Other players' movement updates 30 times a second instead of about 3, and they no longer skip ahead when they walk.
- Passengers receive car position updates up to 60 times a second instead of about 5.
- The car you are riding in no longer stutters or freezes on your screen.
- Smoother remote cars, faster updates after impacts, and better handling of packet loss.
- Late or outdated updates are ignored, so cars and players no longer jump back to old positions or snap back and forth after a hitch.
- Whoever drives owns the car: a short network hitch or a nearby passenger no longer takes it over.
- A car being pushed by another player moves smoothly instead of jumping once a second.
- Passengers no longer drift out of seats or flash to the wrong terrain on long drives.
- Wheels and attached parts no longer drift away from moving cars.
- Trailers can be hitched to cars again, follow whoever is driving, and move smoothly for everyone.
- Both sections of a double bus move together more smoothly, the rear section is less likely to snap out of place, and its wheels stay mounted more reliably.
- Position updates reach other players sooner over Steam.
- Faster multiplayer and car smoothing can each be turned off in Settings > Roadworthy.

#### What other players see and hear

- Doors, windows, mirrors and seats move smoothly for other players.
- When a car crashes and gets bloody, everyone sees the blood.
- Other players can see your cigarette smoke.
- Other players can see and hear you peeing, hear you eating and see the crumbs, and hear the key click when you turn a car's ignition on or off.
- Unpainted car parts no longer show pure black for other players.

#### Items, parts and cargo

- Cars, parts and large objects spawned by guests appear for everyone more reliably, without duplicates.
- New guest items and saved items are protected from early deletion.
- Part and color updates stay in order while missing items load, and moved parts update their mounts correctly.
- Missing items are repaired for other players more reliably.
- Items held by other players no longer disappear, and delayed updates no longer pull items out of your hands or off mounts.
- Quitting no longer deletes held or worn items.
- Loose items in a moving car (glove box, cab, bed) stay in place for passengers and no longer fall out behind the car.
- Box pieces and loose items no longer get stuck in a guest's car or teleport.

### Gameplay

#### Survival, food and drink

- You get thirsty faster, and sprinting uses more food and water.
- Beer is a new drink. It quenches thirst a little less than water and makes you a little drunk. The starting house has beer in glass bottles and a barrel, and bottles found elsewhere can contain it too.
- Drinking alcohol or beer makes your view sway and zoom in and out, stronger the more you drink. Steering is not affected. Turn it off in Settings > Roadworthy > Drunk effects.
- Drinking milk sobers you up faster.
- Drinking fuel or oil can temporarily make you thirstier.
- Your water bar pulses yellow while you are sick.
- You can collect, mix and drink piss as its own yellow liquid. It hydrates less than water and slightly damages your health.
- Peeing no longer adds alcohol to containers.
- Press `;` to switch which way you pour fluids.

#### Driving and vehicles

- In first person, your view no longer goes into a car's roof, pillars or door frame when you lean or the car bounces.
- Cars can now damage enemy NPCs.
- Car radiators explode properly again.
- A wire brush can repair a radiator's condition, the same way it fixes doors. Turn it on in Settings > Roadworthy > Repairable radiators (off by default).
- Wheels with oversized tyres can now be picked up through the tyre.
- Cars no longer vanish when one of their parts is far away.

#### Wildlife and weather

- Rabbits spawn in single-player again, and in multiplayer around the host and guests, including Steam sessions (toggle in Settings > Roadworthy).
- Hard car hits can kill and ragdoll rabbits.
- Fixed rabbits losing forward momentum while moving.
- Sandstorms occur again in single-player (toggle in Settings > Roadworthy).

### World and terrain

#### New worlds

- New worlds stay in your selected biome; existing saves keep their original biome setting.
- Grass thins out more gradually between biomes.
- Less sand covers the roads.
- Large mountains are less likely to overlap roads; ones that would block a road move nearby instead of disappearing, so more mountains stay beside roads.
- Random buildings check for steep ground before spawning, roadside buildings try nearby flatter ground, and the ground around buildings is smoother.
- Roadside buildings sit on the ground instead of the road centre line.
- Bus stops are placed beside the road instead of on it, and the starting-area bus stop sits on the ground once terrain loads.
- If the starting house doesn't appear in a new world, the game tries placing it again before the loading screen closes.

#### Loading fixes

- While the loading screen is up, you're held in place until there's ground under you, so you shouldn't fall through the map.
- Items no longer fall through unloaded ground.
- Changing view distance no longer removes the ground under you.
- Fixed terrain corruption when several pieces generate at once.
- Buildings in new areas may appear a moment later instead of freezing the game.

### Performance

#### Roads and terrain

- Roads draw as static meshes, cost less to draw, and update faster while you drive or when two players are nearby.
- In single-player, roads are no longer rechecked every frame when nothing near you has changed.
- Road chunks at the map edge are no longer rebuilt every frame.
- Distant terrain loads without stuttering, and road and terrain generation use less memory.
- Terrain and prop checks no longer redo work that has not changed.
- Grass costs less to place each frame.

#### World streaming and items

- World streaming stutters less, and items loading in cause fewer visible stutters.
- Large worlds stutter less when items sync, props spawn and leftover objects are cleaned up.
- Large prop requests are split up to reduce frame spikes, and guests generate distant props with less stutter.
- Spawning items, choosing random items and spawning starting items take less work and memory.
- Objects finish spawning before saving or sharing the world.
- Large construction saves no longer slow down as they grow.

#### Physics and objects

- Less CPU spent on rocks, trees and other props when nothing near you changes.
- Parked cars, vehicle wheels, idle tank caps and stored items use less CPU.
- Cheaper physics checks, and item weight checks use less memory.
- Loose debris and breaking objects take less processing; debris pieces pass through each other and shattered pieces use simpler collisions.
- Heavy collisions play fewer overlapping impact sounds.

#### Rendering

- Mirrors that are off screen no longer render, and mirror visibility is checked once per camera per frame.
- Binoculars no longer draw their lens picture while out of sight.
- Cobwebs more than about 20 m away no longer cast shadows.

#### Networking

- Received item and car updates cost less CPU.
- A flood of network updates no longer freezes the game.

### Graphics

- Graphics preset: 2x MSAA with realtime reflection probes off.
- The Performance preset also lowers shadow distance, cascades and pixel lights; every other preset leaves the game's own shadow settings alone.
- Realtime reflection probes stay off after loading a save.
- Shadow quality and Performance preset are in Settings > Roadworthy.

### Saving and stability

- Safer saving with a `.bak` backup.
- Fixed a world-stopping error from duplicate physics bodies on load.
- Fixed an error flood when a worn item is destroyed.
- Removed warning spam from flying animals and frozen far-away creatures.

</details>

## Install

1. Windows 10/11 x64.
2. Close the game.
3. Run `TheLongDrive-Roadworthy.exe` from the release and click the button.
4. The host and every joining player must run the same Roadworthy version.

The installer builds the patch against your own game files and checks it before swapping it in. It does not modify your saves.

## Restore

Run the EXE again and click *Restore original*, or run `Restore Original.cmd`.

## New Buggy multiplayer (experimental)

An optional second multiplayer mode that gives each car exactly one player in control of its movement. The normal multiplayer mode is the default and is unchanged.

Turn it on in **Settings → Roadworthy → New Buggy multiplayer** before creating or joining a session. Every player must choose the same mode; players on different modes can't join each other.

## Reporting problems

Press **F11** in game to save a feedback bundle of your logs, and attach it to your report.

For stutter reports, you can turn on detailed performance logs: create an empty file named `tldmp-diagnostics.on` beside `TheLongDrive.exe` and restart. Reproduce the problem for 30-60 seconds, then press F11. Delete the file and restart to turn the logs off again, since they cost some performance.
