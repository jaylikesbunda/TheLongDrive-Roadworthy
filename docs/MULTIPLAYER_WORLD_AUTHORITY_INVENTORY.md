# Multiplayer World, Creature and Breakable Authority Inventory (Phase 6)

Status: static research inventory. Read-only pass; no runtime, build or two-peer
evidence was produced for this document. Every behavioural statement below is
pinned to either this repository's `runtime/` sources or the pinned decompile
`.local-analysis/all/Assembly-CSharp.decompiled.cs`. Items that are inferred
rather than read directly are marked **[inferred]**. No parity claim of any kind
is made — not against vanilla single-player, not against the RevaMP notes, not
against any earlier Roadworthy behaviour.

Source-pin convention:

- `runtime/Foo.cs:L12-L34` — this repository.
- `DEC:L1234-L1260` — `.local-analysis/all/Assembly-CSharp.decompiled.cs`.
- `docs/Foo.md` — existing planning/inventory documents, cited as documents, not
  as evidence about the game.

Game build referenced by the pinned decompile is the same file the existing
inventories cite (`docs/MULTIPLAYER_MESSAGE_INVENTORY.md` header).

---

## 0. Scope, and what was deliberately not audited

Read for this inventory:

- World/generation: `runtime/StableMap.cs`, `runtime/WorldJump.cs`,
  `runtime/WorldRelocationLedger.cs`, `runtime/WorldBiomePolicy.cs`,
  `runtime/WorldPlacementPolicy.cs`, `runtime/PoiSlopePlacement.cs`,
  `runtime/LargeRockClearance.cs`, `runtime/PoiSaveRestore.cs`.
- Spawn: `runtime/SpawnCoordinator.cs`, `runtime/SpawnClearance.cs`,
  `runtime/NativeSpawnAdapter.cs`, `runtime/FixedPropConstruction.cs`, the spawn
  handoff region of `runtime/StableGameBridge.cs`.
- Interactions/state: `runtime/LiveUsable.cs`, `runtime/SmokeRuntime.cs`,
  `runtime/SmokeProtocol.cs`, `runtime/WildlifeWeather.cs`,
  `runtime/StablePackets.cs`.
- Creatures: `runtime/RabbitAi.cs`, `RabbitPopulation.cs`, `RabbitReplicas.cs`,
  `RabbitNetwork.cs`, `RabbitCombat.cs`, `RabbitContact.cs`,
  `RabbitLocomotion.cs`, `RabbitMetadataSync.cs`, `RabbitMotionProtocol.cs`,
  `RabbitMotionRuntime.cs`, `RabbitProtocol.cs`, `RabbitPlayers.cs`,
  `RabbitQueries.cs`, `RabbitCarryLeases.cs`, `RabbitGuestAnimation.cs`,
  `NpcVehicleImpact.cs`.
- Documents: `docs/MULTIPLAYER_MESSAGE_INVENTORY.md`,
  `docs/SPAWN_REFACTOR_PLAN.md`, `docs/REVAMP_MULTIPLAYER_LESSONS.md`,
  `docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md`.

Not audited in this pass, and therefore **not** described here: the vehicle-side
authority modules (`CarOwnership.cs`, `BusRearAuthority.cs`,
`VehicleMotionCodec` beyond its packet layout, `ExperimentalAuthority.cs`),
`CarCargo.cs`, terrain/road internals beyond their StableMap entry points, and
the IL patch tool itself (`tools/PatchStability.cs`, `tools/VerifyStability.cs`).
Claims about which native methods are actually patched are therefore limited to
what the bridge exposes (a patched native call site exists because the bridge
publishes a hook with that name and semantics); the hook installation itself is
outside this inventory.

---

## 1. World seed, config and generation

### 1.1 What must match between peers

| Requirement | Mechanism | Source |
|---|---|---|
| Map type, map id, map name, seed, host world position | `msgType.mapStuff` (8) sent by the host at world start and on each join | `DEC:L4100-L4138` `SendMapStuff()`, `SendMapStuff(CSteamID)`; `DEC:L90910-L90919` `HostSendOnJoin`; `DEC:L42529-L42536` (host sends mapStuff + time when the session becomes host) |
| Guest applies the host's world by reloading the scene | `RecMapStuff` writes `DataFromMenuScript.s.seed`, `multiMap.map.{tip,defmapID,mapName}`, `multiHostPos`, then `menuhandler.s.LoadScene()` | `DEC:L4140-L4177` |
| Guest must accept only map bootstrap before full sync | `AcceptMessage` allows only `mapStuff`, `password`, `kick`, `ping`, `pingBack`, `chatMsg`, `voiceChat` until `sentAskStartStuff` | `DEC:L91201-L91212` |
| Scene reload for the guest | `menuhandler.LoadScene()` → coroutine `DoLoadScene()` → `SceneManager.LoadScene(sceneName)` | `DEC:L43068-L43078` |
| Calendar / biome / placement profile | Roadworthy's own 4-byte `HostConfig` alongside `mapStuff`; host packs it from `mainscript.CheckHoliday` bits, `WorldBiomePolicy.Mode`, `WorldPlacementPolicy.Version`; guest unpacks and installs all three, and stops the session on a malformed payload | `runtime/StableMap.cs:L99-L109`, `L110-L114`; `runtime/WorldPlacementPolicy.cs:L47-L51`; `runtime/WorldBiomePolicy.cs:L62-L69` |
| Game build / content identity | Content fingerprint gate (`node.Compatible`, `node.Error`, `frame.Fingerprint != fingerprint` rejection) | `runtime/StableGameBridge.cs:L1895-L1899`; `node.Compatible` gates at `runtime/RabbitCombat.cs:L92-L99`, `runtime/RabbitMetadataSync.cs:L58-L61` |

`RecMapStuff` carries **no** calendar, biome or placement version. Those travel
only in Roadworthy's `HostConfig` (`runtime/StableMap.cs:L105`), which is why
`RecMapStuff` alone is not sufficient for a peer to reproduce the host's world.

### 1.2 Native generation inputs

| Entity | Seeded by | Source |
|---|---|---|
| Map seed | `mapSettingScript.seed` | `DEC:L40658` |
| POI chunk points/seeds | `RandomPoints(..., mapSetting.seed, ...)` then `chunkClass.SelectTypes(...)` | `DEC:L56714-L56726` (`poiGenScript.GenChunk`) |
| POI building rock scale | `randomRock.FStart(mapSetting.seed + _poi.posID.GetHashCode() + i)` | `DEC:L56808-L56813` (`poiGenScript.Spawn`) |
| POI identity key | `mainscript.Snap(_pos, round)` inside `mainscript.GetChunkPos(_pos, chunkSize)` | `DEC:L56770-L56790` (`poiGenScript.AddPoi`) |
| Decoration (props) generation | `categoryClass.UpdGen`, run per category from the map's `objGens` | `runtime/StableMap.cs:L572-L593` (host branch) |
| Terrain height / road coverage used for POI placement | `RoadTerrainClearance.Height(...)`, `mapSettingScript.RoadsNear(...)` | `runtime/PoiSlopePlacement.cs:L75-L98` |

Roadworthy does not replace these; it wraps them. `StableMap.Provide` calls the
**host's own** `road.UpdChunks(false); road.ConnectChunks(); road.UpdGenRoad(false)`
for a requested road cell (`runtime/StableMap.cs:L758-L776`), and
`script.GenChunk(pos); ... script.AddPois()` for a requested POI cell
(`runtime/StableMap.cs:L814-L839`).

### 1.3 `StableMap` reconciliation: roads, POIs, decorations

| Direction | Mechanism | Source |
|---|---|---|
| Host generates normally | `RoadUpdate` calls native `doctor.UpdChunksNeeded()` + `road.Upd(more)` in place; `CategoryGen` calls native `cat.UpdGen(more)` | `runtime/StableMap.cs:L152-L174`, `L583-L594` |
| Guest never runs host generators for roads/decorations | Guest `RoadUpdate` requests manifest keys then runs `road.UpdPlaceRemove(more)` only; guest `CategoryGen` runs `CategoryChunks`, `RequestCategoryChunks`, `RestorePlacement`, `EvictCategory` and never `UpdGen` | `runtime/StableMap.cs:L166-L173`, `L595-L607` |
| Guest POI add is version-gated | `PoiAdd` returns `WorldPlacementPolicy.Version < 4` for guests, i.e. the native `AddPois` body runs locally only for older placement profiles; for version ≥ 4 the host manifest supplies POIs | `runtime/StableMap.cs:L194-L196` |
| Manifest keys are self-describing and seed-checked | key = `kind|gen|category|x|z`; applied chunk must match key, `Version==1` and `dto.Seed == Map.seed` | `runtime/StableMap.cs:L117-L118`, `L919-L927` |
| Host supplies road payload in a binary codec | `StableRoadCodec.Write/Read`, distinct magic/header from JSON | `runtime/StableMap.cs:L880-L884`, `L919-L925` |
| Duplicate/stale manifests | decoration replies for cells no longer needed are dropped; a retransmitted decoration chunk whose record already points at live objects is acknowledged but not replaced | `runtime/StableMap.cs:L932-L944` |
| POI manifests can be incomplete | host sets `PoisComplete=false` when the chunk was force-marked generated or still has `poiToPlace`; guest retries such a key at most 3 times then caches it as non-authoritative | `runtime/StableMap.cs:L824-L838`, `L119-L135`, `L973-L990` |
| Guest POI prune | only when `PoisComplete` **and** at least one POI, and only POIs whose chunk equals the payload position | `runtime/StableMap.cs:L975-L980`, `L538` |
| Relocation records are reconciled, not overwritten | conflicting records throw `InvalidOperationException("Conflicting world relocation record.")`; ledger-full throws and the chunk is refused | `runtime/StableMap.cs:L908-L918` |

