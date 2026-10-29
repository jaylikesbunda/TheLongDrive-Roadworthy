[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OriginalAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [Parameter(Mandatory=$true)][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$ManagedDirectory,
    [Parameter(Mandatory=$true)][string]$BuildDirectory
)
$ErrorActionPreference='Stop'
$src=$PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cecil=Join-Path $src '.tools\patcher\Mono.Cecil.dll'
if(!(Test-Path -LiteralPath $OriginalAssembly) -or !(Test-Path -LiteralPath $CandidateAssembly) -or !(Test-Path -LiteralPath $Runtime)) { throw 'Original, candidate, or runtime assembly is missing.' }
New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
Copy-Item -LiteralPath $cecil -Destination (Join-Path $BuildDirectory 'Mono.Cecil.dll') -Force
$verifier=Join-Path $BuildDirectory 'VerifyStability.exe'
$tamper=Join-Path $BuildDirectory 'TamperFlyerKinematicGuard.exe'
& $csc /nologo /optimize+ /main:VerifyStability "/out:$verifier" "/reference:$cecil" (Join-Path $src 'tools\VerifyPatch.cs') (Join-Path $src 'tools\VerifyStability.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile the production stability verifier.'}
& $csc /nologo /optimize+ "/out:$tamper" "/reference:$cecil" (Join-Path $src 'tests\TamperFlyerKinematicGuard.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile the flyer guard negative-control mutator.'}
& $verifier $OriginalAssembly $CandidateAssembly $Runtime
if($LASTEXITCODE -ne 0){throw 'Production stability verifier rejected the candidate assembly.'}
$bad=Join-Path $BuildDirectory 'Assembly-CSharp.bad-flyer-guard.dll'
& $tamper $CandidateAssembly $bad $ManagedDirectory
if($LASTEXITCODE -ne 0){throw 'Could not create the malformed flyer guard negative control.'}
$log=Join-Path $BuildDirectory 'negative-control.txt'
$old=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    & $verifier $OriginalAssembly $bad $Runtime *> $log
    $rejected=$LASTEXITCODE -ne 0
} finally { $ErrorActionPreference=$old }
if(!$rejected -or (Get-Content -LiteralPath $log -Raw) -notmatch 'Flyer kinematic guard control flow or stack pattern changed') {
    throw 'Verifier did not reject the malformed flyer kinematic branch target.'
}
$global:LASTEXITCODE=0
Write-Host 'PASS: production IL parity and malformed-stack negative control for the flyer kinematic velocity guard.'
