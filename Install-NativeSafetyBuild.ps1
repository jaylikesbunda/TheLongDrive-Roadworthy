# Install a verified native-safety stage through the existing five-file v3/v4
# chain. This is prepared for explicit use; creating it does not install a build.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Stage,
    [string]$BackupDirectory='',
    [string]$GameDirectory=''
)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$packagingRoot=Join-Path $sourceRoot 'packaging\singleplayer'
$stageRoot=[IO.Path]::GetFullPath((Join-Path $sourceRoot '.local-analysis\staged'))
. (Join-Path $sourceRoot 'tools\Resolve-GameDirectory.ps1')
$managed=Join-Path (Resolve-GameDirectory $GameDirectory) 'TheLongDrive_Data\Managed'
$stagePath=[IO.Path]::GetFullPath($Stage)
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function FullDirectoryPrefix([string]$path){return $path.TrimEnd('\')+'\'}
function Require([bool]$condition,[string]$message){if(!$condition){throw $message}}
function Closed{
    if(Get-Process -Name 'TheLongDrive' -ErrorAction SilentlyContinue){throw 'The Long Drive is running. Installation stopped.'}
}
function Replace-Checked([string]$source,[string]$target,[string]$expected){
    $temp=$target+'.tldmp-native-safety-new'
    if(Test-Path -LiteralPath $temp){throw ('Unexpected temporary target exists: '+$temp)}
    try{
        Copy-Item -LiteralPath $source -Destination $temp
        if((Hash $temp) -ne $expected){throw ('Staged hash mismatch for '+$source)}
        Closed
        [IO.File]::Replace($temp,$target,[System.Management.Automation.Language.NullString]::Value)
        if((Hash $target) -ne $expected){throw ('Replacement hash mismatch for '+$target)}
    }finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
}
function ComputeSignature([string[]]$relativeInputs,[string]$basePath){
    $signatureText=($relativeInputs | ForEach-Object {$_+':'+(Hash (Join-Path $basePath $_))}) -join "`n"
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($signatureText))).Replace('-','')}
    finally{$hasher.Dispose()}
}
function Run-StageVerifier([string]$exe,[string[]]$arguments){
    $scratch=Join-Path $env:TEMP 'opencode\native-safety-install-verify'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $copy=Join-Path $scratch ([IO.Path]::GetFileNameWithoutExtension($exe)+'-'+(Hash $exe).Substring(0,12)+[IO.Path]::GetExtension($exe))
    Copy-Item -LiteralPath $exe -Destination $copy -Force
    Get-ChildItem -LiteralPath $stagePath -Filter '*.dll' -File -ErrorAction SilentlyContinue |
        ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $scratch $_.Name) -Force}
    & $copy @arguments
    if($LASTEXITCODE -ne 0){throw ('Stage verifier failed: '+$exe)}
}

Require ($stagePath.StartsWith((FullDirectoryPrefix $stageRoot),[StringComparison]::OrdinalIgnoreCase)) 'Stage must be beneath .local-analysis\staged.'
Require (Test-Path -LiteralPath $stagePath) 'Stage directory was not found.'
Closed

$stageInfoPath=Join-Path $stagePath 'stage.json'
$manifestPath=Join-Path $stagePath 'manifest.json'
$stageInfo=Get-Content -LiteralPath $stageInfoPath -Raw | ConvertFrom-Json
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Require ($stageInfo.v4Verification -eq 'PASS') 'Stage does not record a successful candidate v4 verification.'
Require ($stageInfo.scope -like '*No game or install files were changed*') 'Stage metadata is not from the native-safety staging workflow.'
Require ($manifest.scope -like '*native-safety candidate*') 'Manifest does not identify the native-safety candidate scope.'
Require ($stageInfo.sourceSignature -eq $manifest.sourceSignature -and $stageInfo.validationSignature -eq $manifest.validationSignature) 'Stage metadata and frozen build manifest disagree about signatures.'
Require ($stageInfo.v3Sha256 -eq $manifest.patchedSha256 -and $stageInfo.runtimeSha256 -eq $manifest.runtimeSha256) 'Stage metadata and manifest disagree about v3/runtime hashes.'
Require ($manifest.originalSha256 -eq 'CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000') 'Stage does not identify the trusted pristine original.'