Client-side "completeness" is bounded: `StableMap.available` is monotonic per
loaded map (`runtime/StableMap.cs:L23-L31`, `L137-L145`), `LastPlacementCells` /
`lastStreamCenters` are per-category caches (`L62-L70`), and the manifest cache is
capped at 32 cells (`L35`, `L561-L571`).

### 1.4 Origin shifts

| Fact | Source |
|---|---|
| Global space is `mainscript.visszarakva + unityPos` | `DEC:L36390` (`public Vector3d visszarakva`), `DEC:L37240` (`GlobalFromUnityPos`), `DEC:L37245` (`UnityPosFromGlobal`) |
| The native recentre triggers when the player's Unity position squared exceeds `6 250 000` (2 500 m) and shifts `visszarakva` by `-pos + normalize(-pos)*2000` | `DEC:L39601-L39630` (`mainscript.VisszaRakas`) |
| Every recentre is per-peer and unsynchronised; Roadworthy only logs it and logs global-space "view jumps" | `runtime/WorldJump.cs:L25-L86`; logged from `runtime/StableGameBridge.cs:L3545` (`LateUpdate` → `WorldJump.Tick(Connected)`) |
| Native world-position messages mostly carry global space (`itemPosGlobal` 16, `carPosGlobal` 39, `carValues`, `usable` POI branch `Vector3d`, `stepSound` `Vector3d`) | `DEC:L4179-L4189` (`SendItemPosPlayer` uses local+owner), `DEC:L5042-L5045` (`SendUsable` POI overload takes `Vector3d poiid`), `DEC:L5386-L5395` (`SendStepSound(Vector3d …)` → `UnityPosFromGlobal`) |
| One native message does **not** use global space: `soundplay` (35) sends a plain `Vector3` and plays it verbatim | `DEC:L4706-L4715` (`SendSound(Vector3 _pos …)`, `RecSound` → `soundMainScript.s.PlaySound(_obj, …)`) |

Consequence: peers that recentred at different times can place type-35 sounds in
the wrong place, while item/car/POI positions stay comparable. This is a
structural observation from the two sender signatures, not a measured defect.

### 1.5 Which parts are host-authoritative today

| Area | Today | Source |
|---|---|---|
| World seed / map identity | Host, by construction: guest reloads the scene from `RecMapStuff` | `DEC:L4140-L4177` |
| Road geometry | Host generates; guest receives only generated chunks | `runtime/StableMap.cs:L152-L174`, `L801-L885` |
| POI placement/rotation/type | Host generates; guest applies host POI records (`PoiSlopePlacement.AddAtIdentity` + `save_poi.Load`) | `runtime/StableMap.cs:L957-L990` |
| Decoration (props/rocks) placement | Host generates; guest applies `objClass` records and skips destroyed/tombstoned ones | `runtime/StableMap.cs:L991-L1013` |
| Placement profile / relocation ledger | Host-owned by value, but the ledger is *replicated* and conflict-checked, not re-derived | `runtime/StableMap.cs:L908-L918`; `runtime/WorldRelocationLedger.cs:L19-L75` |
| POI contents (items/fixed prefab) | Host decides; guest marks the POI spawned and asks for data | `DEC:L57104-L57122`, `DEC:L57098-L57101` |
| Biome / calendar | Host config; guest installs from `HostConfig` | `runtime/StableMap.cs:L105-L106` |
| Terrain, terrain collider | Local per peer (guest clamps its own draw distance; collider grid pinned locally) | `runtime/StableMap.cs:L707-L756`; readiness requires *local* terrain collider | `runtime/StableMap.cs:L785-L800` |

`StableMap.Ready()` (`runtime/StableMap.cs:L785-L800`) is the guest's
`CanEnterWorld` gate: it requires all road/POI manifests around every player
radius plus a locally-enabled terrain collider mesh at the spawn tile. It does
**not** require decoration manifests.

---

## 2. Spawns

### 2.1 Native spawn entry points and what they own

| Spawn kind | Native routine | Guest behaviour today | Source |
|---|---|---|---|
| Starting POI / `fixedSpawnScript` | `fixedSpawnScript.FStart()` — activates the selected start car, freezes bodies, `FStart()` every `tosaveitemscript`, `FStart()` every `randomTypeSelector` | runs locally (the scene contains the starting POI) | `DEC:L18124-L18149`; Roadworthy re-implementation with pooled lists `runtime/StableGameBridge.cs:L2620-L2649` |
| POI random contents | `poiScript.ItemSpawn()` → `newRandomStuffSpawnScript.Spawn()`, `undergroundSpawnScript.Spawn()` | **skipped** on guests | `DEC:L57224-L57236`; gate `DEC:L57104-L57122` |
| POI fixed prefab + parts + flyers | `poiScript.FixedSpawn()` → `Instantiate(fixedSpawn, …)` + `fixedSpawnScript.FStart()`, `partslots[i].FStart()`, `tempRandomFlyerSpawnScript.Spawn()` | **skipped** on guests | `DEC:L57199-L57222`; gate `DEC:L57104-L57122` |
| Guest response to a skipped POI spawn | `syncScript.s.SendAskPoiData(poigenid, posID)` after `poiGenScript.Spawn` builds the building locally | `DEC:L57098-L57101`; host answer `RecAskPoiData` → `pobj.SyncAll()` `DEC:L5210-L5223` |
| Saved POI state | `AddPoi` consumes `poisFromSave[snappedKey]`, calls `save_poi.Load(poi)` and sets `spawnedItems = spawnedFixed = true` | local on both peers from the save/capture | `DEC:L56770-L56790` |
| Random cars/items far placement | `itemPlaceRemoveScript.PlaceOne(id, index)` (called from `PlaceStuff`) | guest-local placement, but constrained to host-declared IDs | `runtime/StableGameBridge.cs:L1387-L1403` |
| Guest developer spawn | guest creates locally, then the native capture path announces the created items to the host | `runtime/StableGameBridge.cs:L3572-L3620` |

`FixedPropConstruction.Instantiate` is refused while `scene.multi` is set
(`runtime/FixedPropConstruction.cs:L52-L59`), so the single-player "construct the
starting fixed prop on a worker" path is not part of a multiplayer world.

### 2.2 Who decides a spawn

- **Host** decides POI contents, the fixed prefab and every random roll — because
  `poiScript.SpawnStuff` returns before `ItemSpawn()`/`FixedSpawn()` when
  `!syncScript.IsHostOrSingle()`, while still setting both `spawned*` flags
  (`DEC:L57104-L57122`).
- **Guest** decides only its own creations; those are then announced, verified
  and acknowledged by the host before they become part of the shared world
  (`runtime/StableGameBridge.cs:L3565-L3571`, `L3633-L3664`).
- ID allocation is host-brokered: `StableGameBridge.NewKey` calls
  `node.AllocateID(...)` and continues only while the ID is unused in both the
  live item table and the saved definition list
  (`runtime/StableGameBridge.cs:L1242-L1255`).

### 2.3 Duplicate prevention, as implemented

Three separate mechanisms exist; they are not interchangeable.

**(a) `SpawnCoordinator` — in-process construction dedup/ledger.**

| Concept | Implementation | Source |
|---|---|---|
| Job identity | `SpawnJobKey{Generation, Source, Key, Owner}` with `Owner` compared by reference | `runtime/SpawnCoordinator.cs:L29-L66` |
| Sources | `SourcePoi=1`, `SourceSave=2`, `SourceFarPlace=3`, `SourceDeveloper=4` | `runtime/NativeSpawnAdapter.cs:L7-L10` |
| World generation token | `EnsureWorldGeneration` bumps `spawnWorldGeneration` and calls `Construction.ResetWorld` when save/map/data identity changes; a job from an older generation is refused as `StaleWorld` | `runtime/StableGameBridge.cs:L2348-L2358`; `runtime/SpawnCoordinator.cs:L152-L161` |
| Duplicate | identity present in `live` → `Duplicate`, refused | `runtime/SpawnCoordinator.cs:L156` |
| Terminal memory | bounded `terminalFailed` registry, 256 entries, evictable only when not failed and not pinned by a dependency; saturation latches and refuses everything afterwards | `runtime/SpawnCoordinator.cs:L108`, `L423-L484` |
| Dependency ordering | jobs may not run before dependencies exist or complete | `runtime/SpawnCoordinator.cs:L180-L193`, `L486-L528` |
| Budget | per-frame soft budget via `Drain(budgetMs)` with a 2 s aging overrun; atomic units that exceed 3 ms are counted | `runtime/SpawnCoordinator.cs:L559-L593`, `L126-L130` |
| Atomic unadmitted fallback | `RunUnadmittedAtomic` runs the native call when capacity refuses, so required content is not dropped | `runtime/SpawnCoordinator.cs:L299-L335`; used at `runtime/StableGameBridge.cs:L2375-L2381`, `L1392-L1400` |
| Failure rollback | native flags captured and restored around the native spawn call | `runtime/StableGameBridge.cs:L2385-L2392` |
| Consistency fence | `CompletePendingConstruction(reason, scope)` drains everything or throws; used for save, snapshot capture, export | `runtime/SpawnCoordinator.cs:L595-L614`; callers `runtime/StableGameBridge.cs:L2393-L2399`, `L2474-L2476`, `L1411` |

**(b) Guest→host spawn handoff ledger (network).**

The guest captures definitions + persistent state for unannounced items whose ID
lies in `[node.FirstID, node.LastID]`, sends them as one transaction, retries up
to 5 times, and marks the IDs announced only on the host's acknowledgement
(`runtime/StableGameBridge.cs:L3565-L3571`, `L3572-L3620`). The host replays the
packets as native messages, then *verifies* the outcome before publishing
(`L3707-L3757`):

