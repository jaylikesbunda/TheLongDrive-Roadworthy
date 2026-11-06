# Experimental multiplayer: implementation and parity plan

Status: first integrated slice implemented 2026-11-05. Baseline re-checked 2026-11-05: Roadworthy commit `3f5352df22fe1fc7f3c2e8fe1be0a8e2d8e79492`. `runtime/ExperimentalBackend.cs` is in the normal build's explicit source list; the remaining `Experimental*.cs` prototypes are still excluded. The installed test build offers native (default) and an experimental backend that reuses the proven native relay for session/join/players/items and adds host-issued vehicle-motion ownership with epochs. Full parity is not claimed. Supported game assembly is the hash pinned by `Build-Patch.ps1` (2024.11.26b_test). Re-check every hook against that assembly before coding. RevaMP comparison is pinned to Normal99/TLD-RevaMP commit 06f79a2cfa29806408600842d5c3ff30aae00.

## Product decisions and release boundary

Keep patched native multiplayer as the default. An explicitly selected experimental session uses a distinct, incompatible protocol; all participants must select it before hosting or joining. Never switch modes in an active session. First experimental releases are private development builds with isolated copies of saves, not public replacement builds. Normal mode retains its existing behavior and regression tests. Do not expose a public mode option until session isolation, rejection and protected-save gates below pass.

A selectable rewrite is a substantial project: the file map is a concrete implementation proposal, not proof that all game call sites have been found. Phase 0 inventories those call sites; update this plan with exact method signatures, ownership, and test evidence before implementing each category. Open decisions include whether the experimental protocol shares the current connection object or uses another virtual port; and how native world-save records represent entities simulated far from the host. Prototype and verify those before finalizing wire compatibility.

## Existing integration points

- `tools/PatchAssembly.cs` and `tools/PatchStability.cs` inject callbacks in the supported game assembly. Add a narrowly scoped, mode-aware bridge callback at verified call sites, retaining original behavior for native and single-player. Do not bypass native calls globally without mapping their non-network side effects.
- `tools/VerifyPatch.cs`, `tools/VerifyStability.cs`, `tools/CheckRuntimeCallbacks.cs`: extend independent verification for each new hook, signature, guard, original fallback and unchanged baseline method. Document any hook's expected IL shape; fail closed on mismatch.
- `runtime/StableGameBridge.cs`: `Reset`, session creation, `SendEnvelopeCore`, native send/receive dispatch, `CaptureWorld`, `SendWorld`, `ApplyWorld` and per-frame lifecycle currently tie game state to `StableNode`. Add a mode router and lifecycle calls; leave native implementations intact. Audit all partial `StableGameBridge` files for behavior that assumes a native node.
- `runtime/StableCore.cs`, `StablePackets.cs`, `StableMap.cs`, `StableRoadCodec.cs`: existing handshake, native forwarding, snapshot/journal and map support. Share only extracted, tested utilities whose semantics fit; do not assume the native join journal or native payloads establish experimental authority. Avoid changing existing wire numbers and native handshake.
- `runtime/SteamSdrTransport.cs`, `NetDeliveryScheduler.cs`, `LoopbackTransport.cs`: evaluate extracting transport-neutral send/receive interface and lane scheduling, preserving membership checks, bounded queues and transport lifecycle. Isolate session identifiers and message framing; no raw native packets are interpreted as experimental packets.
- `runtime/PatchOptions.cs`, `PatchOptionsMenu.cs`, `MenuLayout.cs`: eventually persist and present mode selection outside native serialized settings. Default missing/corrupt values to native; lock selection for session lifetime. Until verified, use a development-only launch switch with an unmistakable log/banner.
- `tests/scripts/Test-Stability.ps1`: add every new runtime source to its explicit `$signatureInputs` and applicable compile/test commands. `Build-Patch.ps1` derives both runtime sources and handshake signature from that list. Verify all alternate test, installer and packaging build paths too. Retain compatibility with the Windows .NET Framework compiler used by the build.
- `Start-Loopback.ps1` and `tests/Loopback*`, `SteamJoinSimulationTests.cs`: extend test harness with explicit isolated modes and fault injection; real two-account Steam tests remain mandatory.

## New modules and contracts

Proposed files under `runtime/Experimental/` (adjust paths only with corresponding source-list updates):

