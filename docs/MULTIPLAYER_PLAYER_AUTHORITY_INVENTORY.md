# Multiplayer Player, Seat and Held-Item Authority Inventory (Phase 5)

Status: static research inventory, read-only. Pinned to this repository's `runtime/*.cs`,
`tools/PatchStability.cs` and the local decompile
`.local-analysis/all/Assembly-CSharp.decompiled.cs` (referred to below as **DC**).
No runtime, two-peer, or parity evidence is claimed. Everything that is not a direct
reading of the named source is either labelled **Proposal** or listed in §9 (open
questions). Pin format is `file:line` (DC line numbers are the decompile's line
numbers, which are also the function anchors).

Scope requested: player body representation, seat model, held/worn item ownership, and
the authority rules + gate sites + acceptance tests that follow from them.

---

## 0. Pin legend and what this document does not claim

| Short name | File |
|---|---|
| **DC** | `.local-analysis/all/Assembly-CSharp.decompiled.cs` |
| **RP** | `runtime/RabbitPlayers.cs` |
| **RGA** | `runtime/RabbitGuestAnimation.cs` |
| **MVP** | `runtime/MovingVehiclePassenger.cs` |
| **MVPP** | `runtime/MovingVehiclePassengerPolicy.cs` |
| **BSC** | `runtime/BusSeatClaims.cs` |
| **PLS** | `runtime/PlayerLeaveSafety.cs` |
| **RPB** | `runtime/RemotePoseBuffer.cs` |
| **RS** | `runtime/RemoteSmoothing.cs` |
| **SP** | `runtime/StablePackets.cs` |
| **SGB** | `runtime/StableGameBridge.cs` |
| **NSA** | `runtime/NativeSafetyAdapter.cs` |
| **NSS** | `runtime/NativeSeatSafety.cs` |
| **PS** | `tools/PatchStability.cs` |
| **PS seq** | `runtime/PoseSequence.cs` |
| **CC** | `runtime/CarCargo.cs` |

Not claimed: that any of the described gates exist today; that seat contention is
currently resolved; that any behaviour was verified in game. Where this document
contradicts or sharpens `docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md` (Phase 4) or
`docs/MULTIPLAYER_MESSAGE_INVENTORY.md`, the difference is called out in §1.5 and §8.3.

---

## 1. Player representation

### 1.1 The three objects behind one peer

| Layer | Type | Owner in the codebase | Notes |
|---|---|---|---|
| Session record | `syncScript.playerSyncClass` (DC:3473–3686) | one per `CSteamID` in `syncScript.players` (DC:3741) | exists **only** on peers that received a `playerPos` from that ID; created on first message (DC:3983–3987) |
| Remote proxy body | `GameObject` = `syncScript.playerPrefab` (DC:3739) instantiated at DC:3554, carrying `mpplayerscript` + `playeranimationscript` + `playermodeloutfitscript` + `IKscript[]` | created lazily inside `playerSyncClass.GotUpd` (DC:3552–3578) | never exists for the local player |
| Local body | `fpscontroller` (DC:18612) on `mainscript.s.player` | one, always local | the only body with physics, survival, inventory and a Rigidbody in the normal case |

`mpplayerscript` (DC:44460–44654) fields that matter here: `steamID` (DC:44462),
`outfit` (44468), `anim` (44470), `nameText` (44472), `handParent` (44480),
`mass` (44482), `zooming` (44484), `cols` (44486), `HeadCollider` (44488),
`CBody` (44490), `THead` (44492), `Th` (44494), `TProneHeadPos` (44496).
`mpplayerscript.readyToMove` (DC:44466) is declared and **never read or written anywhere
in the assembly** (only occurrence in DC). RP does not read it either; `tests/RabbitPlayersTests.cs:325-329`
asserts it is unused. Do not use it as a readiness signal.

`mpplayerscript.UpdCollider(duck,sitting)` (DC:44549–44588) moves `CBody`/`HeadCollider`
to track `THead`/`TProneHeadPos`, sets `cols[i].isTrigger = sitting || original`
(DC:44605–44619) and switches every col's layer to **10 while sitting, 0 while standing**
(DC:44576–44586). RP relies on this to explain why standing avatars (layer 0) are absent
from the native rabbit attack layer 27 (RP:257–259).

### 1.2 Authoritative fields, and what carries them

Native replication of a player is **pose + seat attachment + anim skeleton + outfit +
damage + footstep + item-in-hand poses**. There is no player-state channel beyond that.

| State | Authoritative field(s) | Writer | Carrier |
|---|---|---|---|
| World position | `playerSyncClass.current.pos` / `next.pos` / `last.pos` (`posStruct.pos`, DC:3480) | sender: DC:3581; receiver apply: DC:3543 | **msgType 10** `playerPos` |
| World rotation | `posStruct.rot` (DC:3482) | DC:3582 / DC:3544 | **10** |
| Velocity | **none.** No velocity field exists on `playerSyncClass`. | — | none (remote avatars are transform-lerped, DC:3543–3544) |
| Duck / crouch | `_duckValue` argument → `mpscript.UpdCollider` (DC:3641) | 10 | **10** |
| Sitting + seat identity | `playerSyncClass.sitting` (DC:3493), `sitObjParent` (3499); seat marker `seatscript.inUseMulti`/`inUseMultiTime` (DC:65686/65688) set at DC:3595–3596 (POI) and DC:3615–3616 (saved item) | 10 | **10** |
| Animation skeleton | `mpplayerscript.anim` via `playeranimationscript.setPose(...)`, `SetThRotX` (DC:4037–4038) | 11 | **11** `playerAnim` |
| Zoom / open menus | `mpplayerscript.zooming` (DC:44484), `mainscript.SetMenus` (DC:4039) | 11 | **11** |
| Body outfit (skin/clothes selection) | `playermodeloutfitscript` on the proxy; `RecPlayerOutfit` → `SetData`+`Refresh` (DC:4080–4087) | 12 | **12** `playerOutfit` |
| Item outfit (per-item `outfit` component) | `tosaveitemscript.outfit` (DC:5403–5411) | 48 | **48** `outfit` |
| Held / worn / stored item pose | `tosaveitemscript` MP hold flags + `SmoothMove` target (DC:4210–4248) | 18 | **18** `itemPosPlayer` |
| Health / survival (hp, food, water, wounds) | `survivalscript.hp/food/water/wounds` (DC:69436–69522) | **local only** | only damage *requests* travel: **34** `dmgPlayer` |
| Death / respawn | `fpscontroller.died` (DC:19561), `mainscript.died` (DC:36865, set at DC:38240) | **local only** | **nothing.** No message carries `died` |
| Inventory contents | `fpscontroller.inventory` (DC:19049) | local; mirrored to peers **only** as `mpstored` + an item pose | **18** (hold type `inventory1..4`) |
| Right-hand item | `fpscontroller.inHandP` (DC:19525) / `pickupable.inRightHand` (DC:54752) | local; mirrored as `mpequipped` | **18** |
| Worn item | `pickupable.wear.wearing` + `fpscontroller.weared.Contains` (DC:54890–54901) | local; mirrored as `mpweared` | **18** |
| Footstep | n/a — a one-shot sound at a position | 47 | **47** `stepSound` |
| Push impulse on an item | n/a | 29 | **29** `push` |

Consequence for the Phase 5 design: **a remote peer's health, inventory contents,
death and respawn are not observable at all**, and a remote peer's "velocity" does not
exist as data. Any authority claim about "player health is replicated" would be false.

