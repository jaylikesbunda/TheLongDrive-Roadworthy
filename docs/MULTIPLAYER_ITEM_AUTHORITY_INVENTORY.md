# Multiplayer Item Authority Inventory (Phase 4)

Status: static research inventory. Pinned to the Roadworthy source in this
repository and the local decompile (`.local-analysis/all/Assembly-CSharp.decompiled.cs`).
No runtime or two-peer evidence is claimed. Unverified items are listed in §7.

## 1. Item categories

Identity is `tosaveitemscript.idInSave` (uint), prefab is `tosaveitemscript.id`.
`mpstored` means "in a remote player's inventory", **not** physlock storage.

| Category | Representation | State that matters | Follows |
|---|---|---|---|
| Loose | non-kinematic `RB`, no `P.physlock`, not attached/picked up, not a car | RB velocity/rotation, `useThisForPos` | world |
| Held | `P.pickedUp`, `P.inRightHand`, remote `mppickedup`/`mpequipped` | hold type, hand/body parent | player |
| Worn | `pickupable.wear`, `P.isweared()`, `mpweared` | hold type, worn parent | player |
| Stored | `P.physlock` (`mountStuff`), `parentTosave`, slot `id` | stored list, local pose | parent object |
| Mounted part | `attachable.attached`, `slot`, `AttachedToTosave()` | `save_attachable` | parent item / POI |
| Container | one `mountStuff` in `physLocks`, nesting via stored child | stored graph | outermost parent |
| Weapon/ammo | `weapon`, `ammo`, `breakable` | reload/mag/power/bullets, health | holder or item owner |
| Fluid/tank | `tanks`, `tankscript.F.fluids` | type/amount arrays | item owner |
| Car part | `attachable.isPart`, `partslotscript` | slot id, partconditions | car |
| Player item | `P.inInventory()` / hold types, `mpstored` | inventory slot | player |
| POI item | `attachType == toPOI`, `poigenid`/`poiid` | POI record | world |
| Car root | `tosaveitemscript.car` | car values/pose/role | driver or host |

Two traps: `mpstored` is not storage; worn items ride `itemPosPlayer` like held
items and only differ by parent transform.

## 2. Native item messages and missing authority

Layouts are in `runtime/StablePackets.cs` (`Valid`, `IsReliable`,
`LatestValueKey`). Unreliable: 10,11,14,16,17,18,19,20,29,39,43,46. Most reliable
item-state receivers have **no** ownership check; the only guard is sender-side
`ThisOneInCharge()`/`ThisOneInChargeCar()`.

| Type | Meaning | Reliability | Receiver authority |
|---|---|---|---|
| 16/17/18 | item world/local/player pose | unreliable | none (car-local checks inside `UpdInWorld`) |
| 19/20/39 | item/car velocity, car pose | unreliable | `ThisOneInCharge`/`Car` only |
| 23 | part condition | reliable | none |
| 24/25/26/27/28 | plate, bus doors, random type, food, ammo | reliable | none |
| 30/31 | weapon state, shot | reliable | none |
| 32/33 | paint, breakable/damage | reliable | none |
| 36 | remove item (destructive) | reliable | none |
| 37/38 | car values, handbrake | reliable | none |
| 40/41 | usable, fluid/tank | reliable | none |
| 42 | attach/detach topology | reliable | none |
| 43/48 | ragdoll, outfit | unreliable/reliable | none |
| 21/22/46 | ask item/POI data, ask item pos | reliable/unreliable | rate-limited |

`SyncAll` (item data reply) is the baseline a new owner must reproduce: pose,
attachable, usables, part conditions, tanks, car values, doors, plate, randoms,
food, ammo, weapon, color, breakable, NPC state, stored children, outfit.

## 3. Car group (verified edges only)

Members are:
- **Fitted parts:** `item.attachable != null && attached` → `AttachedToTosave()`.
- **Stored cargo:** `item.P != null && item.P.physlock != null` → `parentTosave`.
- Walk up to 8 hops (part on part, cargo in a stored container).
- Exclude: the car, `wear != null`, `outfit != null`, `P.pickedUp`,
  `mppickedup`, `mpequipped`, `mpweared`, and held/worn intermediates.