1. `Mode.cs`: Native/Experimental and immutable session state. Selection is captured before lobby admission and reset only after disconnect and cleanup. Single-player never enters experimental authority.
2. `Protocol.cs`, `Codec.cs`, `Session.cs`: distinct magic, protocol and feature version, build/content fingerprint, session nonce and authenticated transport identity. Bounded lengths and decoding; handshake validates mode and capabilities before any world mutation. Host rejects mismatch with reason. Reliable control/events, sequenced disposable motion, and bounded bulk snapshot traffic; validate sender role for every operation.
3. `Registry.cs`, `Identity.cs`: host-issued entity ID independent of native `idInSave`, bidirectional local mapping and lifecycle states (unseen, pending, active, stored, tombstoned). A session nonce and generation prevent old packets resurrecting deleted or replaced objects. Register root plus dependent parts; persist stable identity or define deterministic restoration and migration explicitly in phase 0.
4. `Authority.cs`: host-only grants, owner, monotonic epoch and lease state. Transfer request -> freeze old writer -> capture final state -> host validates and commits new epoch -> apply baseline on new owner -> acknowledge -> resume. Reject stale epochs; idempotent duplicate requests; timeout/disconnect reconciles from last committed state. Resolve simultaneous claims deterministically. Do not treat a received grant as proof that local construction completed.
5. `Snapshot.cs`, `Journal.cs`, `Persistence.cs`: coherent host snapshot at revision R plus ordered changes after R; bounded chunking, integrity and retries; apply only after dependencies exist. Store tombstones and distant entity records. Before save, collect or reconcile active owners and establish a defined durable cut; on timeout abort save with visible reason rather than silently discard objects. Protect guest saves and keep backups. Design crash recovery and atomic persistence before enabling this mode for real saves.
6. `PlayerReplication.cs`, `VehicleReplication.cs`, `ItemReplication.cs`, `WorldReplication.cs`, `CreatureReplication.cs`: category adapters, not additional independent authorities. Each captures events, applies only valid current-epoch data and suppresses echo while applying remote events. Global double-precision coordinates at the boundary; explicit origin conversion on each machine.
7. `Diagnostics.cs`: per-session mode, revision, ownership/epoch transitions, last accepted sequence, queue pressure, rejected reasons, pending dependencies, save transaction and cleanup counters. Avoid personal identifiers in shared reports; bound logs.

The registry, protocol and authority logic should be pure C# where possible; game adapters should be thin. Connection sharing requires proving that lifecycle, admission and message demultiplexing remain isolated. Reuse existing transport primitives, not the assumption that `StableNode` is a new authority engine.

## Phase 0: discovery, parity inventory, and shadow observation

- Enumerate native `msgType`, `syncScript.Send*` and `Rec*`, call sites, receive paths and `tosaveitemscript` authority checks from the supported local game assembly. Record exact signature, trigger, affected fields, reliability, IDs, ordering/dependencies, save representation and whether the method has local side effects. Map every category to existing Roadworthy hooks and tests, especially `CarOwnership`, `CarCargo`, `PendingAttach`, `SpawnCoordinator`, `Rabbit*`, `RemoteSmoothing`, and `World*`.
- Produce `docs/MULTIPLAYER_PARITY_MATRIX.md`: one row per observable action and lifecycle case, with native baseline, Roadworthy, experimental status, host/guest evidence and test link. Mark unverified/unsupported explicitly. Include environmental features and Roadworthy additions, not only original packet types.
- Add a development-only observer using proposed `Registry.cs` and `Diagnostics.cs`; it logs hypothetical entities, ownership and transitions without sending packets, changing physics, suppressing native hooks or writing saves. Compare to two-instance captures. Exit: native regression suite unchanged; observer disabled has no gameplay effect; identity and lifecycle gaps documented.
- Establish protected fixture saves and record reproducible host/guest baseline, including dropped/reordered packets, host and guest as driver, late join and reload. Save hashes and object counts/graphs before and after; record actual evidence separately from intended tests.

## Phase 1: mode and session isolation

Implement `Mode`, `Protocol`, `Codec`, `Session` and adapter to the selected transport. Guard at `StableGameBridge` send/receive and lifecycle boundaries, after verifying exactly which native messages are needed for lobby bootstrap. Discovery must identify mixed-mode peers without allowing an experimental world to load; the mode must be independently checked in the authenticated handshake, not trusted from lobby metadata. Fail closed on unknown mode/version/build/content, malformed frames, unexpected peer, replay or timeout. Reset all queues, temporary objects, callbacks and reservations on disconnect.