- definitions must materialise (`HeldItemExists(id)`),
- type-42 attachments must be settled (`VerifyAttachmentPacket`, `L3758-L3788`),
- type-17 stored poses must resolve to a real storage slot or collider index
  (`VerifyStoredPacket`, `L3795-L3814`).

Bounds: ≤ 8192 items, ≤ 65536 packets, ≤ `StableNode.MaxSpawnBytes` (16 MiB),
rejects duplicates/out-of-range/deleted IDs, requires a definition for every
non-definition packet, and requires parent definitions to be present or already
held (`L3677-L3703`). Replay is keyed by `(peer.Session, sequence)` with a
committed watermark, so a duplicate delivery returns the previous answer rather
than re-instantiating (`L3633-L3664`).

**(c) Host-side "already exists" checks inside native handlers.**

Several receivers fall back to `SendAskItemData` when the ID is unknown
(`RecBreakable` `DEC:L4669-L4687`, `RecPush` `DEC:L4575-L4594`,
`RecTank` `DEC:L5101-L5127`, `RecUsable` `DEC:L5047-L5075`,
`RecRagdoll` `DEC:L5249-L5273`, `RecAISound` `DEC:L5280-L5294`,
`RecAIState` `DEC:L5311-L5358`, `RecAskItemPos`/`RecRemoveItem`
`DEC:L5368-L5384`). That is a repair request, not duplicate prevention: an item
that exists on both peers under different IDs is not detected by it.

### 2.4 Spawn leases / claims — what exists and what does not

| Claim mechanism | Exists? | Source |
|---|---|---|
| Per-identity admission lease for construction | Yes, in-process only | `runtime/SpawnCoordinator.cs:L152-L212` |
| Network transaction identity for guest creations | Yes (`Session`, `Sequence`, `Hash`, committed watermark) | `runtime/StableGameBridge.cs:L3549-L3557`, `L3633-L3664` |
| Network transaction identity for host→guest POI contents | **No dedicated lease.** Host decides; guest asks per POI (`askPoiData`) | `DEC:L57104-L57122`, `DEC:L57098-L57101` |
| Per-creature carrier lease | Yes, but bookkeeping only, not wired to a spawn decision | `runtime/RabbitCarryLeases.cs:L6-L114` |
| Lease for a world prop or breakable | **No** | none found in the files read |
| Lease persisted across save/reload | **No** — the coordinator is rebuilt per process and reset per world generation | `runtime/SpawnCoordinator.cs:L257-L278`; `runtime/StableGameBridge.cs:L2482-L2492` |

So "who decides a spawn" is answered by *role* (host decides POI content) plus
*announcement* (guest announces its own creations), not by a general lease
protocol.

### 2.5 Interrupted spawn, contested arrival, save/reload

| Situation | Current handling | Source |
|---|---|---|
| Spawn work in flight when a save is requested | fence completes or throws before serialization | `runtime/StableGameBridge.cs:L2474-L2476`, `L2477-L2481` |
| Spawn work in flight when a snapshot is captured | `FlushFarPlace()` → `ConstructionFence("world-capture")` before copying `data.itemData` and POI state | `runtime/StableGameBridge.cs:L1728-L1763`, `L1411` |
| Spawn apply partially done at a consistency read | `VerifyPendingSpawnConstruction` re-runs the ledger and fails loudly | `runtime/StableGameBridge.cs:L3815-L3830` |
| World replaced while work is queued | generation bump, all queued jobs cancelled, peers retired | `runtime/StableGameBridge.cs:L2348-L2358`, `L2482-L2492`; `runtime/SpawnCoordinator.cs:L257-L278` |
| Job whose dependency failed | job fails; failure is retained in the terminal registry and blocks re-admission of the same identity | `runtime/SpawnCoordinator.cs:L337-L355`, `L415-L421` |
| POI goes out of range mid-construction | no explicit POI-scoped cancel is called from `StableMap`; only `CancelUnstarted(source)` exists and has no caller in the files read | `runtime/SpawnCoordinator.cs:L241-L255` (no call sites found) |
| Contested arrival (two peers claim the same world object) | no rule exists for props/breakables; for guest-created items the host verifies and either commits or refuses the transaction | `runtime/StableGameBridge.cs:L3707-L3757` |
| Save/reload persistence of spawn decisions | saved item definitions + `save_poi` are the persistence; the coordinator and its terminal registry are not persisted | `runtime/StableGameBridge.cs:L1895-L1937` |
| Guest save during a rejoin | last-acknowledged set (`announcedSpawns`) is process-scoped and cleared on reset | `runtime/StableGameBridge.cs:L3548`, `L3563-L3564` |
| World-capture ordering guard | `CaptureWorld` and `ApplyWorld` are kept free of mod types because a world-load IL replay transplants `SendWorld` | `runtime/StableGameBridge.cs:L1764-L1769` |

### 2.6 `SpawnCoordinator` vs the RevaMP lease lesson

`docs/REVAMP_MULTIPLAYER_LESSONS.md:L7-L12` states the lesson: RevaMP reports
that hiding a locally generated matching car was replaced by server-mediated
building-content spawn leases, and that a seed alone is insufficient because the
start car is time-dependent and building contents are unseeded.

Comparing to the code read here:

| Lesson element | Roadworthy state | Source |
|---|---|---|
| Server-mediated spawn *lease* for building content | Not present. The decision is a host/guest **role gate** inside `poiScript.SpawnStuff`; there is no per-POI grant/complete/retry handshake to a specific peer. | `DEC:L57104-L57122` |
| Explicit grant / completion / failed-spawner / retry | Present only for guest→host item announcements (`TickSpawnHandoff` retry ≤ 5, `AckSpawn`, `spawnCommitted` watermark) and for in-process construction (`SpawnJobState`, terminal registry). | `runtime/StableGameBridge.cs:L3565-L3571`, `L3572-L3620`; `runtime/SpawnCoordinator.cs:L20-L27` |
| "Prefer explicit completion to a timed capture window" | The guest announcement is explicit-completion by hash+verify; the *host* POI path is still a synchronous native call with no peer-visible completion signal other than the item messages it produces. | `runtime/StableGameBridge.cs:L3736-L3757`, `DEC:L57104-L57122` |
| "Seed alone is insufficient" | Confirmed structurally: POI contents use `UnityEngine.Random` through `newRandomStuffSpawnScript`/`undergroundSpawnScript`, which the host runs and guests do not (`DEC:L57224-L57236`, `DEC:L57104-L57122`). The seed does not deterministically reproduce contents on a peer that never runs the spawner. | `DEC:L57224-L57236` |
| Entity identity survives streaming | POI identity is the snapped `posID` (`DEC:L56770-L56790`); decoration identity is `gposID`/`chunkID` (`runtime/StableMap.cs:L96-L97`, `L868-L876`); item identity is `idInSave`. There is no single registry with loaded/dormant/pending/deleted states for world props. | `runtime/StableMap.cs:L528-L529`, `L666-L670` |
| Leases inherited by a new world | Not applicable for spawn; `ResetConstruction` clears all spawn state and bumps the generation on world replacement, which is the opposite failure mode from the RevaMP report. | `runtime/StableGameBridge.cs:L2482-L2492` |

Conclusion to carry into §6: the project already has the *in-process* half of a
lease system (`SpawnCoordinator`) and the *transaction* half for guest-created
items, but no per-POI, per-prop or per-creature lease with an owner epoch.

### 2.7 `SpawnClearance` (join placement) — adjacent but unrelated

`runtime/SpawnClearance.cs` is a join-time **spawn-point clearance** state
machine, not a spawn-authority mechanism: the joining player is held until the
capsule stops overlapping props/cars/walls, a ring search runs at 1.5 m steps out
to 15 m, and the hold gives up after 8 s (or 16 s across moves) and releases at
the native spot (`runtime/SpawnClearance.cs:L14-L101`). It decides *where the
joining player stands*, never *what exists*.

---

## 3. Doors, gates, usables, POI state, time, radio, weather

### 3.1 Native topology and dispatch (the authority baseline)

| Fact | Source |
|---|---|
| Clients send only to the lobby owner; the host sends to every password-cleared lobby member | `DEC:L91872-L91889` (`SendBytes`) |
| The host relays a received message to all other members when `ForwardType(type)` is true | `DEC:L90935-L90945`, `DEC:L92019-L92029` (`ForwardMessage`) |
| `ForwardType` includes `push, weapon, weaponShot, color, breakable, dmgPlayer, soundplay, licenseplate, bus, removeItem, carValues, carHandbrake, usable, fluid, attachable, ragdoll, stepSound, outfit` and the item/car position types | `DEC:L91112-L91119` |
| The host applies the message locally **and** forwards it: forwarding happens before the `switch` (`DEC:L90935-L90945`) | `DEC:L90929-L91110` |
| No native case checks an owner, epoch or lease. `ProcessMessage` only rejects the local sender and, on a client, applies `AcceptMessage` | `DEC:L90929-L90945` |
| `ProcessMessage` never distinguishes host origin from peer origin for any gameplay type | `DEC:L90929-L91110` |

So the *only* native structural fact resembling authority is "host relays". It is
a fan-out rule, not an authority rule.

### 3.2 Message table for the state surfaces in this phase

Layouts are in `runtime/StablePackets.cs:L52-L119`; names/ordinals in
`docs/MULTIPLAYER_MESSAGE_INVENTORY.md:L5-L55`.

