# Build and verify the multiplayer v3 patch from source for one game install.
# This is the installer's build: no test suite, only the patch verifiers. The
# game folder is only read; everything is written beneath -OutputDirectory.
[CmdletBinding()]
param(
    [string]$GameDirectory='',
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
$originalHash='CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000'
$cecilPackageHash='D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5'
$cecilHash='C41BDB9FFD3C5F6E17D2382C1012D73703E035E3F1100245FDD4E08C8DC6EB5B'
$managed=Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out=[IO.Path]::GetFullPath($OutputDirectory)
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Require([bool]$condition,[string]$message){if(!$condition){throw $message}}
function RunCsc([string[]]$arguments){& $csc @arguments;if($LASTEXITCODE -ne 0){throw "C# compiler failed ($LASTEXITCODE)."}}
function RunTool([string]$exe,[string[]]$arguments){& $exe @arguments;if($LASTEXITCODE -ne 0){throw "Failed ($LASTEXITCODE): $(Split-Path -Leaf $exe)"}}

Require (Test-Path -LiteralPath $csc) 'The Windows .NET Framework 4 C# compiler is missing. Enable or repair .NET Framework 4.x.'
Require (!(Test-Path -LiteralPath $out)) ('Output directory already exists: '+$out)
$tools=Join-Path $out 'tools'
New-Item -ItemType Directory -Path $tools | Out-Null

# The supported original assembly: the live file, or a backup from an earlier patch.
$original=$null
foreach($name in @('Assembly-CSharp.dll','Assembly-CSharp.tldmp-v3-original.bin','Assembly-CSharp.tldmp-v1-original.bin')){
    $candidate=Join-Path $managed $name
    if((Test-Path -LiteralPath $candidate) -and (Hash $candidate) -eq $originalHash){$original=$candidate;break}
}
Require ($original -ne $null) 'No verified original Assembly-CSharp.dll for 2024.11.26b_test was found. Verify the game files in Steam.'

# Mono.Cecil comes from the hash-pinned NuGet package.
$package=Join-Path $PSScriptRoot '.tools\cecil.zip'
if(!(Test-Path -LiteralPath $package)){
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $package) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg' -OutFile $package -UseBasicParsing
}
Require ((Hash $package) -eq $cecilPackageHash) 'Mono.Cecil package hash mismatch.'
$cecil=Join-Path $tools 'Mono.Cecil.dll'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{[IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('lib/net40/Mono.Cecil.dll'),$cecil)}finally{$zip.Dispose()}
Require ((Hash $cecil) -eq $cecilHash) 'Mono.Cecil assembly hash mismatch.'

# Same source list and handshake signature as Test-Stability.ps1.
$signatureLine=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tests\scripts\Test-Stability.ps1') | Where-Object {$_.StartsWith('$signatureInputs=')}
Require (@($signatureLine).Count -eq 1) 'Could not read $signatureInputs from Test-Stability.ps1.'
$signatureInputs=[regex]::Matches($signatureLine,"'([^']+)'") | ForEach-Object {$_.Groups[1].Value}
$signatureText=($signatureInputs | ForEach-Object {$_+':'+(Hash (Join-Path $PSScriptRoot $_))}) -join "`n"
$hasher=[Security.Cryptography.SHA256]::Create()
try{$sourceSignature=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($signatureText))).Replace('-','')}finally{$hasher.Dispose()}
$runtimeSources=@($signatureInputs | Where-Object {$_.StartsWith('runtime\')} | ForEach-Object {Join-Path $PSScriptRoot $_})

# v1: the base hook pass on the original assembly.
$v1=Join-Path $out 'Assembly-CSharp.v1.dll'
RunCsc @('/nologo','/optimize+',('/out:'+(Join-Path $tools 'PatchAssembly.exe')),('/reference:'+$cecil),(Join-Path $PSScriptRoot 'tools\PatchAssembly.cs'))
RunCsc @('/nologo','/optimize+',('/out:'+(Join-Path $tools 'VerifyPatch.exe')),('/reference:'+$cecil),(Join-Path $PSScriptRoot 'tools\VerifyPatch.cs'))
RunTool (Join-Path $tools 'PatchAssembly.exe') @($original,$v1)
RunTool (Join-Path $tools 'VerifyPatch.exe') @($original,$v1)

# Runtime helper, compiled against this machine's game assemblies.
$identity=Join-Path $out 'BuildIdentity.cs'
Set-Content -LiteralPath $identity -Encoding UTF8 -Value ('namespace TldMp { internal static class BuildIdentity { public const string Sources="'+$sourceSignature+'"; } }')
# The news window prepends these notes to the game's own news.txt at runtime.
$notes=Join-Path $out 'PatchNotes.cs'
$notesB64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CHANGELOG.md'))))
Set-Content -LiteralPath $notes -Encoding UTF8 -Value ('namespace TldMp { internal static class PatchNotes { public const string ChangelogBase64="'+$notesB64+'"; } }')
$runtime=Join-Path $out 'Tld.Multiplayer.dll'
$references=@('UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.VehiclesModule.dll','UnityEngine.ParticleSystemModule.dll','UnityEngine.JSONSerializeModule.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.ScreenCaptureModule.dll','netstandard.dll','System.IO.Compression.dll','Unity.TextMeshPro.dll','UnityEngine.InputLegacyModule.dll') | ForEach-Object {'/reference:'+(Join-Path $managed $_)}
RunCsc (@('/nologo','/target:library','/optimize+',('/out:'+$runtime),('/reference:'+$original))+$references+$runtimeSources+@($identity,$notes))

# v3: the multiplayer hooks on top of v1, checked by the independent verifier.
$v3=Join-Path $out 'Assembly-CSharp.v3.dll'
RunCsc @('/nologo','/optimize+',('/out:'+(Join-Path $tools 'PatchStability.exe')),('/reference:'+$cecil),(Join-Path $PSScriptRoot 'tools\PatchStability.cs'))
RunCsc @('/nologo','/optimize+','/main:VerifyStability',('/out:'+(Join-Path $tools 'VerifyStability.exe')),('/reference:'+$cecil),(Join-Path $PSScriptRoot 'tools\VerifyPatch.cs'),(Join-Path $PSScriptRoot 'tools\VerifyStability.cs'))
RunTool (Join-Path $tools 'PatchStability.exe') @($v1,$runtime,$v3,$managed)
RunTool (Join-Path $tools 'VerifyStability.exe') @($v1,$v3)
Require ((Hash $original) -eq $originalHash) 'The original assembly changed during the build.'

[ordered]@{
    version=3;sourceSignature=$sourceSignature;originalSha256=$originalHash
    v1=$v1;v1Sha256=(Hash $v1)
    patched=$v3;patchedSha256=(Hash $v3);runtime=$runtime;runtimeSha256=(Hash $runtime)
    scope='Built from source by Build-Patch.ps1 and checked by VerifyPatch/VerifyStability.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Write-Host ('PASS: multiplayer v3 built from source and verified (sources '+$sourceSignature.Substring(0,12)+').')
