[CmdletBinding()]
param(
    [string]$GameDirectory = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
$root = $PSScriptRoot
$managed = Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$original = Join-Path $managed 'Assembly-CSharp.dll'
$expectedOriginal = 'CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000'
$installedHash=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
if ($installedHash -ne $expectedOriginal) {
    $matchedVersion=0
    $v4ReceiptPath=Join-Path $managed 'tldmp-v4-receipt.json'
    $v4Receipt=$null
    if (Test-Path -LiteralPath $v4ReceiptPath) {
        $v4Receipt=Get-Content -LiteralPath $v4ReceiptPath -Raw | ConvertFrom-Json
    }
    if ($v4Receipt -ne $null -and $v4Receipt.patchedSha256 -eq $installedHash) {
        if ($v4Receipt.version -ne 4 -or $v4Receipt.backup -ne 'Assembly-CSharp.tldmp-v4-original.bin') {
            throw 'Unrecognized v4 receipt metadata.'
        }
        if ($v4Receipt.output.sha256 -ne $installedHash -or $v4Receipt.input.sha256 -ne $v4Receipt.originalSha256) {
            throw 'V4 receipt hash chain is inconsistent.'
        }
        $v4Original=Join-Path $managed $v4Receipt.backup
        if ((Get-FileHash -LiteralPath $v4Original -Algorithm SHA256).Hash -ne $v4Receipt.originalSha256) {
            throw 'V4 original backup hash mismatch.'
        }
        $v3ReceiptPath=Join-Path $managed 'tldmp-v3-receipt.json'
        if (!(Test-Path -LiteralPath $v3ReceiptPath)) { throw 'V4 receipt is missing its v3 source receipt.' }
        $v3Receipt=Get-Content -LiteralPath $v3ReceiptPath -Raw | ConvertFrom-Json
        if ($v3Receipt.version -ne 3 -or $v4Receipt.originalSha256 -ne $v3Receipt.patchedSha256) {
            throw 'V4 backup does not match the recorded v3 patch.'
        }
        if ($v3Receipt.originalSha256 -ne $expectedOriginal) {
            throw 'V3 receipt does not identify the expected native original.'
        }
        $original=Join-Path $managed 'Assembly-CSharp.tldmp-v3-original.bin'
        if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $v3Receipt.originalSha256) {
            throw 'V3 original backup hash mismatch.'
        }
        $matchedVersion=4
    }
    if (!$matchedVersion) {
        foreach ($candidateVersion in @(3,2,1)) {
            $receiptPath=Join-Path $managed "tldmp-v$candidateVersion-receipt.json"
            if (Test-Path -LiteralPath $receiptPath) {
                $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
                if ($receipt.patchedSha256 -eq $installedHash) { $matchedVersion=$candidateVersion;break }
            }
        }
        if (!$matchedVersion) { throw 'This test supports only the original 2024.11.26b_test assembly or a recognized experimental patch.' }
        if ($matchedVersion -ne 4) {
            $original = Join-Path $managed "Assembly-CSharp.tldmp-v$matchedVersion-original.bin"
            if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $expectedOriginal) { throw 'Original backup hash mismatch.' }
        }
    }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is unavailable.' }