Tests: default/native/single-player unchanged; native-to-experimental joins rejected both directions; version/content mismatch; unauthorized peer and malformed/bounded packets; rehost and reconnect; no gameplay state before handshake; both transports and mode cleanup. Extend verifier for every injected path. Exit: two real Steam accounts exchange only experimental control messages in a protected test world without affecting native gameplay.

## Phase 2: players and world baseline

Add `PlayerReplication` and explicit world seed/config bootstrap. Preserve independent floating-origin conversion, character lifecycle, player state and seats. Start with non-interacting player replicas; no shared vehicles or persistent interactions. Prevent joining from overwriting guest saves, and prevent native player synchronization from applying to experimental replicas. Add pose sequence and interpolation tests including origin shifts, long distance, delay, reordering, late join and disconnect. Exit: both hosting directions and separation/reunion demonstrated on two machines, with native mode regression green.

## Phase 3: one complete vehicle slice

Implement identity, registry, authority and `VehicleReplication` for one known fixture vehicle, using game save/load records for construction only after verifying completeness and local ID remapping. Spawn full dependency graph before applying position or interactions. The host allocates ID; driver simulates; other copies are controlled replicas. Explicitly handle seat membership, wheel visuals and origin, collision ownership, one-driver arbitration and movement sequences. Scope native suppression to the registered vehicle and verified call sites; preserve local side effects. Add `tests/ExperimentalAuthorityTests.cs`, `ExperimentalRegistryTests.cs`, `ExperimentalVehicleReplayTests.cs` and two-instance scenarios.

Exit: host/guest drive, passenger, handoff, simultaneous claims, owner disconnect, packet loss, stationary and moving collisions, late join, long drive and origin shift; exactly one active simulator and one logical vehicle; no old-epoch writes. Phase 3 is not gameplay parity: block unsupported mutations or mark the build test-only.

## Phase 4: object graph and interactions

Extend `VehicleReplication` and `ItemReplication` for part attach/detach, damage, fluids, fuel, doors, loose and stored cargo, pickup/drop/throw, containers, painting, wear and seat changes. Define reliable idempotent event IDs and causal dependencies; buffer boundedly until parent/child exist, request repair or fail explicitly on expiry. Distinguish a mounted local pose from free world pose. Reuse existing native record codecs and `PendingAttach` ideas only after proving their identifiers and semantics fit. Audit and route Roadworthy's `CarCargo`, `CarOwnership`, `MovingVehiclePassenger`, `BusPair`, and smoothing paths by session mode so neither applies twice.

Tests must cover two people claiming an object, detach during motion, delayed parent, reorder/duplicate, container nesting, retries, replica collision and disconnect. Check actual object graph and item counts, not just visual position. Extend fixture to buses, trailers, nested containers and modded objects only after the simple slice passes.

## Phase 5: shared world, AI and persistence

Implement `WorldReplication` and `CreatureReplication` for POIs, doors, world spawns/removals, usable state, weapons, combat, wildlife, weather, radio and time. Inventory exact game hooks and existing Roadworthy `Rabbit*`, `WildlifeWeather`, `Smoke*` and related code first; select exactly one owner for each category. Design per-region interest, initial state on entering interest, durable far-state storage and deterministic duplicate reconciliation. Interest management must not discard persistence or cause two simulations during overlap.

Implement `Snapshot`, `Journal`, `Persistence` with snapshot barrier and bounded transfer; test mutations during join, join during transfer, reload, host shutdown, guest loss while owner, crash at each save step, far-away players changing objects, unload/reload, duplicate spawn, tombstones and rollback. A durable save cut and protected guest saves are release blockers, not follow-up enhancements.

## Phase 6: hardening and rollout

Build matrix: host/guest roles, 2 and 3+ players, close/separated/reunited, moving and idle, clean/loss/jitter/reordering, Steam and loopback, fresh and existing copied saves, late join/reconnect, session end/rehost, origin shifts, loaded/unloaded entities, large item counts and supported content configurations. Instrument latency, frame time, queue limits and bytes; compare to native baseline with identical scenarios. Run all existing stability/build/verifier tests and new focused protocol/replay tests, then real two-machine Steam tests. No claim of parity until every matrix row is evidenced or explicitly excluded in release notes.