### 1.3 `msgType 10 playerPos` — exact shape and the identity problem

Payload spec in SP:68: `"bbtgvf"` = `bool sitting, bool sitAtPoi, u64 steamID, Vector3d pos, Vector3 rot, float duck`,
plus `"igi"` (poiGenID:int, poiID:Vector3d, seatID:int) when `sitAtPoi`, else `"ii"`
(itemID:uint, seatID:int) when `sitting`. Emitted by `syncScript.SendPlayerPos` (DC:3915–3955)
in three shapes (poi seat DC:3944, saved-item seat DC:3948, free DC:3953). Sent from
`syncScript.Update` on a `playerPosSyncTime` timer (DC:3854–3863); **no `died`, `sitting`
or readiness gate**. Roadworthy can shorten that interval (`runtime/CarPosePolicy.cs:13-24`,
`PlayerRatePolicy`, applied in `SGB.TickPlayerRate` SGB:2015–2022).

When seated, the transmitted pose is **seat-local**:
`seat.parentObj.InverseTransformPoint(player.transform.position)` and
`BodyRot.eulerAngles - seat.parentObj.eulerAngles` (DC:3922–3924). `parentObj` is
`tosave.transform` for a saved item seat (DC:65701) or the POI object for a POI seat (DC:65710).

`RecPlayerPos` (DC:3957–3988) keys everything off the **payload** `steamID` (`_obj7`,
DC:3983–3987). It never compares it to the transport sender. `ProcessMessage` passes
`_SenderID` in but the `playerPos` case only forwards `_bytes` (DC:90983–90985).
**So the payload identity is unauthenticated.** Same for 11 (DC:4034), 12 (DC:4080),
18 (DC:4196/4232), 34 (DC:4699 applies only to `_obj2 == SteamUser.GetSteamID()`),
48 (DC:5406).

### 1.4 `fpscontroller` fields this phase cares about

`survival` (DC:18721), `pickedUp` (19045), `inventory` (`List<pickupable>`, 19049),
`Bsitting` (19255), `seat` (19505), `inHandP` (19525), `lastinHandP` (19527),
`lookedSeat` (19529), `loadedmove` (19557), `died` (19561), `weared` (`pickupable[]`, 18725;
consumed at DC:20877–20883 and DC:4227), `C`/`CBody`/`RB` (used throughout RP/MVP),
`RHSitPose` (18851), `FgetOutDist` (19507).

`GetIn` (DC:24559–24624) and `GetOut` (DC:22773–22816) are described in §2.
`Death()`/`Death2()` (DC:20867–20910) take off all worn items, spawn a ragdoll, set
`died = true`, and call `mainscript.Died()` (DC:38234–38243) — none of which produces a
network message. `Respawn()` (DC:20956–20992) does `Revive()`, `DropAll()`, `Teleport(...)`,
forced `GetOut(...)`; again no message — the next `playerPos` sample carries the new pose.

### 1.5 `playerdeathtriggerscript` — local-only damage volume, and the 34 path

`playerdeathtriggerscript` (DC:7059–7092): `selfCol` (7061), `damageBreakable` (7063),
`damagePlayer` (7065), `bscript` (7067), `pscript` (7069). `OnTriggerStay` (DC:7079–7091)
finds a `survivalscript` on the touched collider and calls
`pscript.DamageInstant(damagePlayer * Time.deltaTime, ...)`. Because a remote proxy
(the `playerPrefab`) carries no `survivalscript`, this trigger damages **only the local
player**. RP exploits exactly that: RP:398 bails out when
`contact.GetComponentInParent<survivalscript>() != null`, then resolves the proxy by
`contact.GetComponentInParent<mpplayerscript>()` (RP:399) and forwards damage as
`syncScript.s.SendDmgPlayer(actor.Mp, amount, direction)` (RP:424) — i.e. **msgType 34**.

`SendDmgPlayer` (DC:4689–4695) resolves the target through
`GetSteamIDByPlayer(mpplayerscript)` (DC:3999–4011) and sends
`(senderSteamID, targetSteamID, dmg, dir)`. `RecDmgPlayer` (DC:4697–4704) discards the
first field and applies `mainscript.s.player.survival.Damage(dmg, dir, show:true)` only
when the second field equals the receiver's own SteamID.

Two facts that follow, and that Phase 4's item inventory did not state:

1. **34 can only ever damage the receiver.** That is a useful built-in containment.
2. **34 carries no rate, no source entity and no victim-side validation.** Any peer may
   send any amount at any time. RP rate-limits itself per fixed step / per rabbit /
   per actor-session (RP:404–413) but that is sender-side politeness, not a gate.
3. `survivalscript.Damage` only decrements the accumulator `toHp` (DC:69757–69765);
   `DamageInstant` is what clamps `hp` and triggers `player.Death()` (DC:69767–69784).
   So a 34 can never kill instantly through `Damage` — death requires the local
   simulation to drain the accumulator.

---

## 2. Seat model

### 2.1 `carscript` seats and `seatscript`

Seats are plain scene objects referenced from `tosaveitemscript.seats` (DC:73729) —
`seatscript[]`, indexed by `seatscript.seatID` (DC:65600). Each seat carries an owner
route and a parent transform:

| `seatscript` field | DC | Meaning |
|---|---|---|
| `tosaveid` / `tosave` | 65598 / 65612 | saved-item owner of the seat |
| `setidpoi`, `poigenid`, `poiid` | 65604 / 65606 / 65608 | POI owner instead of a saved item |
| `seatID` | 65600 | index into `tosave.seats[]` |
| `parentObj` | 65610 | transform the seat hangs from (`tosave.transform` for saved seats, DC:65701) |
| `sitPos` | 65652 | transform a **local** occupant is parented to |
| `Car` | 65690 | `carscript` back-reference |
| `driverSeat0` / `driverSeat1` | 65646 / 65648 | driving / second-driver authority flags |
| `mainseat` | 65640 | canonical seat indirection (DC:23181–23184) |
| `inUse` | 65684 | **local** occupancy |
| `inUseMulti` / `inUseMultiTime` | 65686 / 65688 | **remote** occupancy marker + timestamp |
| `BcantSitAngle` / `FcantSitAngle`, `getUp*`, `getUpLimit` | 65664–65694 | geometry/exit gating |
| `ForwardSeat`…`LeftSeat` | 65676–65682 | adjacency |

`SetID(uint,int,tosaveitemscript)` (DC:65696) and `SetID(int,Vector3d,int,Transform)`
(DC:65705) are the only writers of the owner route and of `parentObj`.

### 2.2 Who may claim a seat today

Claiming is **purely local and unarbitrated**:

1. The interaction raycast resolves `lookedSeat` (DC:23178), substitutes `mainseat`
   (DC:23181–23184), then requires `lookedSeat.FreeCanSit()` and a use-press
   (DC:23185–23194) before calling `fpscontroller.GetIn(lookedSeat)`.
2. `FreeCanSit()` (DC:65715–65726) returns `!inUse` **and**, when `syncScript.IsConnected()`,
   also `!inUseMulti`.
3. `inUseMulti` is set **only by receiving peers**, from a `playerPos` seat claim
   (DC:3595–3596, DC:3615–3616), and `seatscript.Update` clears it one second later
   (DC:65728–65734).

