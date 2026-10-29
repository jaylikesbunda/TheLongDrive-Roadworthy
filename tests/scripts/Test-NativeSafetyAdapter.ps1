[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw "C# 5 compiler not found: $csc"}
$exe=Join-Path $env:TEMP 'NativeSafetyAdapterTests.exe'
$sources=@(
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeAttachmentGuard.cs'),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSeatSafety.cs'),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSpawnSafety.cs'),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\SpawnClearance.cs'),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime\NativeSafetyAdapter.cs'),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\NativeSafetyAdapterTests.cs')
)
& $csc /nologo /optimize+ /target:exe "/out:$exe" $sources
if($LASTEXITCODE -ne 0){throw "Native safety adapter test compilation failed ($LASTEXITCODE)."}
& $exe
if($LASTEXITCODE -ne 0){throw "Native safety adapter tests failed ($LASTEXITCODE)."}
