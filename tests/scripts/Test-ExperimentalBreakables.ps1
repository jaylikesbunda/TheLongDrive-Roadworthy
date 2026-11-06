[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw "The .NET Framework C# compiler is missing: $csc" }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-breakables-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir 'ExperimentalBreakablesTests.exe'
try {
    # The breakable/prop authority core is Unity-free: it needs the native packet
    # validator (StablePackets) and the two codecs that validator delegates to.
    $sources = @(
        (Join-Path $repo 'runtime\NetCore.cs'),
        (Join-Path $repo 'runtime\BusPairProtocol.cs'),
        (Join-Path $repo 'runtime\StablePackets.cs'),
        (Join-Path $repo 'runtime\ExperimentalBreakables.cs'),
        (Join-Path $repo 'tests\ExperimentalBreakablesTests.cs')
    )
    & $csc /nologo /optimize+ /warnaserror+ ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalBreakables compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalBreakables tests failed: $LASTEXITCODE" }
} finally {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Remove-Item -LiteralPath $dir -Force -ErrorAction SilentlyContinue
}