So the only "reservation" is a **1-second, receive-side, advisory marker refreshed by the
claimant's own pose stream**. There is no host decision, no epoch, no ack. From the source
alone, two peers pressing use inside the same second both pass `FreeCanSit()` (each sees the
other's marker only after the next `playerPos` arrives, DC:3856) and both enter; neither is
evicted afterwards, because nothing checks occupancy after `GetIn`. That is an inference
from the read code, not a measured outcome — see §9 Q2.

`fpscontroller.GetIn(seatscript)` (DC:24559–24624), in order: clears `standLean` /
`StopPushGripping`, `sat = true`, calls `seat.GetOut()` on the *previous* seat
(DC:24566), sets `seat = _u`, applies a downward force at `BodyRot.position`
(DC:24571–24574), `Bsitting = true`, `Bduck = false`, `_u.GetIn()` (DC:24577), assigns
`Car`/`currentCar` (DC:24578–24582), moves all colliders to layer **10** (DC:24589–24592),
sets `transform.position = _u.sitPos.position` and `BodyRot.rotation = _u.sitPos.rotation`
(DC:24594–24596), **parents the root to `_u.sitPos.transform`** (DC:24603),
`seat.UpdMass()` (DC:24616) and finally **`Destroy(RB); RB = null`** (DC:24622–24623).

`seatscript.GetIn()` (DC:65741–65757) sets `inUse = true` and, for `driverSeat0`,
`Car.driving = true; Car.isPlayerDriving = true` (for `driverSeat1` the `…2` pair).

`fpscontroller.GetOut(pos, forced)` (DC:22773–22816) requires `input.useDown | forced`;
then `Car = null`, `Bsitting = false`, `seat.GetOut()`, colliders to layer **12**
(DC:22802–22806), `AddRB()` and a small velocity kick from the car's velocity
(DC:22796–22809), `transform.parent = null` (DC:22801), `seat.UpdMass()`, `seat = null`.
`seatscript.GetOut()` (DC:65759–65776) clears `inUse`, the driving flags, and calls
`Car.AutoShiftNeutralOnGetOut()`.

Other forced exits: `seatscript.OnDestroy` (DC:65787–65795) and the breakable/car destroy
path (DC:10390–10394) both call `GetOut(transform.position, forced: true)`;
`Respawn` does the same (DC:20988–20991).

**Deliberate asymmetry:** the local occupant is parented to `seat.sitPos`; a remote
occupant is parented to `seat.parentObj` and positioned with the seat-local offset
(`ParentPlayer(seat.parentObj)` DC:3612/3592, then `UpdSit(sitObjParent, _pos, _rot)`
DC:3649–3653). Both are children of the car's item tree, so both ride the car
hierarchy — but they are *not* the same transform and must not be conflated in a
repair/teleport path.

### 2.3 How a seated remote player is positioned relative to a moving car

`playerSyncClass.Upd()` (DC:3514–3547) is the per-frame driver:

* standing: lerp `player.transform.position/rotation` toward `lerped` (DC:3543–3544),
  where `lerped = Lerp(last, next, InverseLerp(last.time, next.time, now))` (DC:3531–3533)
  and `next` is a predictive extrapolation (DC:3636–3638);
* sitting: **no lerp** — `UpdSit(sitObjParent, current.pos, current.rot.eulerAngles)`
  (DC:3534–3539), i.e. a hard snap to the newest received seat-local pose.

Roadworthy reinforces this: `RemotePlayerSmoothing.Sample` throws away the buffer when the
peer is sitting (RS:19–23) and `Apply` returns early for `player.sitting` (RS:46), so a
seated remote avatar is never smoothed. `MovingVehiclePassenger` also refuses to run for a
seated local player (`player.Bsitting` guard, MVP:42).

### 2.4 `MovingVehiclePassenger` — local velocity-frame correction only

Header comment (MVP:6–8): *"Changes only the velocity frame used by the local player's
native Move. It does not move the player or transfer ownership of the vehicle."*

`FixedTick` (MVP:34–92) gates, in order: null/`loadedmove`/kinematic/`Bsitting`/`died`/
`ladder`/`griping`/`pushgripping`/`jumpDown` → `Reset()` (MVP:42–44); airborne sensor →
`Reset()` (MVP:47); `FindSupport` on the native ground sensor's sphere cast (MVP:94–123);
support must resolve to a `tosaveitemscript` with `car != null` and `RB != null`
(MVP:56); then **the authority gate**:

```csharp
if (item.ThisOneInChargeCar() || !BusPairReplica.Owns(item) || !BusPairReplica.OwnsBody(body))
{ noAuthority++; Reset(); return; }                                  // MVP:57
```

So a passenger only gets the car's point velocity when **this machine does not own the
car and the paired-bus replica does**. `BeginMove`/`EndMove` bracket the native
`fpscontroller.Move` and subtract/add the support velocity (MVP:125–159), with
`Velocity()` short-circuiting reads for the support and owner bodies only (MVP:145–149).
Sample sanity lives in `MovingVehiclePassengerPolicy` (MVPP:6–16): `dt ≤ 0.2 s`,
`angle ≤ 45°`, `localSquared ≤ 1600`, `|d|² ≤ 10000·dt²`, `|v|² ≤ 10000`. `CarryResidual`
(MVPP:20–33) is a helper not used by MVP today.

Bridge call sites: `SGB.PassengerMove` (SGB:1355–1359, invoked via the `fpscontroller.Move`
IL hook, PS:1429/1433) and `SGB.TickMovingPassenger` (SGB:1360–1363, `FixedUpdate`,
SGB:3544). `Reset()` is part of `StableGameBridge.Reset` (SGB:178).

### 2.5 `BusSeatClaims` — the only existing claim ledger

Header (BSC:6–12): claims are observed from `syncScript.playerSyncClass.GotUpd`, a claim
binds one remote player to the exact native seat it occupies, only `driverSeat0` is a
driving authority, and the claim carries the session verified at observe time so a
reconnect cannot reuse a stale claim.

* `Observe(self, sitting, sitAtPoi, itemID, seatID, session, now)` (BSC:18–24): **any**
  failure of `sitting && !sitAtPoi && itemID != 0 && seatID >= 0 && session != 0`
  **removes** the claim. Time is the local receive time (header, BSC:11–12).
* `Fresh = 2.0` s (BSC:17).
* `DriverOf(steamID, item, session, now)` (BSC:30–42) requires: live peer session match,
  `now - c.At <= Fresh`, the claim still maps to the same live `playerSyncClass`,
  `p.player != null && p.player.activeInHierarchy && p.sitting`, `c.ItemID == item.idInSave`,
  `c.SeatID` in range, `seat != null && seat.driverSeat0 && seat.Car == item.car`.
* `ClearPeer` (BSC:25), `Clear` (BSC:43).

Producer: `SGB.BusSeatClaim` (SGB:521–535) is an **entry** hook on `GotUpd`
(PS:1250, args `self, sitting, sitAtPoi, itemID, seatID`); it only fills `session` when the
dictionary entry is the canonical instance and the node peer is `Ready` with
`Session != 0`, otherwise it passes `session = 0` which **drops** the claim.

Consumers: `SGB.ValidateBusPairAuthorities` (SGB:403–423, re-validated every fixed step,
host only) and `SGB.ReceiveBusPair` (SGB:471–501 upstream branch).

### 2.6 `NativeSeatSafety` — drift repair, not authority

`NativeSeatSafety` (NSS:10–176) snapshots the root→`sitPos` local offset right after
`GetIn` and later reports narrow repairs.

* `Capture(...)` (NSS:45–61) refuses unless genuinely seated, all three instance IDs
  non-zero, `currentParentId == sitPosId`, no independent rigidbody, finite offset.
* `Evaluate(...)` (NSS:66–104) returns `None` and clears when not seated or the seat/sitPos
  is dead; otherwise `Parent|Offset` on parent drift, `Offset` on invalid or drifted offset
  (`DefaultOffsetTolerance = 0.025`, NSS:31), and `IndependentRigidbody` when a stray RB
  exists.
* Callers (NSA:220–252): `AfterNativeSeatEntry` (PS hook `fpscontroller.GetIn`, PS:1112)
  captures; `BeforeNativeSeatExit`/`AfterNativeSeatExit` (PS:1113–1114) classify and clear;
  `TickNativeSeatSafety` (LateUpdate, SGB:3545) applies `SetParent(seat.sitPos,true)`,
  restores `localPosition`, and destroys an independent RB. `seatCaptureFrame` guards the
  frame in which native `Destroy(RB)` is still pending (NSA:225, NSA:242).

Nothing here grants, denies or arbitrates a seat. It only keeps a live local occupancy
consistent.

### 2.7 Disconnect, timeout and seat release today

Three independent mechanisms, none of which talks to `seatscript`:

1. **Native timeout.** `playerSyncClass.Upd` (DC:3520–3523) removes the player when
   `now - lastTime > s.playerRemoveTime`; `RemovePlayer` (DC:3655–3662) does
   `DropStuff()` then `Destroy(player)`. `DropStuff` (DC:3664–3685) clears
   `mpequipped/mppickedup/mpstored/mpweared`, unparents, re-activates, restores
   per-player `IgnoreCollision(false)` and calls `s.UpdItemParent(0, item)` (DC:3683).
2. **Roadworthy teardown.** `SGB.RemovePlayer(peer)` (SGB:2080–2097), wired to
   `node.PeerLeft` (SGB:258), does the same drop/destroy and additionally removes the
   `syncScript.players` entry (SGB:2095). `SGB.PrunePlayers` (SGB:1694–1710) handles the
   half-dead state (proxy or script missing) at update boundaries via
   `PlayerLeaveSafety.ShouldPrune` (PLS:13–16), hooked at `NativeBehaviorSyncBegin/End`
   (SGB:2167–2168). The NRE hazard being avoided and the null-safe
   `IgnoreCollisionSafe`/`GetPlayerWithScript` substitution are documented in PLS:6–11 and
   PS:634–657.
3. **Transport-level revocation.** `SGB.RevokeBusPairPeer` (SGB:396–402) drops bus pair
   authorities and calls `BusSeatClaims.ClearPeer(peer)`; called from
   `OnSteamPeerDisconnected` (SGB:904–906) and the loopback disconnect path (SGB:1972).

The seat's `inUseMulti` is **not** explicitly cleared by any of these; it expires through
`seatscript.Update`'s one-second timeout (DC:65730–65733). `SGB.RemovePlayer` does **not**
call `RevokeBusPairPeer`, so a `BusSeatClaims` entry can outlive the session teardown; it
is inert because `DriverOf` re-checks the peer/session and
`ValidateBusPairAuthorities` revokes on peer loss (SGB:418–420). `SGB.ResetBusPair`
(SGB:379–385) calls `BusSeatClaims.Clear()`.

---

## 3. Held and worn items

### 3.1 Two flag families, both on `tosaveitemscript`

Local, simulation-side (on `pickupable`, DC:54720–54772):
`pickedUp` (DC:54750), `inRightHand` (54752), `wear` (`wearable`, 54732),
`physlock` (`mountStuff`, 54764). Derived predicates:
`PickedUp(bool)` (DC:54868–54879), `inInventory()` (DC:54881–54888),
`isweared()` (DC:54890–54901), and `wearable.wearing` (DC:79674).

Remote, receive-side, **on the item** (DC:73819–73825):
`mppickedup`, `mpequipped`, `mpstored`, `mpweared`.

Derived: `tosaveitemscript.GetHoldType()` (DC:74474–74510) maps local state to
`holdType` (DC:73618–73628 = `nothing, pickedup, equiped, weared, inventory1..4`) in this
priority: `inRightHand → equiped`, `isweared() → weared`, `PickedUp(false) → pickedup`,
`inInventory() → inventory1..4`, `P.pickedUp → pickedup`.
`OtherInChargeItem()` (DC:75159–75166) = *any* of `mppickedup|mpequipped|mpstored|mpweared`.
`ThisOneInCharge()` (DC:75115–75119) = `!OtherInChargeItem() && (IsHost() || ThisOneInChargeItem() || ThisOneInChargeCar())`.

So: **`mpstored` means "in a remote player's inventory", not physlock storage** (also
noted in the Phase 4 doc, §1). `GetHoldType` is the single source of truth for the wire
hold type, and it is computed from **local** fields; the `mp*` flags are only ever set by
`RecItemPosPlayer` and cleared by `UpdItemParent(0, …)` / `DropStuff`.

### 3.2 How 18 is sent

`tosaveitemscript.ForcePosUpd(sendPosIfHasParent, forceGlobal, sendEvenIfNotInteracting)`
(DC:74534–74625), for the non-car branch (DC:74538–74603):

* Gate: `transform.parent == null || sendPosIfHasParent || HasToSendSyncLocal() ||
  (P != null && (P.pickedUp || sendEvenIfNotInteracting || P.inRightHand || P.isweared() ||
  P.inInventory() != 0))` (DC:74540) **and** `WouldSendPlayerItemPos(...)` (DC:74521–74532).
* Parent frame is chosen from local state (DC:74557–74568):
  `isweared() → mainscript.s.player.anim.HHTarget`;
  `inRightHand → mainscript.s.player.Th`;
  else `mainscript.s.player.BodyRot`.
* `holdType = GetHoldType()` (DC:74556) and
  `syncScript.s.SendItemPosPlayer(idInSave, RB != null, sendParent.InverseTransformPoint(pos),
   (Quaternion.Inverse(sendParent.rotation) * Quaternion.Euler(euler)).eulerAngles, lht, v, av)`
  (DC:74580).
* Cars take the `carPosGlobal`/`carVelocity` branch instead (DC:74604–74622).

`SendItemPosPlayer` (DC:4179–4189) emits `msgType.itemPosPlayer` with
`(bool hasVelocity, u64 steamID, uint itemID, Vector3 localPos, Vector3 localRot, int holdType
[, Vector3 velocity, Vector3 angularVelocity])`. Spec in SP:75 (`"btivvi"` + `"vv"`).
`StablePackets.Item(18)` reads the item id at **byte offset 10** (SP:128).
Periodic driver: `tosaveitemscript.MultiUpd()` (DC:74443–74468) called from the native
`SyncStuff` coroutine (DC:3898), which Roadworthy replaces with `SGB.SyncItems`
(SGB:2098–2104, `SyncItemScheduler`).

**Transport weaknesses of 18, all confirmed in source:**

* `StablePackets.IsReliable` puts 18 in the unreliable set (SP:144).
* `PoseSequence.Stamped` does **not** include 18 (PS seq:22–29 — only 10,16,17,19,20,39 and
  the live-usable type). So 18 has **no sequence trailer** and no
  `PoseSequenceFilter` ordering (`AcceptSequenced`, SGB:787–795; `poseFilter`).
* `StablePackets.LatestValueKey` does not include 18 (SP:155–163 — only 16,19,20,39 and the
  two extension types), so send-queue coalescing also skips it.
* `SGB.ObserveNative` **does** cache the newest 18 per item id for the late-join snapshot
  (SGB:1126–1131, `lastItems`), and `CaptureWorld` adds locally owned `18`/`17` poses
  (SGB:1755–1759).

Result: a stale `itemPosPlayer` can be applied *after* a newer one for the same item. That
is the single weakest hop in held-item replication today.

### 3.3 How 18 is applied

`syncScript.RecItemPosPlayer` (DC:4191–4271), in order:

1. Decode `(hasVelocity, steamID, itemID, localPos, localRot, holdTypeInt[, v, av])`
   (DC:4193–4209).
2. If the item exists, overwrite the four remote flags from the hold type
   (DC:4212–4215): `mpstored = holdType ∈ {inventory1..4}`, `mpequipped = equiped`,
   `mppickedup = pickedup`, `mpweared = weared`.
3. **Forcibly take the item away from the local body if the local player is holding it**
   (DC:4216–4231): `P.Pick()` (detach/unphyslock, DC:55093–55100); if `P.inRightHand` then
   `player.RightHandRelease(false)`; if `P.pickedUp` then `player.Drop()`; if worn and
   present in `player.weared` then `player.TakeOff(P, dontPickup: true)`.
4. `UpdItemParent(steamID, item)` (DC:4232) — reparents under the *named* remote player's
   proxy: `mpequipped → mpscript.Th` (DC:4950), `mpweared → mpscript.THead` (DC:4954),
   otherwise → `mpscript.handParent` (DC:4958); clears flags and restores
   `IgnoreCollision` when `steamID == 0` (DC:4918–4931); `UpdGravity()` (DC:4967).
5. If the item has no attachment and no ragdoll, start `SmoothMove(localPos, localRot,
   target)` where target is `mpequipped → mpscript.Th`, `mppickedup → player.transform`,
   **else `mpscript.THead`** (DC:4236–4247); otherwise stop a previous coroutine
   (DC:4249–4252).
6. If `hasVelocity`, write `RB.velocity`/`angularVelocity` directly (DC:4253–4264).
7. `UpdGotSyncTime()` (DC:4265).
8. If the item is unknown: `SendAskItemData(itemID)` (DC:4269) — and that reply is exactly
   what `ItemAskSafety.MayRemove` (`runtime/ItemAskSafety.cs:16–19`) and
   `SGB.MayRemoveMissingItem` (SGB:1644–1661, hooked at PS:661–669) exist to contain,
   because the native host reply is `removeItem` even when the item is merely held by
   someone else.

**Two source-level defects worth recording (not fixed by anything in `runtime/`):**

* *Parent/SmoothMove disagreement.* `UpdItemParent` parents an inventory (`mpstored`) item
  to `mpscript.handParent` (DC:4958) while `RecItemPosPlayer` smooths the same item toward
  `mpscript.THead` (DC:4246). A worn item is consistently `THead` in both.
* *Payload identity is trusted.* Steps 4–5 use the payload `steamID`, never the transport
  sender. Any peer can therefore parent any item onto any other peer's proxy and set that
  item's `mp*` flags. See §1.3; same class of defect as `playerPos`.

### 3.4 Why held/worn items must never join a car/cargo group

Three independent code paths already encode this, and all three must be preserved:

1. **Group membership.** `SGB.VehicleGroupMember` (SGB:752–762) returns `false` for
   `item.wear != null`, `item.outfit != null`, `item.mppickedup`, `item.mpequipped`,
   `item.mpweared`, and `item.P.pickedUp`. `ChainReachesCar` (SGB:763–779) additionally
   breaks the chain on any held/worn intermediate. Bounds: 8192 scanned,
   `VehicleAuthority.MaxGroupMembers = 256` (SGB:733–751, ExperimentalBackend.cs:196).
2. **Cargo pose re-expression.** `CarCargoPolicy.Loose` (CC:38–41) requires
   `!stored && !attached && !pickedUp && !isCar`; `CarCargo.RelativePose` (CC:120–145) and
   `CarCargo.Holdable` (CC:219–230) both feed it `item.P.pickedUp || item.mppickedup`
   (CC:126, CC:225). A held item therefore never becomes a collider-anchored
   `itemPosLocal`.
3. **Ownership decision.** `ThisOneInChargeItem(pickupable)` (DC:75138–75157) returns
   `true` for anything the local player holds (`inHandP`, `inventory`, `isweared()`) and for
   any `P.pickedUp` item, i.e. a held item is *always* locally in charge — so it must
   follow the **player**, and giving it a car-group owner would create two writers for one
   transform.

The design reason, stated plainly: a held item's world transform is a function of the
holder's hand/head, which is itself a function of the holder's body pose, which is a
function of the holder's seat, which is a function of the car. Attributing the item to the
car inverts that chain and produces a write race with the holder's `itemPosPlayer` stream
(and with `SmoothMove`, DC:4236–4247). Additionally, a worn item is not even a child of the
car in any native path — `Death2` iterates `weared` to take items off (DC:20877–20883) and
`DropStuff` unparents everything under the proxy (DC:3677).

---

## 4. Authority proposal (Player / Seat / Held item)

**Proposal.** Nothing in this section exists in `runtime/` today unless it cites a file.
It is expressed as invariants so each can be tested independently.

**P1 — Body ownership.** Each peer is the sole writer of its own body: `fpscontroller`
transform, `Bsitting`, `seat`, `died`, `survival.*`, `inventory`, `inHandP`, `weared`,
and of the `playerSyncClass` it populates via `SendPlayerPos`/`SendPlayerAnim`.
No peer may write another peer's body. This is already *almost* true (the local body is the
only simulated one), so the enforceable part is the mirror clause below.

