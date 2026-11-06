# Multiplayer Message Inventory

Source: `.local-analysis/all/Assembly-CSharp.decompiled.cs`, `public enum msgType : byte`. Values below are the implicit enum ordinals (starting at 0); the underlying wire type is `byte`.

| Value | Name |
|---:|---|
| 0 | `none` |
| 1 | `ping` |
| 2 | `pingBack` |
| 3 | `chatMsg` |
| 4 | `voiceChat` |
| 5 | `password` |
| 6 | `kick` |
| 7 | `passwordPlayerList` |
| 8 | `mapStuff` |
| 9 | `askStartStuff` |
| 10 | `playerPos` |
| 11 | `playerAnim` |
| 12 | `playerOutfit` |
| 13 | `askPlayerOutfit` |
| 14 | `time` |
| 15 | `radioChannel` |
| 16 | `itemPosGlobal` |
| 17 | `itemPosLocal` |
| 18 | `itemPosPlayer` |
| 19 | `velocity` |
| 20 | `carVelocity` |
| 21 | `askItemData` |
| 22 | `askPoiData` |
| 23 | `partcondition` |
| 24 | `licenseplate` |
| 25 | `bus` |
| 26 | `rndType` |
| 27 | `food` |
| 28 | `ammo` |
| 29 | `push` |
| 30 | `weapon` |
| 31 | `weaponShot` |
| 32 | `color` |
| 33 | `breakable` |
| 34 | `dmgPlayer` |
| 35 | `soundplay` |
| 36 | `removeItem` |
| 37 | `carValues` |
| 38 | `carHandbrake` |
| 39 | `carPosGlobal` |
| 40 | `usable` |
| 41 | `fluid` |
| 42 | `attachable` |
| 43 | `ragdoll` |
| 44 | `aiSound` |
| 45 | `aiState` |
| 46 | `askItemPos` |
| 47 | `stepSound` |
| 48 | `outfit` |

The table above records the native enum only. The dispatch mapping below is partial; payload details and behavior still need further tracing.

## Dispatch and sync mapping

`networkingScript.ProcessMessage(CSteamID, byte[], msgType)` is the native receive dispatcher. It rejects the local sender, applies host forwarding/client acceptance gates, then switches on `msgType`. For the gameplay sync messages below, each listed method is the exact `syncScript.s` receiver called by that case. The corresponding `Send…` method names are present on `syncScript` for these pairs unless marked as request/response. `tosaveitemscript.MultiUpd()` drives periodic item synchronization.

| Message(s) | Receiver in `syncScript` | Evidence-based category |
|---|---|---|
| `playerPos` | `RecPlayerPos` | Remote player position, orientation, duck value, and optional seat attachment; `SendPlayerPos` emits it. |
| `playerAnim` | `RecPlayerAnim` | Remote player pose, zoom, head rotation, and menu state; `SendPlayerAnim` emits it. |
| `playerOutfit` | `RecPlayerOutfit` | Remote player outfit data; `SendPlayerOutfit` emits it. |
| `askPlayerOutfit` | `RecPlayerAskOutfit` | Request triggers local outfit `SendForMulti()`; `SendPlayerAskOutfit` emits request. |
| `mapStuff` | `RecMapStuff` | Map setup/state; `SendMapStuff` transmits map type/IDs/name, seed, global position, and image-map parameters when applicable. |
| `time` | `RecTime` | Time-of-day delta; `SendTime` emits a float. |
| `radioChannel` | `RecRadioChannel` | FM/AM clip and timing arrays; `SendRadioChannel` emits these arrays. |
| `itemPosGlobal`, `itemPosLocal`, `itemPosPlayer` | `RecItemPosGlobal`, `RecItemPosLocal`, `RecItemPosPlayer` | Item transform synchronization in global, parent-local, or player-held coordinates; matching `SendItemPos…` methods. |
| `carPosGlobal` | `RecCarPosGlobal` | Car global transform; `SendCarPosGlobal` emits it. |
| `velocity`, `carVelocity` | `RecItemVelocity`, `RecCarVelocity` | Item or car linear/angular velocity; matching `SendItemVelocity` / `SendCarVelocity`. |
| `askItemData`, `askPoiData`, `askItemPos`, `askStartStuff` | `RecAskItemData`, `RecAskPoiData`, `RecAskItemPos`, `RecAskStartStuff` | Requests for saved-item data, POI data, item position, or initial synchronization. Handlers use sender-targeted replies for several paths; request senders include `SendAskItemData`, `SendAskPoiData`, `SendAskItemPos`, and `SendAskStartStuff`. |
| `partcondition`, `rndType`, `food`, `ammo`, `weapon`, `weaponShot`, `color`, `breakable` | `RecPartCondition`, `RecRndType`, `RecFood`, `RecAmmo`, `RecWeapon`, `RecWeaponShot`, `RecSprayColor`, `RecBreakable` | Item component/state updates (part condition, random variant, food, ammunition, weapon state/shot, spray color, breakable health); same-named `Send…` methods, with `color` named `SendSprayColor`. |
| `push`, `dmgPlayer` | `RecPush`, `RecDmgPlayer` | Push impulse and player damage; matching `SendPush` / `SendDmgPlayer`. |
| `soundplay`, `stepSound` | `RecSound`, `RecStepSound` | Positional sound event and footstep event; matching `SendSound` / `SendStepSound`. |
| `licenseplate`, `bus`, `carValues`, `carHandbrake` | `RecLicensePlate`, `RecBusDoors`, `RecCarValues`, `RecCarHandbrake` | License plate, bus door, car control/engine values, and handbrake state; matching `Send…` methods. |
| `removeItem` | `RecRemoveItem` | Item removal; `SendRemoveItem` emits it. |
| `usable`, `fluid`, `attachable`, `ragdoll`, `aiSound`, `aiState`, `outfit` | `RecUsable`, `RecTank`, `RecAttachable`, `RecRagdoll`, `RecAISound`, `RecAIState`, `RecOutfit` | Usable-object state, tank contents, attachments, ragdoll pose, AI sound/state, and item outfit; matching send methods (`fluid` maps to `SendTank`). |

`ProcessMessage` routes the non-sync protocol messages to `SteamP2PNetworkingUnderTheHoodScript` (`voiceChat`→`RRVoice`, `ping`→`RRPing`, `pingBack`→`RRPingBack`, `chatMsg`→`RRChat`, `kick`→`KickHandle`, `password`→`PasswordHandle`, `passwordPlayerList`→`GotPlayerListWithCorrectPassword`). These are direct dispatcher facts; detailed payload semantics beyond the category descriptions above have not been exhaustively traced.

`none` has no `ProcessMessage` case and falls through to the unknown-message log. The dispatch confirms `velocity` is specifically the item-velocity receiver. `mapStuff`, initial-sync requests, and several item/POI replies involve more behavior than the short category labels capture; payload schemas and every call-site condition remain to be mapped.

