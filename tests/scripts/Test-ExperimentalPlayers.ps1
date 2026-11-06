[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw "C# 5 compiler not found: $csc"}
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe=Join-Path $env:TEMP 'ExperimentalPlayersTests.exe'
$sources=@(
    (Join-Path $root 'runtime\NetCore.cs'),
    (Join-Path $root 'runtime\BusPairProtocol.cs'),
    (Join-Path $root 'runtime\StablePackets.cs'),
    (Join-Path $root 'runtime\ExperimentalPlayers.cs'),
    (Join-Path $root 'tests\ExperimentalPlayersTests.cs')
)
foreach($source in $sources){
    if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "Required source not found: $source"}
}
& $csc /nologo /optimize+ /target:exe "/out:$exe" $sources
if($LASTEXITCODE -ne 0){throw "Experimental player/seat test compilation failed ($LASTEXITCODE)."}
& $exe
if($LASTEXITCODE -ne 0){throw "Experimental player/seat tests failed ($LASTEXITCODE)."}