**P2 — Message-identity binding.** For 10, 11, 12, 18 and 34 the effective entity is the
**payload** SteamID. Before delivery, a receiver must require
`sender == payloadIdentity` **or** `sender == host && host is relaying a client's own
frame`. This closes §1.3 / §3.3 without changing any native payload.

**P3 — Held/worn item ownership derives from the body.** For every item with
`P.pickedUp || P.inRightHand || P.isweared()` **or** any `mp*` flag set, the owner is the
player bound by the last accepted 18 frame for that item, and that player is the only
writer of the item's transform while the flag is set. Ties break in favour of the local
simulation (`ThisOneInChargeItem`, DC:75138) — never in favour of a car group.

**P4 — Seat claims are host-arbitrated leases.** A seat is owned by at most one peer at a
time, per `(itemID, seatID)` (or `(poigenID, poiID, seatID)` for POI seats).

**Proposal, lease shape** (modelled on the existing, tested `VehicleAuthority.Lease`
shape in `runtime/ExperimentalBackend.cs:199–207`: `Owner, Role, LastSeen, Epoch, Members`;
`Fresh`/expiry logic modelled on `BusSeatClaims.Fresh` BSC:17 and
`VehicleAuthority.AllowPeer` ExperimentalBackend.cs:242–275):

```
SeatClaim { ulong Owner; ulong Session; ulong Epoch; int  Role;   // 0 none / 1 passenger / 2 driverSeat1 / 3 driverSeat0
            uint ItemID;  int  SeatID;  double LastSeen; double Granted; }
```

