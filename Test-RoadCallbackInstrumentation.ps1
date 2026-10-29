[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaselineAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [string]$CecilPath=(Join-Path $PSScriptRoot '.tools\patcher\Mono.Cecil.dll'),
    [string]$BuildDirectory=(Join-Path $env:TEMP 'tld-road-callback-instrumentation')
)
$ErrorActionPreference='Stop'
foreach($path in @($BaselineAssembly,$CandidateAssembly,$CecilPath)){
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Required input not found: $path"}
}
New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture=Join-Path $BuildDirectory 'RoadCallbackInstrumentationTests.exe'
Copy-Item -LiteralPath $CecilPath -Destination (Join-Path $BuildDirectory 'Mono.Cecil.dll') -Force
& $csc '/nologo' '/optimize+' ("/out:"+$fixture) ("/reference:"+$CecilPath) (Join-Path $PSScriptRoot 'tests\RoadCallbackInstrumentationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile road callback instrumentation verifier fixture.'}
$output=& $fixture $BaselineAssembly $CandidateAssembly 2>&1
if($LASTEXITCODE -ne 0){throw ('Road callback instrumentation verifier failed: '+($output -join "`n"))}
$output | ForEach-Object { Write-Output $_ }
