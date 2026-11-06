# VehicleCapture Shadow Probe Plan

## Goal and boundary

Implementation update: `runtime/ExperimentalVehicleShadowProbe.cs`, `tools/PatchVehicleShadowProbe.cs` and `tools/Build-VehicleShadowProbe.ps1` now produce an isolated staged artifact. A verified baseline was built and the staged method fingerprints were compared after stripping the observer. The probe has not been installed or run in the game. Its verifier covers specified method fingerprints and selected metadata, not every metadata table or runtime behavior. The script's manifest hash checks establish consistency with the supplied baseline, not independent provenance.

`tests/scripts/Test-ExperimentalProbeLoad.ps1` copies a staged build and installed managed dependencies into a separate managed-only fixture, loads the observer entry point and invokes it with a null item. This is not a proof of flag behavior, a Unity/game execution, or proof that the injected native method can run safely. Separate stand-in tests exercise the flag and bounded log count. The existing normal build, installer and handshake remain untouched.

Build a separate, opt-in diagnostic artifact that calls `Experimental.VehicleCapture.TryCapture` from a native car-pose update and records the resulting sample locally. The probe must not publish packets, touch `StableNode`, change role/ownership decisions, or alter the mode latch, lobby metadata, handshake, native send/receive dispatch, or save state. Standard `Build-Patch.ps1` output and installer inputs remain the production baseline.

The probe answers only: “Can this adapter capture a finite global vehicle pose/rotation/velocity at a real game callback, and does it agree with the native pose anchor?” It does not establish network identity, authority, transport, ordering, or application safety.

## Existing build constraints

- `Build-Patch.ps1` is the installer build: it emits a v1 patch, compiles `Tld.Multiplayer.dll` from the source list read from `tests/scripts/Test-Stability.ps1`, runs `PatchStability.exe`, verifies via `VerifyStability.exe`, and writes a version-3 manifest. Its source signature is derived from that test source list. Do not add probe sources to this default list or change its hook calls for this staged probe.
- `PatchStability.Main` is intentionally pinned to four arguments and a known v1 SHA; its transport substitutions include `SendBytes`, `SendBytesToTarget`, `ReadMessage`, `ProcessMessage`, item scheduler, and ID allocation. Reuse of this broad patch pass for the observer would make the scope harder to isolate. Apply a second tiny probe patch to a verified v3 artifact instead.
- `ExperimentalVehicleCapture.cs` is currently only in isolated tests/game-compile checks. It uses `tosaveitemscript`, `mainscript.GlobalFromUnityPos`, `item.useThisForPos`, and `item.RB`; there is no runtime callback or caller today.

## Smallest staged change set

Create a separate script such as `Build-VehicleShadowProbe.ps1` and two isolated tools/source files; do not alter `Build-Patch.ps1`, `PatchStability.cs`, production runtime sources, or handshake files for this experiment.

1. **Build baseline into a private stage directory.** The script invokes the existing `Build-Patch.ps1` into a new temporary/stage output directory (never an install folder), captures `manifest.json`, and records hashes of baseline `Assembly-CSharp.v3.dll` and `Tld.Multiplayer.dll`.
2. **Compile a probe-only runtime assembly for the stage.** Use the same pinned game references/compiler and exact baseline runtime source list, adding only `ExperimentalVehiclePose.cs`, `ExperimentalVehicleCapture.cs`, and a new `VehicleCaptureShadowProbe.cs`. Recompute the staged source signature and label the manifest as `vehicleCaptureShadowProbe=true`; do not overwrite or relabel the standard build output. The helper should expose one static `Observe(tosaveitemscript)` entry point, be disabled unless `TLD_MP_VEHICLE_SHADOW_PROBE=1`, use a bounded/rate-limited local log or ring buffer, and never call any send, receive, session, authority, ID-allocation, save, or transform/physics setter.
3. **Patch only the observer call site in the staged v3 assembly.** Add a separate `PatchVehicleShadowProbe.cs` that accepts the baseline patched assembly, the staged probe runtime, and a new output path. It should verify the exact baseline hash from the stage manifest, exact target signature and return layout, and insert one call to `VehicleCaptureShadowProbe.Observe(this)` after each normal return of `tosaveitemscript.ForcePosUpd(bool,bool,bool)` (or an equivalent return-safe observe site if verifier inspection shows the method is unsuitable). It must write a new staged `Assembly-CSharp.vehicle-shadow-probe.dll`, leaving v3 input untouched. Do not rerun the full transport hook patcher on an already patched v3 assembly.
4. **Stage install separately.** Use a separate disposable game install/copy and a distinct output manifest/artifact name. Never place the probe assembly/runtime into the normal game install or production package. Probe flag off must result in no capture/log side effect even in the staged build; normal v3 remains byte-identical to the baseline build.