- Bounds: 8192 scanned items, 256 group members.
- **Not** members: loose cargo, player-held/worn items, POI-parented chains.
- Group transfer is all-or-nothing; a split member refuses the takeover rather
  than moving a guess.

## 4. Proposed event envelope

```
magic u32 | entityId u64 | owner u64 | epoch u64 | eventId u64 |
kind u8 | flags u8 | payloadLen u16 | payload
```

Flags: reliable, ordered, latest-value, replaceable. One `eventId` per
`(entityId, owner)`; duplicates are no-ops. Kind map:
- `PoseWorld` (16), `PoseLocalStorage` (17 index>=0), `PoseLocalMount`
  (17 index<=-100000), `PosePlayer` (18), `Velocity` (19), `CarVelocity` (20),
  `CarPose` (39) — unreliable/latest-value.
- `Attach`/`Detach` (42), `Store`/`Unstore` (17 transition), `Remove` (36),
  `PartCondition` (23), `Paint` (32), `Damage` (33), `Tank` (41), `Ammo` (28),
  `Weapon` (30), `WeaponShot` (31), `Food` (27), `RndType` (26), `Usable` (40),
  `Plate` (24), `BusDoors` (25), `CarValues` (37), `CarHandbrake` (38),
  `Outfit` (48) — reliable, ordered per entity, idempotent.
- `Push` (29) and `WeaponShot` (31) are one-off; never coalesced.

Dependency order: parent before child (`Attach` parent at payload offset 13;
stored child needs its parent). Reuse the bounded `PendingItemQueue` shape:
per-item FIFO 64, total 4096, 2 MiB, 60 s expiry, then a bounded repair request.
A `Remove` cancels queued events for that entity.

## 5. Gate sites

1. `StableGameBridge.AllowIncoming` — before `NotePendingItem`.
2. `StableGameBridge.Deliver` — journal replay must be gated identically.
3. `RouteBroadcast` / `RouteTarget` — suppress a local write for an entity this
   peer does not own, before `ProtectCargoPose`.
4. The host relay must not forward a non-owner write; the receive-side gate is
   the preferred enforcement point.

Re-express, do not relay: stored world pose → local storage; attached world pose
→ attach/detach event or drop; loose cargo in a moving car → collider-anchored
local pose; car pose/velocity → existing `VehicleMotionCodec`.

## 6. Acceptance tests

- Two claimants on one loose crate: one owner, one object, counted rejection.
- Two claimants on one car: role/lease rule decides; no duplicate car.
- Detach during motion: ordered detach, part leaves the group, no dual state.
- Delayed parent: bounded buffer, then repair/explicit failure.
- Nested container: car + box + crate transfer atomically; cycle refused.
- Reorder/duplicate: duplicate no-op; stale does not overwrite.
- Reconnect: car returns to host with a new epoch; old-epoch events rejected.
- Car takeover with cargo: group includes fitted/stored cargo, not loose/held.
- Late join during attach/store: converges to the same graph.
- Save/reload: saved parent/child IDs and poses preserved; item counts match.
- Holdable set: only `{23,24,25,26,27,28,30,32,33,37,38,40,41,42,48}` is buffered.
- Cargo pose re-expression: stored/attached/loose-car cases.
- Gate placement: `AllowIncoming` and `Deliver` reject identically.

## 7. Open questions

- All `mountStuff.Store` runtime callers beyond `FreezeUpd`/load/`ItemPosLocal`.
- `partslotscript.Craft` side effects and extra network messages.
- Whether a guest can drive `RecAskItemData`'s host `RemoveItem` fallback.
- Whether transient MP ownership is ever persisted in save records (not found).
- POI ownership and POI-stored cargo; POI tank form may discard its payload.
- Bus-pair/vehicle-motion coexistence with a per-entity event stream.
- `storageType` differences (`passengerRoom`, `trunk`, `engineBay`, ...).
- Collider ordinal stability across prefab instances.
- AI/creature ownership vs item ownership for `aiState`/`aiSound`.
- How native relay and experimental events coexist for one entity (not yet implemented).

RevaMP is LGPL-3.0; use ideas/tests, not code, without a licence review.