# Recompute the exact candidate signatures used by the gate and staging helper.
$signatureLine=Get-Content -LiteralPath (Join-Path $sourceRoot 'tests\scripts\Test-Stability.ps1') | Where-Object {$_.StartsWith('$signatureInputs=')}
Require ($signatureLine.Count -eq 1) 'Could not resolve the candidate source signature list.'
$signatureInputs=[regex]::Matches($signatureLine,"'([^']+)'") | ForEach-Object {$_.Groups[1].Value}
$sourceSignature=ComputeSignature $signatureInputs $sourceRoot
Require ($sourceSignature -eq $stageInfo.sourceSignature) 'Candidate runtime/patch sources changed after staging.'
$validationInputs=@(
    @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'tests') -File | Sort-Object Name | ForEach-Object {'tests\'+$_.Name})+
    @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'tools') -File | Sort-Object Name | ForEach-Object {'tools\'+$_.Name})+
    @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.ps1' -File | Sort-Object Name | ForEach-Object {$_.Name})+
    @(Get-ChildItem -LiteralPath $packagingRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {'packaging\singleplayer\'+$_.Name})
)
$validationText=($validationInputs | ForEach-Object {$_+':'+(Hash (Join-Path $sourceRoot $_))}) -join "`n"
$validationHasher=[Security.Cryptography.SHA256]::Create()
try {$validationSignature=[BitConverter]::ToString($validationHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($validationText))).Replace('-','')}
finally {$validationHasher.Dispose()}
Require ($validationSignature -eq $stageInfo.validationSignature) 'Candidate tests/tools/packaging/staging inputs changed after staging.'

# Recheck the native-safety helper coverage named by the staging workflow.
$results=Get-Content -LiteralPath (Join-Path $stagePath 'results.txt') -Raw
foreach($test in @(
    @{Name='spawn policy';Pattern='Native spawn safety policy \(\d+ checks\)'},
    @{Name='attachment guard';Pattern='NativeAttachmentGuardTests passed: \d+ assertions'},
    @{Name='passenger seat safety';Pattern='\d+ native passenger seat safety assertions'}
)){
    Require ($results -match $test.Pattern) ('Passed run is missing native-safety validation output: '+$test.Name)
}

$runtime=Join-Path $stagePath 'Tld.Multiplayer.dll'
$v3=Join-Path $stagePath 'Assembly-CSharp.v3.dll'
$v4=Join-Path $stagePath 'Assembly-CSharp.v4.dll'
$v1=Join-Path $stageInfo.runDirectory 'Assembly-CSharp.dll'
$runtimeHash=Hash $runtime
$v3Hash=Hash $v3
$v4Hash=Hash $v4
Require ($runtimeHash -eq $stageInfo.runtimeSha256 -and $runtimeHash -eq $manifest.runtimeSha256) 'Staged runtime changed after staging.'
Require ($v3Hash -eq $stageInfo.v3Sha256 -and $v3Hash -eq $manifest.patchedSha256) 'Staged v3 changed after staging.'
Require ($v4Hash -eq $stageInfo.v4Sha256) 'Staged v4 changed after staging.'
Require ((Test-Path -LiteralPath $v1) -and (Hash $v1) -eq $stageInfo.v1Sha256) 'Trusted v1 patch input is missing or changed.'

$editReport=Get-Content -LiteralPath (Join-Path $stagePath 'v4-receipt.json') -Raw | ConvertFrom-Json
Require ($editReport.input.sha256 -eq $v3Hash -and $editReport.output.sha256 -eq $v4Hash) 'Candidate v4 edit receipt does not match the staged pair.'

# Independently re-run both candidate verifiers before touching the live chain.
Run-StageVerifier (Join-Path $stagePath 'VerifyStability.exe') @($v1,$v3)
Run-StageVerifier (Join-Path $stagePath 'VerifyV4.exe') @($v3,$v4,$v3Hash)

$liveAssembly=Join-Path $managed 'Assembly-CSharp.dll'
$liveRuntime=Join-Path $managed 'Tld.Multiplayer.dll'
$liveV4Original=Join-Path $managed 'Assembly-CSharp.tldmp-v4-original.bin'
$liveV3Original=Join-Path $managed 'Assembly-CSharp.tldmp-v3-original.bin'
$liveReceipt3=Join-Path $managed 'tldmp-v3-receipt.json'
$liveReceipt4=Join-Path $managed 'tldmp-v4-receipt.json'
$old3=Get-Content -LiteralPath $liveReceipt3 -Raw | ConvertFrom-Json
$old4=Get-Content -LiteralPath $liveReceipt4 -Raw | ConvertFrom-Json
$pristineBefore=Hash $liveV3Original
Require ($pristineBefore -eq $manifest.originalSha256) 'The installed pristine v3 original backup is not the trusted original.'
Require ((Hash $liveAssembly) -eq $old4.patchedSha256) 'Installed assembly does not match its v4 receipt.'
Require ((Hash $liveRuntime) -eq $old3.runtimeSha256) 'Installed runtime does not match its v3 receipt.'
Require ($old3.version -eq 3 -and $old4.version -eq 4) 'Installed receipt versions are not a recognized v3/v4 chain.'
Require ($old3.runtimeSha256 -eq $old4.runtimeSha256) 'Installed receipts disagree about the runtime.'
Require ((Hash $liveV4Original) -eq $old3.patchedSha256) 'Installed v4 original backup does not match the v3 receipt.'
Require ($old3.originalSha256 -eq $pristineBefore -and $old4.originalSha256 -eq $old3.patchedSha256) 'Installed original/patch lineage is inconsistent.'
Require ($old4.input.sha256 -eq $old3.patchedSha256 -and $old4.output.sha256 -eq $old4.patchedSha256) 'Installed v4 receipt input/output chain is inconsistent.'

# Prepare stage-local receipts and rollback copies before any live file is changed.
$newManifest=[ordered]@{
    version=3;sourceSignature=$sourceSignature;validationSignature=$validationSignature
    originalSha256=$pristineBefore;patched=$v3;patchedSha256=$v3Hash;runtime=$runtime;runtimeSha256=$runtimeHash
    results=(Join-Path $stagePath 'results.txt')
    scope='Native-safety candidate installed after candidate-local spawn, passenger-seat, attachment/wheel, v3 and v4 offline checks. Live gameplay validation is pending.'
}
$newManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagePath 'manifest.json') -Encoding UTF8
$new3=[ordered]@{version=3;sourceSignature=$sourceSignature;validationSignature=$validationSignature;originalSha256=$pristineBefore;patchedSha256=$v3Hash;runtimeSha256=$runtimeHash;manifest=(Join-Path $stagePath 'manifest.json')}
$new3 | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagePath 'tldmp-v3-receipt.json') -Encoding UTF8
$new4=[ordered]@{
    version=4;patch='single-player';createdUtc=(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    input=@{file='Assembly-CSharp.dll (multiplayer v3)';sha256=$v3Hash}
    output=@{file='Assembly-CSharp.dll';sha256=$v4Hash;bytes=(Get-Item -LiteralPath $v4).Length}
    originalSha256=$v3Hash;patchedSha256=$v4Hash;runtimeSha256=$runtimeHash
    backup='Assembly-CSharp.tldmp-v4-original.bin';verify='PASS';edits=$editReport.edits
    sourceSignature=$sourceSignature;validationSignature=$validationSignature
}
$new4 | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $stagePath 'tldmp-v4-receipt.json') -Encoding UTF8
if($BackupDirectory -eq ''){$BackupDirectory=Join-Path $stagePath 'installed-backup'}
$backupPath=[IO.Path]::GetFullPath($BackupDirectory)
Require ($backupPath.StartsWith((FullDirectoryPrefix $stagePath),[StringComparison]::OrdinalIgnoreCase)) 'Rollback backups must remain beneath the selected stage.'
Require (!(Test-Path -LiteralPath $backupPath)) 'Rollback backup directory already exists; refusing to overwrite prior evidence.'
New-Item -ItemType Directory -Path $backupPath | Out-Null
$swap=@(
    @{Name='Tld.Multiplayer.dll';Target=$liveRuntime;Source=$runtime;Expected=$runtimeHash},
    @{Name='Assembly-CSharp.tldmp-v4-original.bin';Target=$liveV4Original;Source=$v3;Expected=$v3Hash},
    @{Name='Assembly-CSharp.dll';Target=$liveAssembly;Source=$v4;Expected=$v4Hash},
    @{Name='tldmp-v3-receipt.json';Target=$liveReceipt3;Source=(Join-Path $stagePath 'tldmp-v3-receipt.json');Expected=(Hash (Join-Path $stagePath 'tldmp-v3-receipt.json'))},
    @{Name='tldmp-v4-receipt.json';Target=$liveReceipt4;Source=(Join-Path $stagePath 'tldmp-v4-receipt.json');Expected=(Hash (Join-Path $stagePath 'tldmp-v4-receipt.json'))}
)
foreach($entry in $swap){
    $beforeHash=Hash $entry.Target
    Copy-Item -LiteralPath $entry.Target -Destination (Join-Path $backupPath $entry.Name)
    Require ((Hash (Join-Path $backupPath $entry.Name)) -eq $beforeHash) ('Rollback copy failed hash verification: '+$entry.Name)
    $entry['BeforeHash']=$beforeHash
}
$rollback=@()
try{
    foreach($entry in $swap){
        Closed
        Require ((Hash $entry.Target) -eq $entry['BeforeHash']) ('Live chain changed after preflight: '+$entry.Name)
        $rollback+=$entry
        Replace-Checked $entry.Source $entry.Target $entry.Expected
        Closed
    }
    Require ((Hash $liveRuntime) -eq $runtimeHash) 'Post-install runtime hash mismatch.'
    Require ((Hash $liveV4Original) -eq $v3Hash) 'Post-install v4 original backup is not the new v3.'
    Require ((Hash $liveAssembly) -eq $v4Hash) 'Post-install assembly hash mismatch.'
    Require ((Hash $liveV3Original) -eq $pristineBefore) 'Pristine original backup changed during installation.'
    $installed3=Get-Content -LiteralPath $liveReceipt3 -Raw | ConvertFrom-Json
    $installed4=Get-Content -LiteralPath $liveReceipt4 -Raw | ConvertFrom-Json
    Require ($installed3.patchedSha256 -eq $v3Hash -and $installed3.runtimeSha256 -eq $runtimeHash) 'Installed v3 receipt does not describe the new v3/runtime.'
    Require ($installed4.originalSha256 -eq $v3Hash -and $installed4.patchedSha256 -eq $v4Hash -and $installed4.runtimeSha256 -eq $runtimeHash) 'Installed v4 receipt does not describe the new v3/v4/runtime chain.'
    Require ($installed4.input.sha256 -eq $installed3.patchedSha256 -and $installed4.output.sha256 -eq $installed4.patchedSha256) 'Installed receipt chain is inconsistent.'
}catch{
    $problem=$_
    Closed
    foreach($entry in $rollback){
        $saved=Join-Path $backupPath $entry.Name
        Replace-Checked $saved $entry.Target (Hash $saved)
    }
    foreach($entry in $swap){Require ((Hash $entry.Target) -eq $entry['BeforeHash']) ('Rollback hash mismatch: '+$entry.Name)}
    Require ((Hash $liveV3Original) -eq $pristineBefore) 'Pristine original backup changed during rollback.'
    throw $problem
}
[ordered]@{
    installedUtc=(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    stage=$stagePath;runtimeSha256=$runtimeHash;v3Sha256=$v3Hash;v4Sha256=$v4Hash
    pristineOriginalSha256=$pristineBefore;sourceSignature=$sourceSignature;validationSignature=$validationSignature
    backup=$backupPath
    scope='Native-safety test build. Candidate policy/integration offline checks passed; live gameplay validation remains pending.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagePath 'deployment.json') -Encoding UTF8
Write-Host ('Installed verified native-safety test build from '+$stagePath)
Write-Host ('Rollback copies: '+$backupPath)
Write-Host 'The pristine v3 original and saves were preserved.'
