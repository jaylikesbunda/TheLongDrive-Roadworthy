[CmdletBinding()]
param([string]$GameDirectory='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repoRoot
try { & python -m unittest tests.test_starting_stop tests.test_loading_overlay tests.test_storage_optimizations tests.test_debris_optimizations tests.test_debris_ccd tests.test_debris_contacts tests.test_crate_contacts tests.test_impact_audio tests.test_slice_sort tests.test_slice_buffers tests.test_world_biome_bridge tests.test_reverse_fluid tests.test_urine_drinking tests.test_particle_urine_drinking tests.test_npc_vehicle_impact tests.test_guest_relocation tests.test_survival_rates tests.test_survival_hud tests.test_grass_transition; if ($LASTEXITCODE -ne 0) { throw 'Starting-stop or loading-overlay regression tests failed.' } } finally { Pop-Location }
. (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
& (Join-Path $PSScriptRoot 'Test-Offline.ps1') -GameDirectory $GameDirectory
$baseRun=(Get-Content -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.local-analysis\latest-run.txt') -Raw).Trim()
$baseline=Get-Content -LiteralPath (Join-Path $baseRun 'manifest.json') -Raw | ConvertFrom-Json
$run=Join-Path $baseRun 'stability-v3'
New-Item -ItemType Directory -Path $run | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed=Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$toolDir=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\patcher'
$cecil=Join-Path $toolDir 'Mono.Cecil.dll'

function Test-ExePath([string]$exe){
    if([IO.Path]::GetFullPath($exe).StartsWith([IO.Path]::GetFullPath($env:WINDIR),[StringComparison]::OrdinalIgnoreCase)){return $exe}
    $sourceDir=Split-Path -Parent ([IO.Path]::GetFullPath($exe))
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{$dirHash=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceDir))).Replace('-','').Substring(0,12)}finally{$hasher.Dispose()}
    $scratch=Join-Path $env:TEMP ('opencode\tld-test-exec\'+$dirHash)
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $copy=Join-Path $scratch ([IO.Path]::GetFileNameWithoutExtension($exe)+'-'+(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.Substring(0,12)+[IO.Path]::GetExtension($exe))
    if(!(Test-Path -LiteralPath $copy)){
        Copy-Item -LiteralPath $exe -Destination $copy
        Get-ChildItem -LiteralPath $sourceDir -Filter '*.dll' -ErrorAction SilentlyContinue | Where-Object {$_.Name -ne 'Assembly-CSharp.dll'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $scratch -Force}
        foreach($n in @('UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.VehiclesModule.dll','UnityEngine.JSONSerializeModule.dll','netstandard.dll','com.rlabrecque.steamworks.net.dll')){
            $dep=Join-Path $managed $n
            if((Test-Path -LiteralPath $dep) -and !(Test-Path -LiteralPath (Join-Path $scratch $n))){Copy-Item -LiteralPath $dep -Destination $scratch -Force}
        }
    }
    return $copy
}
function Test-LoadableCopy([string]$path){
    # .NET Assembly.LoadFrom cannot open files inside the sandboxed Documents
    # tree (HRESULT 0x80131515) even though their bytes are readable. Hand tests
    # a scratch copy of the exact same bytes instead.
    $scratch=Join-Path $env:TEMP 'opencode\tld-test-exec\loadable'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $copy=Join-Path $scratch ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash+[IO.Path]::GetExtension($path))
    if(!(Test-Path -LiteralPath $copy)){Copy-Item -LiteralPath $path -Destination $copy}
    return $copy
}
function Run([string]$Exe,[string[]]$ArgsList) { & (Test-ExePath $Exe) @ArgsList; if ($LASTEXITCODE -ne 0) { throw "Failed ($LASTEXITCODE): $Exe" } }
$experimentalBackend=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalBackend.cs'
$experimentalCoordinator=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalCoordinator.cs'
$experimentalAuthority=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalAuthority.cs'
$experimentalSession=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalSession.cs'
$experimentalGraph=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalGraph.cs'
$experimentalGraphTransfer=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalGraphTransfer.cs'
$experimentalHandoff=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalHandoff.cs'
$experimentalIdentity=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalIdentity.cs'
$experimentalJoin=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalJoin.cs'
$experimentalSnapshot=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalSnapshot.cs'
$experimentalItems=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalItems.cs'
$experimentalPlayers=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalPlayers.cs'
$experimentalBreakables=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalBreakables.cs'
$experimentalItemStream=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalItemStream.cs'
$experimentalItemRelay=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalItemRelay.cs'
$experimentalInterest=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalInterest.cs'
$experimentalReplication=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalReplication.cs'
$experimentalPlayerReplication=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ExperimentalPlayerReplication.cs'
$net=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NetCore.cs'
$core=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StableCore.cs'
$packets=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StablePackets.cs'
$pairProto=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\BusPairProtocol.cs'
$bridge=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StableGameBridge.cs'
$map=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StableMap.cs'
$roadCodec=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StableRoadCodec.cs'
$deferredCore=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\DeferredCore.cs'
$loopback=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\LoopbackTransport.cs'
$terrainPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainDistancePolicy.cs'
$cargoDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CargoDiagnostics.cs'
$frameDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\FrameDiagnostics.cs'
$modDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ModDiagnostics.cs'
$groundDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\GroundDiagnostics.cs'
$attachDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\AttachDiagnostics.cs'
$pendingAttach=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PendingAttach.cs'
$attachReplay=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\AttachReplay.cs'
$phaseTimings=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PhaseTimings.cs'
$syncItemScheduler=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SyncItemScheduler.cs'
$nativeLoopTimings=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeLoopTimings.cs'
$nativeScriptTimings=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeScriptTimings.cs'
$slowFrameDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SlowFrameDiagnostics.cs'
$itemIndexTable=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ItemIndexTable.cs'
$terrainHoleAdjacency=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainHoleAdjacency.cs'
$terrainRoadSelection=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainRoadSelection.cs'
$rendererCensus=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RendererCensus.cs'
$roadChunkPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RoadChunkPolicy.cs'
$carPosePolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CarPosePolicy.cs'
$profilerSlotPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ProfilerSlotPolicy.cs'
$terrainMembership=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainMembership.cs'
$terrainWorkDiagnostics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainWorkDiagnostics.cs'
$worldPlacementPolicy=Join-Path $repoRoot 'runtime\WorldPlacementPolicy.cs'
$roadTerrainClearance=Join-Path $repoRoot 'runtime\RoadTerrainClearance.cs'
$poiSlopePlacement=Join-Path $repoRoot 'runtime\PoiSlopePlacement.cs'
$roadStateEpoch=Join-Path $repoRoot 'runtime\RoadStateEpoch.cs'
$worldRelocationLedger=Join-Path $repoRoot 'runtime\WorldRelocationLedger.cs'
$largeRockClearance=Join-Path $repoRoot 'runtime\LargeRockClearance.cs'
$streamingBudget=Join-Path $repoRoot 'runtime\StreamingBudget.cs'
$worldBiomePolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\WorldBiomePolicy.cs'
$terrainWorker=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainWorker.cs'
$contentFingerprint=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ContentFingerprint.cs'
$netDeliveryScheduler=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NetDeliveryScheduler.cs'
$steamSdrTransport=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SteamSdrTransport.cs'
$nativeSpawnSafety=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSpawnSafety.cs'
$nativeSeatSafety=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSeatSafety.cs'
$nativeAttachmentGuard=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeAttachmentGuard.cs'
$nativeSafetyAdapter=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSafetyAdapter.cs'
$spawnClearance=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SpawnClearance.cs'
$spawnClearanceAdapter=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SpawnClearanceAdapter.cs'
$wildlifeWeather=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\WildlifeWeather.cs'
$drunkPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\DrunkPolicy.cs'
$drunkEffects=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\DrunkEffects.cs'
$mpSkinPolicy=Join-Path $repoRoot 'runtime\MultiplayerMenuSkinPolicy.cs'
$menuLayoutPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\MenuLayoutPolicy.cs'
$menuLayout=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\MenuLayout.cs'
$menuProbe=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\MenuProbe.cs'
$grassProbe=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\GrassTransitionProbe.cs'
$spawnProbe=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SpawnProbe.cs'
$steamInterfaceCompat=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SteamInterfaceCompat.cs'
$playerLeaveSafety=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PlayerLeaveSafety.cs'
$itemAskSafety=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ItemAskSafety.cs'
$roadLiveCache=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RoadLiveCache.cs'
$roadFarSkip=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RoadFarSkip.cs'
$cameraCostCore=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CameraCostCore.cs'
$cameraCost=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CameraCost.cs'
$mirrorOnScreen=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\MirrorOnScreen.cs'
$roadBake=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RoadBake.cs'
$renderSwitches=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RenderSwitches.cs'
$smallFixes=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SmallFixes.cs'
$trailerSyncPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TrailerSyncPolicy.cs'
$trailerSync=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TrailerSync.cs'
$nativeStoreGate=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeStoreGate.cs'
$nativeStoreAdapter=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeStoreAdapter.cs'
$nativeStoreText=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeStoreText.cs'
$patchInfo=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PatchInfo.cs'
$patchOptions=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PatchOptions.cs'
$patchOptionsMenu=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PatchOptionsMenu.cs'
$patchSurface=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PatchSurface.cs'
$joinPhysics=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\JoinPhysics.cs'
$cargoGhost=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CargoGhost.cs'
$netLogLimiter=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NetLogLimiter.cs'
$feedbackPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\FeedbackPolicy.cs'
$feedbackBundle=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\FeedbackBundle.cs'
$itemStreamPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ItemStreamPolicy.cs'
$terrainAlignPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\TerrainAlignPolicy.cs'
$streamScanPolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\StreamScanPolicy.cs'
$itemSettlePolicy=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ItemSettlePolicy.cs'
$farPlaceQueue=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\FarPlaceQueue.cs'
$remotePoseBuffer=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RemotePoseBuffer.cs'
$remoteSmoothing=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\RemoteSmoothing.cs'
$collisionRestoreCache=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CollisionRestoreCache.cs'
$netStats=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NetStats.cs'
$worldJump=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\WorldJump.cs'
$poseSequence=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PoseSequence.cs'
$partsCensus=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PartsCensus.cs'
$carOwnership=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CarOwnership.cs'
$placementSkip=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PlacementSkip.cs'
$liveUsable=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\LiveUsable.cs'
$carCargo=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\CarCargo.cs'
$spawnCoordinator=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SpawnCoordinator.cs'
$fixedPropConstruction=Join-Path $repoRoot 'runtime\FixedPropConstruction.cs'
$nativeSpawnAdapter=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSpawnAdapter.cs'
$results=Join-Path $run 'results.txt'
$nativeRefs=@("/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\UnityEngine.PhysicsModule.dll","/reference:$managed\com.rlabrecque.steamworks.net.dll","/reference:$managed\netstandard.dll")
$tests=@(
    @{Name='RabbitMetadataSyncTests';Sources=@((Join-Path $repoRoot 'runtime\RabbitProtocol.cs'),(Join-Path $repoRoot 'runtime\RabbitMetadataSync.cs'),(Join-Path $repoRoot 'tests\RabbitMetadataSyncTests.cs'));Refs=@()},
    @{Name='MovingVehiclePassengerTests';Sources=@((Join-Path $repoRoot 'runtime\MovingVehiclePassenger.cs'),(Join-Path $repoRoot 'runtime\MovingVehiclePassengerPolicy.cs'),(Join-Path $repoRoot 'runtime\BusRearAuthority.cs'),(Join-Path $repoRoot 'tests\MovingVehiclePassengerTests.cs'));Refs=@()},
    @{Name='BusPairTests';Sources=@((Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\BusPairReplica.cs'),(Join-Path $repoRoot 'runtime\BusSeatClaims.cs'),(Join-Path $repoRoot 'tests\BusPairTests.cs'));Refs=@()},
    @{Name='RabbitContactTests';Sources=@((Join-Path $repoRoot 'runtime\RabbitProtocol.cs'),(Join-Path $repoRoot 'runtime\RabbitContact.cs'),(Join-Path $repoRoot 'tests\RabbitContactTests.cs'));Refs=@()},
    @{Name='RabbitCombatReachTests';Sources=@((Join-Path $repoRoot 'runtime\RabbitCombatReach.cs'),(Join-Path $repoRoot 'runtime\RabbitPlayers.cs'),(Join-Path $repoRoot 'runtime\RabbitLocomotion.cs'),(Join-Path $repoRoot 'tests\RabbitCombatReachTests.cs'));Refs=@()},
    @{Name='RabbitGuestAnimationTests';Sources=@($net,$packets,$pairProto,(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\RabbitGuestAnimation.cs'),(Join-Path $repoRoot 'tests\RabbitGuestAnimationTests.cs'));Refs=@()},
    @{Name='RabbitMotionTests';Sources=@($net,(Join-Path $repoRoot 'runtime\RemotePoseBuffer.cs'),(Join-Path $repoRoot 'runtime\RabbitMotionProtocol.cs'),(Join-Path $repoRoot 'tests\RabbitMotionTests.cs'));Refs=@()},
    @{Name='RabbitMotionRuntimeTests';Sources=@($net,(Join-Path $repoRoot 'runtime\RemotePoseBuffer.cs'),(Join-Path $repoRoot 'runtime\RabbitMotionProtocol.cs'),(Join-Path $repoRoot 'runtime\RabbitMotionRuntime.cs'),(Join-Path $repoRoot 'tests\RabbitMotionRuntimeTests.cs'));Refs=@()},
    @{Name='RabbitPlayersTests';Sources=@((Join-Path $repoRoot 'runtime\RabbitPlayers.cs'),(Join-Path $repoRoot 'runtime\RabbitCombatReach.cs'),(Join-Path $repoRoot 'tests\RabbitPlayersTests.cs'));Refs=@()},
    @{Name='RabbitLocomotionTests';Sources=@((Join-Path $repoRoot 'runtime\RabbitLocomotion.cs'),(Join-Path $repoRoot 'runtime\RabbitCombatReach.cs'),(Join-Path $repoRoot 'tests\RabbitLocomotionTests.cs'));Refs=@()},
    @{Name='SmokeRuntimeStubTests';Sources=@($net,(Join-Path $repoRoot 'runtime\SmokeProtocol.cs'),(Join-Path $repoRoot 'runtime\SmokeRuntime.cs'),(Join-Path $repoRoot 'tests\SmokeRuntimeStubTests.cs'));Refs=@()},
    @{Name='SmokeProtocolTests';Sources=@($net,(Join-Path $repoRoot 'runtime\SmokeProtocol.cs'),(Join-Path $repoRoot 'tests\SmokeProtocolTests.cs'));Refs=@()},    @{Name='CosmeticPolicyTests';Sources=@((Join-Path $repoRoot 'runtime\CosmeticPolicy.cs'),(Join-Path $repoRoot 'tests\CosmeticPolicyTests.cs'));Refs=@()},
    @{Name='BusArticulationRuntimeTests';Sources=@((Join-Path $repoRoot 'runtime\BusArticulation.cs'),(Join-Path $repoRoot 'runtime\BusArticulationPolicy.cs'),(Join-Path $repoRoot 'tests\TestBusArticulationRuntime.cs'));Refs=@()},
    @{Name='BusArticulationPolicyTests';Sources=@((Join-Path $repoRoot 'runtime\BusArticulationPolicy.cs'),(Join-Path $repoRoot 'tests\TestBusArticulationPolicy.cs'));Refs=@()},
    @{Name='LargeRockClearanceTests';Sources=@($largeRockClearance,$worldRelocationLedger,(Join-Path $repoRoot 'tests\LargeRockClearanceTests.cs'));Refs=@()},
    @{Name='StreamingBudgetTests';Sources=@($streamingBudget,(Join-Path $repoRoot 'tests\StreamingBudgetTests.cs'));Refs=@()},
    @{Name='TerrainPadBlendTests';Sources=@($worldBiomePolicy,$worldPlacementPolicy,(Join-Path $repoRoot 'runtime\TerrainPadBlend.cs'),(Join-Path $repoRoot 'tests\TerrainPadBlendTests.cs'));Refs=$nativeRefs},
    @{Name='WorldPlacementPolicyTests';Sources=@($worldBiomePolicy,$worldPlacementPolicy,(Join-Path $repoRoot 'tests\WorldPlacementPolicyTests.cs'));Refs=$nativeRefs},
    @{Name='PoiSlopePlacementTests';Sources=@($worldBiomePolicy,$worldPlacementPolicy,$roadTerrainClearance,$poiSlopePlacement,$roadStateEpoch,$worldRelocationLedger,(Join-Path $repoRoot 'tests\PoiSlopePlacementTests.cs'));Refs=$nativeRefs},
    @{Name='PoiSlopePlacementReplayTests';Sources=@($worldPlacementPolicy,$poiSlopePlacement,$roadStateEpoch,$worldRelocationLedger,(Join-Path $repoRoot 'tests\PoiSlopePlacementReplayTests.cs'));Refs=@()},
    @{Name='PoiSlopePlacementCacheTests';Sources=@($worldPlacementPolicy,$poiSlopePlacement,$roadStateEpoch,$worldRelocationLedger,(Join-Path $repoRoot 'tests\PoiSlopePlacementCacheTests.cs'));Refs=@()},
    @{Name='PoiSaveRestoreTests';Sources=@((Join-Path $repoRoot 'runtime\PoiSaveRestore.cs'),(Join-Path $repoRoot 'tests\PoiSaveRestoreTests.cs'));Refs=@()},
    @{Name='WorldBiomeSaveTests';Sources=@($worldBiomePolicy,(Join-Path $repoRoot 'tests\WorldBiomeSaveTests.cs'));Refs=$nativeRefs},
    @{Name='WorldBiomePolicyTests';Sources=@($worldBiomePolicy,(Join-Path $repoRoot 'tests\WorldBiomePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='SteamJoinSimulationTests';Sources=@($steamSdrTransport,$netDeliveryScheduler,$net,$core,$packets,$pairProto,$deferredCore,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamSocketSimulation.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamJoinSimulationTests.cs'));Refs=@()},
    @{Name='JoinCompatibilityTests';Sources=@($net,$core,$packets,$pairProto,$deferredCore,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\JoinCompatibilityTests.cs'));Refs=@()},
    @{Name='SteamP2PTransportTests';Sources=@($steamSdrTransport,$netDeliveryScheduler,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SteamP2PTransport.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamSocketSimulation.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamP2PSimulation.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamP2PTransportTests.cs'));Refs=@()},
    @{Name='SteamConnectionDirectionTests';Sources=@($steamSdrTransport,$netDeliveryScheduler,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamConnectionDirectionTests.cs'));Refs=$nativeRefs},
    @{Name='StableCoreTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StableCoreTests.cs'));Refs=@()},
    @{Name='SpawnTransferTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SpawnTransferTests.cs'));Refs=@()},
    @{Name='SpawnTransportTests';Sources=@($steamSdrTransport,$netDeliveryScheduler,$net,$core,$packets,$pairProto,$loopback,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SteamSocketSimulation.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SpawnTransportTests.cs'));Refs=@()},
    @{Name='StableAuditTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StableAuditTests.cs'));Refs=@()},
    @{Name='StableJournalTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StableJournalTests.cs'));Refs=@()},
    @{Name='ReliableOutboxTests';Sources=@($deferredCore,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ReliableOutboxTests.cs'));Refs=@()},
    @{Name='LoopbackTests';Sources=@($loopback,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\LoopbackTests.cs'));Refs=@()},
    @{Name='LoopbackIntegrationTests';Sources=@($net,$core,$packets,$pairProto,$loopback,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\LoopbackIntegrationTests.cs'));Refs=@()},
    @{Name='RabbitTransportTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RabbitTransportTests.cs'));Refs=@()},
    @{Name='LoopbackBackpressureTests';Sources=@($loopback,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\LoopbackBackpressureTests.cs'));Refs=@()},
    @{Name='StableFailureTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StableFailureTests.cs'));Refs=@()},
    @{Name='JoinStageBoundTests';Sources=@($net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\JoinStageBoundTests.cs'));Refs=@()},
    @{Name='NetCoreTests';Sources=@($net,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NetCoreTests.cs'));Refs=@()},
    @{Name='StableWireTests';Sources=@($net,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StableWireProductionTests.cs'));Refs=$nativeRefs},
    @{Name='SyncItemSchedulerTests';Sources=@($syncItemScheduler,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SyncItemSchedulerTests.cs'));Refs=@()},
    @{Name='TerrainDistanceTests';Sources=@($terrainPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainDistanceTests.cs'));Refs=@()},
    @{Name='TerrainMembershipTests';Sources=@($terrainMembership,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainMembershipTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainWorkDiagnosticsTests';Sources=@($terrainWorkDiagnostics,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ModDiagnostics.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainWorkDiagnosticsTests.cs'));Refs=$nativeRefs},
    @{Name='NativeLoopTimingsTests';Sources=@($nativeLoopTimings,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeLoopTimingsTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='SpawnClearanceTests';Sources=@($spawnClearance,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SpawnClearanceTests.cs'));Refs=@()},
    @{Name='NativeSpawnSafetyTests';Sources=@($nativeSpawnSafety,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeSpawnSafetyTests.cs'));Refs=@()},
    @{Name='SlowFrameDiagnosticsTests';Sources=@($slowFrameDiagnostics,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SlowFrameDiagnosticsTests.cs'));Refs=@()},
    @{Name='NativeScriptTimingsTests';Sources=@($nativeScriptTimings,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeScriptTimingsTests.cs'));Refs=@()},
    @{Name='PendingAttachTests';Sources=@($pendingAttach,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PendingAttachTests.cs'));Refs=@()},
    @{Name='ItemRecoveryBridgeTests';Sources=@($pendingAttach,$attachReplay,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ItemRecoveryBridgeTests.cs'));Refs=@()},
    @{Name='VehicleMotionTests';Sources=@($net,$packets,$pairProto,$poseSequence,$liveUsable,$remotePoseBuffer,$carPosePolicy,$patchOptions,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\VehicleMotionTests.cs'));Refs=$nativeRefs},
    @{Name='VehicleMotionLoopbackTests';Sources=@($net,$packets,$pairProto,$poseSequence,$liveUsable,$remotePoseBuffer,$carPosePolicy,$patchOptions,$loopback,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\VehicleMotionLoopbackTests.cs'));Refs=$nativeRefs},
    @{Name='PhaseTimingsTests';Sources=@((Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\PhaseTimings.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\ModDiagnostics.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PhaseTimingsTests.cs'));Refs=@()},
    @{Name='ItemIndexTableTests';Sources=@($itemIndexTable,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ItemIndexTableTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainHoleAdjacencyTests';Sources=@($terrainHoleAdjacency,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainHoleAdjacencyTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainRoadSelectionTests';Sources=@($terrainRoadSelection,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainRoadSelectionTests.cs'));Refs=$nativeRefs},
    @{Name='RoadChunkPolicyTests';Sources=@($roadChunkPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RoadChunkPolicyTests.cs'));Refs=$nativeRefs},
    @{Name='TrailerSyncTests';Sources=@($trailerSyncPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TrailerSyncTests.cs'));Refs=@()},
    @{Name='CarPosePolicyTests';Sources=@($patchOptions,$carPosePolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\CarPosePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='PlayerLeavePolicyTests';Sources=@($playerLeaveSafety,$collisionRestoreCache,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PlayerLeavePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='ItemAskPolicyTests';Sources=@($itemAskSafety,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ItemAskPolicyTests.cs'));Refs=@()},
    @{Name='NativeStoreGateTests';Sources=@($nativeStoreGate,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeStoreGateTests.cs'));Refs=@()},
    @{Name='RoadLiveCacheTests';Sources=@($roadLiveCache,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RoadLiveCacheTests.cs'));Refs=$nativeRefs},
    @{Name='RoadFarSkipTests';Sources=@($roadFarSkip,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RoadFarSkipTests.cs'));Refs=$nativeRefs},
    @{Name='RenderCostPolicyTests';Sources=@($cameraCostCore,$mirrorOnScreen,$roadBake,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RenderCostPolicyTests.cs'));Refs=$nativeRefs},
    @{Name='RuntimePolicyTests';Sources=@($renderSwitches,$smallFixes,$patchOptions,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RuntimePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='ProfilerSlotPolicyTests';Sources=@($profilerSlotPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ProfilerSlotPolicyTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='NativeAttachmentGuardTests';Sources=@($nativeAttachmentGuard,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeAttachmentGuardTests.cs'));Refs=@()},
    @{Name='NativeSeatSafetyTests';Sources=@($nativeSeatSafety,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeSeatSafetyTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='MenuLayoutPolicyTests';Sources=@($menuLayoutPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\MenuLayoutPolicyTests.cs'));Refs=@()},
    @{Name='ContentFingerprintTests';Sources=@($net,$core,$packets,$pairProto,$contentFingerprint,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ContentFingerprintTests.cs'));Refs=@()},
    @{Name='NetLogLimiterTests';Sources=@($netLogLimiter,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NetLogLimiterTests.cs'));Refs=@()},
    @{Name='FeedbackPolicyTests';Sources=@($feedbackPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\FeedbackPolicyTests.cs'));Refs=@()},
    @{Name='StreamScanPolicyTests';Sources=@($streamScanPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\StreamScanPolicyTests.cs'));Refs=@()},
    @{Name='TerrainAlignPolicyTests';Sources=@($terrainAlignPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainAlignPolicyTests.cs'));Refs=@()},
    @{Name='ItemStreamPolicyTests';Sources=@($itemStreamPolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ItemStreamPolicyTests.cs'));Refs=@()},
    @{Name='ItemSettlePolicyTests';Sources=@($itemSettlePolicy,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\ItemSettlePolicyTests.cs'));Refs=@()},
    @{Name='FarPlaceQueueTests';Sources=@($farPlaceQueue,$spawnCoordinator,$nativeSpawnAdapter,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\FarPlaceQueueTests.cs'));Refs=$nativeRefs},
    @{Name='WorldRelocationLedgerTests';Sources=@($worldRelocationLedger,(Join-Path $repoRoot 'tests\WorldRelocationLedgerTests.cs'));Refs=@()},
    @{Name='UrineLiquidTests';Sources=@((Join-Path $repoRoot 'runtime\UrineLiquid.cs'),(Join-Path $repoRoot 'tests\UrineLiquidTests.cs'));Refs=@()},
    @{Name='TerrainWorkerParityTests';Sources=@($terrainWorker,$worldBiomePolicy,$worldPlacementPolicy,$roadTerrainClearance,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainWorkerParityTests.cs'));Refs=$nativeRefs},
    @{Name='RemotePoseBufferTests';Sources=@($remotePoseBuffer,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\RemotePoseBufferTests.cs'));Refs=@()},
    @{Name='NetUpdateCostTests';Sources=@($net,$packets,$pairProto,$collisionRestoreCache,$netStats,$patchOptions,$carPosePolicy,$remotePoseBuffer,$netDeliveryScheduler,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NetUpdateCostTests.cs'));Refs=$nativeRefs},
    @{Name='NativeStoreTextTests';Sources=@($nativeStoreText,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeStoreTextTests.cs'));Refs=$nativeRefs},
    @{Name='PoseSequenceTests';Sources=@($poseSequence,$liveUsable,$remotePoseBuffer,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PoseSequenceTests.cs'));Refs=$nativeRefs},
    @{Name='CarCargoTests';Sources=@($net,$packets,$pairProto,$carCargo,$poseSequence,$liveUsable,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\CarCargoTests.cs'));Refs=$nativeRefs},
    @{Name='LiveUsableTests';Sources=@($liveUsable,$poseSequence,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\LiveUsableTests.cs'));Refs=$nativeRefs},
    @{Name='PlacementSkipTests';Sources=@($placementSkip,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PlacementSkipTests.cs'));Refs=$nativeRefs},
    @{Name='CarOwnershipTests';Sources=@($carOwnership,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\CarOwnershipTests.cs'));Refs=$nativeRefs},
    @{Name='WorldJumpTests';Sources=@($netStats,$collisionRestoreCache,$remotePoseBuffer,$worldJump,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WorldJumpTests.cs'));Refs=$nativeRefs},
    @{Name='WildlifeWeatherTests';Sources=@($patchInfo,$patchOptions,$wildlifeWeather,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WildlifeWeatherTests.cs'));Refs=$nativeRefs},
    @{Name='DrunkPolicyTests';Sources=@($drunkPolicy,$patchInfo,$patchOptions,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\DrunkPolicyTests.cs'));Refs=@()},
    @{Name='MultiplayerMenuSkinTests';Sources=@($mpSkinPolicy,(Join-Path $repoRoot 'tests\MultiplayerMenuSkinTests.cs'));Refs=@()},
    @{Name='PatchOptionsTests';Sources=@($patchInfo,$patchOptions,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\PatchOptionsTests.cs'));Refs=@()},
    @{Name='SpawnCoordinatorTests';Sources=@($spawnCoordinator,$nativeSpawnAdapter,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SpawnCoordinatorTests.cs'));Refs=@()}
)
foreach ($test in $tests) {
    $exe=Join-Path $run ($test.Name+'.exe')
    Run $csc (@('/nologo','/optimize+',"/out:$exe")+$test.Refs+$test.Sources)
    & (Test-ExePath $exe) $managed $baseline.original 2>&1 | Tee-Object -FilePath $results -Append
    if ($LASTEXITCODE -ne 0) { throw "$($test.Name) failed." }
}
# Compiler-generated module IDs differ between machines. Compare the exact source
# signature in the handshake instead of hashing our locally compiled helper DLL.
$signatureInputs=@('runtime\PoiSaveRestore.cs','runtime\RabbitProtocol.cs','runtime\RabbitMetadataSync.cs','runtime\RabbitFlowDiagnostics.cs','runtime\BusRearAuthority.cs','runtime\RabbitContact.cs','runtime\RabbitCombatReach.cs','runtime\RabbitAttackDiagnostics.cs','runtime\BusPairProtocol.cs','runtime\BusPairReplica.cs','runtime\BusSeatClaims.cs','runtime\MovingVehiclePassenger.cs','runtime\MovingVehiclePassengerPolicy.cs','runtime\RabbitGuestAnimation.cs','runtime\RabbitMotionProtocol.cs','runtime\RabbitMotionRuntime.cs','runtime\RabbitPlayers.cs','runtime\RabbitLocomotion.cs','runtime\BusArticulation.cs','runtime\BusArticulationPolicy.cs','runtime\SmokeProtocol.cs','runtime\SmokeRuntime.cs','runtime\CosmeticPolicy.cs','runtime\CosmeticReplay.cs','runtime\GrassTransition.cs','runtime\RabbitAi.cs','runtime\RabbitNetwork.cs','runtime\RabbitReplicas.cs','runtime\RabbitPopulation.cs','runtime\SurvivalHud.cs','runtime\DrunkPolicy.cs','runtime\DrunkEffects.cs','runtime\MultiplayerMenuSkinPolicy.cs','runtime\MultiplayerMenuSkin.cs','runtime\SurvivalRates.cs','runtime\TerrainPadBlend.cs','runtime\WorldRelocationLedger.cs', 'runtime\NpcVehicleImpact.cs', 'runtime\UrineLiquid.cs','runtime\ParticleFluidDrinking.cs','runtime\LargeRockClearance.cs','runtime\StreamingBudget.cs','runtime\WorldPlacementPolicy.cs','runtime\RoadTerrainClearance.cs','runtime\PoiSlopePlacement.cs','runtime\RoadStateEpoch.cs','runtime\SteamP2PTransport.cs','runtime\SteamSelfTest.cs','runtime\FixedPropConstruction.cs','runtime\NetCore.cs','runtime\StableCore.cs','runtime\ExperimentalBackend.cs','runtime\ExperimentalCoordinator.cs','runtime\ExperimentalAuthority.cs','runtime\ExperimentalSession.cs','runtime\ExperimentalGraph.cs','runtime\ExperimentalGraphTransfer.cs','runtime\ExperimentalHandoff.cs','runtime\ExperimentalIdentity.cs','runtime\ExperimentalJoin.cs','runtime\ExperimentalSnapshot.cs','runtime\StablePackets.cs','runtime\StableGameBridge.cs','runtime\StableMap.cs','runtime\StableRoadCodec.cs','runtime\DeferredCore.cs','runtime\LoopbackTransport.cs','runtime\TerrainDistancePolicy.cs','runtime\CargoDiagnostics.cs','runtime\FrameDiagnostics.cs','runtime\ModDiagnostics.cs','runtime\PhaseTimings.cs','runtime\AttachDiagnostics.cs','runtime\PendingAttach.cs','runtime\AttachReplay.cs','runtime\GroundDiagnostics.cs','runtime\SyncItemScheduler.cs','runtime\NativeLoopTimings.cs','runtime\NativeScriptTimings.cs','runtime\SlowFrameDiagnostics.cs','runtime\ItemIndexTable.cs','runtime\TerrainHoleAdjacency.cs','runtime\TerrainRoadSelection.cs','runtime\RendererCensus.cs','runtime\RoadChunkPolicy.cs','runtime\CarPosePolicy.cs','runtime\PlayerLeaveSafety.cs','runtime\ItemAskSafety.cs','runtime\NativeStoreGate.cs','runtime\NativeStoreAdapter.cs','runtime\NativeStoreText.cs','runtime\PatchInfo.cs','runtime\PatchOptions.cs','runtime\PatchOptionsMenu.cs','runtime\PatchSurface.cs','runtime\JoinPhysics.cs','runtime\CargoGhost.cs','runtime\RoadLiveCache.cs','runtime\RoadFarSkip.cs','runtime\CameraCostCore.cs','runtime\CameraCost.cs','runtime\MirrorOnScreen.cs','runtime\RoadBake.cs','runtime\RenderSwitches.cs','runtime\SmallFixes.cs','runtime\TrailerSyncPolicy.cs','runtime\TrailerSync.cs','runtime\ProfilerSlotPolicy.cs','runtime\TerrainMembership.cs','runtime\TerrainWorkDiagnostics.cs','runtime\ContentFingerprint.cs','runtime\NetDeliveryScheduler.cs','runtime\SteamSdrTransport.cs','runtime\NativeSpawnSafety.cs','runtime\NativeSeatSafety.cs','runtime\NativeAttachmentGuard.cs','runtime\NativeSafetyAdapter.cs','runtime\SpawnClearance.cs','runtime\SpawnClearanceAdapter.cs','runtime\WildlifeWeather.cs','runtime\MenuLayoutPolicy.cs','runtime\MenuLayout.cs','runtime\MenuProbe.cs','runtime\GrassTransitionProbe.cs','runtime\SpawnProbe.cs','runtime\SteamInterfaceCompat.cs','runtime\NetLogLimiter.cs','runtime\FeedbackPolicy.cs','runtime\FeedbackBundle.cs','runtime\ItemStreamPolicy.cs','runtime\TerrainAlignPolicy.cs','runtime\StreamScanPolicy.cs','runtime\ItemSettlePolicy.cs','runtime\FarPlaceQueue.cs','runtime\TerrainWorker.cs','runtime\WorldBiomePolicy.cs','runtime\RemotePoseBuffer.cs','runtime\RemoteSmoothing.cs','runtime\CollisionRestoreCache.cs','runtime\NetStats.cs','runtime\WorldJump.cs','runtime\PoseSequence.cs','runtime\PartsCensus.cs','runtime\CarOwnership.cs','runtime\PlacementSkip.cs','runtime\LiveUsable.cs','runtime\CarCargo.cs','runtime\SpawnCoordinator.cs','runtime\NativeSpawnAdapter.cs','runtime\ExperimentalItems.cs','runtime\ExperimentalPlayers.cs','runtime\ExperimentalBreakables.cs','runtime\ExperimentalItemStream.cs','runtime\ExperimentalItemRelay.cs','runtime\ExperimentalInterest.cs','runtime\ExperimentalReplication.cs','runtime\ExperimentalPlayerReplication.cs','tools\PatchStability.cs','tools\VerifyStability.cs')
$signatureText=($signatureInputs | ForEach-Object { $_+':'+(Get-FileHash -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) $_) -Algorithm SHA256).Hash }) -join "`n"
$hasher=[Security.Cryptography.SHA256]::Create()
try {$sourceSignature=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($signatureText))).Replace('-','')} finally {$hasher.Dispose()}
$validationInputs=@(
    @(Get-ChildItem -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests') -File | Sort-Object Name | ForEach-Object {'tests\'+$_.Name})+
    @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name | ForEach-Object {'tests\scripts\'+$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools') -File | Sort-Object Name | ForEach-Object {'tools\'+$_.Name})+
    @(Get-ChildItem -LiteralPath (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Filter '*.ps1' -File | Sort-Object Name | ForEach-Object {$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\singleplayer') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {'packaging\singleplayer\'+$_.Name})
)
$validationText=($validationInputs | ForEach-Object {
    $_+':'+(Get-FileHash -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) $_) -Algorithm SHA256).Hash
}) -join "`n"
$validationHasher=[Security.Cryptography.SHA256]::Create()
try {$validationSignature=[BitConverter]::ToString($validationHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($validationText))).Replace('-','')} finally {$validationHasher.Dispose()}
$identity=Join-Path $run 'BuildIdentity.cs'
Set-Content -LiteralPath $identity -Encoding UTF8 -Value ('namespace TldMp { internal static class BuildIdentity { public const string Sources="'+$sourceSignature+'"; } }')
# The news window prepends these notes to the game's own news.txt at runtime.
$notes=Join-Path $run 'PatchNotes.cs'
$notesB64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText((Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'CHANGELOG.md'))))
Set-Content -LiteralPath $notes -Encoding UTF8 -Value ('namespace TldMp { internal static class PatchNotes { public const string ChangelogBase64="'+$notesB64+'"; } }')
$runtime=Join-Path $run 'Tld.Multiplayer.dll'
Run $csc @('/nologo','/target:library','/optimize+',"/out:$runtime","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\UnityEngine.PhysicsModule.dll","/reference:$managed\UnityEngine.AnimationModule.dll","/reference:$managed\UnityEngine.AudioModule.dll","/reference:$managed\UnityEngine.VehiclesModule.dll","/reference:$managed\UnityEngine.ParticleSystemModule.dll","/reference:$managed\UnityEngine.JSONSerializeModule.dll","/reference:$managed\com.rlabrecque.steamworks.net.dll","/reference:$managed\UnityEngine.UI.dll","/reference:$managed\UnityEngine.UIModule.dll","/reference:$managed\UnityEngine.ScreenCaptureModule.dll","/reference:$managed\netstandard.dll","/reference:$managed\System.IO.Compression.dll","/reference:$managed\Unity.TextMeshPro.dll","/reference:$managed\UnityEngine.InputLegacyModule.dll","/reference:$managed\UnityEngine.ImageConversionModule.dll",(Join-Path $repoRoot 'runtime\PoiSaveRestore.cs'),(Join-Path $repoRoot 'runtime\BusArticulation.cs'),(Join-Path $repoRoot 'runtime\BusArticulationPolicy.cs'),(Join-Path $repoRoot 'runtime\SmokeProtocol.cs'),(Join-Path $repoRoot 'runtime\SmokeRuntime.cs'),(Join-Path $repoRoot 'runtime\CosmeticPolicy.cs'),(Join-Path $repoRoot 'runtime\CosmeticReplay.cs'),(Join-Path $repoRoot 'runtime\TerrainPadBlend.cs'),(Join-Path $repoRoot 'runtime\SurvivalRates.cs'),(Join-Path $repoRoot 'runtime\SurvivalHud.cs'),(Join-Path $repoRoot 'runtime\DrunkPolicy.cs'),(Join-Path $repoRoot 'runtime\DrunkEffects.cs'),(Join-Path $repoRoot 'runtime\MultiplayerMenuSkinPolicy.cs'),(Join-Path $repoRoot 'runtime\MultiplayerMenuSkin.cs'),(Join-Path $repoRoot 'runtime\GrassTransition.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\RabbitPopulation.cs'),$worldRelocationLedger,(Join-Path $repoRoot 'runtime\NpcVehicleImpact.cs'),(Join-Path $repoRoot 'runtime\UrineLiquid.cs'),(Join-Path $repoRoot 'runtime\ParticleFluidDrinking.cs'),$experimentalBackend,$experimentalCoordinator,$experimentalAuthority,$experimentalSession,$experimentalGraph,$experimentalGraphTransfer,$experimentalHandoff,$experimentalIdentity,$experimentalJoin,$experimentalSnapshot,$experimentalItems,$experimentalPlayers,$experimentalBreakables,$experimentalItemStream,$experimentalItemRelay,$experimentalInterest,$experimentalReplication,$experimentalPlayerReplication,$net,$core,$packets,$bridge,$map,$roadCodec,$deferredCore,$loopback,$terrainPolicy,$cargoDiagnostics,$frameDiagnostics,$syncItemScheduler,$nativeLoopTimings,$nativeScriptTimings,$slowFrameDiagnostics,$itemIndexTable,$terrainHoleAdjacency,$terrainRoadSelection,$rendererCensus,$roadChunkPolicy,$carPosePolicy,$playerLeaveSafety,$itemAskSafety,$nativeStoreGate,$nativeStoreAdapter,$nativeStoreText,$patchInfo,$patchOptions,$patchOptionsMenu,$patchSurface,$joinPhysics,$cargoGhost,$roadLiveCache,$roadFarSkip,$cameraCostCore,$cameraCost,$mirrorOnScreen,$roadBake,$renderSwitches,$smallFixes,$profilerSlotPolicy,$terrainMembership,$terrainWorkDiagnostics,$terrainWorker,$worldBiomePolicy,$worldPlacementPolicy,$roadTerrainClearance,$poiSlopePlacement,$roadStateEpoch,$largeRockClearance,$streamingBudget,$contentFingerprint,$netDeliveryScheduler,$steamSdrTransport,(Join-Path $repoRoot 'runtime\SteamP2PTransport.cs'),(Join-Path $repoRoot 'runtime\SteamSelfTest.cs'),$nativeSpawnSafety,$nativeSeatSafety,$nativeAttachmentGuard,$nativeSafetyAdapter,$spawnClearance,$spawnClearanceAdapter,$wildlifeWeather,$menuLayoutPolicy,$menuLayout,$menuProbe,$grassProbe,$spawnProbe,$steamInterfaceCompat,$netLogLimiter,$feedbackPolicy,$feedbackBundle,$itemStreamPolicy,$terrainAlignPolicy,$itemSettlePolicy,$farPlaceQueue,$remotePoseBuffer,$remoteSmoothing,$collisionRestoreCache,$netStats,$worldJump,$poseSequence,$partsCensus,$carOwnership,$placementSkip,$liveUsable,$carCargo,$spawnCoordinator,$nativeSpawnAdapter,$fixedPropConstruction,$modDiagnostics,$phaseTimings,$groundDiagnostics,$attachDiagnostics,$pendingAttach,$attachReplay,$streamScanPolicy,(Join-Path $repoRoot 'runtime\RabbitCombatReach.cs'),(Join-Path $repoRoot 'runtime\RabbitLocomotion.cs'),(Join-Path $repoRoot 'runtime\RabbitPlayers.cs'),(Join-Path $repoRoot 'runtime\RabbitMotionProtocol.cs'),(Join-Path $repoRoot 'runtime\RabbitMotionRuntime.cs'),(Join-Path $repoRoot 'runtime\RabbitGuestAnimation.cs'),(Join-Path $repoRoot 'runtime\MovingVehiclePassenger.cs'),(Join-Path $repoRoot 'runtime\MovingVehiclePassengerPolicy.cs'),(Join-Path $repoRoot 'runtime\BusRearAuthority.cs'),(Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\BusPairReplica.cs'),(Join-Path $repoRoot 'runtime\BusSeatClaims.cs'),(Join-Path $repoRoot 'runtime\RabbitContact.cs'),(Join-Path $repoRoot 'runtime\RabbitAttackDiagnostics.cs'),(Join-Path $repoRoot 'runtime\RabbitFlowDiagnostics.cs'),(Join-Path $repoRoot 'runtime\RabbitProtocol.cs'),(Join-Path $repoRoot 'runtime\RabbitMetadataSync.cs'),$trailerSyncPolicy,$trailerSync,$identity,$notes)
# v4 is built later (Stage-NativeSafetyBuild.ps1 / the installer) and calls back into
# the runtime. A missing callback makes Mono reject the whole patched method at run
# time (the save button silently did nothing), so require every one here.
$callbackCheck=Join-Path $toolDir 'CheckRuntimeCallbacks.exe'
Run $csc @('/nologo','/optimize+',"/out:$callbackCheck","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\CheckRuntimeCallbacks.cs'))
Run $callbackCheck @($runtime,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\singleplayer')) 2>&1 | Tee-Object -FilePath $results -Append
& (Join-Path $PSScriptRoot 'Test-NativeSafetyAdapter.ps1') 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Native safety production adapter suite failed.'}
& (Join-Path $PSScriptRoot 'Test-PlacementCenters.ps1') -Runtime $runtime -ManagedDirectory $managed -BuildDirectory (Join-Path $run 'placement-centers') 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Placement-center parity suite failed.'}
& (Join-Path $PSScriptRoot 'Test-OwnershipPerformance.ps1') -BuildDirectory $run -ManagedDirectory $managed 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Ownership/performance regression suite failed.'}
$serializationTests=Join-Path $run 'WorldSerializationTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$serializationTests","/reference:$runtime","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WorldSerializationTests.cs'))
& (Test-ExePath $serializationTests) $managed $baseline.original 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'World/chunk serialization regression failed.'}
$generationTests=Join-Path $run 'WorldGenerationReplay.exe'
Copy-Item -LiteralPath $cecil -Destination (Join-Path $run 'Mono.Cecil.dll') -Force
Run $csc @('/nologo','/nowarn:0436','/optimize+',"/out:$generationTests","/reference:$cecil","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WorldGenerationReplay.cs'))
& (Test-ExePath $generationTests) $managed $baseline.original $runtime 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Host generation lifecycle regression failed.'}
$patcher=Join-Path $toolDir 'PatchStability.exe'
$verifier=Join-Path $toolDir 'VerifyStability.exe'
Run $csc @('/nologo','/optimize+',"/out:$patcher","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\PatchStability.cs'))
Run $csc @('/nologo','/optimize+','/main:VerifyStability',"/out:$verifier","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\VerifyPatch.cs'),(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\VerifyStability.cs'))
$patched=Join-Path $run 'Assembly-CSharp.dll'
Run $patcher @($baseline.patched,$runtime,$patched,$managed)
& (Test-ExePath $verifier) $baseline.patched $patched 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'V3 hook verification failed.' }
$fixedPropTests=Join-Path $run 'FixedPropConstructionTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$fixedPropTests",$fixedPropConstruction,(Join-Path $repoRoot 'tests\FixedPropConstructionTests.cs'))
Run $fixedPropTests @()
$fixedSpawnTests=Join-Path $run 'FixedSpawnHelperTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$fixedSpawnTests","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\FixedSpawnHelperTests.cs'))
& (Test-ExePath $fixedSpawnTests) $runtime $patched 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Fixed-spawn/fence production IL regression failed.'}
$systemTimingTests=Join-Path $run 'SystemTimingInstrumentationTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$systemTimingTests","/reference:$cecil",(Join-Path $repoRoot 'tests\SystemTimingInstrumentationTests.cs'))
& (Test-ExePath $systemTimingTests) $baseline.patched $patched $runtime (Test-ExePath $verifier) (Join-Path $run 'system-timing-mutations') $managed 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'System timing instrumentation regression failed.'}
& (Join-Path $PSScriptRoot 'Test-RoadCallbackInstrumentation.ps1') -BaselineAssembly $baseline.patched -CandidateAssembly $patched -CecilPath $cecil -BuildDirectory (Join-Path $env:TEMP ('opencode\road-callback-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Road callback instrumentation regression failed.'}
& (Join-Path $PSScriptRoot 'Test-RandomFlyerKinematicGuard.ps1') -OriginalAssembly $baseline.patched -CandidateAssembly $patched -Runtime $runtime -ManagedDirectory $managed -BuildDirectory (Join-Path $env:TEMP ('opencode\flyer-guard-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Flyer kinematic guard regression failed.'}
& (Join-Path $PSScriptRoot 'Test-FlyerProductionBehavior.ps1') -BaselineAssembly $baseline.patched -CandidateAssembly $patched -BuildDirectory (Join-Path $env:TEMP ('opencode\flyer-behavior-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Flyer production behavior regression failed.'}
# Execute the shipped Mono JIT against real patched bodies. CLR/IL parity alone
# does not detect observer instructions accidentally inside a finally handler.
$ehTamper=Join-Path $run 'TamperObserverExceptionBoundary.exe'
Run $csc @('/nologo','/optimize+',"/out:$ehTamper","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TamperObserverExceptionBoundary.cs'))
$badEh=Join-Path $run 'Assembly-CSharp.malformed-eh.dll'
Run $ehTamper @($patched,$badEh,$managed)
$ehNegativeLog=Join-Path $run 'negative-control-eh.txt'
$ehPreviousErrorAction=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & (Test-ExePath $verifier) $baseline.patched $badEh > $ehNegativeLog 2>&1
    $ehRejected=$LASTEXITCODE -ne 0
}finally{$ErrorActionPreference=$ehPreviousErrorAction}
if(!$ehRejected -or (Get-Content -LiteralPath $ehNegativeLog -Raw) -notmatch 'exception-region end boundary'){throw 'Verifier did not reject malformed observer exception boundary.'}
'PASS: verifier rejects malformed observer exception-boundary negative control.' | Tee-Object -FilePath $results -Append
& (Join-Path $PSScriptRoot 'Test-MonoJit.ps1') -OriginalAssembly $baseline.patched -CandidateAssembly $patched -Runtime $runtime -GameDirectory $GameDirectory -BuildDirectory (Join-Path $env:TEMP ('opencode\mono-jit-'+(Split-Path $baseRun -Leaf))) -RejectedAssembly $badEh 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Shipped Mono JIT regression failed.'}
# Negative control: removing one diagnostic exit observer must be rejected by
# the same verifier that accepted the exact candidate.
$tamper=Join-Path $run 'TamperObserver.exe'
Run $csc @('/nologo','/optimize+',"/out:$tamper","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\TamperObserver.cs'))
$tampered=Join-Path $run 'Assembly-CSharp.tampered.dll'
$negativeLog=Join-Path $run 'negative-control.txt'
& (Test-ExePath $tamper) $patched $tampered 'objGenScript' 'UpdGen' '1' 'NativeObjGenEnd' $managed 2>&1 | Tee-Object -FilePath $negativeLog
if($LASTEXITCODE -ne 0){throw 'Could not build the negative-control tampered assembly.'}
$previousErrorAction=$ErrorActionPreference
$ErrorActionPreference='Continue'
try {
    & (Test-ExePath $verifier) $baseline.patched $tampered 2>&1 | Tee-Object -FilePath $negativeLog -Append
    $negativeExit=$LASTEXITCODE
} finally { $ErrorActionPreference=$previousErrorAction }
if($negativeExit -eq 0){throw 'Negative control: verifier accepted an assembly with a removed exit observer.'}
'PASS: negative control rejected a removed exit observer.' | Tee-Object -FilePath $results -Append
Write-Host 'PASS: negative control rejected a removed exit observer.'

$tamperedItem=Join-Path $run 'Assembly-CSharp.tampered-item.dll'
$itemNegativeLog=Join-Path $run 'negative-control-item.txt'
& (Test-ExePath $tamper) $patched $tamperedItem 'itemPlaceRemoveScript' 'PlaceStuff' '0' 'ItemSelectionBegin' $managed 2>&1 | Tee-Object -FilePath $itemNegativeLog
if($LASTEXITCODE -ne 0){throw 'Could not build the item-selection negative-control assembly.'}
$previousErrorAction=$ErrorActionPreference
$ErrorActionPreference='Continue'
try {
    & (Test-ExePath $verifier) $baseline.patched $tamperedItem 2>&1 | Tee-Object -FilePath $itemNegativeLog -Append
    $itemNegativeExit=$LASTEXITCODE
} finally { $ErrorActionPreference=$previousErrorAction }
if($itemNegativeExit -eq 0){throw 'Item-selection negative control: verifier accepted a missing Begin observer.'}
'PASS: item-selection negative control rejected a missing Begin observer.' | Tee-Object -FilePath $results -Append
Write-Host 'PASS: item-selection negative control rejected a missing Begin observer.'
& (Join-Path $PSScriptRoot 'Test-WorldCleanupPerformance.ps1') -OriginalAssembly $baseline.original -CandidateAssembly $patched -ManagedDirectory $managed -BuildDirectory (Join-Path $env:TEMP ('opencode\world-cleanup-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Production world-cleanup IL parity failed.'}
$extractor=Join-Path $toolDir 'ExtractReplay.exe'
Run $csc @('/nologo','/optimize+',"/out:$extractor","/reference:$cecil",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\ExtractReplay.cs'))
$creatureFixture=Join-Path $run 'CreatureMovementFixture.exe'
$creatureReplay=Join-Path $run 'CreatureMovementReplay.exe'
Run $csc @('/nologo','/optimize+',"/out:$creatureFixture",(Join-Path $repoRoot 'tests\CreatureMovementReplayTests.cs'))
Run $extractor @($patched,$creatureFixture,$creatureReplay,'creature')
Run $creatureReplay @()
$rabbitFixture=Join-Path $run 'RabbitAiReplayFixture.exe'
$rabbitBaseline=Join-Path $run 'RabbitAiBaseline.exe'
$rabbitCandidate=Join-Path $run 'RabbitAiCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$rabbitFixture",(Join-Path $repoRoot 'tests\RabbitAiReplayTests.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\StablePackets.cs'),(Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\NetCore.cs'))
Run $extractor @($baseline.original,$rabbitFixture,$rabbitBaseline,'rabbitai')
Run $extractor @($patched,$rabbitFixture,$rabbitCandidate,'rabbitai')
& (Test-ExePath $rabbitBaseline) 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit AI baseline replay failed.' }
& (Test-ExePath $rabbitCandidate) 'candidate' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit AI candidate replay failed.' }
$attackFixture=Join-Path $run 'RabbitAttackFlowFixture.exe'
$attackBaseline=Join-Path $run 'RabbitAttackFlowBaseline.exe'
$attackCandidate=Join-Path $run 'RabbitAttackFlowCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$attackFixture",(Join-Path $repoRoot 'tests\RabbitAttackFlowReplayTests.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),$packets,$pairProto,$net)
Run $extractor @($baseline.original,$attackFixture,$attackBaseline,'rabbitattack')
Run $extractor @($patched,$attackFixture,$attackCandidate,'rabbitattack')
& (Test-ExePath $attackBaseline) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Native rabbit attack baseline replay failed.' }
& (Test-ExePath $attackCandidate) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Native rabbit attack candidate replay failed.' }
$grassFixture=Join-Path $run 'GrassFixture.exe'
$grassBaseline=Join-Path $run 'GrassBaseline.exe'
$grassCandidate=Join-Path $run 'GrassCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$grassFixture",(Join-Path $repoRoot 'tests\GrassTransitionTests.cs'),(Join-Path $repoRoot 'runtime\GrassTransition.cs'))
Run $extractor @($baseline.original,$grassFixture,$grassBaseline,'grass')
Run $extractor @($patched,$grassFixture,$grassCandidate,'grass')
& (Test-ExePath $grassBaseline) 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Grass baseline replay failed.' }
& (Test-ExePath $grassCandidate) 'candidate' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Grass candidate replay failed.' }
$ragdollFixture=Join-Path $run 'RabbitRagdollFixture.exe'
$ragdollBaseline=Join-Path $run 'RabbitRagdollBaseline.exe'
$ragdollCandidate=Join-Path $run 'RabbitRagdollCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$ragdollFixture",(Join-Path $repoRoot 'tests\RabbitRagdollReplayTests.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\StablePackets.cs'),(Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\NetCore.cs'))
Run $extractor @($baseline.original,$ragdollFixture,$ragdollBaseline,'rabbitrags')
Run $extractor @($patched,$ragdollFixture,$ragdollCandidate,'rabbitrags')
& (Test-ExePath $ragdollBaseline) 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit ragdoll baseline replay failed.' }
& (Test-ExePath $ragdollCandidate) 'candidate' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit ragdoll candidate replay failed.' }
$guardFixture=Join-Path $run 'RabbitNetworkGuardFixture.exe'
$rabbitPolicy=Join-Path $run 'RabbitPolicy.exe'
Run $csc @('/nologo','/optimize+',"/out:$guardFixture","/reference:$cecil",(Join-Path $repoRoot 'tests\RabbitNetworkGuardTests.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\StablePackets.cs'),(Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\NetCore.cs'))
Run $extractor @($runtime,$guardFixture,$rabbitPolicy,'rabbitnetwork')
& (Test-ExePath $rabbitPolicy) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit network policy replay failed.' }
& (Test-ExePath $guardFixture) 'order' $runtime 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit network guard call-order checks failed.' }
$replicaFixture=Join-Path $run 'RabbitReplicaFixture.exe'
$replicaBaseline=Join-Path $run 'RabbitReplicaBaseline.exe'
$replicaCandidate=Join-Path $run 'RabbitReplicaCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$replicaFixture",(Join-Path $repoRoot 'tests\RabbitReplicaReplayTests.cs'),(Join-Path $repoRoot 'runtime\RabbitNetwork.cs'),(Join-Path $repoRoot 'runtime\RabbitReplicas.cs'),(Join-Path $repoRoot 'runtime\RabbitAi.cs'),(Join-Path $repoRoot 'runtime\StablePackets.cs'),(Join-Path $repoRoot 'runtime\BusPairProtocol.cs'),(Join-Path $repoRoot 'runtime\NetCore.cs'))
Run $extractor @($baseline.original,$replicaFixture,$replicaBaseline,'rabbitrep')
Run $extractor @($patched,$replicaFixture,$replicaCandidate,'rabbitrep')
& (Test-ExePath $replicaBaseline) 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit replica baseline replay failed.' }
& (Test-ExePath $replicaCandidate) 'candidate' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit replica candidate replay failed.' }
$spawnRun=Join-Path $run 'spawn-production-replay'
$populationFixture=Join-Path $run 'RabbitPopulationFixture.exe'
$populationBaseline=Join-Path $run 'RabbitPopulationBaseline.exe'
$populationCandidate=Join-Path $run 'RabbitPopulationCandidate.exe'
Run $csc @('/nologo','/optimize+',"/out:$populationFixture",(Join-Path $repoRoot 'tests\RabbitPopulationReplayTests.cs'),(Join-Path $repoRoot 'runtime\RabbitPopulation.cs'),$net,$core,$packets,$pairProto)
Run $extractor @($baseline.original,$populationFixture,$populationBaseline,'rabbitpop')
Run $extractor @($patched,$populationFixture,$populationCandidate,'rabbitpop')
& (Test-ExePath $populationBaseline) 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit population baseline replay failed.' }
& (Test-ExePath $populationCandidate) 'candidate' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Rabbit population candidate replay failed.' }
New-Item -ItemType Directory -Path $spawnRun | Out-Null
$spawnFixture=Join-Path $spawnRun 'SpawnProductionReplayFixture.exe'
Run $csc @('/nologo','/nowarn:0169','/optimize+',"/out:$spawnFixture",$net,$core,$packets,$pairProto,$pendingAttach,$spawnCoordinator,$nativeSpawnAdapter,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\SpawnProductionReplayFixture.cs'))
$spawnNative=Join-Path $spawnRun 'SpawnNative.exe'
Run $extractor @($baseline.original,$spawnFixture,$spawnNative,'spawnnative')
$spawnReplay=Join-Path $spawnRun 'SpawnProductionReplay.exe'
Run $extractor @($runtime,$spawnNative,$spawnReplay,'spawn')
& (Test-ExePath $spawnReplay) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Spawn production-method replay failed.' }
$vehicleFixture=Join-Path $run 'VehicleMotionReplayFixture.exe'
Run $csc @('/nologo','/nowarn:0649','/optimize+',"/out:$vehicleFixture",$net,$core,$packets,$pairProto,$poseSequence,$liveUsable,$deferredCore,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\VehicleMotionReplayFixture.cs'))
$vehicleReplay=Join-Path $run 'VehicleMotionReplay.exe'
Run $extractor @($runtime,$vehicleFixture,$vehicleReplay,'vehicle')
& (Test-ExePath $vehicleReplay) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'Production vehicle motion replay failed.' }
$worldRun=Join-Path $run 'world-load-replay'
New-Item -ItemType Directory -Path $worldRun | Out-Null
$worldFixture=Join-Path $worldRun 'WorldLoadReplayFixture.exe'
Run $csc @('/nologo','/nowarn:0649','/optimize+',"/out:$worldFixture","/reference:$managed\netstandard.dll",$net,$core,$packets,$pairProto,(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WorldLoadReplayFixture.cs'))
$worldNative=Join-Path $worldRun 'WorldLoadNative.exe'
Run $extractor @($baseline.original,$worldFixture,$worldNative,'worldnative')
$worldReplay=Join-Path $worldRun 'WorldLoadReplay.exe'
Run $extractor @($runtime,$worldNative,$worldReplay,'world')
& (Test-ExePath $worldReplay) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'World-load production-method replay failed.' }
$terrainReplay=Join-Path $run 'terrain-replay'
New-Item -ItemType Directory -Path $terrainReplay | Out-Null
$terrainTest=Join-Path $terrainReplay 'TerrainProductionTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$terrainTest","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\TerrainProductionTests.cs'))
& (Test-ExePath $terrainTest) $managed $baseline.original 'baseline' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) {throw 'Terrain baseline replay failed.'}
& (Test-ExePath $terrainTest) $managed (Test-LoadableCopy $patched) 'patched' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) {throw 'Patched terrain race test failed.'}
$terrainWorkerParity=Join-Path $run 'TerrainWorkerParityTests.exe'
& (Test-ExePath $terrainWorkerParity) $managed (Test-LoadableCopy $patched) 'patched' 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) {throw 'Terrain worker parity failed.'}
# The installer builds v3 with Build-Patch.ps1 on the player's machine. Its v1
# and v3 must be byte-identical to the assemblies this gate just tested.
$installerBuild=Join-Path $env:TEMP ('opencode\installer-build-'+(Split-Path $baseRun -Leaf))
& (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Build-Patch.ps1') -GameDirectory $GameDirectory -OutputDirectory $installerBuild 2>&1 | Tee-Object -FilePath $results -Append
$installerManifest=Get-Content -LiteralPath (Join-Path $installerBuild 'manifest.json') -Raw | ConvertFrom-Json
if($installerManifest.sourceSignature -ne $sourceSignature){throw 'Installer build source signature differs from this gate.'}
if($installerManifest.v1Sha256 -ne $baseline.patchedSha256 -or $installerManifest.patchedSha256 -ne (Get-FileHash -LiteralPath $patched).Hash){throw 'Installer build is not byte-identical to the tested v1/v3.'}
'PASS: installer source build (Build-Patch.ps1) is byte-identical to the tested v1/v3.' | Tee-Object -FilePath $results -Append
$manifest=[ordered]@{version=3;sourceSignature=$sourceSignature;validationSignature=$validationSignature;originalSha256=$baseline.originalSha256;patched=$patched;patchedSha256=(Get-FileHash -LiteralPath $patched).Hash;runtime=$runtime;runtimeSha256=(Get-FileHash -LiteralPath $runtime).Hash;results=$results;scope='Experimental native-restore build. Native gameplay path only. Offline protocol/codec/hook tests do not validate Unity physics, native JSON/load or generated mesh behavior.'}
$manifestPath=Join-Path $run 'manifest.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$testGame=Join-Path $run 'install-test'
$testManaged=Join-Path $testGame 'TheLongDrive_Data\Managed'
New-Item -ItemType Directory -Path $testManaged -Force | Out-Null
$testAssembly=Join-Path $testManaged 'Assembly-CSharp.dll'
Copy-Item -LiteralPath $baseline.original -Destination $testAssembly
$manager=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Manage-Stability.ps1'
& $manager -Action Install -GameDirectory $testGame -ManifestPath $manifestPath
& $manager -Action Install -GameDirectory $testGame -ManifestPath $manifestPath
& $manager -Action Status -GameDirectory $testGame
if ((Get-FileHash $testAssembly).Hash -ne $manifest.patchedSha256) { throw 'V3 install failed.' }
$testRuntime=Join-Path $testManaged 'Tld.Multiplayer.dll'
[IO.File]::AppendAllText($testRuntime,'tamper fixture')
$refused=$false
try {& $manager -Action Restore -GameDirectory $testGame} catch {$refused=$true}
if (!$refused -or (Get-FileHash $testAssembly).Hash -ne $manifest.patchedSha256) { throw 'V3 tamper protection failed.' }
Copy-Item -LiteralPath $runtime -Destination $testRuntime -Force
& $manager -Action Restore -GameDirectory $testGame
& $manager -Action Restore -GameDirectory $testGame
if ((Get-FileHash $testAssembly).Hash -ne $baseline.originalSha256 -or (Test-Path $testRuntime)) {throw 'V3 restore failed.'}
'PASS: v3 install/repeat/status/restore/repeat and altered-helper refusal on disposable files.' | Tee-Object -FilePath $results -Append
Set-Content -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.local-analysis\latest-stability.txt') -Value $run
Write-Host "V3 offline build passed. Results: $results"
Write-Host 'Experimental build only. Real game and saves have not been changed.'
