[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ProbeDirectory, [string]$GameDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $root 'tools\Resolve-GameDirectory.ps1')
$game = Resolve-GameDirectory $GameDirectory
$probe = [IO.Path]::GetFullPath($ProbeDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $probe 'vehicle-shadow-probe-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.kind -ne 'vehicle-capture-shadow-probe' -or !$manifest.observerOnly -or $manifest.transportChanged -ne $false) { throw 'Not an observer-only stage.' }
$shadow = Join-Path $probe 'Assembly-CSharp.shadow.dll'
$helper = Join-Path $probe 'Tld.Multiplayer.VehicleShadowProbe.dll'
$runtime = Join-Path $probe 'Tld.Multiplayer.dll'
if ((Get-FileHash -LiteralPath $shadow -Algorithm SHA256).Hash -ne $manifest.shadowAssemblySha256 -or
    (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash -ne $manifest.probeRuntimeSha256 -or
    (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash -ne $manifest.copiedBaselineRuntimeSha256) { throw 'Observer stage hash mismatch.' }
$stageRoot = Join-Path $root '.local-analysis'
$parent = $stageRoot
while ($parent) {
    if (Test-Path -LiteralPath $parent) {
        $entry = Get-Item -LiteralPath $parent -Force
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point staging path is not supported: $parent" }
    }
    $next = Split-Path -Parent $parent
    if (!$next -or $next -eq $parent) { break }
    $parent = $next
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$stage = Join-Path $stageRoot ('probe-load-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$managed = Join-Path $game 'TheLongDrive_Data\Managed'
Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | Copy-Item -Destination $stage
Copy-Item -LiteralPath $shadow -Destination (Join-Path $stage 'Assembly-CSharp.dll') -Force
Copy-Item -LiteralPath $helper -Destination $stage -Force
Copy-Item -LiteralPath $runtime -Destination $stage -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $stage 'ProbeLoad.exe'
$refs = @('Assembly-CSharp.dll','Tld.Multiplayer.VehicleShadowProbe.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','netstandard.dll') | ForEach-Object { '/reference:' + (Join-Path $stage $_) }
& $compiler /nologo ('/out:' + $exe) $refs (Join-Path $root 'tests\ExperimentalProbeLoadTests.cs')
if ($LASTEXITCODE -ne 0) { throw "Probe-load compilation failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Probe-load check failed: $LASTEXITCODE" }
Write-Output ('Managed-only fixture: ' + $stage)