Rules:
* **Claim** is a *request*, not a state write. Grant requires: claimant is a `Ready` peer
  with a live session; the seat is free or its lease is stale (`now - LastSeen > Lease`);
  the claimant is not already seated elsewhere on this machine's view; the claimant is not
  currently dead (`fpscontroller.died` is local-only, so this is unverifiable for a remote —
  see §9).
* **Grant** increments `Epoch`, publishes `SeatGranted`, and *then* the claimant is allowed
  to emit a `playerPos` with `sitting = true`. A `playerPos` claiming a seat it does not own
  is downgraded to `sitting = false` before delivery (never dropped: a dropped pose would
  freeze the avatar).
* **Refresh** is implicit: every accepted `playerPos` with a matching claim sets
  `LastSeen = now`. This preserves the native 1 s `inUseMulti` heartbeat (DC:65730–65733)
  and the 2 s `BusSeatClaims.Fresh` window (BSC:17) instead of inventing a third clock.
* **Release** on: explicit exit (`fpscontroller.GetOut` / `seatscript.GetOut`),
  a `playerPos` with `sitting = false`, seat destruction, lease expiry, peer leave.
  Release publishes `SeatReleased(epoch)` and frees the seat for the next claimant.
* **Contention** resolves by, in order: live lease beats stale lease; higher role beats
  lower (`driverSeat0 > driverSeat1 > passenger`, mirroring `VehicleAuthority.AllowPeer`'s
  "strictly higher role" rule, ExperimentalBackend.cs:272); otherwise first-come. Every
  refused claim is counted and answered (no silent drop).