| Type | Name | Receiver | What it mutates | Owner check today | Source |
|---:|---|---|---|---|---|
| 14 | `time` | `RecTime` | `napszakvaltakozas.s.tekeres += _t - s.t` (delta applied to local clock) | none | `DEC:L4054-L4066` |
| 15 | `radioChannel` | `RecRadioChannel` | `mainscript.s.RadioChannelFromMultiplayerUpd(fm, fmTimes, am, amTimes)` | none | `DEC:L4043-L4052` |
| 32 | `color` | `RecSprayColor` | `item.colors[cid].color`, `sprayscript.UpdColor()` | none | `DEC:L4638-L4662` |
| 33 | `breakable` | `RecBreakable` | `health`, `shootHealth`, `explosionHealth`, then `Upd()` (which may `Break()`) | none | `DEC:L4664-L4687` |
| 34 | `dmgPlayer` | `RecDmgPlayer` | `mainscript.s.player.survival.Damage(...)` — only when the target ID equals the local Steam ID | target-ID equality only | `DEC:L4689-L4704` |
| 35 | `soundplay` | `RecSound` | plays a clip at a **Unity-space** position | none | `DEC:L4706-L4715` |
| 36 | `removeItem` | `RecRemoveItem` | `Object.Destroy(items[id].gameObject)` | none | `DEC:L4878-L4890` |
| 40 | `usable` | `RecUsable` | POI branch: `poi.pobj.usables[i].UpdFromMulti(...)`; item branch: `item.usables[i].UpdFromMulti(...)` | none | `DEC:L5042-L5075`, `DEC:L76365-L76401` |
| 41 | `fluid` | `RecTank` | item branch replaces `tanks[t].F.fluids` and calls `tank.Upd(dontSync: true)`; **POI branch decodes and returns without applying** | none | `DEC:L5077-L5127` |
| 43 | `ragdoll` | `RecRagdoll` | sets each `ragdollPos.childs[i]` local pos/rot and RB velocities | none | `DEC:L5225-L5273` |
| 44 | `aiSound` | `RecAISound` | `item.npc.SoundSound(...)` — `tosaveitemscript.npc` is `newAiScript`, not the rabbit | none | `DEC:L5275-L5294`; `DEC:L73759` |
| 45 | `aiState` | `RecAIState` | `item.npc.UpdState(...)` — likewise `newAiScript` only | none | `DEC:L5296-L5358`; `DEC:L73759` |
| 47 | `stepSound` | `RecStepSound` | plays a walk clip at `UnityPosFromGlobal(pos)` | none | `DEC:L5386-L5395` |
| 29 | `push` | `RecPush` | `pushablescript.PushLocal(dir, pos)` | none | `DEC:L4570-L4594` |
| 42 | `attachable` | `RecAttachable` | detach, or coroutine `UpdAttachable` retrying `save_attachable.Load` | none | `DEC:L5129-L5200` |

Types 44/45 do not apply to rabbits: `nyulscript` is a separate component and
`tosaveitemscript.npc` is typed `newAiScript` (`DEC:L73759`). Rabbit state is
carried entirely by Roadworthy's own messages — see §4.

### 3.3 Who owns a POI / a usable today

- A POI object is identified by `(generator index, poiPosID)`; a usable inside it
  by `(generator index, poiPosID, usableIndex)`. The *native* sender fills these
  only if the usable is flagged `setidPoi` (`DEC:L76599-L76613`), otherwise it
  sends the item form (`tosaveid`).
- Receiving a POI `usable` requires the local POI instance to exist with a live
  `pobj` and an in-range usable index; otherwise the message is silently
  dropped (`DEC:L5047-L5062`) — there is no repair request on the POI branch.
- There is **no** POI owner. Any peer may send type 40 for any POI it can see;
  the host forwards it to everyone else, applies it locally, and no peer checks
  whether the sender is the one currently interacting.
- Host `RecAskPoiData` answers a peer's request by running `pobj.SyncAll()`
  **directed at that peer** (`SetSendUnusualToOneReliable`, `DEC:L5210-L5223`),
  and `SyncAll` emits usables, attached parts and tanks
  (`DEC:L57164-L57197`). So the *baseline* POI state is host-sourced, while
  *incremental* POI changes are peer-sourced with no owner.
- Item-form type 41 (`fluid`) works; POI-form type 41 is parsed and discarded
  (`DEC:L5101-L5108`). POI tanks therefore have no working native update path.

### 3.4 How a guest interaction reaches the host and is applied exactly once

Today it is **not** applied exactly once by construction; it is applied once per
peer that receives it, plus possibly once more if the sender also has a local
native code path that already applied it. The actual chain for, say, a guest
opening a POI door:

1. Guest's local `usablescript` changes; `SyncMulti(true)` →
   `syncScript.s.SendUsable(poigenid, poiid, usableid, xRot, yRot, slideValue,
   currentTurnState, tunedFloat)` (`DEC:L76599-L76613`).
2. Guest transports it to the host only (`DEC:L91872-L91889`).
3. Host relays to all other members because `ForwardType(usable)` is true
   (`DEC:L91112-L91119`, `DEC:L92019-L92029`) **and** applies it locally via
   `RecUsable` → `UpdFromMulti` (`DEC:L5047-L5059`, `DEC:L76365-L76401`).
4. Each other guest applies it via `RecUsable`.

Duplicate suppression that does exist:

| Guard | Effect | Source |
|---|---|---|
| `UpdFromMulti` calls `FStart()` first and pushes absolute values (rotations, slide, turn state, tuned float), so a repeat is idempotent **in value** | repeated application converges to the last value | `DEC:L76365-L76401` |
| Roadworthy coalesces type 40 on the send path: while `turnState` is unchanged it goes unreliably as latest-value per usable, a turn-state change keeps the native reliable send, and a final reliable settle is sent after 0.3 s of quiet | turns per-frame door drags into a bounded stream, with no ownership meaning | `runtime/LiveUsable.cs:L10-L101`; hook at `runtime/StableGameBridge.cs:L811` |
| Roadworthy's `LiveUsablePolicy.TryRead` validates exact lengths (30 item / 54 POI) and computes an FNV identity key | malformed or unknown-shape type 40 is left on the native reliable path | `runtime/LiveUsable.cs:L26-L58` |
| `UnsetPaint` drops `color` messages whose RGBA is the "unset" value | skips a known no-op paint write | `runtime/StableGameBridge.cs:L1205-L1211` |
| Structural validation only | rejects malformed framing/NaN; explicitly **not** an authority check | `runtime/StablePackets.cs:L7-L10`, `L52-L119`; `runtime/StableGameBridge.cs:L1084-L1114` |

There is no event ID, no epoch, no per-object owner and no per-usable ordering
guarantee beyond the transport's reliability flag. Two peers dragging the same
door produce two interleaved absolute streams; convergence is by last arrival.

### 3.5 Time, radio, weather

| Surface | Who drives it | Source |
|---|---|---|
| Time of day | Host sends at world start and on join; `RecTime` applies `t - local` as a rotational delta, so it is a **correction**, not an assignment | `DEC:L4054-L4066`, `DEC:L42529-L42536`, `DEC:L90910-L90919` |
| Radio channel | Whoever sends; `RecRadioChannel` overwrites local channel arrays wholesale | `DEC:L4043-L4052`; producer `DEC:L38329` |
| Weather / sandstorms | **Not synchronised in multiplayer at all.** `WildlifeWeatherPolicy.SandstormsShouldRun` requires `!connected`; the Roadworthy sandstorm spawner is created only in single-player and removed when connected | `runtime/WildlifeWeather.cs:L36-L41`, `L130-L155` |
| Sandstorm authority inside a storm | none needed in MP because the spawner never runs; the native `sandStormSpawnScript` picks its own random offset and prefab rotation and applies local forces | `DEC:L61807-L61864` |
| Fog / sky / day cycle | no Roadworthy interception found in the files read; only time is synchronised | — |
| Native rabbit spawner | gated off while connected; MP population is a host-only Roadworthy spawner | `runtime/WildlifeWeather.cs:L25-L29`, `L89-L113`; `DEC:L6633-L6654` |

Because sandstorms apply wind forces to shared items locally, running them per
peer would desynchronise item state with no correction path. That is the stated
reason for the single-player-only gate (`runtime/WildlifeWeather.cs:L36-L38`).

---

## 4. Creatures

### 4.1 Roles today

| Role | Behaviour | Source |
|---|---|---|
| Host | Owns AI (`aiscript`) update, `sensoryscript` scans, attack windows, damage, death, ragdoll, item physics, native `aiState`/`aiSound` for `newAiScript` NPCs | `runtime/RabbitMetadataSync.cs:L136-L216`; `runtime/NpcVehicleImpact.cs:L40-L57` |
| Guest | Holds the canonical rabbit as a **kinematic, inert replica**: bodies kinematic, gravity off, collisions off, colliders disabled, animators selectively enabled, `moveDir`/`moveRot` zeroed, senses cleared, AI gated | `runtime/RabbitReplicas.cs:L160-L200`, `L252-L280`; guards in `runtime/RabbitNetwork.cs:L21-L50` |
| Guest animation | Visual-only: only the audited `nyul` controller, root motion and animation events forced off, `Attack`/`Roar` used as visual triggers | `runtime/RabbitGuestAnimation.cs:L60-L103` |
| Guest contact | One kinematic capsule proxy per living replica, driven from the authenticated pose buffer, colliding **only** with the local player's physical colliders via `ContactModifyEvent` | `runtime/RabbitContact.cs:L9-L20`, `L66-L82`, `L195-L262` |

### 4.2 Rabbit AI ownership and simulation gates

