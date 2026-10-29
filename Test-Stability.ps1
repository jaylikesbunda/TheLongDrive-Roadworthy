[CmdletBinding()]
param([string]$GameDirectory='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
& (Join-Path $PSScriptRoot 'Test-Offline.ps1') -GameDirectory $GameDirectory
$baseRun=(Get-Content -LiteralPath (Join-Path $PSScriptRoot '.local-analysis\latest-run.txt') -Raw).Trim()
$baseline=Get-Content -LiteralPath (Join-Path $baseRun 'manifest.json') -Raw | ConvertFrom-Json
$run=Join-Path $baseRun 'stability-v3'
New-Item -ItemType Directory -Path $run | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed=Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$toolDir=Join-Path $PSScriptRoot '.tools\patcher'
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
$net=Join-Path $PSScriptRoot 'runtime\NetCore.cs'
$core=Join-Path $PSScriptRoot 'runtime\StableCore.cs'
$packets=Join-Path $PSScriptRoot 'runtime\StablePackets.cs'
$bridge=Join-Path $PSScriptRoot 'runtime\StableGameBridge.cs'
$map=Join-Path $PSScriptRoot 'runtime\StableMap.cs'
$roadCodec=Join-Path $PSScriptRoot 'runtime\StableRoadCodec.cs'
$deferredCore=Join-Path $PSScriptRoot 'runtime\DeferredCore.cs'
$loopback=Join-Path $PSScriptRoot 'runtime\LoopbackTransport.cs'
$terrainPolicy=Join-Path $PSScriptRoot 'runtime\TerrainDistancePolicy.cs'
$cargoDiagnostics=Join-Path $PSScriptRoot 'runtime\CargoDiagnostics.cs'
$frameDiagnostics=Join-Path $PSScriptRoot 'runtime\FrameDiagnostics.cs'
$modDiagnostics=Join-Path $PSScriptRoot 'runtime\ModDiagnostics.cs'
$groundDiagnostics=Join-Path $PSScriptRoot 'runtime\GroundDiagnostics.cs'
$attachDiagnostics=Join-Path $PSScriptRoot 'runtime\AttachDiagnostics.cs'
$pendingAttach=Join-Path $PSScriptRoot 'runtime\PendingAttach.cs'
$attachReplay=Join-Path $PSScriptRoot 'runtime\AttachReplay.cs'
$phaseTimings=Join-Path $PSScriptRoot 'runtime\PhaseTimings.cs'
$syncItemScheduler=Join-Path $PSScriptRoot 'runtime\SyncItemScheduler.cs'
$nativeLoopTimings=Join-Path $PSScriptRoot 'runtime\NativeLoopTimings.cs'
$nativeScriptTimings=Join-Path $PSScriptRoot 'runtime\NativeScriptTimings.cs'
$slowFrameDiagnostics=Join-Path $PSScriptRoot 'runtime\SlowFrameDiagnostics.cs'
$itemIndexTable=Join-Path $PSScriptRoot 'runtime\ItemIndexTable.cs'
$terrainHoleAdjacency=Join-Path $PSScriptRoot 'runtime\TerrainHoleAdjacency.cs'
$terrainRoadSelection=Join-Path $PSScriptRoot 'runtime\TerrainRoadSelection.cs'
$rendererCensus=Join-Path $PSScriptRoot 'runtime\RendererCensus.cs'
$roadChunkPolicy=Join-Path $PSScriptRoot 'runtime\RoadChunkPolicy.cs'
$carPosePolicy=Join-Path $PSScriptRoot 'runtime\CarPosePolicy.cs'
$profilerSlotPolicy=Join-Path $PSScriptRoot 'runtime\ProfilerSlotPolicy.cs'
$terrainMembership=Join-Path $PSScriptRoot 'runtime\TerrainMembership.cs'
$terrainWorkDiagnostics=Join-Path $PSScriptRoot 'runtime\TerrainWorkDiagnostics.cs'
$terrainWorker=Join-Path $PSScriptRoot 'runtime\TerrainWorker.cs'
$contentFingerprint=Join-Path $PSScriptRoot 'runtime\ContentFingerprint.cs'
$netDeliveryScheduler=Join-Path $PSScriptRoot 'runtime\NetDeliveryScheduler.cs'
$steamSdrTransport=Join-Path $PSScriptRoot 'runtime\SteamSdrTransport.cs'
$nativeSpawnSafety=Join-Path $PSScriptRoot 'runtime\NativeSpawnSafety.cs'
$nativeSeatSafety=Join-Path $PSScriptRoot 'runtime\NativeSeatSafety.cs'
$nativeAttachmentGuard=Join-Path $PSScriptRoot 'runtime\NativeAttachmentGuard.cs'
$nativeSafetyAdapter=Join-Path $PSScriptRoot 'runtime\NativeSafetyAdapter.cs'
$spawnClearance=Join-Path $PSScriptRoot 'runtime\SpawnClearance.cs'
$spawnClearanceAdapter=Join-Path $PSScriptRoot 'runtime\SpawnClearanceAdapter.cs'
$wildlifeWeather=Join-Path $PSScriptRoot 'runtime\WildlifeWeather.cs'
$menuLayoutPolicy=Join-Path $PSScriptRoot 'runtime\MenuLayoutPolicy.cs'
$menuLayout=Join-Path $PSScriptRoot 'runtime\MenuLayout.cs'
$menuProbe=Join-Path $PSScriptRoot 'runtime\MenuProbe.cs'
$playerLeaveSafety=Join-Path $PSScriptRoot 'runtime\PlayerLeaveSafety.cs'
$itemAskSafety=Join-Path $PSScriptRoot 'runtime\ItemAskSafety.cs'
$roadLiveCache=Join-Path $PSScriptRoot 'runtime\RoadLiveCache.cs'
$roadFarSkip=Join-Path $PSScriptRoot 'runtime\RoadFarSkip.cs'
$cameraCostCore=Join-Path $PSScriptRoot 'runtime\CameraCostCore.cs'
$cameraCost=Join-Path $PSScriptRoot 'runtime\CameraCost.cs'
$mirrorOnScreen=Join-Path $PSScriptRoot 'runtime\MirrorOnScreen.cs'
$roadBake=Join-Path $PSScriptRoot 'runtime\RoadBake.cs'
$renderSwitches=Join-Path $PSScriptRoot 'runtime\RenderSwitches.cs'
$smallFixes=Join-Path $PSScriptRoot 'runtime\SmallFixes.cs'
$nativeStoreGate=Join-Path $PSScriptRoot 'runtime\NativeStoreGate.cs'
$nativeStoreAdapter=Join-Path $PSScriptRoot 'runtime\NativeStoreAdapter.cs'
$nativeStoreText=Join-Path $PSScriptRoot 'runtime\NativeStoreText.cs'
$patchInfo=Join-Path $PSScriptRoot 'runtime\PatchInfo.cs'
$patchOptions=Join-Path $PSScriptRoot 'runtime\PatchOptions.cs'
$patchOptionsMenu=Join-Path $PSScriptRoot 'runtime\PatchOptionsMenu.cs'
$patchSurface=Join-Path $PSScriptRoot 'runtime\PatchSurface.cs'
$joinPhysics=Join-Path $PSScriptRoot 'runtime\JoinPhysics.cs'
$cargoGhost=Join-Path $PSScriptRoot 'runtime\CargoGhost.cs'
$netLogLimiter=Join-Path $PSScriptRoot 'runtime\NetLogLimiter.cs'
$feedbackPolicy=Join-Path $PSScriptRoot 'runtime\FeedbackPolicy.cs'
$feedbackBundle=Join-Path $PSScriptRoot 'runtime\FeedbackBundle.cs'
$itemStreamPolicy=Join-Path $PSScriptRoot 'runtime\ItemStreamPolicy.cs'
$terrainAlignPolicy=Join-Path $PSScriptRoot 'runtime\TerrainAlignPolicy.cs'
$streamScanPolicy=Join-Path $PSScriptRoot 'runtime\StreamScanPolicy.cs'
$itemSettlePolicy=Join-Path $PSScriptRoot 'runtime\ItemSettlePolicy.cs'
$farPlaceQueue=Join-Path $PSScriptRoot 'runtime\FarPlaceQueue.cs'
$remotePoseBuffer=Join-Path $PSScriptRoot 'runtime\RemotePoseBuffer.cs'
$remoteSmoothing=Join-Path $PSScriptRoot 'runtime\RemoteSmoothing.cs'
$collisionRestoreCache=Join-Path $PSScriptRoot 'runtime\CollisionRestoreCache.cs'
$netStats=Join-Path $PSScriptRoot 'runtime\NetStats.cs'
$worldJump=Join-Path $PSScriptRoot 'runtime\WorldJump.cs'
$poseSequence=Join-Path $PSScriptRoot 'runtime\PoseSequence.cs'
$partsCensus=Join-Path $PSScriptRoot 'runtime\PartsCensus.cs'
$carOwnership=Join-Path $PSScriptRoot 'runtime\CarOwnership.cs'
$placementSkip=Join-Path $PSScriptRoot 'runtime\PlacementSkip.cs'
$liveUsable=Join-Path $PSScriptRoot 'runtime\LiveUsable.cs'
$carCargo=Join-Path $PSScriptRoot 'runtime\CarCargo.cs'
$results=Join-Path $run 'results.txt'
$nativeRefs=@("/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\UnityEngine.PhysicsModule.dll","/reference:$managed\com.rlabrecque.steamworks.net.dll","/reference:$managed\netstandard.dll")
$tests=@(
    @{Name='StableCoreTests';Sources=@($net,$core,$packets,(Join-Path $PSScriptRoot 'tests\StableCoreTests.cs'));Refs=@()},
    @{Name='StableAuditTests';Sources=@($net,$core,$packets,(Join-Path $PSScriptRoot 'tests\StableAuditTests.cs'));Refs=@()},
    @{Name='StableJournalTests';Sources=@($net,$core,$packets,(Join-Path $PSScriptRoot 'tests\StableJournalTests.cs'));Refs=@()},
    @{Name='ReliableOutboxTests';Sources=@($deferredCore,(Join-Path $PSScriptRoot 'tests\ReliableOutboxTests.cs'));Refs=@()},
    @{Name='LoopbackTests';Sources=@($loopback,(Join-Path $PSScriptRoot 'tests\LoopbackTests.cs'));Refs=@()},
    @{Name='LoopbackIntegrationTests';Sources=@($net,$core,$packets,$loopback,(Join-Path $PSScriptRoot 'tests\LoopbackIntegrationTests.cs'));Refs=@()},
    @{Name='LoopbackBackpressureTests';Sources=@($loopback,(Join-Path $PSScriptRoot 'tests\LoopbackBackpressureTests.cs'));Refs=@()},
    @{Name='StableFailureTests';Sources=@($net,$core,$packets,(Join-Path $PSScriptRoot 'tests\StableFailureTests.cs'));Refs=@()},
    @{Name='JoinStageBoundTests';Sources=@($net,$core,$packets,(Join-Path $PSScriptRoot 'tests\JoinStageBoundTests.cs'));Refs=@()},
    @{Name='NetCoreTests';Sources=@($net,$packets,(Join-Path $PSScriptRoot 'tests\NetCoreTests.cs'));Refs=@()},
    @{Name='StableWireTests';Sources=@($net,$packets,(Join-Path $PSScriptRoot 'tests\StableWireProductionTests.cs'));Refs=$nativeRefs},
    @{Name='SyncItemSchedulerTests';Sources=@($syncItemScheduler,(Join-Path $PSScriptRoot 'tests\SyncItemSchedulerTests.cs'));Refs=@()},
    @{Name='TerrainDistanceTests';Sources=@($terrainPolicy,(Join-Path $PSScriptRoot 'tests\TerrainDistanceTests.cs'));Refs=@()},
    @{Name='TerrainMembershipTests';Sources=@($terrainMembership,(Join-Path $PSScriptRoot 'tests\TerrainMembershipTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainWorkDiagnosticsTests';Sources=@($terrainWorkDiagnostics,(Join-Path $PSScriptRoot 'runtime\ModDiagnostics.cs'),(Join-Path $PSScriptRoot 'tests\TerrainWorkDiagnosticsTests.cs'));Refs=$nativeRefs},
    @{Name='NativeLoopTimingsTests';Sources=@($nativeLoopTimings,(Join-Path $PSScriptRoot 'tests\NativeLoopTimingsTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='SpawnClearanceTests';Sources=@($spawnClearance,(Join-Path $PSScriptRoot 'tests\SpawnClearanceTests.cs'));Refs=@()},
    @{Name='NativeSpawnSafetyTests';Sources=@($nativeSpawnSafety,(Join-Path $PSScriptRoot 'tests\NativeSpawnSafetyTests.cs'));Refs=@()},
    @{Name='SlowFrameDiagnosticsTests';Sources=@($slowFrameDiagnostics,(Join-Path $PSScriptRoot 'tests\SlowFrameDiagnosticsTests.cs'));Refs=@()},
    @{Name='NativeScriptTimingsTests';Sources=@($nativeScriptTimings,(Join-Path $PSScriptRoot 'tests\NativeScriptTimingsTests.cs'));Refs=@()},
    @{Name='PendingAttachTests';Sources=@($pendingAttach,(Join-Path $PSScriptRoot 'tests\PendingAttachTests.cs'));Refs=@()},
    @{Name='PhaseTimingsTests';Sources=@((Join-Path $PSScriptRoot 'runtime\PhaseTimings.cs'),(Join-Path $PSScriptRoot 'runtime\ModDiagnostics.cs'),(Join-Path $PSScriptRoot 'tests\PhaseTimingsTests.cs'));Refs=@()},
    @{Name='ItemIndexTableTests';Sources=@($itemIndexTable,(Join-Path $PSScriptRoot 'tests\ItemIndexTableTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainHoleAdjacencyTests';Sources=@($terrainHoleAdjacency,(Join-Path $PSScriptRoot 'tests\TerrainHoleAdjacencyTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainRoadSelectionTests';Sources=@($terrainRoadSelection,(Join-Path $PSScriptRoot 'tests\TerrainRoadSelectionTests.cs'));Refs=$nativeRefs},
    @{Name='RoadChunkPolicyTests';Sources=@($roadChunkPolicy,(Join-Path $PSScriptRoot 'tests\RoadChunkPolicyTests.cs'));Refs=$nativeRefs},
    @{Name='CarPosePolicyTests';Sources=@($patchOptions,$carPosePolicy,(Join-Path $PSScriptRoot 'tests\CarPosePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='PlayerLeavePolicyTests';Sources=@($playerLeaveSafety,$collisionRestoreCache,(Join-Path $PSScriptRoot 'tests\PlayerLeavePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='ItemAskPolicyTests';Sources=@($itemAskSafety,(Join-Path $PSScriptRoot 'tests\ItemAskPolicyTests.cs'));Refs=@()},
    @{Name='NativeStoreGateTests';Sources=@($nativeStoreGate,(Join-Path $PSScriptRoot 'tests\NativeStoreGateTests.cs'));Refs=@()},
    @{Name='RoadLiveCacheTests';Sources=@($roadLiveCache,(Join-Path $PSScriptRoot 'tests\RoadLiveCacheTests.cs'));Refs=$nativeRefs},
    @{Name='RoadFarSkipTests';Sources=@($roadFarSkip,(Join-Path $PSScriptRoot 'tests\RoadFarSkipTests.cs'));Refs=$nativeRefs},
    @{Name='RenderCostPolicyTests';Sources=@($cameraCostCore,$mirrorOnScreen,$roadBake,(Join-Path $PSScriptRoot 'tests\RenderCostPolicyTests.cs'));Refs=$nativeRefs},
    @{Name='RuntimePolicyTests';Sources=@($renderSwitches,$smallFixes,$patchOptions,(Join-Path $PSScriptRoot 'tests\RuntimePolicyTests.cs'));Refs=$nativeRefs},
    @{Name='ProfilerSlotPolicyTests';Sources=@($profilerSlotPolicy,(Join-Path $PSScriptRoot 'tests\ProfilerSlotPolicyTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='NativeAttachmentGuardTests';Sources=@($nativeAttachmentGuard,(Join-Path $PSScriptRoot 'tests\NativeAttachmentGuardTests.cs'));Refs=@()},
    @{Name='NativeSeatSafetyTests';Sources=@($nativeSeatSafety,(Join-Path $PSScriptRoot 'tests\NativeSeatSafetyTests.cs'));Refs=@("/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll")},
    @{Name='MenuLayoutPolicyTests';Sources=@($menuLayoutPolicy,(Join-Path $PSScriptRoot 'tests\MenuLayoutPolicyTests.cs'));Refs=@()},
    @{Name='ContentFingerprintTests';Sources=@($net,$core,$packets,$contentFingerprint,(Join-Path $PSScriptRoot 'tests\ContentFingerprintTests.cs'));Refs=@()},
    @{Name='NetLogLimiterTests';Sources=@($netLogLimiter,(Join-Path $PSScriptRoot 'tests\NetLogLimiterTests.cs'));Refs=@()},
    @{Name='FeedbackPolicyTests';Sources=@($feedbackPolicy,(Join-Path $PSScriptRoot 'tests\FeedbackPolicyTests.cs'));Refs=@()},
    @{Name='StreamScanPolicyTests';Sources=@($streamScanPolicy,(Join-Path $PSScriptRoot 'tests\StreamScanPolicyTests.cs'));Refs=@()},
    @{Name='TerrainAlignPolicyTests';Sources=@($terrainAlignPolicy,(Join-Path $PSScriptRoot 'tests\TerrainAlignPolicyTests.cs'));Refs=@()},
    @{Name='ItemStreamPolicyTests';Sources=@($itemStreamPolicy,(Join-Path $PSScriptRoot 'tests\ItemStreamPolicyTests.cs'));Refs=@()},
    @{Name='ItemSettlePolicyTests';Sources=@($itemSettlePolicy,(Join-Path $PSScriptRoot 'tests\ItemSettlePolicyTests.cs'));Refs=@()},
    @{Name='FarPlaceQueueTests';Sources=@($farPlaceQueue,(Join-Path $PSScriptRoot 'tests\FarPlaceQueueTests.cs'));Refs=$nativeRefs},
    @{Name='TerrainWorkerParityTests';Sources=@($terrainWorker,(Join-Path $PSScriptRoot 'tests\TerrainWorkerParityTests.cs'));Refs=$nativeRefs},
    @{Name='RemotePoseBufferTests';Sources=@($remotePoseBuffer,(Join-Path $PSScriptRoot 'tests\RemotePoseBufferTests.cs'));Refs=@()},
    @{Name='NetUpdateCostTests';Sources=@($net,$packets,$collisionRestoreCache,$netStats,$patchOptions,$carPosePolicy,$remotePoseBuffer,$netDeliveryScheduler,(Join-Path $PSScriptRoot 'tests\NetUpdateCostTests.cs'));Refs=$nativeRefs},
    @{Name='NativeStoreTextTests';Sources=@($nativeStoreText,(Join-Path $PSScriptRoot 'tests\NativeStoreTextTests.cs'));Refs=$nativeRefs},
    @{Name='PoseSequenceTests';Sources=@($poseSequence,$liveUsable,$remotePoseBuffer,(Join-Path $PSScriptRoot 'tests\PoseSequenceTests.cs'));Refs=$nativeRefs},
    @{Name='CarCargoTests';Sources=@($net,$packets,$carCargo,$poseSequence,$liveUsable,(Join-Path $PSScriptRoot 'tests\CarCargoTests.cs'));Refs=$nativeRefs},
    @{Name='LiveUsableTests';Sources=@($liveUsable,$poseSequence,(Join-Path $PSScriptRoot 'tests\LiveUsableTests.cs'));Refs=$nativeRefs},
    @{Name='PlacementSkipTests';Sources=@($placementSkip,(Join-Path $PSScriptRoot 'tests\PlacementSkipTests.cs'));Refs=$nativeRefs},
    @{Name='CarOwnershipTests';Sources=@($carOwnership,(Join-Path $PSScriptRoot 'tests\CarOwnershipTests.cs'));Refs=$nativeRefs},
    @{Name='WorldJumpTests';Sources=@($netStats,$collisionRestoreCache,$remotePoseBuffer,$worldJump,(Join-Path $PSScriptRoot 'tests\WorldJumpTests.cs'));Refs=$nativeRefs},
    @{Name='WildlifeWeatherTests';Sources=@($patchInfo,$patchOptions,$wildlifeWeather,(Join-Path $PSScriptRoot 'tests\WildlifeWeatherTests.cs'));Refs=$nativeRefs},
    @{Name='PatchOptionsTests';Sources=@($patchInfo,$patchOptions,(Join-Path $PSScriptRoot 'tests\PatchOptionsTests.cs'));Refs=@()}
)
foreach ($test in $tests) {
    $exe=Join-Path $run ($test.Name+'.exe')
    Run $csc (@('/nologo','/optimize+',"/out:$exe")+$test.Refs+$test.Sources)
    & (Test-ExePath $exe) $managed $baseline.original 2>&1 | Tee-Object -FilePath $results -Append
    if ($LASTEXITCODE -ne 0) { throw "$($test.Name) failed." }
}
# Compiler-generated module IDs differ between machines. Compare the exact source
# signature in the handshake instead of hashing our locally compiled helper DLL.
$signatureInputs=@('runtime\NetCore.cs','runtime\StableCore.cs','runtime\StablePackets.cs','runtime\StableGameBridge.cs','runtime\StableMap.cs','runtime\StableRoadCodec.cs','runtime\DeferredCore.cs','runtime\LoopbackTransport.cs','runtime\TerrainDistancePolicy.cs','runtime\CargoDiagnostics.cs','runtime\FrameDiagnostics.cs','runtime\ModDiagnostics.cs','runtime\PhaseTimings.cs','runtime\AttachDiagnostics.cs','runtime\PendingAttach.cs','runtime\AttachReplay.cs','runtime\GroundDiagnostics.cs','runtime\SyncItemScheduler.cs','runtime\NativeLoopTimings.cs','runtime\NativeScriptTimings.cs','runtime\SlowFrameDiagnostics.cs','runtime\ItemIndexTable.cs','runtime\TerrainHoleAdjacency.cs','runtime\TerrainRoadSelection.cs','runtime\RendererCensus.cs','runtime\RoadChunkPolicy.cs','runtime\CarPosePolicy.cs','runtime\PlayerLeaveSafety.cs','runtime\ItemAskSafety.cs','runtime\NativeStoreGate.cs','runtime\NativeStoreAdapter.cs','runtime\NativeStoreText.cs','runtime\PatchInfo.cs','runtime\PatchOptions.cs','runtime\PatchOptionsMenu.cs','runtime\PatchSurface.cs','runtime\JoinPhysics.cs','runtime\CargoGhost.cs','runtime\RoadLiveCache.cs','runtime\RoadFarSkip.cs','runtime\CameraCostCore.cs','runtime\CameraCost.cs','runtime\MirrorOnScreen.cs','runtime\RoadBake.cs','runtime\RenderSwitches.cs','runtime\SmallFixes.cs','runtime\ProfilerSlotPolicy.cs','runtime\TerrainMembership.cs','runtime\TerrainWorkDiagnostics.cs','runtime\ContentFingerprint.cs','runtime\NetDeliveryScheduler.cs','runtime\SteamSdrTransport.cs','runtime\NativeSpawnSafety.cs','runtime\NativeSeatSafety.cs','runtime\NativeAttachmentGuard.cs','runtime\NativeSafetyAdapter.cs','runtime\SpawnClearance.cs','runtime\SpawnClearanceAdapter.cs','runtime\WildlifeWeather.cs','runtime\MenuLayoutPolicy.cs','runtime\MenuLayout.cs','runtime\MenuProbe.cs','runtime\NetLogLimiter.cs','runtime\FeedbackPolicy.cs','runtime\FeedbackBundle.cs','runtime\ItemStreamPolicy.cs','runtime\TerrainAlignPolicy.cs','runtime\StreamScanPolicy.cs','runtime\ItemSettlePolicy.cs','runtime\FarPlaceQueue.cs','runtime\TerrainWorker.cs','runtime\RemotePoseBuffer.cs','runtime\RemoteSmoothing.cs','runtime\CollisionRestoreCache.cs','runtime\NetStats.cs','runtime\WorldJump.cs','runtime\PoseSequence.cs','runtime\PartsCensus.cs','runtime\CarOwnership.cs','runtime\PlacementSkip.cs','runtime\LiveUsable.cs','runtime\CarCargo.cs','tools\PatchStability.cs','tools\VerifyStability.cs')
$signatureText=($signatureInputs | ForEach-Object { $_+':'+(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash }) -join "`n"
$hasher=[Security.Cryptography.SHA256]::Create()
try {$sourceSignature=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($signatureText))).Replace('-','')} finally {$hasher.Dispose()}
$validationInputs=@(
    @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File | Sort-Object Name | ForEach-Object {'tests\'+$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tools') -File | Sort-Object Name | ForEach-Object {'tools\'+$_.Name})+
    @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name | ForEach-Object {$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'packaging\singleplayer') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {'packaging\singleplayer\'+$_.Name})
)
$validationText=($validationInputs | ForEach-Object {
    $_+':'+(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash
}) -join "`n"
$validationHasher=[Security.Cryptography.SHA256]::Create()
try {$validationSignature=[BitConverter]::ToString($validationHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($validationText))).Replace('-','')} finally {$validationHasher.Dispose()}
$identity=Join-Path $run 'BuildIdentity.cs'
Set-Content -LiteralPath $identity -Encoding UTF8 -Value ('namespace TldMp { internal static class BuildIdentity { public const string Sources="'+$sourceSignature+'"; } }')
$runtime=Join-Path $run 'Tld.Multiplayer.dll'
Run $csc @('/nologo','/target:library','/optimize+',"/out:$runtime","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\UnityEngine.PhysicsModule.dll","/reference:$managed\UnityEngine.VehiclesModule.dll","/reference:$managed\UnityEngine.ParticleSystemModule.dll","/reference:$managed\UnityEngine.JSONSerializeModule.dll","/reference:$managed\com.rlabrecque.steamworks.net.dll","/reference:$managed\UnityEngine.UI.dll","/reference:$managed\UnityEngine.UIModule.dll","/reference:$managed\UnityEngine.ScreenCaptureModule.dll","/reference:$managed\netstandard.dll","/reference:$managed\System.IO.Compression.dll","/reference:$managed\Unity.TextMeshPro.dll","/reference:$managed\UnityEngine.InputLegacyModule.dll",$net,$core,$packets,$bridge,$map,$roadCodec,$deferredCore,$loopback,$terrainPolicy,$cargoDiagnostics,$frameDiagnostics,$syncItemScheduler,$nativeLoopTimings,$nativeScriptTimings,$slowFrameDiagnostics,$itemIndexTable,$terrainHoleAdjacency,$terrainRoadSelection,$rendererCensus,$roadChunkPolicy,$carPosePolicy,$playerLeaveSafety,$itemAskSafety,$nativeStoreGate,$nativeStoreAdapter,$nativeStoreText,$patchInfo,$patchOptions,$patchOptionsMenu,$patchSurface,$joinPhysics,$cargoGhost,$roadLiveCache,$roadFarSkip,$cameraCostCore,$cameraCost,$mirrorOnScreen,$roadBake,$renderSwitches,$smallFixes,$profilerSlotPolicy,$terrainMembership,$terrainWorkDiagnostics,$terrainWorker,$contentFingerprint,$netDeliveryScheduler,$steamSdrTransport,$nativeSpawnSafety,$nativeSeatSafety,$nativeAttachmentGuard,$nativeSafetyAdapter,$spawnClearance,$spawnClearanceAdapter,$wildlifeWeather,$menuLayoutPolicy,$menuLayout,$menuProbe,$netLogLimiter,$feedbackPolicy,$feedbackBundle,$itemStreamPolicy,$terrainAlignPolicy,$itemSettlePolicy,$farPlaceQueue,$remotePoseBuffer,$remoteSmoothing,$collisionRestoreCache,$netStats,$worldJump,$poseSequence,$partsCensus,$carOwnership,$placementSkip,$liveUsable,$carCargo,$modDiagnostics,$phaseTimings,$groundDiagnostics,$attachDiagnostics,$pendingAttach,$attachReplay,$streamScanPolicy,$identity)
# v4 is built later (Stage-NativeSafetyBuild.ps1 / the installer) and calls back into
# the runtime. A missing callback makes Mono reject the whole patched method at run
# time (the save button silently did nothing), so require every one here.
$callbackCheck=Join-Path $toolDir 'CheckRuntimeCallbacks.exe'
Run $csc @('/nologo','/optimize+',"/out:$callbackCheck","/reference:$cecil",(Join-Path $PSScriptRoot 'tools\CheckRuntimeCallbacks.cs'))
Run $callbackCheck @($runtime,(Join-Path $PSScriptRoot 'packaging\singleplayer')) 2>&1 | Tee-Object -FilePath $results -Append
& (Join-Path $PSScriptRoot 'Test-NativeSafetyAdapter.ps1') 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Native safety production adapter suite failed.'}
& (Join-Path $PSScriptRoot 'Test-PlacementCenters.ps1') -Runtime $runtime -ManagedDirectory $managed -BuildDirectory (Join-Path $run 'placement-centers') 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Placement-center parity suite failed.'}
& (Join-Path $PSScriptRoot 'Test-OwnershipPerformance.ps1') -BuildDirectory $run -ManagedDirectory $managed 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Ownership/performance regression suite failed.'}
$serializationTests=Join-Path $run 'WorldSerializationTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$serializationTests","/reference:$runtime","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path $PSScriptRoot 'tests\WorldSerializationTests.cs'))
& (Test-ExePath $serializationTests) $managed $baseline.original 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'World/chunk serialization regression failed.'}
$generationTests=Join-Path $run 'WorldGenerationReplay.exe'
Copy-Item -LiteralPath $cecil -Destination (Join-Path $run 'Mono.Cecil.dll') -Force
Run $csc @('/nologo','/nowarn:0436','/optimize+',"/out:$generationTests","/reference:$cecil","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path $PSScriptRoot 'tests\WorldGenerationReplay.cs'))
& (Test-ExePath $generationTests) $managed $baseline.original $runtime 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Host generation lifecycle regression failed.'}
$patcher=Join-Path $toolDir 'PatchStability.exe'
$verifier=Join-Path $toolDir 'VerifyStability.exe'
Run $csc @('/nologo','/optimize+',"/out:$patcher","/reference:$cecil",(Join-Path $PSScriptRoot 'tools\PatchStability.cs'))
Run $csc @('/nologo','/optimize+','/main:VerifyStability',"/out:$verifier","/reference:$cecil",(Join-Path $PSScriptRoot 'tools\VerifyPatch.cs'),(Join-Path $PSScriptRoot 'tools\VerifyStability.cs'))
$patched=Join-Path $run 'Assembly-CSharp.dll'
Run $patcher @($baseline.patched,$runtime,$patched,$managed)
& (Test-ExePath $verifier) $baseline.patched $patched 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'V3 hook verification failed.' }
& (Join-Path $PSScriptRoot 'Test-RoadCallbackInstrumentation.ps1') -BaselineAssembly $baseline.patched -CandidateAssembly $patched -CecilPath $cecil -BuildDirectory (Join-Path $env:TEMP ('opencode\road-callback-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Road callback instrumentation regression failed.'}
& (Join-Path $PSScriptRoot 'Test-RandomFlyerKinematicGuard.ps1') -OriginalAssembly $baseline.patched -CandidateAssembly $patched -Runtime $runtime -ManagedDirectory $managed -BuildDirectory (Join-Path $env:TEMP ('opencode\flyer-guard-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Flyer kinematic guard regression failed.'}
& (Join-Path $PSScriptRoot 'Test-FlyerProductionBehavior.ps1') -BaselineAssembly $baseline.patched -CandidateAssembly $patched -BuildDirectory (Join-Path $env:TEMP ('opencode\flyer-behavior-'+(Split-Path $baseRun -Leaf))) 2>&1 | Tee-Object -FilePath $results -Append
if($LASTEXITCODE -ne 0){throw 'Flyer production behavior regression failed.'}
# Execute the shipped Mono JIT against real patched bodies. CLR/IL parity alone
# does not detect observer instructions accidentally inside a finally handler.
$ehTamper=Join-Path $run 'TamperObserverExceptionBoundary.exe'
Run $csc @('/nologo','/optimize+',"/out:$ehTamper","/reference:$cecil",(Join-Path $PSScriptRoot 'tests\TamperObserverExceptionBoundary.cs'))
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
Run $csc @('/nologo','/optimize+',"/out:$tamper","/reference:$cecil",(Join-Path $PSScriptRoot 'tools\TamperObserver.cs'))
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
Run $csc @('/nologo','/optimize+',"/out:$extractor","/reference:$cecil",(Join-Path $PSScriptRoot 'tools\ExtractReplay.cs'))
$worldRun=Join-Path $run 'world-load-replay'
New-Item -ItemType Directory -Path $worldRun | Out-Null
$worldFixture=Join-Path $worldRun 'WorldLoadReplayFixture.exe'
Run $csc @('/nologo','/nowarn:0649','/optimize+',"/out:$worldFixture","/reference:$managed\netstandard.dll",$net,$core,$packets,(Join-Path $PSScriptRoot 'tests\WorldLoadReplayFixture.cs'))
$worldNative=Join-Path $worldRun 'WorldLoadNative.exe'
Run $extractor @($baseline.original,$worldFixture,$worldNative,'worldnative')
$worldReplay=Join-Path $worldRun 'WorldLoadReplay.exe'
Run $extractor @($runtime,$worldNative,$worldReplay,'world')
& (Test-ExePath $worldReplay) 2>&1 | Tee-Object -FilePath $results -Append
if ($LASTEXITCODE -ne 0) { throw 'World-load production-method replay failed.' }
$terrainReplay=Join-Path $run 'terrain-replay'
New-Item -ItemType Directory -Path $terrainReplay | Out-Null
$terrainTest=Join-Path $terrainReplay 'TerrainProductionTests.exe'
Run $csc @('/nologo','/optimize+',"/out:$terrainTest","/reference:$($baseline.original)","/reference:$managed\UnityEngine.CoreModule.dll","/reference:$managed\netstandard.dll",(Join-Path $PSScriptRoot 'tests\TerrainProductionTests.cs'))
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
& (Join-Path $PSScriptRoot 'Build-Patch.ps1') -GameDirectory $GameDirectory -OutputDirectory $installerBuild 2>&1 | Tee-Object -FilePath $results -Append
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
$manager=Join-Path $PSScriptRoot 'Manage-Stability.ps1'
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
Set-Content -LiteralPath (Join-Path $PSScriptRoot '.local-analysis\latest-stability.txt') -Value $run
Write-Host "V3 offline build passed. Results: $results"
Write-Host 'Experimental build only. Real game and saves have not been changed.'
