# Stage a passed native-safety Test-Stability run as a reproducible v3 +
# candidate-local v4 pair. This script only writes beneath .local-analysis\staged.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RunDirectory,
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$GameDirectory=''
)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$packagingRoot=Join-Path $sourceRoot 'packaging\singleplayer'
$stageRoot=[IO.Path]::GetFullPath((Join-Path $sourceRoot '.local-analysis\staged'))
. (Join-Path $sourceRoot 'tools\Resolve-GameDirectory.ps1')
$managed=Join-Path (Resolve-GameDirectory $GameDirectory) 'TheLongDrive_Data\Managed'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cecil=Join-Path $sourceRoot '.tools\patcher\Mono.Cecil.dll'
$runPath=[IO.Path]::GetFullPath($RunDirectory)
$runsRoot=[IO.Path]::GetFullPath((Join-Path $sourceRoot '.local-analysis\runs'))
$destinationPath=[IO.Path]::GetFullPath($Destination)
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function FullDirectoryPrefix([string]$path){return $path.TrimEnd('\')+'\'}
function Require([bool]$condition,[string]$message){if(!$condition){throw $message}}
function ComputeSignature([string[]]$relativeInputs,[string]$basePath){
    $signatureText=($relativeInputs | ForEach-Object {$_+':'+(Hash (Join-Path $basePath $_))}) -join "`n"
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($signatureText))).Replace('-','')}
    finally{$hasher.Dispose()}
}
function RunCsc([string[]]$arguments){& $csc @arguments;if($LASTEXITCODE -ne 0){throw "C# compiler failed ($LASTEXITCODE)."}}
$toolScratch=Join-Path $env:TEMP 'opencode\native-safety-stage'
function RunTool([string]$exe,[string[]]$arguments){
    $exePath=[IO.Path]::GetFullPath($exe)
    New-Item -ItemType Directory -Force -Path $toolScratch | Out-Null
    $target=Join-Path $toolScratch ([IO.Path]::GetFileNameWithoutExtension($exePath)+'-'+(Hash $exePath).Substring(0,12)+[IO.Path]::GetExtension($exePath))
    Copy-Item -LiteralPath $exePath -Destination $target -Force
    Get-ChildItem -LiteralPath (Split-Path -Parent $exePath) -Filter '*.dll' -File -ErrorAction SilentlyContinue |
        ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $toolScratch $_.Name) -Force}
    & $target @arguments
    if($LASTEXITCODE -ne 0){throw "Failed ($LASTEXITCODE): $exePath"}
}

