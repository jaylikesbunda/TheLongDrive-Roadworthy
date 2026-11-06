# Multiplayer Mode Boundary Inventory

Research snapshot from the local Roadworthy source and the pinned decompile. This documents current wiring; it does not establish runtime parity or prove every native side effect under multiplayer.

## Native session lifecycle

- Native lobby creation/join and the menu state live in `GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript` and `SteamP2PMenuHandlerScript`. The native hood sets `isConnected`, `isHost`, `ConnectedLobbyID`, and password/join state. `syncScript.IsConnected()`, `IsHost()`, and `IsHostOrSingle()` are the native role queries used throughout game code.
- `tools/PatchStability.cs` retains each native `hood.Init()` body and inserts `StableGameBridge.Bootstrap()` at each return. Bootstrap starts the persistent `Driver`, applies optional test loopback, starts content fingerprinting and calls `Ensure()`.
- `StableGameBridge.Ensure()` derives lobby and owner from the native hood/Steam lobby (or test Loopback), waits for the fingerprint, creates a `StableNode` for the session, binds transport callbacks and `StableMap`, and resets state on disconnect or lobby/host change. `Reset()` disposes SDR connections and clears queues, node/session, map state and mode-dependent adapters.
- `StableGameBridge.Driver` owns ongoing bridge work. Its `Update` polls transport, drains loopback, ticks `StableNode`, handles peer membership and sessions; `FixedUpdate`/`LateUpdate` advance mode-dependent features. `OnDestroy` resets and stops test loopback.
- Current gameplay role checks and native session state are still sourced from the game hood / `syncScript`; `StableNode` is a parallel Roadworthy handshake, compatibility, peer-readiness, session/host-epoch and extension-message state. A connected lobby is not proof that the Roadworthy peer is compatible or ready.

## Transport and callback replacement sites

| Native site patched by `PatchStability` | Runtime callback | Boundary / consequence |
|---|---|---|
| hood `SendBytes(byte[], EP2PSend)` | `StableGameBridge.RouteBroadcast` | Replaces native broadcast packet send globally. Captures startup/password/map/bootstrap traffic, handles native packet routing and sends session envelopes over SDR (or test loopback). |
| hood `SendBytesToTarget(CSteamID, byte[], EP2PSend)` | `StableGameBridge.RouteTarget` | Replaces targeted native packet send globally; handles direct bootstrap and targeted traffic. |
| hood `ReadMessage()` | `StableGameBridge.ReadBudgeted` | Replaces native P2P receive polling. Polls SDR/loopback and dispatches accepted native packets back to the game. Not a passive observer. |
| `networkingScript.ProcessMessage(CSteamID, byte[], msgType)` | guard `StableGameBridge.AllowIncoming(sender, bytes)` | Rejects malformed/unauthenticated/stale/unsafe packets; accepts native packets only after session checks; consumes extension type 254 in `StableNode`; avoids native handler re-entry while bridge-dispatching. |
| hood `ForwardMessage(sender, bytes, sendType)` | guard `AllowForward()` | Suppresses only duplicate local relay during bridge dispatch / vehicle-motion application; otherwise leaves native relay path available. |
| hood `Init()` returns | `Bootstrap()` | Starts persistent bridge/driver; init's native body otherwise runs. |
| `syncScript.SyncStuff()` | `SyncItems()` | Replaces native item scheduling coroutine with Roadworthy scheduler (native item methods still used for sends). |
| `savedatascript.GetNewKey()` / `GetNewKeyIgnoreSave()` | `UseOriginalIDs()` then `NewKey()` | Offline uses original body; connected sessions switch to node-wide ID allocator. |

Transport implementation: `SteamSdrTransport` creates a Steam Networking Sockets P2P listener/client connection, accepts only `AcceptPeer` members, configures traffic lanes when supported, queues sends and polls messages. `SteamSdrTransport.Poll()` intentionally relies on the game's `SteamAPI.RunCallbacks()` pump (it does not run a second socket callback pump). StableNode/reliable outboxes validate and frame packets above the socket layer. The bridge's receive callback consumes its own protocol/extension packets or calls `networkingScript.ProcessMessage` once for a native message.

**Experimental transport integration gate:** current `SteamSdrTransport.Poll(Action<ulong,byte[]>)` exposes authenticated peer and bytes but not incoming lane/reliability metadata. The prototype `ConnectionGuard.Receive` expects an `isReliable` value from the transport. It must not derive that value from an untrusted payload or assume the current callback supplies it. Determine what the game's actual Steam runtime exposes and test it on the supported DLL, or redesign sequencing to work safely without such metadata. Do not change the native transport callback until the compatibility and default-mode regression impact are verified.

Valve's current [Steam networking types documentation](https://partner.steamgames.com/doc/api/steamnetworkingtypes) states that received message flags expose the reliable bit, and its [Sockets documentation](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets) limits ordering guarantees across lanes. This suggests a possible receive callback extension, not proof that this game's older native DLL and binding marshal the field safely. Verify the exact runtime ABI and real receive behavior before using it; keep dependent reliable events on an ordering domain with established guarantees.

