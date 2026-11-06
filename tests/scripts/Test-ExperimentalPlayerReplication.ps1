[CmdletBinding()]
param()
# Standalone, Unity-free check for the Phase 5 player replication core
# (runtime/ExperimentalPlayerReplication.cs + tests/ExperimentalPlayerReplicationTests.cs).
# Compiles NetCore, BusPairProtocol, StablePackets, ExperimentalPlayers, the replication
# core and the console test with the .NET Framework C# 5 compiler and runs it. It does not
# touch the game, the patch, Test-Stability or any file outside a private temp directory.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw "The .NET Framework C# 5 compiler is missing: $compiler" }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-player-replication-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $exe = Join-Path $dir 'ExperimentalPlayerReplicationTests.exe'
    $sources = @(
        'runtime\NetCore.cs',
        'runtime\BusPairProtocol.cs',
        'runtime\StablePackets.cs',
        'runtime\ExperimentalPlayers.cs',
        'runtime\ExperimentalPlayerReplication.cs',
        'tests\ExperimentalPlayerReplicationTests.cs'
    ) | ForEach-Object { Join-Path $root $_ }
    foreach ($source in $sources) {
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" }
    }
    & $compiler /nologo /optimize+ /target:exe ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "Experimental player replication test compilation failed ($LASTEXITCODE)." }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Experimental player replication tests failed ($LASTEXITCODE)." }
    Write-Output 'PASS: experimental player replication'
} finally {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}