| Gate | Effect | Source |
|---|---|---|
| `RabbitAiGuard(aiscript)` | returns false on a guest while the replica is held → native `aiscript` body skipped | `runtime/RabbitNetwork.cs:L37`; `runtime/RabbitReplicas.cs:L266-L272` |
| `RabbitScanGuard(sensoryscript)` | guest clears `senses.stimuls` and skips scanning | `runtime/RabbitNetwork.cs:L38`; `runtime/RabbitReplicas.cs:L273-L279` |
| `RabbitRotGuard(movecreaturescript)` | guest blocks rotation and also runs `RabbitMotionGuard` on host | `runtime/RabbitNetwork.cs:L40` |
| `RabbitNyulGuard` / `RabbitTriggerGuard` | guest skips `nyulscript` update and `playerdeathtriggerscript` | `runtime/RabbitNetwork.cs:L41`, `L44` |
| `RabbitAttackGate` | `RabbitNyulGuard && RabbitCombatReach.AllowAttack` — attack additionally requires a resolved canonical target inside the native hit volume | `runtime/StableGameBridge.cs:L1334` |
| Host locomotion replacement | `RabbitLocomotion` takes over living motion on the host after the native AI update, with the native interest/target/flee/strike decisions retained | `runtime/RabbitLocomotion.cs:L56-L66`, `L156-L165`, `L176-L264` |
| Flee coroutine rebinding | the native flee coroutine is restarted through `RabbitFleeState` so a stale `fleeing` flag cannot wedge the AI | `runtime/RabbitAi.cs:L11-L69` |
| Kinematic velocity skip | `movecreaturescript` velocity writes on kinematic bodies are skipped and `moveDir` is cleared | `runtime/WildlifeWeather.cs:L45-L50`, `L160-L178` |

Guest move/animation gating therefore has two layers: the replica hold
(`GuestMove`/`GuestRot` zero the native fields, `runtime/RabbitReplicas.cs:L252-L265`)
and the visual layer (`RabbitGuestAnimation`, driven by the pose stream only).

### 4.3 Host → guest state paths

| Path | Wire form | Content | Source |
|---|---|---|---|
| Rabbit metadata | `node.PublishRabbitState(RabbitProtocol.WriteMetadata(...))`; version 1, `Removed=1, Alive=2, HasEars=4, HasEyes=8, Ragdoll=16`; length 10 (removed) or 62 | item id, prefab, flags, `size`, `health`, `shootHealth`, `explosionHealth`, `Holder`, `HolderSession`, `Lease`, `Request`, `Part` | `runtime/RabbitProtocol.cs:L20-L28`, `L125-L178`; `runtime/RabbitMetadataSync.cs:L116-L124`, `L136-L179` |
| Rabbit pose stream | type 251, 59 bytes | session, sequence, item, prefab, Active/Attacking/Roaring flags, global position, euler rotation | `runtime/RabbitMotionProtocol.cs:L4-L34`; sender `runtime/RabbitMotionRuntime.cs:L80-L115`; receiver `L44-L65` |
| Per-item pose/ragdoll (native types 16/39/43) | native | host `SendItemPosGlobal` + `SendRagdoll`, suppressed while the motion stream owns living poses | `runtime/RabbitMetadataSync.cs:L180-L202`; `RabbitMetadataSync.MotionStreamOwnsLivingPoses = true` at `runtime/StableGameBridge.cs:L1342` |

Guest apply is a single mutation block under
`RabbitReplicas.BeginMutation/EndMutation`: `size` (via `SetBasedOnSize`),
`health`/`shootHealth`/`explosionHealth`, ear/eye loss, `AI.StartDie()`,
`ragdoll.SetRagodol(true/false)` (`runtime/RabbitMetadataSync.cs:L232-L256`).

Careful reading of the flags: **bit 2 means ALIVE**, and the code comments say so
explicitly (`runtime/RabbitContact.cs:L191-L192`); `Apply` uses
`(meta.Flags & 2) == 0 && senses.alive && !wasDead → StartDie()`
(`runtime/RabbitMetadataSync.cs:L250`).

Tombstones: a `Removed`-flagged metadata packet (`Flags==1`) deletes the pending
and applied records only — it does not destroy the local object
(`runtime/RabbitMetadataSync.cs:L97-L100`, `L204-L215`).

### 4.4 Combat and damage

| Actor | Who computes damage | Source |
|---|---|---|
| Host car vs rabbit | host only: `AdmitRabbitCollision` returns false on a guest, dedups one impact per vehicle per `Time.fixedTime` (max 16 vehicles), then forwards to the native `breakablescript.OnCollisionEnter` | `runtime/NpcVehicleImpact.cs:L40-L57`, `L78-L95`, `L110-L128` |
| Guest weapon/melee vs rabbit | guest sends a `RabbitCommand` (type 1 Shot 54 B / type 2 Melee 66 B, monotonic `Token`, per-burst `Index`); host re-derives the burst, raycasts/overlaps with the **host's** physics, calls native `TryBreakShoot`/`TryBreak` | `runtime/RabbitProtocol.cs:L9-L19`, `L95-L124`; `runtime/RabbitCombat.cs:L228-L329` (shot), `L330-L401` (melee), `L402-L442` (client shot), `L443-L470` (client melee) |
| Host player vs rabbit | native local path, unrestricted | `DEC:L10272-L10348` |
| Rabbit vs remote player | host only, via `RabbitPlayers.ProbeDamage` → `Physics.ComputePenetration` against the remote actor's validated colliders → `Damage(trigger, contact)` | `runtime/RabbitPlayers.cs:L260-L308`; driven from `RabbitContact.HostTick` `L304`; native entry `runtime/StableGameBridge.cs:L1329` |
| Rabbit vs local player | native trigger | `DEC:L10264-L10270` |
| Guest replica "sees" host-only rabbits | guest weapon/melee raycasts are augmented by a private physics scene containing collider proxies | `runtime/RabbitQueries.cs:L31-L55`, `L230-L282`; bridge seams `runtime/RabbitQueries.cs:L284-L301` |

Idempotency in this path is real but narrow: the shot burst requires
`Token > st.nonce`, `Index == 0` to open a burst, `Index` strictly increasing
within a burst, one application per `Time.frameCount` per tool, plus an ownership
check `Owns(it, actor.Mp)` or a world-gun sanity check against `FrayRange`
(`runtime/RabbitCombat.cs:L240-L263`, `L283-L297`). Melee is once per frame per
tool with a `Token > st.nonce` gate (`L335-L344`).

### 4.5 Population and interest

| Rule | Implementation | Source |
|---|---|---|
| Only the host spawns | `RabbitSpawnGate` returns false for any non-host while connected | `runtime/RabbitPopulation.cs:L159-L168` |
| Spawn centres | host player + every ready peer's `syncScript.players` proxy within `sync.playerRemoveTime`, sorted by Steam ID for determinism | `runtime/RabbitPopulation.cs:L71-L99` |
| Limits | `spawner.maxAINum * centreCount` capped at `HardCap = 32`; per-centre near-count uses `spawner.maxDist * 2` as radius | `runtime/RabbitPopulation.cs:L10`, `L53-L70`, `L127-L142` |
| Admission inside the native spawner | `RabbitPopulation.Admit` checks the reservation counters during the native `TrySpawn` call window | `runtime/RabbitPopulation.cs:L106-L112`, `L143-L156` |
| Cadence | 0.5 s tick; per-centre next-time reused from `Random.Range(minTime, maxTime)` | `runtime/RabbitPopulation.cs:L11`, `L124`, `L154-L155` |
| Native spawner disabled in MP | `startedSpawner.started = false` before ticking the MP population | `runtime/WildlifeWeather.cs:L89-L96` |
| Legacy rabbit removal | when MP population is unavailable, the host destroys rabbit-prefab items | `runtime/WildlifeWeather.cs:L31-L34`, `L118-L127` |
| Guest streaming window | motion tracks capped at 256, scan 128 items/frame, 8 sends/frame, 0.1 s interval, 0.6 s stale | `runtime/RabbitMotionRuntime.cs:L15-L22`, `L99-L119`, `L158-L185` |
| Guest contact proxies | discovery bounded to active motion ids (≤ 256), 64 scans/frame, 0.6 s pose staleness | `runtime/RabbitContact.cs:L46-L47`, `L232-L261` |
| Host ↔ remote-avatar collision policy | host ignores collisions between canonical rabbit anim colliders and *remote player* colliders only, restoring only pairs it created | `runtime/RabbitContact.cs:L288-L335` |
| Navigation transitivity | remote-avatar proxies are ignored by host rabbit navigation raycasts | `runtime/RabbitContact.cs:L357-L362`; `runtime/RabbitLocomotion.cs:L385`, `L401`, `L417` |

There is **no distance-based unload of rabbits by interest**: population is
global (centres are players), and a guest's replica state is driven by the host's
metadata/pose stream for every rabbit the guest has as a live item. There is no
"far state" kept for an unloaded rabbit beyond the save definition itself.

### 4.6 Rabbit network-authority facts worth recording

| Fact | Source |
|---|---|
| `RabbitNetworkPolicy.AllowState` restricts native types `{16,17,18,19,29,33,36,42,43}` on **rabbit items** to host origin | `runtime/RabbitNetwork.cs:L21-L30` |
| Applied at both receive (`AllowIncoming`) and send (`RouteBroadcast`/`RouteTarget`) and again on journal replay (`Deliver`) | `runtime/StableGameBridge.cs:L1040`, `L1064`, `L1143`, `L1155`, `L1225` |
| This is the only owner-shaped native-message gate in the files read | — |
| Type 45 (`aiState`) is **not** in that set, and in any case targets `tosaveitemscript.npc` (`newAiScript`), not rabbits | `runtime/RabbitNetwork.cs:L21-L23`; `DEC:L73759` |
| Rabbit carry leases exist as bookkeeping only (`Timeout = 3.0 s`, watermark/pending, token, expiry) and the caller must validate geometry and do the pickup/drop | `runtime/RabbitCarryLeases.cs:L6-L114` |
| `RabbitCommandKind` declares `Grab/Hold/Drop` (3/4/5) but `RabbitCombat.Receive` dispatches only `Shot` and `Melee` | `runtime/RabbitProtocol.cs:L9`; `runtime/RabbitCombat.cs:L165-L171` |

---

## 5. Breakables, props and damage

### 5.1 Native breakable model

