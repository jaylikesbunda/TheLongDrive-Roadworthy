[CmdletBinding()]
param([string]$GameDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $root 'tools\Resolve-GameDirectory.ps1')
$game = Resolve-GameDirectory $GameDirectory
$managed = Join-Path $game 'TheLongDrive_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-game-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $output = Join-Path $dir 'VehicleCapture.dll'
    $refs = @('Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll', 'netstandard.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
    $sources = @('runtime\ExperimentalVehiclePose.cs', 'runtime\ExperimentalVehicleCapture.cs', 'runtime\ExperimentalVehicleShadowProbe.cs', 'runtime\ExperimentalIdRange.cs', 'runtime\ExperimentalSavedIds.cs', 'runtime\ExperimentalDetachedRemap.cs') | ForEach-Object { Join-Path $root $_ }
    & $compiler /nologo /target:library ('/out:' + $output) $refs $sources
    if ($LASTEXITCODE -ne 0) { throw "Experimental game adapter compilation failed: $LASTEXITCODE" }
    Write-Output 'PASS: read-only vehicle capture and saved-ID checks compile against installed game'
} finally {
    $output = Join-Path $dir 'VehicleCapture.dll'
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    Remove-Item -LiteralPath $dir -Force
}