$toolsDir = Join-Path $root '.tools'
$patchTools = Join-Path $toolsDir 'patcher'
New-Item -ItemType Directory -Force -Path $patchTools | Out-Null
$cecilDll = Join-Path $patchTools 'Mono.Cecil.dll'
$package = Join-Path $toolsDir 'cecil.zip'
$packageHash = 'D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5'
if (!(Test-Path -LiteralPath $package)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg' -OutFile $package -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $packageHash) { throw 'Mono.Cecil package hash mismatch.' }
# Always refresh the tool dependency from the verified package, including cached runs.
$verifiedUnpack = Join-Path $toolsDir 'cecil-verified'
Expand-Archive -LiteralPath $package -DestinationPath $verifiedUnpack -Force
Copy-Item -LiteralPath (Join-Path $verifiedUnpack 'lib\net40\Mono.Cecil.dll') -Destination $cecilDll -Force

function Test-ExePath([string]$exe){
    if([IO.Path]::GetFullPath($exe).StartsWith([IO.Path]::GetFullPath($env:WINDIR),[StringComparison]::OrdinalIgnoreCase)){return $exe}
    $sourceDir=Split-Path -Parent ([IO.Path]::GetFullPath($exe))
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{$dirHash=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceDir))).Replace('-','').Substring(0,12)}finally{$hasher.Dispose()}
    $scratch=Join-Path $env:TEMP ('opencode\tld-test-exec\'+$dirHash)
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $copy=Join-Path $scratch ([IO.Path]::GetFileNameWithoutExtension($exe)+'-'+(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.Substring(0,12)+[IO.Path]::GetExtension($exe))
    if(!(Test-Path -LiteralPath $copy)){
        Copy-Item -LiteralPath $exe -Destination $copy
        Get-ChildItem -LiteralPath $sourceDir -Filter '*.dll' -ErrorAction SilentlyContinue | Where-Object {$_.Name -ne 'Assembly-CSharp.dll'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $scratch -Force}
    }
    return $copy
}
function Run-Native([string]$Executable, [string[]]$Arguments) {
    & (Test-ExePath $Executable) @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Executable" }
}
$patcher = Join-Path $patchTools 'PatchAssembly.exe'
$extractor = Join-Path $patchTools 'ExtractReplay.exe'
$verifier = Join-Path $patchTools 'VerifyPatch.exe'
Run-Native $compiler @('/nologo','/optimize+',"/out:$patcher","/reference:$cecilDll",(Join-Path $root 'tools\PatchAssembly.cs'))
Run-Native $compiler @('/nologo','/optimize+',"/out:$extractor","/reference:$cecilDll",(Join-Path $root 'tools\ExtractReplay.cs'))
Run-Native $compiler @('/nologo','/optimize+',"/out:$verifier","/reference:$cecilDll",(Join-Path $root 'tools\VerifyPatch.cs'))
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$runDir = Join-Path $root ('.local-analysis\runs\' + $runName)
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$fixture = Join-Path $runDir 'TldReplayFixture.exe'
$steamDll = Join-Path $managed 'com.rlabrecque.steamworks.net.dll'
Copy-Item -LiteralPath $steamDll -Destination $runDir
Run-Native $compiler @('/nologo','/nowarn:0436','/optimize-',"/out:$fixture","/reference:$steamDll",(Join-Path $root 'tests\ReplayFixture.cs'))
$patched = Join-Path $runDir 'Assembly-CSharp.dll'
Run-Native $patcher @($original, $patched)
Run-Native $verifier @($original, $patched)
$baselineReplay = Join-Path $runDir 'BaselineReplay.exe'
$patchedReplay = Join-Path $runDir 'PatchedReplay.exe'
Run-Native $extractor @($original, $fixture, $baselineReplay)
Run-Native $extractor @($patched, $fixture, $patchedReplay)
$report = Join-Path $runDir 'results.txt'
'OFFLINE BASELINE: original game IL' | Tee-Object -FilePath $report
& (Test-ExePath $baselineReplay) 2>&1 | Tee-Object -FilePath $report -Append
if ($LASTEXITCODE -ne 0) { throw 'Baseline replay failed unexpectedly.' }
'OFFLINE PATCH: modified game IL' | Tee-Object -FilePath $report -Append
& (Test-ExePath $patchedReplay) --patched 2>&1 | Tee-Object -FilePath $report -Append
if ($LASTEXITCODE -ne 0) { throw 'Patched replay failed.' }
$manifest = [ordered]@{
    original = $original
    originalSha256 = $expectedOriginal
    patched = $patched
    patchedSha256 = (Get-FileHash -LiteralPath $patched -Algorithm SHA256).Hash
    steamworksSha256 = (Get-FileHash -LiteralPath $steamDll -Algorithm SHA256).Hash
    results = $report
    scope = 'Managed IL replay; synthetic transport and Unity stand-ins; real Steamworks managed callbacks. No lobby, game launch, installation, or saves changed.'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'manifest.json') -Encoding UTF8
# Exercise install and restore on a disposable game-shaped directory, never the real install.
$packageTest = Join-Path $runDir 'install-test'
$packageManaged = Join-Path $packageTest 'TheLongDrive_Data\Managed'
New-Item -ItemType Directory -Force -Path $packageManaged | Out-Null
$packageAssembly = Join-Path $packageManaged 'Assembly-CSharp.dll'
Copy-Item -LiteralPath $original -Destination $packageAssembly
$manager = Join-Path $root 'Manage-Patch.ps1'
$testManifest = Join-Path $runDir 'manifest.json'
& $manager -Action Install -GameDirectory $packageTest -ManifestPath $testManifest
if ((Get-FileHash -LiteralPath $packageAssembly -Algorithm SHA256).Hash -ne $manifest.patchedSha256) { throw 'Installer test failed.' }
& $manager -Action Install -GameDirectory $packageTest -ManifestPath $testManifest
& $manager -Action Restore -GameDirectory $packageTest
& $manager -Action Restore -GameDirectory $packageTest
if ((Get-FileHash -LiteralPath $packageAssembly -Algorithm SHA256).Hash -ne $expectedOriginal) { throw 'Restore test failed.' }
'PASS: install, repeat install, restore, repeat restore on disposable copy; real game unchanged.' | Tee-Object -FilePath $report -Append
Set-Content -LiteralPath (Join-Path $root '.local-analysis\latest-run.txt') -Value $runDir
Write-Host "`nAll offline checks passed. Results: $report" -ForegroundColor Green
Write-Host 'Experimental assembly built locally. Installed game and saves were not changed.'
