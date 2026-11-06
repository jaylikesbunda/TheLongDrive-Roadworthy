# RevaMP lessons for Roadworthy multiplayer

Source review: RevaMP commit `06f79a2cfa29806408600842d5c3ff33f30aae00`, Roadworthy commit `451c98e7` plus the working tree. This is not an independent playtest. RevaMP's architecture document is a proposal containing historical milestones; its current code may differ. Its reported outcomes are not independently verified.

Source: [pinned RevaMP repository](https://github.com/Normal99/TLD-RevaMP/tree/06f79a2cfa29806408600842d5c3ff33f30aae00), particularly [multiplayer architecture](https://github.com/Normal99/TLD-RevaMP/blob/06f79a2cfa29806408600842d5c3ff30aae00/docs/MULTIPLAYER-ARCHITECTURE.md). Paths below are in that revision.

## Spawn once; do not hide duplicates

RevaMP initially hid a locally generated matching car. Its notes say the heuristic created edge cases and was replaced by server-mediated building-content spawn leases. The notes identify a time-dependent start car and unseeded building contents: a seed alone is insufficient. See architecture §3e–3f and `src/TLDRevamp/Net/Entities.Leases.cs`.

Roadworthy: inventory POI creation and native spawn side effects, then implement host-controlled, idempotent spawn decisions keyed to a stable world and POI. Define grant, completion, failed spawner and retry. Prefer explicit completion to a timed capture window; test simultaneous arrival, spawner disconnect, save/restart, nearby identical cars and new worlds with identical coordinates.

## Entity identity survives streaming

`Entities.FarStore.cs` updates records when shared objects have no live GameObject. The historical notes describe previously losing shared-object bindings on stream-out, allowing apparent duplicates.

Roadworthy: registry states must cover loaded, dormant, pending and deleted objects, with parent relationships, save records, chunk indexes, remapping and tombstones. Test changes while the host has an object unloaded, arrival, save/restart and late join. Compare existing `ItemStreamPolicy.cs`, `StableMap.cs`, spawn and attachment recovery before reusing them.

The historical proposal describes using the same save and network IDs; current `Entities.cs` describes server-issued network IDs mapped to local IDs. Do not assume the older proposal describes current code. Our separate network ID is a proposal requiring verified remapping of every embedded reference.

## Ownership handoff follows the game's lifecycle

`Entities.Handoff.cs` describes earlier proximity thresholds leaving an object owned by a player whose game had already stored it. Current code uses freeze/unfreeze thresholds for hysteresis and excludes driven, held, stored, worn and some crash/part cases.

Roadworthy: measure the supported game's configured streaming distances and state. Require the recipient and dependencies to be ready, use ownership epochs, and specify recovery when either player disconnects. Held and worn items follow the player; attached groups transfer coherently. Test threshold oscillation, unavailable receiver, simultaneous claims and old-owner packets. Our proposed freeze/ack transaction is a design to validate, not a description of RevaMP's implementation.

## A vehicle includes cargo, seats and collision consequences

`Entities.Cargo.cs` documents a takeover failure: a newly driven car contained kinematic copies of another player's items, with stored objects and respawned copies escaping the initial claim. RevaMP added claims on takeover and replacement and periodic reconciliation. Architecture §3g also explains why passengers must be positioned relative to the local car/seat replica.

Roadworthy: define vehicle, passenger, attachment, stored and loose cargo authority together. Reconcile on takeover and stream-in; do not steal held/worn objects using a bounding-box guess. Test contention, passengers, nested storage, loose and held items, crashes and re-placement. Compare item graphs and damage across peers. Route `CarCargo.cs`, `CarOwnership.cs`, `MovingVehiclePassenger.cs`, bus and smoothing behavior explicitly by mode.

## Replica physics needs explicit interaction rules

The architecture notes report replica parts detaching due to local physics consequences. RevaMP has additional handling in `Entities.CrashAuthority.cs`, `Entities.Contact.cs`, `Entities.PhysLock.cs` and `Entities.Cargo.cs`. Kinematic replicas can also impart unrealistic forces on locally simulated objects.

Roadworthy: specify who computes collisions, damage, detach, push and player contact, including two different owners' cars and disconnect during a crash. Test outcomes exactly once on both peers, late join and reload; visual smoothness alone is insufficient. Preserve native local side effects when suppressing network effects.

## Saves and sidecars follow the actual world

`Entities.Leases.cs` describes a new world inheriting the previous world's leases and a case where sidecars rotated when the save did not.

Roadworthy: associate metadata with an explicit world and save revision, not simply the last save name. Design crash-consistent save/metadata commits or detect incomplete writes and recover. Test new-world-after-load, save-as, copied saves, skipped autosave, rotation, interrupted writes, backup restore and rehost. Keep guest saves protected. A sidecar alone is not an atomic save strategy.

## Steam and runtime compatibility

`SteamCompat.cs` reports an interface-version mismatch between the game's Steamworks.NET and older native Steam DLL, with a reported `CreatePollGroup` crash. Roadworthy already uses Steam Networking Sockets in `runtime/SteamSdrTransport.cs`; inspect the actual DLL and existing handling before adding API calls or adopting a workaround. Probe in an isolated environment; do not blindly replace Steam's global context. The notes also report a Mono compatibility failure with `ConditionalWeakTable.GetOrCreateValue`. Compile with Roadworthy's toolchain and verify in the real game runtime.

## Determinism and origin shifts

RevaMP reports timing-dependent scenery and describes one process-wide floating origin. Its historical sections cite differing shift distances, so measure the supported build instead of hard-coding a number. Agree on seed, starting options, build/schema and content, plus authority for random spawns. Compare generated output on both peers. Test global coordinates across independent origin shifts and audit Roadworthy terrain and placement modifications.

## Test scenarios to adapt

RevaMP's `tools/README.md` describes seeded real-game scenarios, two-machine automation and protected saves. Inspect assertions and setup in `determinism.py`, `handoff.py`, `takeover.py`, `invsync.py`, `copylock.py`, `leasepersist.py`, `leaseworld.py`, `farworld.py`, `joinfar.py`, `latejoin.py`, `crashlease.py`, `dropout.py` and `joinersave.py`. They target Linux/Proton and SSH; adapt the scenarios to Roadworthy's harness and real Steam tests rather than assuming they are drop-in. A filename does not prove coverage.

Roadworthy starting points include `tests/scripts/Test-Stability.ps1`, `Start-Loopback.ps1`, and `CarOwnershipTests.cs`, `CarCargoTests.cs`, `PendingAttachTests.cs`, `SpawnTransportTests.cs`, `StableJournalTests.cs`, `WorldSerializationTests.cs` and `SteamJoinSimulationTests.cs`. Add cases above to the parity matrix with fixture, expected behavior, evidence and status. Distinguish loopback from two-machine results.

## Implementation gate

During the coordinated pass, map each applicable lesson to a Roadworthy owner, implementation file and acceptance test. Mark unknown or unsupported behavior explicitly; don't claim parity from source review. Audit existing Roadworthy fixes before duplicating them. Treat proximity matching, timed spawn capture, optimistic claims, cargo bounds and sidecars as heuristics or mechanisms requiring failure tests, not automatic solutions.

RevaMP is LGPL-3.0: review license compatibility, attribution and distribution obligations before copying or adapting code. Independently deriving test cases or design ideas is distinct from copying implementation.
