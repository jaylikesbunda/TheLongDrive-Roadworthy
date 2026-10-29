[CmdletBinding()]
param(
    [ValidateSet('Install','Restore','Status')][string]$Action = 'Status',
    [string]$GameDirectory = '',
    [string]$ManifestPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
$expectedOriginal = 'CF2C2D0EE7941B981FCF2352D39C628EE36B8B6609223C0BD05C9308B245E000'
$managed = Join-Path $GameDirectory 'TheLongDrive_Data\Managed'
$installed = Join-Path $managed 'Assembly-CSharp.dll'
$backup = Join-Path $managed 'Assembly-CSharp.tldmp-v1-original.bin'
$receiptPath = Join-Path $managed 'tldmp-v1-receipt.json'
function File-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
$currentHash = File-Hash $installed
if ($Action -eq 'Status') {
    if ($currentHash -eq $expectedOriginal) { Write-Host 'Original game assembly installed.'; return }
    if (Test-Path -LiteralPath $receiptPath) {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        if ($currentHash -eq $receipt.patchedSha256) { Write-Host 'Experimental v1 patch installed.'; return }
    }
    throw 'The game assembly is not a recognized original or installed patch.'
}
if (Get-Process -Name TheLongDrive -ErrorAction SilentlyContinue) { throw 'Close The Long Drive before applying or restoring the patch.' }
function Replace-Assembly([string]$Source, [string]$ExpectedHash) {
    $temp = Join-Path $managed ('tldmp-' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        Copy-Item -LiteralPath $Source -Destination $temp
        if ((File-Hash $temp) -ne $ExpectedHash) { throw 'Staged assembly hash mismatch.' }
        [IO.File]::Replace($temp, $installed, [System.Management.Automation.Language.NullString]::Value)
        if ((File-Hash $installed) -ne $ExpectedHash) { throw 'Installed assembly verification failed.' }
    }
    finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
if ($Action -eq 'Restore') {
    if ($currentHash -eq $expectedOriginal) { Write-Host 'Already restored.'; return }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.originalSha256 -ne $expectedOriginal -or $currentHash -ne $receipt.patchedSha256) {
        throw 'Game files changed since installation. Refusing to overwrite them.'
    }
    if ((File-Hash $backup) -ne $expectedOriginal) { throw 'Original backup hash mismatch.' }
    Replace-Assembly $backup $expectedOriginal
    Write-Host 'Original assembly restored and verified. Saves were not changed.'
    return
}
if (!$ManifestPath) {
    $latest = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '.local-analysis\latest-run.txt') -Raw).Trim()
    $ManifestPath = Join-Path $latest 'manifest.json'
}
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.originalSha256 -ne $expectedOriginal -or !$manifest.patchedSha256 -or !$manifest.results) {
    throw 'Unrecognized offline test manifest. Run Test Offline.cmd first.'
}
if ((File-Hash $manifest.patched) -ne $manifest.patchedSha256) { throw 'Built patch hash mismatch. Run the offline tests again.' }
if ($currentHash -eq $manifest.patchedSha256) { Write-Host 'This experimental patch is already installed.'; return }
if ($currentHash -ne $expectedOriginal) { throw 'Unsupported or modified game assembly. Refusing to overwrite it.' }
if (Test-Path -LiteralPath $backup) {
    if ((File-Hash $backup) -ne $expectedOriginal) { throw 'Existing original backup does not match.' }
}
else {
    Copy-Item -LiteralPath $installed -Destination $backup
    if ((File-Hash $backup) -ne $expectedOriginal) { throw 'Backup verification failed.' }
}
[ordered]@{
    originalSha256 = $expectedOriginal
    patchedSha256 = $manifest.patchedSha256
    manifest = [IO.Path]::GetFullPath($ManifestPath)
} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Replace-Assembly $manifest.patched $manifest.patchedSha256
Write-Host 'Experimental patch installed and verified. Original assembly backed up. Saves were not changed.'
