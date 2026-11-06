[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is missing.' }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-experimental-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    foreach ($name in @('Authority', 'Session', 'Graph', 'Mode', 'Envelope', 'Sequence', 'Identity', 'VehiclePose', 'IdRange', 'Snapshot', 'Journal')) {
        $exe = Join-Path $dir ($name + '.exe')
        $source = Join-Path $root ('runtime\Experimental' + $name + '.cs')
        $test = Join-Path $root ('tests\Experimental' + $name + 'Tests.cs')
        & $compiler /nologo ('/out:' + $exe) $source $test
        if ($LASTEXITCODE -ne 0) { throw "$name compilation failed: $LASTEXITCODE" }
        & $exe
        if ($LASTEXITCODE -ne 0) { throw "$name test failed: $LASTEXITCODE" }
    }
    $exe = Join-Path $dir 'GraphAuthority.exe'
    & $compiler /nologo ('/out:' + $exe) (Join-Path $root 'runtime\ExperimentalGraph.cs') (Join-Path $root 'runtime\ExperimentalAuthority.cs') (Join-Path $root 'runtime\ExperimentalGraphTransfer.cs') (Join-Path $root 'tests\ExperimentalGraphAuthorityTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "GraphAuthority compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "GraphAuthority test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Connection.exe'
    $connectionSources = @('Session', 'Envelope', 'Sequence', 'Policy', 'Connection') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $connectionSources (Join-Path $root 'tests\ExperimentalConnectionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Connection compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Connection test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'VehicleAdmission.exe'
    $vehicleSources = @('Authority', 'VehiclePose', 'VehicleAdmission') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $vehicleSources (Join-Path $root 'tests\ExperimentalVehicleAdmissionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "VehicleAdmission compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "VehicleAdmission test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'VehicleFlow.exe'
    $flowSources = @('Authority', 'Session', 'Envelope', 'Sequence', 'Policy', 'Connection', 'VehiclePose', 'VehicleAdmission') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $flowSources (Join-Path $root 'tests\ExperimentalVehicleFlowTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "VehicleFlow compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "VehicleFlow test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'VehicleCapture.exe'
    & $compiler /nologo ('/out:' + $exe) (Join-Path $root 'runtime\ExperimentalVehiclePose.cs') (Join-Path $root 'runtime\ExperimentalVehicleCapture.cs') (Join-Path $root 'runtime\ExperimentalVehicleShadowProbe.cs') (Join-Path $root 'tests\ExperimentalVehicleCaptureTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "VehicleCapture compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "VehicleCapture test failed: $LASTEXITCODE" }
    & $exe probe
    if ($LASTEXITCODE -ne 0) { throw "VehicleCapture enabled-probe test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Handoff.exe'
    $handoffSources = @('Authority', 'Session', 'Graph', 'GraphTransfer', 'Handoff') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $handoffSources (Join-Path $root 'tests\ExperimentalHandoffTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Handoff compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Handoff test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'SavedIds.exe'
    & $compiler /nologo ('/out:' + $exe) (Join-Path $root 'runtime\ExperimentalIdRange.cs') (Join-Path $root 'runtime\ExperimentalSavedIds.cs') (Join-Path $root 'tests\ExperimentalSavedIdsTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "SavedIds compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "SavedIds test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'SnapshotReceiver.exe'
    $receiverSources = @('Session', 'Snapshot', 'SnapshotReceiver') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $receiverSources (Join-Path $root 'tests\ExperimentalSnapshotReceiverTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "SnapshotReceiver compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "SnapshotReceiver test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Backend.exe'
    # ExperimentalBackend now hosts VehicleAuthority, which owns an
    # EntityCoordinator, so the backend suite compiles the coordinator stack.
    $backendSources = @('Authority', 'Session', 'Graph', 'GraphTransfer', 'Handoff', 'Identity', 'Coordinator', 'Backend') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $backendSources (Join-Path $root 'tests\ExperimentalBackendTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Backend compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Backend test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'VehicleAuthority.exe'
    & $compiler /nologo ('/out:' + $exe) $backendSources (Join-Path $root 'tests\ExperimentalVehicleAuthorityTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "VehicleAuthority compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "VehicleAuthority test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Policy.exe'
    $policySources = @('Session', 'Envelope', 'Sequence', 'Connection', 'Policy') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $policySources (Join-Path $root 'tests\ExperimentalPolicyTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Policy compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Policy test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Coordinator.exe'
    $coordinatorSources = @('Authority', 'Session', 'Graph', 'GraphTransfer', 'Handoff', 'Identity', 'Coordinator') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    & $compiler /nologo ('/out:' + $exe) $coordinatorSources (Join-Path $root 'tests\ExperimentalCoordinatorTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Coordinator compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Coordinator test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'HandoffBinding.exe'
    & $compiler /nologo ('/out:' + $exe) $coordinatorSources (Join-Path $root 'tests\ExperimentalHandoffBindingTests.cs')
    if ($LASTEXITCODE -ne 0) { throw "HandoffBinding compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "HandoffBinding test failed: $LASTEXITCODE" }
    $exe = Join-Path $dir 'Items.exe'
    # Item authority core: the native packet validator and the bus-pair layout it
    # delegates to are the only non-experimental sources the gate itself needs.
    # The backend/coordinator stack is compiled too because the gate tests also
    # cover the host's car-group member lookup (VehicleAuthority.TryMemberOwner).
    $itemSources = @('runtime\NetCore.cs', 'runtime\BusPairProtocol.cs', 'runtime\StablePackets.cs', 'runtime\ExperimentalItems.cs') | ForEach-Object { Join-Path $root $_ }
    $itemSources += $backendSources
    $itemSources += Join-Path $root 'tests\ExperimentalItemsTests.cs'
    & $compiler /nologo ('/out:' + $exe) $itemSources
    if ($LASTEXITCODE -ne 0) { throw "Items compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Items test failed: $LASTEXITCODE" }
    $assembly = Join-Path $dir 'Experimental.dll'
    $sources = @('Authority', 'Session', 'Graph', 'GraphTransfer', 'Handoff', 'Mode', 'Envelope', 'Sequence', 'Connection', 'Policy', 'Coordinator', 'Identity', 'VehiclePose', 'VehicleAdmission', 'IdRange', 'Snapshot', 'SnapshotReceiver', 'Journal', 'Backend', 'Items', 'Players', 'Breakables', 'ItemStream', 'ItemRelay', 'Interest', 'Replication', 'PlayerReplication') | ForEach-Object { Join-Path $root ('runtime\Experimental' + $_ + '.cs') }
    # ExperimentalItems.cs is the only experimental source that depends on the
    # native packet validator, so those non-experimental sources join the same
    # build to keep the whole set compiling together.
    $sources += @('runtime\NetCore.cs', 'runtime\BusPairProtocol.cs', 'runtime\StablePackets.cs') | ForEach-Object { Join-Path $root $_ }
    & $compiler /nologo /target:library ('/out:' + $assembly) $sources
    if ($LASTEXITCODE -ne 0) { throw "Combined compilation failed: $LASTEXITCODE" }
    Write-Output 'PASS: experimental sources compile together'
} finally {
    foreach ($name in @('Authority.exe', 'Session.exe', 'Graph.exe', 'Mode.exe', 'Envelope.exe', 'Sequence.exe', 'Identity.exe', 'VehiclePose.exe', 'IdRange.exe', 'Snapshot.exe', 'Journal.exe', 'GraphAuthority.exe', 'Connection.exe', 'VehicleAdmission.exe', 'VehicleFlow.exe', 'VehicleCapture.exe', 'Handoff.exe', 'SavedIds.exe', 'SnapshotReceiver.exe', 'Policy.exe', 'Coordinator.exe', 'HandoffBinding.exe', 'Backend.exe', 'VehicleAuthority.exe', 'Items.exe', 'Experimental.dll')) {
        $file = Join-Path $dir $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    Remove-Item -LiteralPath $dir -Force
}
