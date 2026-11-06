# Build an isolated, staged-only vehicle capture observer on top of an existing
# verified Build-Patch output. This script never writes to the game directory.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaselineDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$GameDirectory=''
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory

function Require([bool]$condition,[string]$message){if(!$condition){throw $message}}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()}
function RunCsc([string[]]$arguments){& $csc @arguments;if($LASTEXITCODE -ne 0){throw "C# compiler failed ($LASTEXITCODE)."}}
function RunTool([string]$exe,[string[]]$arguments){& $exe @arguments;if($LASTEXITCODE -ne 0){throw "Failed ($LASTEXITCODE): $(Split-Path -Leaf $exe)"}}
function IsSameOrChild([string]$candidate,[string]$parent){
    $candidateFull=[IO.Path]::GetFullPath($candidate).TrimEnd('\','/')
    $parentFull=[IO.Path]::GetFullPath($parent).TrimEnd('\','/')
    return $candidateFull.Equals($parentFull,[StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith(($parentFull+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith(($parentFull+[IO.Path]::AltDirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)
}

$baseline=[IO.Path]::GetFullPath($BaselineDirectory)
$out=[IO.Path]::GetFullPath($OutputDirectory)
function RejectReparseAncestors([string]$path){
    $itemPath=$path
    while($itemPath){
        if(Test-Path -LiteralPath $itemPath){
            $item=Get-Item -LiteralPath $itemPath -Force
            Require (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) ('Reparse-point path is not supported for staging: '+$itemPath)
        }
        $parent=Split-Path -Parent $itemPath
        if(!$parent -or $parent -eq $itemPath){break}
        $itemPath=$parent
    }
}
RejectReparseAncestors $baseline
RejectReparseAncestors $out
RejectReparseAncestors ([IO.Path]::GetFullPath($GameDirectory))
$game=[IO.Path]::GetFullPath($GameDirectory)
Require (Test-Path -LiteralPath $baseline -PathType Container) ('Baseline directory not found: '+$baseline)
Require (!(Test-Path -LiteralPath $out)) ('Output directory already exists: '+$out)
Require (!(IsSameOrChild $out $baseline)) 'Output directory must be outside the baseline directory.'
Require (!(IsSameOrChild $out $game)) 'Output directory must be outside the resolved game installation.'

$baselineManifestPath=Join-Path $baseline 'manifest.json'
$baselineV3=Join-Path $baseline 'Assembly-CSharp.v3.dll'
$baselineRuntime=Join-Path $baseline 'Tld.Multiplayer.dll'
$cecil=Join-Path (Join-Path $baseline 'tools') 'Mono.Cecil.dll'
foreach($path in @($baselineManifestPath,$baselineV3,$baselineRuntime,$cecil)){
    Require (Test-Path -LiteralPath $path -PathType Leaf) ('Required baseline file missing: '+$path)
}
$baselineManifest=Get-Content -LiteralPath $baselineManifestPath -Raw | ConvertFrom-Json
Require ($baselineManifest.version -eq 3) 'Baseline manifest is not a Build-Patch version 3 manifest.'
Require ($baselineManifest.originalSha256 -and $baselineManifest.originalSha256.ToUpperInvariant() -eq 'CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000') 'Baseline manifest does not identify the supported original game assembly.'
$baselineV3Hash=Hash $baselineV3
$baselineRuntimeHash=Hash $baselineRuntime
Require ($baselineManifest.patchedSha256 -and $baselineManifest.patchedSha256.ToUpperInvariant() -eq $baselineV3Hash) 'Baseline v3 SHA256 does not match manifest.'
Require ($baselineManifest.runtimeSha256 -and $baselineManifest.runtimeSha256.ToUpperInvariant() -eq $baselineRuntimeHash) 'Baseline runtime SHA256 does not match manifest.'

$repo=Split-Path $PSScriptRoot -Parent
$probeSources=@(
    (Join-Path $repo 'runtime\ExperimentalVehiclePose.cs'),
    (Join-Path $repo 'runtime\ExperimentalVehicleCapture.cs'),
    (Join-Path $repo 'runtime\ExperimentalVehicleShadowProbe.cs')
)
$patcherSource=Join-Path $PSScriptRoot 'PatchVehicleShadowProbe.cs'
$verifyPatchSource=Join-Path $PSScriptRoot 'VerifyPatch.cs'
foreach($path in @($probeSources)+@($patcherSource,$verifyPatchSource)){
    Require (Test-Path -LiteralPath $path -PathType Leaf) ('Required source file missing: '+$path)
}

$managed=Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
Require (Test-Path -LiteralPath $managed -PathType Container) ('Game Managed directory not found: '+$managed)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Require (Test-Path -LiteralPath $csc -PathType Leaf) 'The Windows .NET Framework 4 C# compiler is missing. Enable or repair .NET Framework 4.x.'
$gameReferenceNames=@(
    'UnityEngine.CoreModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.PhysicsModule.dll',
    'UnityEngine.ImageConversionModule.dll','UnityEngine.AudioModule.dll','UnityEngine.VehiclesModule.dll',
    'UnityEngine.ParticleSystemModule.dll','UnityEngine.JSONSerializeModule.dll','com.rlabrecque.steamworks.net.dll',
    'UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.ScreenCaptureModule.dll','netstandard.dll',
    'System.IO.Compression.dll','Unity.TextMeshPro.dll','UnityEngine.InputLegacyModule.dll'
)
$references=@(('/reference:'+$baselineV3),('/reference:'+$baselineRuntime))
foreach($name in $gameReferenceNames){
    $reference=Join-Path $managed $name
    Require (Test-Path -LiteralPath $reference -PathType Leaf) ('Required game reference missing: '+$reference)
    $references+=('/reference:'+$reference)
}

New-Item -ItemType Directory -Path $out | Out-Null
$tools=Join-Path $out 'tools'
New-Item -ItemType Directory -Path $tools | Out-Null
$probeRuntime=Join-Path $out 'Tld.Multiplayer.VehicleShadowProbe.dll'
$shadowAssembly=Join-Path $out 'Assembly-CSharp.shadow.dll'
$patcher=Join-Path $tools 'PatchVehicleShadowProbe.exe'
Copy-Item -LiteralPath $cecil -Destination (Join-Path $tools 'Mono.Cecil.dll')
foreach($name in $gameReferenceNames){Copy-Item -LiteralPath (Join-Path $managed $name) -Destination $tools -Force}
try {
    RunCsc (@('/nologo','/target:library','/optimize+',('/out:'+$probeRuntime))+$references+$probeSources)
    RunCsc @('/nologo','/optimize+','/main:PatchVehicleShadowProbe',('/out:'+$patcher),('/reference:'+$cecil),$patcherSource,$verifyPatchSource)
    Push-Location $tools
    try { RunTool $patcher @($baselineV3,$probeRuntime,$shadowAssembly) } finally { Pop-Location }
    Require (Test-Path -LiteralPath $shadowAssembly -PathType Leaf) 'Probe patcher did not produce Assembly-CSharp.shadow.dll.'
    Copy-Item -LiteralPath $baselineRuntime -Destination (Join-Path $out 'Tld.Multiplayer.dll')
    Require ((Hash $baselineV3) -eq $baselineV3Hash) 'Baseline Assembly-CSharp.v3.dll changed during probe build.'
    Require ((Hash $baselineRuntime) -eq $baselineRuntimeHash) 'Baseline Tld.Multiplayer.dll changed during probe build.'
    [ordered]@{
        kind='vehicle-capture-shadow-probe'
        observerOnly=$true
        transportChanged=$false
        baselineDirectory=$baseline
        baselineV3Sha256=$baselineV3Hash
        baselineRuntimeSha256=$baselineRuntimeHash
        probeRuntime='Tld.Multiplayer.VehicleShadowProbe.dll'
        probeRuntimeSha256=(Hash $probeRuntime)
        shadowAssembly='Assembly-CSharp.shadow.dll'
        shadowAssemblySha256=(Hash $shadowAssembly)
        copiedBaselineRuntime='Tld.Multiplayer.dll'
        copiedBaselineRuntimeSha256=(Hash (Join-Path $out 'Tld.Multiplayer.dll'))
        scope='Separate staged artifact only; not installed and not part of the default build or handshake. Baseline hashes match the supplied manifest; this does not authenticate its provenance.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'vehicle-shadow-probe-manifest.json') -Encoding UTF8
    Write-Host 'PASS: isolated vehicle shadow probe artifacts staged; baseline files unchanged.'
}
finally {
    if((Test-Path -LiteralPath $baselineV3) -and (Hash $baselineV3) -ne $baselineV3Hash){throw 'Baseline Assembly-CSharp.v3.dll changed during probe build.'}
    if((Test-Path -LiteralPath $baselineRuntime) -and (Hash $baselineRuntime) -ne $baselineRuntimeHash){throw 'Baseline Tld.Multiplayer.dll changed during probe build.'}
}