| Field / behaviour | Source |
|---|---|
| `health`, `shootHealth`, `explosionHealth`, `destroyed`, `noDestroy`, `noCollider`, `noTrigger`, `noShoot`, `sliceOnDestroy`, `sliceCuts`, `Gib`, `reducedGib`, `onlyDecreaseAfterAttackIfAbove(+Health)`, `decreaseAfterAttack`, `childs`/`breakchilds`, `AI`/`newAI` | `DEC:L10177-L10241` |
| `OnCollisionEnter`: computes `relativeVelocity.magnitude`, applies `noRBCollisionMultiplier` when the other body is null, honours `onlyDecreaseAfterAttackIfAbove`, calls `DamageFrom(contactPoint - position)` then `TryBreak(force)` | `DEC:L10272-L10297` |
| `OnTriggerEnter` → `TryBreak(attachedRigidbody)` | `DEC:L10264-L10270` |
| `TryBreak(float)`: `force >= health → Break()`, else if `decreaseAfterAttack → health -= force; BreakableUpd()` | `DEC:L10337-L10348` |
| `TryBreakShoot(float force, Vector3 dir)`: writes an `overRideStimul` for `AI`, then either `shootHealth -= force` / `Break()` or `TryBreak(force)` | `DEC:L10299-L10327` |
| `Break()`: zeroes the three healths, `BreakableUpd()`, ejects seated player, drops pickupable, `AI.Die()`/`newAI.Die()`, sets `destroyed = true`, **instantiates gibs only when `syncScript.IsHostOrSingle()`**, plays clips, disables reflection planes, optional `randomSlice.Slicc`, `Destroy(gameObject)` unless `noDestroy` | `DEC:L10371-L10460` |
| `BreakableUpd()` → `tosave.BreakableUpd()` → `SendBreakable(idInSave, health, shootHealth, explosionHealth)` | `DEC:L10471-L10477`; `DEC:L74825-L74831` |

### 5.2 Messages that mutate world state without an authority check

All of the following are forwarded by the host (`DEC:L91112-L91119`) and are
applied on every peer with no owner, epoch or lease test. Reliability differs —
`29` and `43` are in the unreliable set (`runtime/StablePackets.cs:L139-L147`),
`32/33/34/35/36/40/41/42/47` are reliable (`DEC:L91143-L91160`) — but reliability
is a transport property, never an authority one:

| Type | Mutation | Receiver | Source |
|---:|---|---|---|
| 32 `color` | writes `item.colors[i].color` and repaints | `RecSprayColor` | `DEC:L4643-L4662` |
| 33 `breakable` | overwrites three health fields, then `Upd()` may `Break()` → local `Destroy` + local slice | `RecBreakable` | `DEC:L4669-L4687` |
| 36 `removeItem` | `Object.Destroy(items[id].gameObject)`, unconditional if the ID exists | `RecRemoveItem` | `DEC:L4883-L4890` |
| 29 `push` | `pushablescript.PushLocal(dir, pos)` — an impulse the local physics then integrates | `RecPush` | `DEC:L4575-L4594` |
| 34 `dmgPlayer` | applies damage if the target Steam ID matches the local player | `RecDmgPlayer` | `DEC:L4697-L4704` |
| 35 / 47 sounds | play a clip (no state) | `RecSound` / `RecStepSound` | `DEC:L4711-L4715`, `DEC:L5391-L5395` |
| 43 `ragdoll` | writes child local poses **and** rigidbody velocities | `RecRagdoll` | `DEC:L5249-L5273` |
| 40 `usable` | door/window/switch state | `RecUsable` | `DEC:L5047-L5075` |
| 41 `fluid` | replaces tank contents | `RecTank` | `DEC:L5101-L5127` |
| 42 `attachable` | detach / re-attach | `RecAttachable` | `DEC:L5141-L5169` |

Note the asymmetry of 33 and 36: 33 is a *state assignment* (`health = hp`) while
36 is a *destructive command*. Both are forwarded and both apply everywhere.

`docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md:L30-L51` already records the same
absence for the item side ("no ownership check; the only guard is sender-side
`ThisOneInCharge()`/`ThisOneInChargeCar()`"); this inventory confirms it for the
world/POI/creature surfaces and adds the host `ForwardType` fan-out
(`DEC:L91112-L91119`).

### 5.3 How a client collision with a breakable prop is replicated today

Sequence, all source-pinned:

1. The guest's own car and the prop's local colliders are simulated locally by
   the guest's PhysX (the prop exists on the guest because the host manifest
   created it, `runtime/StableMap.cs:L991-L1013`).
2. Local `breakablescript.OnCollisionEnter` runs on the guest and decides damage
   from the **guest's** `Collision.relativeVelocity` and the guest's copy of
   `health` (`DEC:L10272-L10297`).
3. If it breaks, the guest runs the whole `Break()` body — including
   `sliceOnDestroy` and `Destroy(gameObject)` — but *not* the gib instantiation,
   which is host/single-only (`DEC:L10408`, `DEC:L10452-L10459`).
4. `BreakableUpd()` sends type 33 to the host with the guest's resulting
   absolute health values (`DEC:L10471-L10477`, `DEC:L74825-L74831`).
5. The host applies type 33 locally (`RecBreakable` → `Upd()` → possible second
   `Break()` on the host's copy) and forwards it to every other guest
   (`DEC:L91112-L91119`, `DEC:L92019-L92029`).
6. Other guests apply it, each running their own local `Break()` path.

So the current replication is **"local simulation, then broadcast the resulting
numbers"**, not "authoritative simulation, then replicate the event". The gib
prefab is the only part with an explicit role split, and the split is expressed
as `IsHostOrSingle()` inside `Break()`, not as a protocol rule.

### 5.4 Why two peers can break or destroy the same prop

Four independent mechanisms, all present in the code read:

1. **Both peers simulate the same collision.** Any peer whose own physics touches
   the prop runs the native break path locally
   (`DEC:L10272-L10297`, `DEC:L10264-L10270`). A prop between two cars breaks
   twice, from two different `relativeVelocity` values.
2. **No owner gate on 33 or 36.** `RecBreakable` assigns health and then calls
   `Upd()`; `RecRemoveItem` destroys. Neither consults who sent it
   (`DEC:L4669-L4687`, `DEC:L4883-L4890`). The host relay
   (`ForwardType`, `DEC:L91112-L91119`) forwards peer-originated breakables to
   other peers exactly like host-originated ones.
3. **Absolute health makes the last writer win and loses concurrent decrements.**
   Each peer decrements its own `health` from its own impact and then publishes
   the absolute value; two simultaneous hits by 40 and 30 against health 60 leave
   20 (one break) or 30 (other break) depending on arrival order, never 0 by
   addition (`DEC:L10337-L10348`, `DEC:L4669-L4687`).
4. **`Break()` is per-process idempotent but not cross-process idempotent.** The
   `destroyed` early-out (`DEC:L10373-L10376`) only guards the local object.
   After a local `Destroy`, a later type 33 for the same ID cannot re-break it,
   and a later type 36 finds no ID and (host only) may answer
   `SendRemoveItem` (`DEC:L5368-L5384` is the `askItemPos` analogue; `RecRemoveItem`
   itself is silent).

Separately, `DebrisContactFilter` suppresses *debris-vs-debris* contacts by
`Physics.IgnoreCollision` for up to 256 registered bodies, 2048 checks/frame and a
2 ms slice (`runtime/StableGameBridge.cs:L3210-L3290`). That changes contact
outcomes, so it must be identical on every peer for the same masses; it is a
performance/behaviour mechanism, not an authority mechanism.

### 5.5 The one-simulator rule this inventory argues for

Three distinct contact classes need an explicit rule, because each currently can
be computed twice:

| Class | Simulators today | One-simulator rule needed |
|---|---|---|
| car-vs-prop | every peer whose own car touches a local prop copy | exactly one peer (the simulating owner of the involved car, fallback host) computes the impact and the health delta; that peer emits a *damage event* with a monotonic id; everyone else applies the delta once and never runs the native impact path |
| player-vs-prop | every peer whose local player body touches the prop (walking into a fence, pushing a crate) | same: the peer that owns the acting player is the only simulator of that contact; other peers receive the resulting `push`/`use`/`damage` event |
| car-vs-car | both drivers' processes simulate the contact between the same two vehicles (outside this inventory's audited files) | one simulator per contact pair, chosen by role/lease, not by "who noticed first"; requires the same owner epoch the vehicle slice needs |

The rule has to be expressed as *who computes* (physics authority) and *how it
propagates* (event with identity), because the native code has neither: the only
role split expressed natively is the gib spawn
(`DEC:L10408`) and the host relay (`DEC:L90935-L90945`).

---

## 6. Authority proposal

Everything in this section is a **proposal**. None of it exists in the code read;
nothing here is a parity claim.

### 6.1 Host-owned world

| Surface | Proposal | Why (evidence) |
|---|---|---|
| Seed / map identity / `HostConfig` | Host, unchanged from today; additionally bind it to a world token that is not just the map object | guest already reloads from `RecMapStuff` (`DEC:L4140-L4177`); Roadworthy config is a 4-byte payload with no world id (`runtime/StableMap.cs:L105-L106`) |
| Road/POI/decoration manifests | Host stays the sole generator; keep `StableMap.Provide` | guest never runs `UpdGen` (`runtime/StableMap.cs:L595-L607`) |
| POI contents (items, fixed prefab, rolls) | Host stays the sole decider; add an explicit per-POI grant/completion record so a guest can tell "not spawned yet" from "empty" | today the guest simply sets `spawnedItems = spawnedFixed = true` and asks for data (`DEC:L57104-L57122`) |
| Doors / gates / usables | Host remains the *state* authority; a guest interaction becomes a **request** (idempotent, with owner + epoch + request id), and only the host emits the authoritative type-40 state | currently any peer emits absolute state and the host relays (`DEC:L76599-L76613`, `DEC:L91112-L91119`) |
| Time | Host only, keep `RecTime` delta semantics, but stamp it with the host session so a stale host cannot rotate a reconnected world | `DEC:L4054-L4066` |
| Radio | Host only; treat as world state, not player state | `DEC:L4043-L4052` |
| Weather / sandstorms | Remains single-player-only until a deterministic, host-driven storm model exists; if enabled later, storms must be host-spawned with a world-space id and no per-peer `Random` | today storms are gated off in MP (`runtime/WildlifeWeather.cs:L39-L46`) |