**P5 — A car owner does not own its passengers.** `ThisOneInChargeCar()` (DC:75168–75193)
and `CarWhatDoing()` (DC:75195–75228) already express driving/shotgun/push-grip/pickup
roles; passenger occupancy must not enter that decision. Concretely: `seat.ClaimOwner` and
the car's `Lease.Owner` are independent, and a passenger on a remotely-driven car still
receives `MovingVehiclePassenger`'s velocity bracket (MVP:57) without gaining any car
authority. `MovingVehiclePassenger`'s own header (MVP:6–8) is the precedent to keep.

**P6 — Disconnect releases seats and drops/reparents held items.** On peer leave the
existing drop path must run *before* proxy destruction because destroying the proxy deletes
child items and broadcasts `removeItem` — the exact ordering already implemented in
`SGB.RemovePlayer` (SGB:2080–2096, comment at 2091–2093) and in the native
`DropStuff`→`Destroy` order (DC:3659–3660). Add to that: clear the peer's seat leases
(so `SeatReleased` is emitted, not merely timed out) and clear `BusSeatClaims` for the peer
in the same place (today only `RevokeBusPairPeer` does the latter, SGB:401).

**P7 — No peer may write another player's body or item flags.** Enforce at receive:
* 10/11/12 with a foreign payload ID → reject (or rewrite to `sitting=false` and consume as
  a pose of the sender, never of a third party).
* 18 with a foreign payload ID → reject unless the sender is the host relaying that ID's own
  frame.
* 34 with a target that is not the receiver → already ignored natively (DC:4700); additionally
  require the sender to be a `Ready` peer and rate-limit per `(sender, victim)`.
* 18 whose hold type *downgrades* an existing `mp*` flag held by a different player → reject
  as a stale duplicate (§3.2: 18 has no sequence number, so this is the only defence).

**P8 — Seated bodies are placed by parenting, never by teleport.** A seated remote avatar is
`parentObj`-parented with a seat-local offset (DC:3612/3649); a seated local body is
`sitPos`-parented (DC:24603). Repairs must go through `NativeSeatSafety` (NSS:66–104) and
must never move a body into a different parent.

---

## 5. Event and message plan

### 5.1 Classes of message

**(a) Relay unchanged — no new gate beyond P2.** These are already host-forwarded by
`ForwardType` (DC:91112–91118) and are semantically "the sender's own state":

| Type | Why it can be relayed as-is |
|---|---|
| 10 `playerPos` | sender's own body (DC:3915); forwarded DC:91114. Needs P2 only. |
| 11 `playerAnim` | sender's own skeleton/zoom/menus (DC:4022–4028); forwarded DC:91114. |
| 12 `playerOutfit` | sender's own outfit (DC:4068–4075); forwarded DC:91114; reliable. |
| 47 `stepSound` | pure positional one-shot; **carries no identity at all** (`giif`, SP:101; DC:5391–5395). Cannot be attributed, needs no gate. |
| 48 `outfit` | per-item outfit; reliable; identifies the item by `uint` at offset 1 (SP:133; DC:5403–5411). Gate by item owner (§5.2), not by player. |

**(b) Relay, but behind an owner/epoch gate.**

| Type | Gate |
|---|---|
| 18 `itemPosPlayer` | owner = the `steamID` in the payload; require P2 + P3 + P7's stale-downgrade rule. **Highest-value gate in this phase.** |
| 13 `askPlayerOutfit` | triggers `mainscript.s.player.outfit.SendForMulti()` (DC:4095–4098) on *whoever receives it*; must be relayed but rate-limited per sender. Already in `RepairRequest` (SGB:1016) so it survives `dispatching`. |
| 34 `dmgPlayer` | sender must be a `Ready` peer; victim must be the receiver (native, DC:4700); add per-`(sender,victim)` budget because RP's own dedup is sender-side only (RP:404–413). |
| 29 `push` | identifies the pushed item only (DC:4577); gate by item owner. |
| 16/17/19/20/39 | already sequenced (PS seq:26) and already gated where relevant (`CaptureVehicleMotion` SGB:584–602, `ValidateData` SGB:1084–1114, `ProtectCargoPose`/`CarCargo`, CC:120–145). Out of scope here. |

**(c) Needs a new event.** Native has no message for any of these:

| New event | Payload (proposal) | Supersedes / relation |
|---|---|---|
| `SeatClaimRequest` | `itemID or (poigenID,poiID)`, `seatID`, `session`, `role`, nonce | nothing; the native claim is only the receive-side `inUseMulti` marker (DC:3595) |
| `SeatGranted` | same key + `owner`, `epoch`, `expiresAt` | answers the request; gives the seat a real owner |
| `SeatReleased` | same key + `owner`, `epoch`, `reason` | makes `inUseMulti`'s 1 s fade explicit and observable |
| `SeatPreempted` | same key + old owner/epoch, new owner/epoch | replaces the silent "both players are sitting" outcome |
| `TeleportOrEnterExit` | `peerID`, `epoch`, `kind ∈ {EnterSeat, ExitSeat, Respawn, Teleport}`, `seatKey`, `pos`, `rot` | nothing; `Respawn` (DC:20956) and `Teleport` today emit no message; needed so a peer can *justify* a 50 m jump (`RemotePoseBuffer.TeleportMetres`, RPB:28) instead of treating it as a reset |
| `HeldItemTransfer` | `itemID`, `fromPeer`, `toPeer`, `holdType`, `fromEpoch`, `toEpoch` | makes the 18 hold-type overwrite (DC:4212–4215) an explicit, ordered handover with an epoch instead of a race |
| `PlayerStateDigest` (optional) | `peerID`, `died`, `hp` bucket, `inventoryItemIDs[]` | not replicated natively at all (§1.2); only needed if remote HUD/death is ever required |

Reuse the Phase 4 envelope shape (`docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md` §4) so one
codec and one duplicate-suppression rule serve both phases: `magic|entityId|owner|epoch|eventId|kind|flags|len|payload`, with
`flags` = reliable / ordered / latest-value and `eventId` dedup per `(entityId, owner)`.

### 5.2 Where the gates belong

Exact existing seam functions, in delivery order:

| Seam | File:line | What goes there |
|---|---|---|
| `StableGameBridge.AllowIncoming(CSteamID,byte[])` | SGB:1134–1163; installed as a `Guard` on `networkingScript.ProcessMessage` at PS:1104 | P2 identity binding for 10/11/12/18/34, before `AllowRabbitState` (SGB:1155) and before `NotePendingItem`. This is the *only* place that sees both the transport sender and the payload identity. **Caveat:** it short-circuits to `AllowRabbitState` while `dispatching` (SGB:1143), so it never runs for journal/snapshot replay. |
| `StableGameBridge.Deliver(ulong,byte[])` | SGB:1222–1240 | the **same** gate must be repeated here, twice over: (a) `Deliver` is the snapshot replay entry — `ApplyWorld` replays staged player/item messages through it (SGB:1935, `if(message.Key!=Own)Deliver(...)`), and (b) it is the only path that runs while `dispatching` is true. A gate only in `AllowIncoming` would let every replayed `18` frame through. This mirrors Phase 4 §5. |
| `StableGameBridge.RouteBroadcast(byte[],EP2PSend)` | SGB:1027–1054 | outbound suppression: do not broadcast a *local* 18 for an item whose `mp*` flag belongs to another player (`OtherInChargeItem`, DC:75159), and do not emit `playerPos` with `sitting=true` for a seat this peer does not hold a lease on. Placement is after `SuppressJoiningPlayerPosition` (SGB:1033) and **before** `ProtectCargoPose` (SGB:1050). |
| `StableGameBridge.RouteTarget(CSteamID,byte[],EP2PSend)` | SGB:1055–1080 | same suppression; additionally the peer-aware variant: an 18 for another player's item must not be routed to a third peer as if it were ours (SGB:1078 host `PublishNativeTo`). |
| `StableGameBridge.ReceiveBusPair` upstream branch | SGB:471–501 | the **pattern to copy**: verify `source == sample.Origin`, peer `Ready`, `peer.Session == sample.Epoch`, then `BusSeatClaims.DriverOf(...)`. That triple check is exactly P2+P3 for the bus. |
| `StableGameBridge.ValidateBusPairAuthorities` | SGB:403–423 | the pattern for a periodic lease re-validation (currently every fixed step, host only). A seat-lease validator belongs in the same `TickBusPair` (SGB:424–458) neighbourhood or the `FixedUpdate` tick (SGB:3544). |
| `StableGameBridge.BusSeatClaim` | SGB:521–535 | the single existing observe point for `GotUpd` seat claims; a `SeatClaimRequest` observer attaches here (PS:1250 already hooks it with exactly the needed args). |
| `NetCore`/delivery lanes | `SGB.Classify` (SGB:851), `NetDeliveryScheduler` | new seat/teleport events are reliable-ordered; `HeldItemTransfer` is reliable-ordered; `SeatGranted/Released` reliable. `18` stays unreliable (SP:144) and must therefore carry its own epoch/sequence. |
| `PoseSequence.Stamped` | PS seq:22–29 | if 18 is to gain ordering, add `case 18` here *and* add it to `LatestValueKey` (SP:155–163) with the same key shape. Both are one-line, join-fingerprinted changes. |

Host relay note: the native host relay inside `ProcessMessage` runs `ForwardType`
(DC:91112–91118) **before** any Roadworthy gate, and `RouteBroadcast`/`RouteTarget` are the
outbound seams that intercept it. A receiver-side gate is therefore the enforcement point;
the relay is at best a bandwidth optimisation (this matches Phase 4 §5.4).

---

## 6. Acceptance tests

Each test names the seam and the observable. All are two-peer (or loopback, see
`Start-Loopback.ps1`) tests; none require a new build step beyond the change under test.

**Seat / player**

1. **Join.** Guest joins; host sees one `playerSyncClass` per peer, one proxy per peer,
   `mpscript.steamID` matches, `THead` is a child of the proxy root. (RP:86–89 already
   checks the last three.)
2. **Late join.** Guest B joins while A walks. B's first snapshot contains A's newest
   `playerPos` (`lastPlayers`, SGB:1121–1125, SGB:1752) and A's newest `18`/`17` per item
   (`lastItems`, SGB:1126–1131, SGB:1754); the ApplyWorld validator accepts only 10/17/18
   (SGB:1909). Assert: no proxy at the origin, no held item at the world origin.
3. **Seat claim contention.** A and B both aim at the same `driverSeat0` and press use in
   the same 1 s window. Expected: exactly one `SeatGranted`; the loser's `playerPos` is
   downgraded to `sitting=false` (P4) and the loser is not parented. Assert the seat's
   `inUse`/`inUseMulti` for the winner's peer, `Car.driving` on the car, and one rejected
   claim counted.
4. **Driver/passenger swap.** A drives, B is in `driverSeat1`. A exits (DC:22773) and B
   enters `driverSeat0` (DC:24559). Assert: `SeatReleased(driver)` then
   `SeatGranted(driver)` with a higher epoch; `Car.driving`/`isPlayerDriving` flip once;
   `seatscript.mainseat` indirection respected (DC:23181).
5. **Enter/exit a moving car.** A drives at speed, B enters `driverSeat1`. Assert:
   B's local body is parented to `seat.sitPos` and `RB` destroyed (DC:24603/24622);
   A's view of B is `parentObj`-parented with the seat-local offset (DC:3612/3649) and is
   **not** smoothed (RS:19–23, RS:46); `NativeSeatSafety` reports zero repairs; no
   `playerPos` teleport longer than `RemotePoseBuffer.TeleportMetres` appears.
6. **Held item follows the player.** A picks up a crate (`pickupable.Pickup`, DC:55144) and
   walks through a car. Assert: `holdType` in `{pickedup, equiped}` for the whole walk; the
   item never appears in `CarCargo.RelativePose` output (`Loose` returns false, CC:38–41);
   the item is never in `VehicleGroupMembers` (SGB:752–762); on the remote peer the item is
   parented to `handParent`/`Th`/`THead` per DC:4950–4958.
7. **Worn item.** A wears a hat (`wearable.Wear(true,...)`, DC:79680) and enters a car.
   Assert: `mpweared` set on the remote peer, item parented to `mpscript.THead`, item
   excluded from the car group, and `DropStuff` (DC:3664) clears all four flags on leave.
8. **Disconnect while seated.** A is `driverSeat0` and holds an item; A disconnects.
   Assert, in order: `SeatReleased` for A's seat; `DropStuff` unparents and clears the four
   `mp*` flags **before** the proxy is destroyed (SGB:2091–2096, DC:3659–3660); no
   `removeItem` broadcast for A's held items; `inUse`/`driving` cleared on the car.
9. **Reconnect.** A rejoins with the same SteamID and an older epoch. Assert: old-epoch
   `SeatGranted`/`18` frames are rejected; `BusSeatClaims.Observe` with `session == 0`
   drops the stale claim (BSC:22); no stale parent survives on the item.
10. **Death / respawn.** A dies (DC:20867) while seated and respawns (DC:20956). Assert:
    `died` produces no message (§1.2); forced `GetOut` frees the seat via the normal path;
    the `Teleport` hook (`AfterNativeSpawnTeleport`, PS:1111, NSA:84) starts the spawn hold;
    the next `playerPos` is accepted as a *justified* teleport (see §5.1 `TeleportOrEnterExit`)
    rather than as a `RemotePoseBuffer` reset (RPB:82–90).
