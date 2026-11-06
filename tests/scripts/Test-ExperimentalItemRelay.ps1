[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is missing.' }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('roadworthy-item-relay-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $exe = Join-Path $dir 'ItemRelay.exe'
    $sources = @(
        'runtime\NetCore.cs',
        'runtime\BusPairProtocol.cs',
        'runtime\StablePackets.cs',
        'runtime\ExperimentalItems.cs',
        'runtime\ExperimentalItemStream.cs',
        'runtime\ExperimentalItemRelay.cs',
        'tests\ExperimentalItemRelayTests.cs'
    ) | ForEach-Object { Join-Path $root $_ }
    & $compiler /nologo ('/out:' + $exe) $sources
    if ($LASTEXITCODE -ne 0) { throw "Item relay compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Item relay test failed: $LASTEXITCODE" }
} finally {
    $exe = Join-Path $dir 'ItemRelay.exe'
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Remove-Item -LiteralPath $dir -Force
}
