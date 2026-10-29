[CmdletBinding()]
param(
    [ValidateSet('Install','Restore','Status')][string]$Action='Status',
    [string]$GameDirectory='',
    [string]$ManifestPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
$originalHash='CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000'
$managed=Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$assembly=Join-Path $managed 'Assembly-CSharp.dll'
$runtime=Join-Path $managed 'Tld.Multiplayer.dll'
$backup=Join-Path $managed 'Assembly-CSharp.tldmp-v3-original.bin'
$receiptPath=Join-Path $managed 'tldmp-v3-receipt.json'
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Replace-Checked([string]$Source,[string]$Target,[string]$Expected) {
    $temp=Join-Path $managed ('tldmp-v3-'+[guid]::NewGuid().ToString('N')+'.tmp')
    try {
        Copy-Item -LiteralPath $Source -Destination $temp
        if ((Hash $temp) -ne $Expected) { throw 'Staged file hash mismatch.' }
        if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($temp,$Target,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temp,$Target) }
        if ((Hash $Target) -ne $Expected) { throw 'Installed file hash mismatch.' }
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
$current=Hash $assembly
if ($Action -eq 'Status') {
    if ($current -eq $originalHash) { Write-Host 'Original game installed.';return }
    if (Test-Path -LiteralPath $receiptPath) {
        $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        if ($current -eq $receipt.patchedSha256 -and (Hash $runtime) -eq $receipt.runtimeSha256) { Write-Host 'Experimental stability v3 installed.';return }
    }
    throw 'Game is not a recognized original or v3 installation.'
}
if (Get-Process -Name TheLongDrive -ErrorAction SilentlyContinue) { throw 'Close The Long Drive before installing or restoring.' }
if ($Action -eq 'Restore') {
    if ($current -eq $originalHash) { Write-Host 'Original game already restored.';return }
    $receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($current -ne $receipt.patchedSha256 -or $receipt.originalSha256 -ne $originalHash -or (Hash $backup) -ne $originalHash -or (Hash $runtime) -ne $receipt.runtimeSha256) {
        throw 'Installed files or original backup changed. Refusing to overwrite them.'
    }
    Replace-Checked $backup $assembly $originalHash
    # Remove only our exact, verified helper file, after restoring the original DLL.
    Remove-Item -LiteralPath $runtime
    Write-Host 'Original game restored and verified. V3 helper removed. Saves unchanged.'
    return
}
if (!$ManifestPath) {
    $run=(Get-Content -LiteralPath (Join-Path $PSScriptRoot '.local-analysis\latest-stability.txt') -Raw).Trim()
    $ManifestPath=Join-Path $run 'manifest.json'
}
$manifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne 3 -or $manifest.originalSha256 -ne $originalHash -or !$manifest.runtimeSha256 -or !$manifest.patchedSha256) { throw 'Unrecognized build manifest.' }
if ((Hash $manifest.patched) -ne $manifest.patchedSha256 -or (Hash $manifest.runtime) -ne $manifest.runtimeSha256) { throw 'Build files changed. Rerun Test-Stability.ps1.' }
if ($current -eq $manifest.patchedSha256 -and (Hash $runtime) -eq $manifest.runtimeSha256) { Write-Host 'This stability v3 build is already installed.';return }
if ($current -ne $originalHash) { throw 'Restore the original game before installing a different experimental build.' }
if (Test-Path -LiteralPath $runtime) { throw 'A Tld.Multiplayer.dll already exists. Refusing to replace an unrelated helper.' }
if (Test-Path -LiteralPath $backup) {
    if ((Hash $backup) -ne $originalHash) { throw 'Existing original backup hash mismatch.' }
}
else { Copy-Item -LiteralPath $assembly -Destination $backup }
if ((Hash $backup) -ne $originalHash) { throw 'Original backup verification failed.' }
[ordered]@{
    version=3;sourceSignature=$manifest.sourceSignature;originalSha256=$originalHash;patchedSha256=$manifest.patchedSha256;runtimeSha256=$manifest.runtimeSha256
    manifest=[IO.Path]::GetFullPath($ManifestPath)
} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
# Stage the dependency first. If the assembly replacement fails, remove only the
# helper we just installed while leaving the original game assembly in place.
try {
    Replace-Checked $manifest.runtime $runtime $manifest.runtimeSha256
    Replace-Checked $manifest.patched $assembly $manifest.patchedSha256
}
catch {
    if ((Hash $assembly) -eq $originalHash -and (Test-Path -LiteralPath $runtime) -and (Hash $runtime) -eq $manifest.runtimeSha256) { Remove-Item -LiteralPath $runtime }
    throw
}
Write-Host 'Experimental stability v3 installed and verified. Host and clients should install the same build. Saves unchanged.'