11. **Replay / duplicate.** Feed the same `playerPos`, `18`, `SeatClaimRequest` and
    `SeatGranted` twice. Assert: `poseFilter` rejects the stale `10` (PS seq:153–176);
    `18` is idempotent under the new epoch (§5.1b) even though it has no sequence number;
    a duplicate `SeatClaimRequest` is a no-op (same `eventId`); a duplicate `34` does not
    double-damage.

**Gates**

12. **Gate symmetry.** A hand-crafted `18` whose payload SteamID ≠ sender is rejected by
    *both* `AllowIncoming` (SGB:1134) and `Deliver` (SGB:1222). Same for `10`/`11`/`12`.
13. **Gate ordering.** A gated-out message must not have mutated anything:
    no `UpdItemParent` call, no `mp*` flag change, no `RecItemPosPlayer` forced release
    (DC:4216–4231), and no `SendAskItemData` side-effect (DC:4269).
14. **No passenger authority.** While riding a remotely-driven car, B's
    `ThisOneInChargeCar()` stays false and `MovingVehiclePassenger` still brackets
    (MVP:57); B's car group request is refused.
15. **No cross-body write.** A cannot cause B's `mpequipped`/`mpweared` to change by
    sending `18` naming B as the payload identity (P7).

---

## 7. Behaviour that is deliberately *not* proposed to change

* Native `playerSyncClass.Upd` lerp/predictive path for standing players (DC:3531–3544,
  DC:3636–3638). `RemotePlayerSmoothing` replaces only the applied pose, and only for
  standing players (RS:44–58).
* `RabbitGuestAnimation` — guest-only cosmetics (RGA:30, RGA:42–44); it never touches a
  player object and composes with `RabbitReplicas` only.
* `MovingVehiclePassenger`'s "velocity frame only" contract (MVP:6–8, MVP:145–149).
* `PlayerLeaveSafety.IgnoreCollision`'s cache (`CollisionRestoreCache`, PLS:18,
  PLS:34–63) and the `TLD_MP_IGNORE_CACHE=0` escape hatch (PLS:24–32).
* `NativeSeatSafety`'s "clear, never repair, once not seated" rule (NSS:63–79) — this is
  what makes death and seat destruction safe.

---

## 8. Corrections / clarifications to earlier inventory documents

1. `docs/MULTIPLAYER_MESSAGE_INVENTORY.md:78` groups `soundplay`/`stepSound` as
   "positional sound event and footstep event". Correct, but note **47 carries no identity
   field** (`giif`, SP:101) — it cannot be attributed to a player at all.
2. Same doc, line 77: `dmgPlayer` is "player damage". Precisely: the payload is
   `(senderID, victimID, dmg, dir)` and the native receiver applies it **only when
   `victimID == own SteamID`** and only as a *pending* accumulator
   (`survivalscript.Damage`, DC:69757–69765), never as instant death.
3. Same doc, line 72: `itemPosPlayer` is "player-held coordinates". Precisely: the identity
   used for parenting is the **payload** SteamID (DC:4232), and the local player's own
   holdings are forcibly released first (DC:4216–4231). `StablePackets.Item(18)` reads the
   id at offset **10** (SP:128).
4. `docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md` §2 lists 18 as unreliable with
   "no receiver authority". Sharpen: 18 additionally has **no `PoseSequence` trailer**
   (PS seq:22–29) and **no `LatestValueKey` coalescing** (SP:155–163), unlike 16/17/19/20/39.
5. `docs/MULTIPLAYER_VEHICLE_SLICE_INVENTORY.md:36` describes `RecPlayerPos` as
   "parents/smooths the remote avatar and sets that seat's multiplayer-use marker/time".
   Add: `inUseMulti` is cleared after 1 s by `seatscript.Update` (DC:65730–65733) and is
   checked by `FreeCanSit()` only at the local interaction moment (DC:23185) — it is a
   heartbeat, not a reservation.
6. `mpplayerscript.readyToMove` (DC:44466) is dead: declared, never read or written.

---

## 9. Open questions

1. **`playerRemoveTime` value.** The native default is a serialized field (DC:3735); the
   actual scene value was not read here. Every "how long does a ghost avatar live" answer
   depends on it, and RP uses it as a liveness gate (RP:91–93).
2. **Whether two peers can both be seated in the same `seatscript` today.** The source path
   suggests yes (both `GetIn` calls are local, `FreeCanSit` is advisory). No runtime
   confirmation, and no evidence of what the car physics does with two `sitPos` parents.
3. **Whether `inUseMulti` ever protects anything in practice.** It is only set on
   *receivers*; at `playerPosSyncTime` ~3–4 Hz (DC:3856) a 1 s marker gives roughly a
   one-sample grace window. Whether that window is meaningful at the default interval is
   unmeasured.
4. **POI seats.** `BusSeatClaims` explicitly refuses `sitAtPoi` claims (BSC:22); a POI seat
   has no `tosaveitemscript` and therefore no `idInSave` and no item owner. The lease key
   `(poigenID, poiID, seatID)` is proposed but its uniqueness/stability is unverified
   (`mainscript.GetChunkPos` at DC:3589).
5. **`seatscript.mainseat` semantics.** DC:23181 substitutes it before `GetIn`, but
   `BusSeatClaims.DriverOf` compares `seat.driverSeat0` on the *stored* seat without the
   indirection (BSC:40–41). Whether a `mainseat` alias can ever carry `driverSeat0` was not
   established.
6. **Which seat does `inUseMulti` mark when `mainseat` differs?** `RecPlayerPos` sets
   `seats[_seatID].inUseMulti` directly (DC:3615) without resolving `mainseat`.
7. **Held-item epoch/ordering for 18.** Adding 18 to `PoseSequence.Stamped` changes the wire
   format for a type that is currently untrailered; that is a join-fingerprint break and its
   interaction with `CaptureWorld`'s captured `18` bytes (SGB:1758) and `ApplyWorld`'s
   `StablePackets.Valid` check on staged messages (SGB:1909) has not been traced.
8. **Whether `RecItemPosPlayer`'s forced local release (DC:4216–4231) is required for
   correctness or is a legacy workaround.** It calls `RightHandRelease(false)` and
   `Drop()` on the *local* body; the interaction with `MovingVehiclePassenger`,
   `NativeSeatSafety` and `fpscontroller.Drop`'s throw force (DC:55249) is unverified.
9. **The `UpdItemParent` vs `SmoothMove` parent disagreement** for `mpstored` items
   (DC:4958 vs DC:4246, §3.3). Whether the smoothed target wins because it runs every frame
   or the parent wins because it is a `SetParent` has not been measured.
10. **Where a remote `died` state would come from.** §1.2 shows nothing carries it. If the
    design needs remote death visibility, a new event is required; whether the native
    respawn/ragdoll path can be observed without a message (`fpscontroller.SpawnRagdol`,
    DC:20912) is unexamined.
11. **`34` fairness.** `RecDmgPlayer` has no per-sender budget. RP's dedup (RP:404–413) is
    sender-side. Whether a cheating peer can trivially drain `toHp` is unmitigated in source.
12. **Interaction with the experimental backend.** `ExperimentalBackend.VehicleAuthority`
    already holds `(carId → owner, epoch)` without any seat concept; how a seat lease would
    be published alongside `PublishAuthority` (SGB:780–784) is not designed.
13. **`carscript`/`tosaveitemscript.seats` population timing.** Seats are wired by
    `seatscript.SetID`; whether every seat on every prefab calls it (vs. only
    `driverSeat0`) determines whether a lease can be keyed on all seats or only the driver
    seat was not fully audited here.