### 6.2 Creature leases with host fallback

- The **interested peer** (nearest ready peer within a configured creature
  interest radius, hysteresis on both edges) holds a lease on a creature and is
  the only peer that simulates it (`aiscript`, `movecreaturescript`, damage,
  death). Lease fields mirror the existing precedent: `Peer, Session, Request,
  Token, Deadline` (`runtime/RabbitCarryLeases.cs:L9-L22`).
- The **host** retains fallback authority for far state: metadata (size, three
  healths, ears/eyes, ragdoll, alive) is host-produced today
  (`runtime/RabbitMetadataSync.cs:L116-L124`) and should stay host-produced as
  the authoritative record, with the lease only choosing *who simulates*.
- Lease transfer requires: receiver ready, receiver has the item locally
  (`canonical` check as in `runtime/RabbitMetadataSync.cs:L125-L130`), and an
  epoch bump on every transfer and on every disconnect. Old-epoch packets are
  dropped.
- Guest replicas never become simulated objects implicitly; the current guest
  hold (`runtime/RabbitReplicas.cs:L160-L200`) already has the right shape for
  "this peer is not the simulator".
- Rabbit damage from a car must be computed by the lease holder, not by the host,
  if the host is not the simulating peer — otherwise the host's `Admit` dedup
  (`runtime/NpcVehicleImpact.cs:L78-L95`) governs a contact it did not simulate.

### 6.3 Breakables and props

Two acceptable shapes; pick one per object class and record it:

