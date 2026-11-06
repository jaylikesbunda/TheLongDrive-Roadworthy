# Experimental multiplayer handoff

Updated 2026-11-06 (supersedes the 2026-11-05 prototype-stage handoff). The user wants a functioning, thoroughly tested opt-in multiplayer rewrite for Roadworthy. Native multiplayer stays the default. Preferred UI is the existing Misc settings row; it must not suggest unfinished features are playable. Broad coordinated edits followed by debugging are preferred over many tiny releases, but simple architecture and no regressions matter more. Do not install unverified code or use personal saves for tests.

## Baseline and installed state

- Checkout `C:\Users\deki\Documents\ChatGPT\The Long Drive`, HEAD `3f5352df22fe1fc7f3c2e8fe1be0a8e2d8e79492`. Experimental sources/tests/docs are untracked; `.commandcode/` and `PLANS.md` predate this work. Preserve all user files; no reset/clean.
- Installed test build: `Assembly-CSharp.dll` = `B97572B16C60EBF81FFAC42337B339592ACD653C4BB8D7257E174BDC5F81B96A`, `Tld.Multiplayer.dll` = `4D5CA64A33E220D7166BF97A13A78EED2140DD8C4CF29D300C25D02138EE94B8`.
- Rollback: `.local-analysis/staged/experimental-mp-20261105-final8/installed-backup`. Vanilla: copy `Assembly-CSharp.tldmp-v3-original.bin` over `Assembly-CSharp.dll` and delete `Tld.Multiplayer.dll`.
- Latest full `Test-Stability` run `20261105-212930-963c1700`: PASS (an earlier 21:17 run never completed; not evidence).
- Save isolation verified against the installed assembly; host-only smoke launch started backend, relay and seat ledger cleanly with Documents untouched.
- Loopback session `20261105-2152`/`2153` (host + client, experimental, external-snapshot join) ran ~4.5 min. Client log shows repeated `car released: no pose for ~2000–2900ms` and one 51.7 m view jump. Not yet triaged: could be the 5% loss/jitter profile, the ~1.5 s lease expiry, or a relay gap.

## How it works

1. **Mode boundary.** `PatchOptions.ExperimentalMultiplayer` or `TLD_MP_EXPERIMENTAL=1` selects experimental. The `StableNode` signature is tagged `|exp-mp-1`, so native and experimental peers reject each other at Hello before world mutation. `TLD_MP_VEHICLE_SNAPSHOTS=0` falls back to native.
2. **Session/join.** Native `StableNode` owns lobby, map bootstrap, heartbeat and join journal. Experimental join (`ExpSnapshotBegin/Part/End`, ops 26–28) replaces the world snapshot in loopback: 32 MB bound, 48 KB chunks, SHA-256, native world frame/apply, journal replay after Ack. Steam join stays native.
3. **Vehicle authority.** Host `VehicleAuthority` over `EntityCoordinator`: per-car lease with current role, ~1.5 s stale expiry, host reclaim, disconnect reassignment. `ExperimentalAllowsPeer` compares the host's actual role with the incoming driver's (`ProtectLocalCar` + `ThisInOneInChargeCar(incomingRole)`). Cars plus fitted parts/stored cargo transfer as one atomic group; loose/held/worn/POI items are excluded.
4. **Item/cargo events.** `ItemAuthority` (owner/epoch/eventId), `ItemEventStream` (ordering, dependency buffering, expiry/repair, removal tombstones, join journal), `ExperimentalItemRelay` (host→guest `ExpItemEvent`, op 29). Host-local door/part/car-value/paint/damage/storage/usable/fluid actions get monotonic event ids; guests apply each once via native dispatch. Poses stay native. Owner gate is opt-in (`TLD_MP_EXPERIMENTAL_ITEMS=1`).
5. **Breakables.** First writer claims, host can override, removal is a tombstone. Live gate for types 29/32/33/36.
6. **Players/seats.** Host identity gate for 10/11/12/18 (payload Steam ID must equal authenticated sender). `PlayerReplication`/`SeatLeaseTable` fed by native `BusSeatClaim`, released on exit/disconnect; observational only.
7. **Save isolation.** `pathscript.path()` → `RedirectSaveRoot`; `TLD_MP_SAVE_ROOT` moves saves/settings/options/maps/logs/diagnostics. `Start-Loopback.ps1` uses `loopback-data/host|client`.
8. **UI.** Misc row "Experimental multiplayer (preview)" is enabled, switches automatically before a peer joins. Kill switches: `…JOIN=0`, `…RELAY=0`, `…PLAYERS=0`, `…BREAKABLES=0`.

## Live vs core-only

| Area | State |
|---|---|
| Mode isolation, loopback join, vehicle lease/groups, host→guest item relay, breakables, player identity, seat ledger, save isolation, Misc UI | Live |
| Item owner gate | Live, opt-in |
| Interest/far-store, generic replication registry | Compiled + tested, not delivering |
| Player replication (held/worn, passenger placement, seat enforcement) | Core + ledger only |
| Guest→guest item events, cargo events beyond host actions | Not live |
| World/creature/POI ownership, save-cut/guest-save protocol, Steam experimental join | Not implemented |
| Two-account/two-machine evidence, loss/jitter parity | Not done |

`docs/MULTIPLAYER_PARITY_MATRIX.md` still opens with "none wired"; update it with the table above when real evidence lands.

