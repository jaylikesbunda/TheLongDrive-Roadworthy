[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw "C# 5 compiler not found: $csc" }
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# Item event stream only: the authority core, its native packet validator and the
# bus-pair layout it delegates to. Nothing here needs Unity, Steam or the bridge.
$sources = @(
    (Join-Path $root 'runtime\NetCore.cs'),
    (Join-Path $root 'runtime\BusPairProtocol.cs'),
    (Join-Path $root 'runtime\StablePackets.cs'),
    (Join-Path $root 'runtime\ExperimentalItems.cs'),
    (Join-Path $root 'runtime\ExperimentalItemStream.cs'),
    (Join-Path $root 'tests\ExperimentalItemStreamTests.cs')
)
foreach ($source in $sources) {
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" }
}
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-itemstream-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir 'ExperimentalItemStreamTests.exe'
try {
    & $csc /nologo /optimize+ /target:exe "/out:$exe" $sources
    if ($LASTEXITCODE -ne 0) { throw "Experimental item stream compilation failed ($LASTEXITCODE)." }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Experimental item stream tests failed ($LASTEXITCODE)." }
} finally {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}
