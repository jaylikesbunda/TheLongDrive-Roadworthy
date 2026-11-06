[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw "The .NET Framework C# compiler is missing: $csc" }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-replication-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir 'ExperimentalReplicationTests.exe'
try {
    # The generic replication core is Unity-free: it needs only the numeric helpers
    # in NetCore.cs and its own test. This does not build the patch, run the game or
    # read a save; it compiles and runs the isolated model only.
    $sources = @(
        (Join-Path $root 'runtime\NetCore.cs'),
        (Join-Path $root 'runtime\ExperimentalReplication.cs'),
        (Join-Path $root 'tests\ExperimentalReplicationTests.cs')
    )
    foreach ($source in $sources) {
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" }
    }
    & $csc /nologo /optimize+ /warnaserror+ /target:exe ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalReplication compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalReplication tests failed: $LASTEXITCODE" }
} finally {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Remove-Item -LiteralPath $dir -Force -ErrorAction SilentlyContinue
}