### Callback choice and identity labels

`tosaveitemscript.MultiUpd()` calls `ForcePosUpd(false,false,false)` only when `ThisOneInCharge()` and sync deltas warrant it; `ForcePosUpd` is therefore close to the native car-pose update and sees the same `tosaveitemscript`. After-return observation avoids reading a partially updated state while retaining native execution. Verify actual overload/return count against the pinned assembly before patching.

`VehicleCapture.TryCapture` requires `networkId` and `epoch`, but this observer has no experimental identity/authority session. For this experiment, use a clearly labelled synthetic sample ID derived from the local `idInSave` and fixed epoch `1`, and keep it strictly inside the diagnostic record. Do not serialize or send it. A future live transport must replace these labels with allocated network IDs and current ownership epochs; this probe does not validate that mapping.

The adapter now captures `useThisForPos`, matching the native pose anchor used by `ForcePosUpd`; a stand-in test covers an offset root. The observer should still log both anchors/delta to establish behavior on real prefabs. The adapter has only been compiled against the installed game, not exercised in it.

## Independent verification gates

- **Baseline integrity:** Build standard output first; assert its hashes and manifest are unchanged after producing probe output. Confirm standard `Build-Patch.ps1` plus `VerifyStability` still passes unmodified.
- **Patch isolation:** A new verifier checks that input v3 hash matches the manifest, output adds exactly the intended call(s) in `ForcePosUpd`, and no other method body/metadata changed. Also assert there are no calls from probe code to `SteamP2PNetworkingUnderTheHoodScript.Send*`, `StableNode.Publish*`, `ProcessMessage`, `AuthorityRegistry`, `PeerSession`, ID allocators, save APIs, or Unity setters.
- **Flag off:** In a staged game, omit `TLD_MP_VEHICLE_SHADOW_PROBE`; confirm no probe logs/counters and compare native send/receive counters and session handshake logs with baseline.
- **Flag on, single-player:** Exercise one stationary and one moving car plus missing/non-car/no-Rigidbody cases. Confirm callback is observational, samples are finite, and velocity/pose values track native values within a declared tolerance; verify no transform, rigidbody, save record, multiplayer flags, lobby state, or traffic count changes.
- **Flag on, native multiplayer:** Use a disposable two-peer lobby with the ordinary Native mode. Confirm the probe only observes cars for which the native callback runs; compare captured world pose to the native `carPosGlobal`/`carVelocity` messages and quantify sampling delay. Confirm experimental code sends zero messages and Native admission/handshake/world load stay unchanged.
- **Failure behavior:** Force malformed/extreme sample values and disabled/missing logger conditions; the observer must catch/report locally without throwing into the native method. Logging must be capped to avoid a hot path flood.

Run compile/unit tests for the adapter and new helper plus the full existing build/verifier gates before a staged runtime session. Tests should specifically cover anchor mismatch, float range/plausibility, local ID absent/zero, no `RB`, non-car, and rate-limit/reset behavior. A game probe is diagnostic only and cannot approve future authoritative replication.

## Risks / open decisions

- `ForcePosUpd` may be called from several gameplay paths and at a higher rate than expected; rate cap and opt-in flag are mandatory. Confirm that post-return insertion covers all return paths and preserves the existing value/stack shape before building.
- Vehicle pose anchor equivalence is unverified. `useThisForPos` can be a custom transform in native code, so observe both anchors before deciding to replace root capture.
- Synthetic ID/epoch must never be interpreted as actual network authority. The staged manifest/log label should say `observer-only; no transport`.
- Runtime loader/install handling for a staged variant is not yet specified. If the standard installer accepts only version-3 manifests or standard filenames, add a separate staging installer path rather than weakening its checks.
- `Start-Loopback.ps1` does not isolate Documents saves; a copied game directory alone does not change the Windows profile. Native `pathscript.path()` uses `Environment.SpecialFolder.MyDocuments`. Do not launch the staged copy with real user saves until an isolated Windows profile/VM or a separately verified redirection for all user-data paths is available.
- No separate `ExperimentalVehicleCapture` callback exists today; all proposed callback/patcher names above are design-only until implemented and verified against the pinned assembly.