Only then expose the menu option; document incompatible modes, versions, experimental feature coverage and backup/restore instructions in README and installer/package notes. A failed or incomplete experimental handshake must return safely to menu, never silently fall back into native mode mid-session.

## Near-term changes to normal multiplayer

Independently prioritize measured defects with a reproducible fixture. Use existing `CarOwnershipTests`, `CarCargoTests`, `PendingAttachTests`, `SpawnTransportTests`, `StableJournalTests`, and `WorldSerializationTests` as starting points. Add a test only for a demonstrated failure or necessary invariant, fix the narrowest native path, and rerun both baseline and impaired-network scenario. Track native improvements in the parity matrix so they become requirements for experimental mode. Avoid assuming that a higher send rate, interpolation, or a collision suppression hack fixes ownership or persistence; measure the outcome and side effects.

## AI-assisted single implementation pass

Use `docs/REVAMP_MULTIPLAYER_LESSONS.md` as an input to the hook inventory, coordinated edit and parity matrix. It maps RevaMP's reported failures to Roadworthy implementation and test cases; verify historical notes against current source and game behavior.

### Differentiation without uncontrolled refactoring

Aim for the best verified player experience, not the largest rewrite. Source inspection cannot establish that either patch is universally better. Compare both on identical supported game builds, seeded saves, machines and network conditions; record versions, repetitions and unresolved failures. Do not advertise an advantage without evidence.

Architectural bets:

1. **One authoritative lifecycle per object graph.** Treat a vehicle, dependent parts, stored cargo and seats as a versioned graph, with explicit exceptions for held or worn items. Host-approved transfers carry revisions and epochs; reconcile dependencies before the new writer acts. Test competing claims, stale packets, graph splits, disconnect and streaming. This is a proposed improvement to evaluate, not a claim that RevaMP lacks graph handling.
2. **A coherent durable revision.** Define how the game save, sidecar, spawn leases, tombstones and remote-owner changes belong together. Save against a consistent cut; detect interrupted writes and recover the last complete revision. Multiple files require a manifest and recovery protocol, not just individual atomic replacements. Fail visibly if required state cannot be obtained. Test interrupted writes and autosave rotation on copies.
3. **Separate interest from simulation.** A player can receive a display copy without owning physics, while the host retains persistent state when nothing is loaded. Use actual game streaming state, hysteresis and bounded reconciliation. Test players splitting, meeting, entering a moving car and disconnecting during transfer.
4. **Recovery is a normal protocol operation.** Use bounded queues, idempotent events, revisions, epochs and targeted repair. Expose clear failures rather than silently duplicating or deleting items. Define when the session must stop safely; test loss, reordering, reconnect and interrupted join.
5. **Preserve working gameplay.** Keep native mode as the default and share fixes only when their assumptions are verified. Use existing game load and interaction paths where safe. Compare actual behavior and saved object graphs, including Roadworthy-specific features.

Simplicity constraints:

- Two backends only: current native and experimental. No plugin framework, generic ECS, universal serialization rewrite, new dedicated-server architecture or unrelated single-player rewrite. Introduce an abstraction only when two concrete call sites need it.
- Each new message has a sender, receiver, reliability, authority check, lifecycle and failure behavior. Each entity has one simulator and one host record. Centralize authority decisions.
- Reuse existing save/load records and proven helpers when their semantics fit. Document each custom format, heuristic, timer, collision exception or repair loop with the failing scenario it addresses, a bound and removal criterion.
- Complete gameplay slices as graphs, not isolated transforms; keep unsupported actions visibly blocked in development. Preserve mode isolation and save safety during the coordinated implementation pass.
- Every feature maps to a parity or failure scenario and a test or recorded manual check. Review modules for overlapping ownership and remove redundant paths during integration.

Competitive validation: maintain a versioned matrix comparing patched native, experimental and a separately installed RevaMP build. Measure successful joins, duplication or loss after save/reload, ownership transitions, vehicle motion, separation and reunion, frame-time percentiles, bandwidth and recovery under latency/loss. Include usability, installation, rollback and relevant mod compatibility. Record fixtures, hardware, settings and repetitions. A crash, save corruption, silent item loss or native-mode regression blocks release regardless of performance.