| Shape | Rule | When it fits |
|---|---|---|
| Host-authoritative | host computes all damage for the object; peers only apply damage events | props that are few, contested, or drive progression (POI structures, gates, vehicles' breakable shells) |
| Nearest-simulating-peer with a lease | one peer simulates contacts for the object and emits damage events with a lease+epoch; host takes over on timeout/disconnect | ordinary scattered props, where host-only simulation would require the host to simulate contact for objects it cannot see |

In both shapes:

- Health must be **event-sourced** (damage deltas with a monotonic per-object
  `eventId`), not last-writer absolute assignment. The current assignment
  (`DEC:L4669-L4683`) loses concurrent deltas.
- `removeItem` (36) must carry owner + epoch + the event that justified it, and
  must not be applicable to an object whose owner has changed. Today it is an
  unconditional `Destroy` (`DEC:L4883-L4890`).
- Gib/debris creation stays exactly where it is today (host/single) unless it is
  promoted to an explicitly replicated creation event. Today gibs are
  host-only (`DEC:L10408`) while slicing and destruction are everywhere
  (`DEC:L10452-L10459`), which already produces peers with equal destruction and
  unequal debris.
- `push` (29) and `ragdoll` (43) are one-off / replaceable respectively and should
  be routed by the same ownership rule, not left ungated.
- Sound types 35/47 stay cosmetic and ungated — but 35's Unity-space payload
  (`DEC:L4706-L4715`) must become global-space before it can be trusted across
  independent origin shifts.

### 6.4 Interactions as reliable, idempotent events with owner and epoch

Minimum envelope for every mutation in scope (world object **or** creature):

```
magic u32 | entityKind u8 | entityId u64 (or POI: gen u16 + poiId 3×f64) |
owner u64 | epoch u64 | eventId u64 | kind u8 | flags u8 | payloadLen u16 | payload
```

Rules:

1. `eventId` is monotonic per `(entityId, owner)`; a duplicate is a no-op.
2. `epoch` increments on every ownership transfer, world replacement and
   reconnect; a lower epoch is dropped without a repair request.
3. Mutations that are *state* (door angles, tank contents, paint) may be
   latest-value/coalesced — `LiveUsablePolicy` already shows the safe shape for
   doors (`runtime/LiveUsable.cs:L26-L101`).
4. Mutations that are *events* (break, destroy, push, attach/detach, shot,
   melee) are reliable, ordered per entity, and never coalesced.
5. Every event is applied through the existing native receiver body where one
   exists, so native side effects (sounds, seat ejection, `AI.Die()`, reflection
   planes) are preserved — the same "re-express, do not reimplement" rule the
   item inventory already states (`docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md:L102-L104`).

### 6.5 No dual simulation

The invariant to enforce: **for any (entity, instant) exactly one process
advances that entity's authoritative state.** Concretely:

- one simulator per breakable (owner or host),
- one simulator per creature (lease holder or host),
- one simulator per interactable (host),
- one generator per world cell (host).

The existing code has exactly one place that already satisfies this and can be
used as the pattern: `poiScript.SpawnStuff`'s role gate
(`DEC:L57104-L57122`), and the guest-side replica hold
(`runtime/RabbitReplicas.cs:L160-L200`).

---

## 7. Interest management: what a peer sees and what the host keeps

### 7.1 What a peer sees today

| Object class | Guest visibility rule | Source |
|---|---|---|
| Roads | per-chunk manifest, requested for `road.chunksNeeded` ± 1 cell ring | `runtime/StableMap.cs:L170-L174` |
| POIs | per-chunk manifest requested for `gen.chunksNeeded` | `runtime/StableMap.cs:L192-L193`; readiness wants ±1 ring around each player | `L679-L688` |
| Decorations (props/rocks) | per-cell manifest driven by `cat.chunksNeeded`, which is built from a draw-radius query inflated by anchor quantum and jitter | `runtime/StableMap.cs:L374-L430`, `L609-L628` |
| Terrain | local; guest clamps only visual radii and pins the collision grid | `runtime/StableMap.cs:L707-L756` |
| Rabbits | every rabbit the guest has as a live item; pose/metadata streams, no distance unload | `runtime/RabbitMotionRuntime.cs:L158-L185` |
| Smoke | cosmetic stream keyed by remote player, expired after 2 s | `runtime/SmokeProtocol.cs:L39-L56` |

### 7.2 Streaming distances and hysteresis, as configured

| Parameter | Value | Source |
|---|---|---|
| Minimum logical stream cell | 128 m; target `maxRange/8` clamped to 128…512 m and to ≤ 32 grid cells | `runtime/StableMap.cs:L61`, `L296-L312` |
| Stream ring edge | `ceil(drawDistance/cellSize + 1.5) * 2`, plus anchor quantum inflation `drawDist + quantum*0.71` | `runtime/StableMap.cs:L324-L331`, `L404-L406` |
| Need re-pass cadence | once per second per unchanged needed-list, sliced by `ScanBudget.KeysPerCall` | `runtime/StableMap.cs:L619-L627` |
| Guest cell eviction | `EvictGate` on `(chunksNeeded, count, generatedChunks count, time)`; eviction refused while a placed object is still live or a tombstone is pending | `runtime/StableMap.cs:L632-L665`, `L527` |
| Departed stream cells | cancelled from `available` and from the transport (`node.CancelMap`) | `runtime/StableMap.cs:L431-L439` |
| Incomplete POI retry | 5 s, max 3 refreshes, then treated as non-authoritative | `runtime/StableMap.cs:L82`, `L119-L135` |
| Manifest cache | 32 cells, LRU by use counter, invalidated on proximity-cache invalidation | `runtime/StableMap.cs:L35`, `L555-L571`, `L266-L272` |
| Rabbit motion | 0.1 s interval, 0.6 s stale, 256 tracks | `runtime/RabbitMotionRuntime.cs:L15-L16` |
| Rabbit contact pose | 0.6 s stale tolerance before the proxy is destroyed | `runtime/RabbitContact.cs:L47` |
| Join clearance | 1.5 m rings to 15 m, 1.0 s settle, 8 s give-up, 0.5 s retry | `runtime/SpawnClearance.cs:L14-L21` |

Hysteresis in this code is implicit (cells are only dropped when the whole ring
changes; contacts hold for 0.6 s; the 3 ms/2 s aging rule in the coordinator)
rather than an explicit enter/exit distance pair.

### 7.3 What the host keeps for unloaded objects

| Class | Host-side dormant record | Source |
|---|---|---|
| Decorations | `categoryClass.generatedChunks`/`objs` records with `place=false`, plus `categoryClass.destroyed` tombstones | `runtime/StableMap.cs:L464-L474`, `L666-L670`, `L528-L529` |
| POIs | `poiGenScript.chunks[].pois` records persist independently of the live `pobj`; `poisFromSave` holds saved records not yet re-added | `DEC:L56405`, `L56447-L56466`; capture loop `runtime/StableGameBridge.cs:L1739-L1745` |
| Items | `savedatascript.data.itemData` definitions + `save.items` for live instances; the capture path copies definitions and an `Active` flag | `runtime/StableGameBridge.cs:L1733-L1738` |
| Relocation | `WorldRelocationLedger` records for relocated POIs (kind 1) and decorations (kind 2), max 4096, persisted in the save version string | `runtime/WorldRelocationLedger.cs:L18-L24`, `L71-L75` |
| Rabbits | live item definition only; no separate far-state record | `runtime/RabbitMetadataSync.cs:L136-L179` iterates `d.items` |

### 7.4 Late join

1. Guest joins → `mapStuff` (+`HostConfig`) → scene reload
   (`DEC:L4140-L4177`, `runtime/StableMap.cs:L105-L106`).
2. Guest requests `askStartStuff`; the host answers with the world snapshot
   (`runtime/StableGameBridge.cs:L1783-L1796`).
3. Snapshot content: item definitions, per-item `Active`, POI `save_poi` records,
   and staged player/item messages (types 10/17/18 only)
   (`runtime/StableGameBridge.cs:L1728-L1763`, `L1900-L1911`).
4. Guest applies: destroys definitions it should not have, rebuilds the item
   table, spawns/loads each active item, loads POI state into the local POI or
   `poisFromSave`, adds `frame.Time - local` to the day cycle, then replays the
   staged messages through `Deliver` (`runtime/StableGameBridge.cs:L1912-L1936`).
5. Guest then requests manifests until `Ready()` (`runtime/StableMap.cs:L785-L800`).

Deterministic ordering note: the snapshot replays through `Deliver`, which
re-applies `ValidateData`, `AllowRabbitState`, `UnsetPaint`, cargo observation and
`AllowNativeSafetyPacket` — the same gates as live traffic
(`runtime/StableGameBridge.cs:L1222-L1240`).

### 7.5 Unload / reload and duplicate reconciliation

| Risk | Current handling | Source |
|---|---|---|
| Stale manifest reintroducing metadata | decoration reply dropped if the cell is no longer needed | `runtime/StableMap.cs:L938` |
| Duplicate manifest replacing live-linked records | acknowledged, not replaced | `runtime/StableMap.cs:L942-L943` |
| POI manifest with missing POIs deleting local POIs | guarded by `PoisComplete` | `runtime/StableMap.cs:L975-L980` |
| Saved decoration destroyed while unloaded | tombstone written on eviction when `saveDestroy && placed && obj == null` | `runtime/StableMap.cs:L651-L654`, `L666-L670` |
| Relocation conflict between peers | throws; chunk refused | `runtime/StableMap.cs:L908-L918` |
| Duplicate guest spawn transaction | `(session, sequence)` + hash + committed watermark | `runtime/StableGameBridge.cs:L3633-L3664` |
| Duplicate native message | **none** (no de-dup on 33/36/40/41/43) | `DEC:L4669-L4890` etc. |
| Late item message for a removed item | `itemRecovery.Deleted(id, now)` is consulted by the spawn ledger only | `runtime/StableGameBridge.cs:L3687` |

---

## 8. Acceptance tests

Static list only — no test was executed for this document. Each test names the
observable to compare, not an implementation.

| # | Scenario | Setup | Must observe |
|---|---|---|---|
| 1 | Separated players | two peers ~2 km apart, each walking | each peer's cell manifests stay bounded; no manifest thrash when a player sits still; `incompletePois` never exceeds one entry per stuck cell; host `Provide` cost per cell stays under the configured budget |
| 2 | POI interaction | guest opens/closes a POI door and a gate, both while the host is 3 km away | host ends with the same `xRot/yRot/slide/turnState`; no second application on any peer; a duplicate delivery of the same type 40 changes nothing |
| 3 | Contested spawn | two guests create items in the same second; host announces IDs concurrently | exactly one object per ID; no `spawnCommitted` regression; refused transactions reported with a reason, not silently dropped |
| 4 | Breakable hit by a client car | guest drives into a POI fence while the host is elsewhere | one break event, one damage total, identical remaining health on all peers; gibs exist only where the rule says; the object is destroyed exactly once per peer and re-delivery of the break is a no-op |
| 5 | Concurrent double hit | two peers hit the same breakable in the same second with forces that individually break it | one break; health never goes negative on any peer; no "second break" log |
| 6 | Creature attack/death | host-owned rabbit attacks a remote player; then a guest shoots it dead | remote player takes damage once; `StartDie` runs once; ragdoll flag consistent; the guest's contact proxy disappears; a stale pose packet cannot animate a new rabbit with the same ID |
| 7 | Unload/reload | host walks 3 km away and back past a POI that was partly looted | identical POI state before/after; no duplicate props; tombstones respected; relocation ledger unchanged |
| 8 | Late join | join while a construction job and a rabbit death are in flight | the snapshot is complete or refused with a reason (`CompletePendingConstruction` must not silently drop a group); the joiner converges to the same item/POI/creature state |
| 9 | Save/reload | save mid-spawn, mid-break, mid-carry; reload; rehost | saved definitions and POI state match the pre-save world; no resurrection of destroyed decorations; relocation ledger round-trips; a guest save cannot overwrite host state |
| 10 | Host loss | host drops during a POI interaction, a creature lease and an in-flight spawn transaction | guests stop applying host-epoch events; the unacknowledged spawn transaction is retried or reported, never half-applied; creature state does not fork into two simulators; a reconnect gets a new epoch and old packets are rejected |
| 11 | Origin shift | force a recentre on one peer only, then exchange item/car/POI/sound updates | all global-space messages land correctly; type-35 sounds are the known exception to verify |
| 12 | Weather | host and guest both enabled with storms on | no storm spawns while connected; item state does not drift from unsynchronised wind |

Explicit non-goals for these tests: they must not be read as parity gates, and
they must not be reduced to "both peers look the same on screen".

---

## 9. Open questions

Marked explicitly; none of these is answered by the files read.

1. **Whether the IL patch actually wraps `breakablescript.OnCollisionEnter` for
   non-rabbit breakables.** `NpcVehicleImpact` installs a
   `RabbitVehicleImpactReceiver` only for `ragdollactivatescript` +
   `nyulscript` (`runtime/NpcVehicleImpact.cs:L16-L38`). For a plain prop, the
   files read show no interception, so the impact is fully local.
2. **Whether `ForwardType` fan-out is ever suppressed by Roadworthy.** The bridge
   gates send/receive for rabbit items and for structural validity, but no gate
   was found that stops the host relaying a peer-originated `breakable`,
   `removeItem`, `usable` or `ragdoll`.
3. **What `poiScript.SpawnStuff` does when a POI is spawned by a peer that is not
   the host and the placement version is < 4** — the guest path sets both
   `spawned*` flags without content, and `PoiAdd` allows the native add on
   guest for version < 4 (`runtime/StableMap.cs:L194-L196`,
   `DEC:L57104-L57122`). Whether that can produce a guest-authored POI that the
   host never accepts is unverified.
4. **Whether a guest can be asked for POI data.** `RecAskPoiData` runs on any
   peer; only the host is expected to answer, and nothing rejects the request on
   a guest (`DEC:L5210-L5223`).
5. **Whether `CancelUnstarted(source)` is ever called.** No call site was found
   in `runtime/`.
6. **Whether the terminal failure registry can latch for a whole session.**
   `terminalLatched` refuses all further admission once 256 non-evictable
   terminal records accumulate (`runtime/SpawnCoordinator.cs:L423-L484`); whether
   that is reachable in a normal long session is unmeasured.
7. **How `Toast`-class creatures (`newAiScript` NPCs) are meant to be
   authoritative.** Native `aiState`/`aiSound` exist for them
   (`DEC:L5275-L5358`) and there is no Roadworthy analogue in the files read;
   whether they are simply unsupported in MP is not documented here.
8. **Whether the vehicle slice already provides an owner epoch that creatures and
   breakables could reuse.** `CarOwnership.cs`, `BusRearAuthority.cs` and
   `ExperimentalAuthority.cs` were not audited in this pass.
9. **Whether type-35 `soundplay` is used with non-trivial positions in practice.**
   The signature is Unity-space while every other positional message is
   global-space; no call site was found in the decompile, so the practical impact
   is unknown.
10. **Whether POI tank state has any working replication path at all.** Native
    POI-form type 41 is decoded and discarded (`DEC:L5101-L5108`); no other path
    was found.
11. **How `DebrisContactFilter` affects contact outcomes across peers.** It
    applies `IgnoreCollision` between registered debris bodies
    (`runtime/StableGameBridge.cs:L3262-L3279`); identical registration order and
    capacities are required for identical physics, and that ordering is
    registration-driven, not synchronised.
12. **Whether a guest's locally destroyed prop can be resurrected by a later
    manifest reply.** Decoration manifests skip tombstoned records
    (`runtime/StableMap.cs:L1008-L1010`) and the bridge cache is cleared on
    invalidation, but a live object with `obj != null` is not skipped, so a reply
    arriving before the destroy propagates is a candidate path.

---

## Appendix A — Message-class summary for this phase

| Surface | Types | Native authority | Roadworthy layer |
|---|---|---|---|
| world config | 8, 9 | host-origin by convention only | `HostConfig` 4-byte side payload |
| world manifests | (Roadworthy-only transport keys `r|…`, `p|…`, `o|…`) | host-only by code | `StableMap.Provide`/`Apply` |
| item/car pose, velocity | 16,17,18,19,20,39 | sender-side `ThisOneInCharge*` | vehicle motion codec, cargo re-expression |
| POI/usable/fluid | 40,41 | none | door coalescing (`LiveUsable`) |
| breakable/prop mutation | 29,32,33,36 | none | none |
| player damage | 34 | target-ID equality only | none |
| sounds | 35,47 | none | none |
| ragdoll | 43 | none (rabbit items: host-only via `RabbitNetworkPolicy`) | `RabbitReplicas` hold, metadata |
| AI (non-rabbit) | 44,45 | none | none |
| attach/store | 42, 17 | none | `PendingAttach`/spawn ledger verify |
| time | 14 | host sends | none |
| radio | 15 | none | none |
| creature state (rabbits) | none native | — | metadata message, motion type 251 |
| smoke (cosmetic) | 252 | — | `SmokeCodec` + relay sequence |
| vehicle motion envelope | 253 | — | `VehicleMotionCodec` |

Type numbers 251/252/253 and the manifest keys are Roadworthy transport
extensions; the numeric values are defined in
`runtime/RabbitMotionProtocol.cs:L11`, `runtime/SmokeProtocol.cs:L10`,
`runtime/StablePackets.cs:L187`.
