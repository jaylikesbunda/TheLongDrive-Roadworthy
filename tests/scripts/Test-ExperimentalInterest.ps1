[CmdletBinding()]
param()
# Interest/far-store core (runtime/ExperimentalInterest.cs). Standalone: the
# Unity-free core is compiled with the numeric helpers it shares and the console
# test, then run. No game assembly, no runtime source list, no live wiring.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw "The .NET Framework C# compiler is missing: $compiler" }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-interest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir 'ExperimentalInterestTests.exe'
try {
    $sources = @(
        'runtime\NetCore.cs',
        'runtime\ExperimentalInterest.cs',
        'tests\ExperimentalInterestTests.cs'
    ) | ForEach-Object { Join-Path $root $_ }
    foreach ($source in $sources) {
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" }
    }
    & $compiler /nologo /optimize+ /warnaserror+ /target:exe ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalInterest compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "ExperimentalInterest tests failed: $LASTEXITCODE" }
} finally {
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Remove-Item -LiteralPath $dir -Force -ErrorAction SilentlyContinue
}