The intended workflow is one coordinated implementation pass across the complete experimental backend, followed by integration, debugging and hardening. The numbered phases above describe dependency order and acceptance gates, not a requirement to stop and release after every phase. Do not deploy the unverified result or enable it by default. One pass is an editing strategy, not a promise that the first build or playthrough will work.

### Preparation in the same workstream

1. Record checkout commit and existing dirty files. Preserve user changes; do not reset, overwrite or install into the live game. Create a protected fixture and separate output directories. Inventory game hooks and native messages using the supported local assembly, `rg` and existing patch/verifier sources. Generate the parity matrix with actual signatures and mark ambiguous behavior as unverified; never fabricate coverage.
2. Produce a hook ownership table before editing: native call site, existing injected callback, native side effects, selected mode, experimental handler, suppress/continue decision, and corresponding verifier and scenario. Trace cross-cutting assumptions (`Connected`, `syncScript.IsHost`, `node`, local save IDs, applying-remote guards) across all partial bridge files. This is the safety boundary for the large edit.
3. Fix interfaces and invariants first: mode lifetime, session handshake, network IDs, authoritative owner/epoch, event IDs, snapshot revision, attachment graph, save cut and cleanup. Specify bounded failure behavior and explicit unsupported cases. Include a machine-readable message/type inventory if helpful for checking completeness. Resolve protocol and save schema as a single compatible design before writing adapters.

### Coordinated edit pass

Work in dependency order without waiting for separate releases: add pure protocol/registry/authority/persistence components; add mode routing and explicit guards around existing native-only hooks; implement adapters for players, vehicles, parts/cargo, world events, wildlife and save/restore based on the inventory; add matching assembly patches and independent verifier assertions; update source/signature lists, harnesses and experimental-mode documentation. Preserve shared gameplay fixes where verified mode-neutral. Add instrumentation, assertions and focused tests alongside interfaces, so the subsequent repair phase has useful evidence.

This pass should cover the whole mapped surface rather than leave silently unhandled native actions. Where an action cannot safely be implemented from the available evidence, explicitly block that action or the experimental session with a visible diagnostic, register it as incomplete in the matrix, and continue independent work. Do not suppress local game side effects merely to silence native network traffic. Do not use two authorities for an object. Never mark a category complete merely because its packet has a handler.

AI can accelerate this work by batching source searches, mechanically enumerating message handlers and hook sites, drafting repetitive codecs, adapters, test fixtures and verifier entries, and reviewing cross-file consistency against the ownership table. Automation can flag missing dispatch cases and source-list entries, but generated code and inventories still require compilation, review and game evidence. Use concurrent agents only when explicitly requested; independent searches may be batched without concurrent edits.

### Integration and repair after the edit pass

1. Build into a new staging directory and run assembly hook/callback verifiers. Fix compile errors, source-list omissions, invalid IL and handshake defects first. Review the diff for changes to the default/native path and user-owned edits.
2. Run pure protocol, malformed-input, authority/epoch, idempotency, registry lifecycle, snapshot/journal, attachment-dependency and persistence tests. Add regression tests for failures discovered during repair, especially cross-system failures.
3. Run existing native and single-player regression tests with the experimental mode disabled. Verify mismatched modes fail before mutating world state. Repair until this baseline passes; do not trade a native regression for progress on the experimental path.
4. Run protected loopback scenarios, then two real Steam accounts and machines: joining, driving in either direction, passenger and ownership transfer, attached and loose cargo, repeated origin shifts, simultaneous interaction, latency/loss, late join, disconnect/reconnect, distant world changes and save/reload. Capture both peers' logs and compare identities, ownership, object graphs and saves. Fix by root cause, re-running the relevant scenario and any impacted baseline.
5. Run extended multi-player and long-session matrix and review unresolved rows. No public opt-in, live installation or existing-save support until mode isolation, safe failure, persistence and explicitly scoped parity have real-game evidence. If hardware or accounts are unavailable, deliver the staged build and evidence honestly as unverified, with exact remaining tests.

### Deliverable and prioritization

The target of the first pass is a complete, wired implementation against the discovered inventory, with failures visible and repairable, not a claim of full parity. In a conflict, preserve the existing default path and save integrity; experimental features may remain blocked. Group fixes by shared root cause and run focused checks while repairing; run broad gates after related changes settle. Do not repeatedly rerun the full suite for unrelated edits, but never defer essential build and save-safety checks before a game test.
