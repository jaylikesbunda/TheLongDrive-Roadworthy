[CmdletBinding()]
param([string]$GameDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $root 'tools\Resolve-GameDirectory.ps1')
$game = Resolve-GameDirectory $GameDirectory
$managed = Join-Path $game 'TheLongDrive_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
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
$stage = Join-Path $stageRoot ('experimental-native-records-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
# The game assemblies are read-only inputs. The isolated stage is retained for
# diagnosis; this test never launches the game or opens the user's save files.
Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | Copy-Item -Destination $stage
$refs = @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','netstandard.dll') | ForEach-Object { '/reference:' + (Join-Path $stage $_) }
$sources = @('runtime\ExperimentalIdRange.cs','runtime\ExperimentalSavedIds.cs','runtime\ExperimentalDetachedRemap.cs','tests\ExperimentalSavedIdsGameTests.cs') | ForEach-Object { Join-Path $root $_ }
$exe = Join-Path $stage 'ExperimentalSavedIdsGameTests.exe'
& $compiler /nologo ('/out:' + $exe) $refs $sources
if ($LASTEXITCODE -ne 0) { throw "Native saved-ID test compilation failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Native saved-ID test failed: $LASTEXITCODE" }
$auditExe = Join-Path $stage 'ExperimentalCopyAudit.exe'
& $compiler /nologo ('/out:' + $auditExe) $refs (Join-Path $root 'tests\ExperimentalCopyAudit.cs')
if ($LASTEXITCODE -ne 0) { throw "Native scalar copy audit compilation failed: $LASTEXITCODE" }
& $auditExe
if ($LASTEXITCODE -ne 0) { throw "Native scalar copy audit failed: $LASTEXITCODE" }
Write-Output ('Detached-record test artifacts: ' + $stage)