## Regression history

- **Host lockout/desync:** high-water role + strict takeover let a host fallback owner reject the first guest-driver update. Fixed with current-role leases and incoming-role comparison.
- **Item gate overreach:** rejected guest door/part/car-value interactions as `RejectOwnerMismatch`. Fixed by making the gate opt-in; host state goes through the relay.
- **`itemPosLocal` layout:** index at offset 9, parent at 5. Fixed in `ExperimentalItems` and fixtures.
- **Build-list omissions:** new sources must be in `$signatureInputs` and the `Test-Stability` csc list, or the shipped assembly omits them.

## Next work, in order

1. Interest/far-store delivery: per-peer `InterestSet` send filtering, `FarStore` for unloaded authoritative records, stream-in reconciliation. Creation/state stay native until wired.
2. Item event completion: guest-originated events to other guests, author/source field against echo, per-peer budgets; then enable the owner gate with interaction handoff.
3. Seat enforcement + held/worn transfer: wire `SeatLeaseTable` to GetIn/GetOut, enforce claims, move held/worn items with the player, passenger placement via `TryWorldSeatPose`.
4. Generic replication delivery: map car/item/breakable/player to `ReplicationRegistry`; enforce revisions/epochs on the relay.
5. World/creature/POI ownership: host-owned spawns/POIs/doors/weather; creature leases with host fallback; contact authority.
6. Persistence/save-cut: manifest + revision, guest-save protection, crash recovery.
7. Steam transport and evidence: receive reliability/lanes on this DLL, two-account/two-machine late join, loss/jitter, reconnect, saves.
8. Parity matrix/docs with real evidence; unsupported actions visibly blocked.

## Constraints

- Native stays default; never mutate the same object from both systems.
- Do not default-enable the item owner gate before interaction handoff.
- Keep host-local item relay (`DispatchType==-1`, `!dispatching`); poses/guest actions stay native.
- New runtime sources → `$signatureInputs` + `Test-Stability` csc list; run the full gate.
- Use `Start-Loopback.ps1` stable roots; never Documents. Run `Test-SaveIsolation.ps1` before a world test.
- D: has ~64 MB free; stage/install need it. Use `-ExecutionPolicy Bypass`.
- Build/stage/install: `Test-Stability.ps1` → `Stage-NativeSafetyBuild.ps1` → `Install-NativeSafetyBuild.ps1`; keep the rollback stage.

## Key files and commands

- Runtime: `ExperimentalBackend.cs`, `ExperimentalCoordinator.cs`, `ExperimentalItems.cs`, `ExperimentalItemStream.cs`, `ExperimentalItemRelay.cs`, `ExperimentalBreakables.cs`, `ExperimentalPlayers.cs`, `ExperimentalPlayerReplication.cs`, `ExperimentalInterest.cs`, `ExperimentalReplication.cs`, `ExperimentalJoin.cs`, `ExperimentalSnapshot.cs`; bridge `StableGameBridge.cs`, protocol `StableCore.cs`.
- Tests: `tests/scripts/Test-Experimental*.ps1`, `Test-SaveIsolation.ps1`, `Test-Stability.ps1`.
- Host: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\deki\Documents\ChatGPT\The Long Drive\Start-Loopback.ps1" -Role Host` (also has `-Experimental`, `-DataRoot`, `-FreshData`).
- Client: same with `-Role Client -RttMs 200 -LossPercent 5 -JitterMs 40`.
- Logs: `.local-analysis/loopback-logs/<stamp>-<role>.log`, `.local-analysis/loopback-data/<role>/diagnostics/runtime-*.log`.

## Still-valid caveats from the prototype stage

- `ExperimentalDetachedRemap.cs` works on detached copies only; no prefab-aware validation; native `RearrangeID` may leave nested IDs unchanged. Not used in gameplay.
- Vehicle admission checks finite pose and owner/epoch, not speed/displacement/teleport plausibility.
- RevaMP comparisons and test scenarios: `docs/REVAMP_MULTIPLAYER_LESSONS.md` (LGPL-3.0; no code copied).

## Transport reliability evidence (2026-11-05, read-only)

- Installed binding is `TheLongDrive_Data/Managed/com.rlabrecque.steamworks.net.dll`. Its `SteamNetworkingMessage_t` marshals `m_nFlags` between `m_nChannel` and `m_nUserData`, and `Constants.k_nSteamNetworkingSend_Reliable` is `8`, `Unreliable` `0`, `UnreliableNoNagle` `1`. `ReceiveMessagesOnConnection` returns native `IntPtr[]` messages, so the field is readable without changing the receive API.
- Valve's current `steamnetworkingtypes.h` says `m_nFlags` is a bitmask of `k_nSteamNetworkingSend_xxx` and that for received messages only the reliable bit is valid.
- The game ships `TheLongDrive_Data/Plugins/x86_64/steam_api64.dll`, version `05.69.73.98`, size `262944`, SHA-256 `473F5A312B56519F347741B63F3DEA590946B96EA40EF3803D5F452C39AF2F1E`. Whether this older build populates the bit on receive is **not** established.
- `SteamSdrTransport.Poll(Action<ulong,byte[]>)` still discards the field. Do not gate correctness on `m_nFlags` until a two-account live test on this DLL shows reliable frames carry bit `8` and unreliable frames do not.
