[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw "C# 5 compiler not found: $csc"}
$exe=Join-Path $env:TEMP 'NativeSafetyAdapterTests.exe'
$sources=@(
    (Join-Path $PSScriptRoot 'runtime\NativeAttachmentGuard.cs'),
    (Join-Path $PSScriptRoot 'runtime\NativeSeatSafety.cs'),
    (Join-Path $PSScriptRoot 'runtime\NativeSpawnSafety.cs'),
    (Join-Path $PSScriptRoot 'runtime\SpawnClearance.cs'),
    (Join-Path $PSScriptRoot 'runtime\NativeSafetyAdapter.cs'),
    (Join-Path $PSScriptRoot 'tests\NativeSafetyAdapterTests.cs')
)
& $csc /nologo /optimize+ /target:exe "/out:$exe" $sources
if($LASTEXITCODE -ne 0){throw "Native safety adapter test compilation failed ($LASTEXITCODE)."}
& $exe
if($LASTEXITCODE -ne 0){throw "Native safety adapter tests failed ($LASTEXITCODE)."}
