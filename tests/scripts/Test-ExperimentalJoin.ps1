[CmdletBinding()]
param()
# Experimental world-join transfer (TLD_MP_EXPERIMENTAL_JOIN=1). Unity-free: the
# production StableNode, ExperimentalJoin codec and ExperimentalJoinReceiver are
# compiled with the console test, and the bridge's adaptation is mirrored there.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is missing.' }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-experimental-join-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $exe = Join-Path $dir 'ExperimentalJoin.exe'
    $sources = @(
        'runtime\NetCore.cs',
        'runtime\StablePackets.cs',
        'runtime\BusPairProtocol.cs',
        'runtime\StableCore.cs',
        'runtime\ExperimentalSnapshot.cs',
        'runtime\ExperimentalJoin.cs',
        'tests\ExperimentalJoinTests.cs'
    ) | ForEach-Object { Join-Path $root $_ }
    & $compiler /nologo ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalJoin compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalJoin test failed: $LASTEXITCODE" }
    Write-Output 'PASS: experimental join protocol'
} finally {
    $exe = Join-Path $dir 'ExperimentalJoin.exe'
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Remove-Item -LiteralPath $dir -Force
}