## Other Roadworthy files coupled to native multiplayer state

These are source-level dependencies found by searching `runtime/` for `syncScript.IsConnected/IsHost`, `StableGameBridge.Node`, or `node` readiness/role/session fields. They must be considered in any mode boundary or “disable multiplayer” switch.

| Files | Coupling observed | Confidence / caveat |
|---|---|---|
| `StableGameBridge.cs`, `SteamSdrTransport.cs`, `LoopbackTransport.cs` | Session creation, transport, lifecycle, dispatch and loopback toggling. | Direct core path. Loopback is test-only but sets native hood connected/host/lobby flags. |
| `StableMap.cs`, `StableRoadCodec.cs` | Map/world generation and encode/decode gates use node/session data. | Mode-specific extension path; map hooks also run with native disconnected fallback. |
| `CarCargo.cs`, `CargoGhost.cs`, `AttachReplay.cs`, `JoinPhysics.cs`, `NativeSafetyAdapter.cs`, `NativeStoreAdapter.cs` | Host/guest/connected or ready gates control native item/cargo/attachment/physics/store safety. | Some are observers/adapters; disabling them can affect local gameplay or recovery even if wire sends stop. |
| `RabbitNetwork.cs`, `RabbitPlayers.cs`, `RabbitCombat.cs`, `RabbitContact.cs`, `RabbitPopulation.cs`, `RabbitMetadataSync.cs`, `RabbitMotionRuntime.cs`, `RabbitAttackDiagnostics.cs` | Host ownership, peer/session epochs and guest gates for AI/player combat, population, contact, metadata and motion. | Feature-option gated in places; not all native rabbit paths are replaced by a single transport switch. |
| `SmokeRuntime.cs`, `WildlifeWeather.cs`, `NpcVehicleImpact.cs`, `PartsCensus.cs` | Connected/host/node gating for smoke, environment, vehicle impact and diagnostics/census. | Mixed gameplay and observational responsibilities; verify each callback's side effects before disabling. |

`Connected` is not synonymous with active Roadworthy mode: it reads the native hood's `isConnected`; node readiness, compatibility, host role and session epoch are separate gates. Conversely, some standalone native gameplay functions use `syncScript.IsHostOrSingle()` and run offline. A broad replacement of these queries can change single-player behavior.

## Hooks that are unsafe to globally disable

- **Send/receive adapters** (`SendBytes`, `SendBytesToTarget`, `ReadMessage`, `ProcessMessage` guard) are shared by native menu/join/password/map and gameplay packet flows. Disabling or bypassing them globally can split session handshake from gameplay, double-dispatch native messages, reopen malformed packet paths, or strand buffered traffic. Keep native fallback semantics explicit for disconnected/offline state.
- **`Bootstrap` and the driver** coordinate reset, compatibility fingerprint, lobby/session rebuild, peer departure, and adapters. Removing only the transport while leaving stale `node`/driver state is unsafe; stopping the driver without a deliberate reset path risks retaining state across disconnect/world replacement.
- **`SyncStuff` and allocator replacements** affect recurring item replication and identity uniqueness for every native synchronized item while connected. Returning to native scheduling/IDs inside the same session risks duplicate work or ID collision; keep stock behavior for single-player only as currently guarded.
- **`ProcessMessage` admission guard** is security/correctness boundary for bridge packets. Turning it into unconditional allow or reject globally has different failure modes; its 254 extension dispatch and native packet validation/session admission must remain coordinated with `Deliver`.
- **Mode gates in safety/ownership files** are not transport-only. Store eligibility, passenger transitions, attachment recovery, cargo reconciliation, combat role, NPC impact and smoothing alter local simulation or persistence. Disabling based only on a stopped socket can leave remote-owned state frozen, duplicate authority, or change offline behavior.
- **Steam callback pump:** do not disable the game's `SteamAPI.RunCallbacks()` on the assumption SDR polls callbacks itself. The transport explicitly expects the game pump to deliver `Callback<T>` connection status.

## Known gaps and uncertainty

- This is static source tracing, not a two-peer runtime trace. It does not prove join/rejoin, disconnect cleanup, packet ordering, host migration, or save behavior in all cases.
- `PatchStability.cs` is the current patch generator and documents specific injected sites, but successful patch output/runtime installation was not revalidated for this inventory.
- The dependency list is lexical and representative: `Connected`/`node` usages can be indirect through adapters, delegates, partial bridge files or hooks not captured by the search terms. It is not a complete callback ownership graph.
- Native hooks retain different amounts of original behavior: `Replace` removes the body, guards may skip the body conditionally, entry/after-return hooks preserve it, and `ConnectedReplacement` explicitly keeps the stock ID allocator offline. Do not infer semantics from a hook name alone.