Require ($runPath.StartsWith((FullDirectoryPrefix $runsRoot),[StringComparison]::OrdinalIgnoreCase)) 'RunDirectory must be a Test-Stability run under .local-analysis\runs.'
Require ($destinationPath.StartsWith((FullDirectoryPrefix $stageRoot),[StringComparison]::OrdinalIgnoreCase)) 'Destination must be a new subdirectory beneath .local-analysis\staged.'
Require (!(Test-Path -LiteralPath $destinationPath)) 'Stage destination already exists; choose a new path.'
Require (Test-Path -LiteralPath $csc) 'Microsoft .NET Framework C# compiler is unavailable.'
Require (Test-Path -LiteralPath $cecil) 'Candidate Mono.Cecil is missing; run candidate Test-Stability first.'
Require (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')) 'Game managed reference directory is unavailable.'

$stability=Join-Path $runPath 'stability-v3'
$runManifestPath=Join-Path $stability 'manifest.json'
Require (Test-Path -LiteralPath $runManifestPath) 'Run has no completed stability-v3 manifest.'
$runManifest=Get-Content -LiteralPath $runManifestPath -Raw | ConvertFrom-Json
Require ($runManifest.version -eq 3) 'Run manifest is not a v3 candidate.'
Require ($runManifest.originalSha256 -eq 'CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000') 'Run manifest does not identify the trusted native original.'
$latestStabilityPath=Join-Path $sourceRoot '.local-analysis\latest-stability.txt'
Require (Test-Path -LiteralPath $latestStabilityPath) 'Candidate latest-stability pointer is missing; run Test-Stability to completion.'
$latestStability=[IO.Path]::GetFullPath((Get-Content -LiteralPath $latestStabilityPath -Raw).Trim())
Require ($latestStability.Equals([IO.Path]::GetFullPath($stability),[StringComparison]::OrdinalIgnoreCase)) 'RunDirectory is not the latest completed candidate Test-Stability run.'

# Recompute the v3 runtime/patch source signature from the candidate's own list.
$signatureLine=Get-Content -LiteralPath (Join-Path $sourceRoot 'Test-Stability.ps1') | Where-Object {$_.StartsWith('$signatureInputs=')}
Require ($signatureLine.Count -eq 1) 'Could not resolve the candidate signatureInputs list.'
$signatureInputs=[regex]::Matches($signatureLine,"'([^']+)'") | ForEach-Object {$_.Groups[1].Value}
$sourceSignature=ComputeSignature $signatureInputs $sourceRoot
Require ($runManifest.sourceSignature -eq $sourceSignature) 'Candidate runtime/patch sources changed after this Test-Stability run.'
Require (Test-Path -LiteralPath (Join-Path $stability 'BuildIdentity.cs')) 'Passing run has no BuildIdentity.cs.'
$identityText=Get-Content -LiteralPath (Join-Path $stability 'BuildIdentity.cs') -Raw
Require ($identityText -match ('Sources="'+[regex]::Escape($sourceSignature)+'"')) 'Passing runtime identity does not match the recomputed source signature.'

# Freeze test, tool, harness, packaging, and staging script inputs as a second
# signature so the staged receipts identify exactly what was validated.
$validationInputs=@(
    @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'tests') -File | Sort-Object Name | ForEach-Object {'tests\'+$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'tools') -File | Sort-Object Name | ForEach-Object {'tools\'+$_.Name})+
    @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.ps1' -File | Sort-Object Name | ForEach-Object {$_.Name})+
    @(Get-ChildItem -LiteralPath $packagingRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {'packaging\singleplayer\'+$_.Name})
)
$validationText=($validationInputs | ForEach-Object {
    $_+':'+(Hash (Join-Path $sourceRoot $_))
}) -join "`n"
$validationHasher=[Security.Cryptography.SHA256]::Create()
try {$validationSignature=[BitConverter]::ToString($validationHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($validationText))).Replace('-','')}
finally {$validationHasher.Dispose()}
Require ($runManifest.validationSignature -eq $validationSignature) 'Tests, tools, packaging, harness, or staging script changed after this Test-Stability run.'

$runtimeSource=[IO.Path]::GetFullPath($runManifest.runtime)
$v3Source=[IO.Path]::GetFullPath($runManifest.patched)
$v1=Join-Path $runPath 'Assembly-CSharp.dll'
$results=[IO.Path]::GetFullPath($runManifest.results)
Require ((Test-Path -LiteralPath $runtimeSource) -and (Hash $runtimeSource) -eq $runManifest.runtimeSha256) 'Passing runtime is missing or its hash differs from the run manifest.'
Require ((Test-Path -LiteralPath $v3Source) -and (Hash $v3Source) -eq $runManifest.patchedSha256) 'Passing v3 is missing or its hash differs from the run manifest.'
Require ((Test-Path -LiteralPath $results) -and (Get-Item -LiteralPath $results).Length -gt 0) 'Passing run results are missing.'
Require ((Test-Path -LiteralPath $v1) -and (Hash $v1) -eq 'ACF994A64E82564E471B487A9CE31853894880D142370C9204592047AB217165') 'Run does not contain the trusted v1 patch input.'

New-Item -ItemType Directory -Path $destinationPath | Out-Null
Copy-Item -LiteralPath $cecil -Destination $destinationPath
Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $destinationPath 'Tld.Multiplayer.dll')
Copy-Item -LiteralPath (Join-Path $stability 'BuildIdentity.cs') -Destination $destinationPath
Copy-Item -LiteralPath $runManifestPath -Destination (Join-Path $destinationPath 'passed-run-manifest.json')
Copy-Item -LiteralPath $results -Destination (Join-Path $destinationPath 'results.txt')
$sourceSignature | Set-Content -LiteralPath (Join-Path $destinationPath 'source-signature.txt') -Encoding UTF8
$validationSignature | Set-Content -LiteralPath (Join-Path $destinationPath 'validation-signature.txt') -Encoding UTF8

# Rebuild v3 from the trusted v1 input with candidate-local patcher/verifier and
# require byte identity with the exact v3 that passed Test-Stability.
$patcher=Join-Path $destinationPath 'PatchStability.exe'
$verifier=Join-Path $destinationPath 'VerifyStability.exe'
RunCsc @('/nologo','/optimize+',('/out:'+$patcher),('/reference:'+$cecil),(Join-Path $sourceRoot 'tools\PatchStability.cs'))
RunCsc @('/nologo','/optimize+','/main:VerifyStability',('/out:'+$verifier),('/reference:'+$cecil),(Join-Path $sourceRoot 'tools\VerifyPatch.cs'),(Join-Path $sourceRoot 'tools\VerifyStability.cs'))
$v3=Join-Path $destinationPath 'Assembly-CSharp.v3.dll'
RunTool $patcher @($v1,(Join-Path $destinationPath 'Tld.Multiplayer.dll'),$v3,$managed)
Require ((Hash $v3) -eq $runManifest.patchedSha256) 'Rebuilt candidate v3 is not byte-identical to the passing Test-Stability v3.'
RunTool $verifier @($v1,$v3)

# Build the native-restore packaging copy (12-edit, no-PropTiers v4) on this
# exact v3, then require the candidate-local independent v4 verifier to pass.
$helper=Join-Path $destinationPath 'TldStableHelper.dll'
RunCsc @('/nologo','/target:library','/optimize+',('/out:'+$helper),(Join-Path $packagingRoot 'TldStable.cs'))
$shared=@('Checks.cs','Expected.cs','Invariants.cs') | ForEach-Object {Join-Path $packagingRoot $_}
$patchV4=Join-Path $destinationPath 'PatchV4.exe'
$verifyV4=Join-Path $destinationPath 'VerifyV4.exe'
RunCsc (@('/nologo','/optimize+',('/out:'+$patchV4),('/reference:'+$cecil),('/resource:'+$helper+',TldStableHelper.dll'))+$shared+@(Join-Path $packagingRoot 'PatchV4.cs'))
RunCsc (@('/nologo','/optimize+',('/out:'+$verifyV4),('/reference:'+$cecil))+$shared+@(Join-Path $packagingRoot 'VerifyV4.cs'))
$v3Hash=Hash $v3
$v4=Join-Path $destinationPath 'Assembly-CSharp.v4.dll'
RunTool $patchV4 @($v3,$v4,$managed,$v3Hash)
RunTool $verifyV4 @($v3,$v4,$v3Hash)
# Inspect the emitted game IL. Tool diagnostics and dormant tool helper methods
# can mention PropTiers without injecting a call into the game.
[void][Reflection.Assembly]::UnsafeLoadFrom($cecil)
foreach($assemblyPath in @($v3,$v4)){
    $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
    try{
        $types=New-Object 'System.Collections.Generic.Queue[Mono.Cecil.TypeDefinition]'
        foreach($type in $assembly.MainModule.Types){$types.Enqueue($type)}
        while($types.Count -gt 0){
            $type=$types.Dequeue()
            foreach($nested in $type.NestedTypes){$types.Enqueue($nested)}
            foreach($method in $type.Methods){
                if(!$method.HasBody){continue}
                foreach($instruction in $method.Body.Instructions){
                    $callee=$instruction.Operand -as [Mono.Cecil.MethodReference]
                    Require (!($callee -and $callee.DeclaringType.FullName -eq 'TldMp.PropTiers' -and $callee.Name -eq 'Register')) ('Removed PropTiers call emitted in '+$method.FullName)
                }
            }
        }
    }finally{$assembly.Dispose()}
}
Require (Test-Path -LiteralPath (Join-Path $destinationPath 'v4-receipt.json')) 'Candidate PatchV4 did not produce its verification receipt.'

$v4Receipt=Get-Content -LiteralPath (Join-Path $destinationPath 'v4-receipt.json') -Raw | ConvertFrom-Json
$v4Hash=Hash $v4
Require ($v4Receipt.input.sha256 -eq $v3Hash) 'Candidate v4 receipt input hash does not match the verified v3.'
Require ($v4Receipt.output.sha256 -eq $v4Hash) 'Candidate v4 receipt output hash does not match the verified v4.'
$manifest=[ordered]@{
    version=3
    sourceSignature=$sourceSignature
    validationSignature=$validationSignature
    originalSha256=$runManifest.originalSha256
    patched=$v3
    patchedSha256=$v3Hash
    runtime=(Join-Path $destinationPath 'Tld.Multiplayer.dll')
    runtimeSha256=(Hash (Join-Path $destinationPath 'Tld.Multiplayer.dll'))
    results=(Join-Path $destinationPath 'results.txt')
    scope='Native-safety candidate staged from a passed candidate Test-Stability run; candidate v4 packaging excludes PropTiers registration. Offline validation only; live gameplay validation pending.'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destinationPath 'manifest.json') -Encoding UTF8
[ordered]@{
    runDirectory=$runPath
    sourceSignature=$sourceSignature
    validationSignature=$validationSignature
    v1Sha256=(Hash $v1)
    runtimeSha256=(Hash (Join-Path $destinationPath 'Tld.Multiplayer.dll'))
    v3Sha256=$v3Hash
    v4Sha256=$v4Hash
    v4Verification='PASS'
    sourceManifest=(Join-Path $destinationPath 'manifest.json')
    scope='Staged only. No game or install files were changed.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destinationPath 'stage.json') -Encoding UTF8
Get-FileHash -LiteralPath (Join-Path $destinationPath 'Tld.Multiplayer.dll'),$v3,$v4 | Format-Table -AutoSize
Write-Host ('Staged candidate-native-safety v3/v4 at '+$destinationPath)
