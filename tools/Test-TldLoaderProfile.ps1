[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [string]$GameDirectory,
    [string]$ModsDirectory,
    [string]$CoreBundle,
    [ValidateSet('Odometer','CarBattery','TestItem','StableApi')]
    [string[]]$Case=@('Odometer','CarBattery','TestItem')
)

$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Resolve-GameDirectory.ps1')
$game=Resolve-GameDirectory $GameDirectory
if (!$ModsDirectory) {
    $ModsDirectory=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'TheLongDrive\Mods'
}
$build=(Resolve-Path -LiteralPath $BuildDirectory).Path
$managed='TheLongDrive_Data\Managed'
$gameAssembly=Join-Path $build 'Assembly-CSharp.v3.dll'
$runtime=Join-Path $build 'Tld.Multiplayer.dll'
$loader=Join-Path $game "$managed\TLDLoader.roadworthy-original.bin"
if (!(Test-Path -LiteralPath $loader)) { $loader=Join-Path $game "$managed\TLDLoader.dll" }

$cases=@{
    Odometer=@{File='Hud_Odometer.dll'; Feature='Drive and verify the HUD displays increasing distance'}
    CarBattery=@{File='RUNDEN_M_CarBattery.dll'; Feature='Mount, power the car, and save/reload the battery'}
    TestItem=@{File='Roadworthy_TestItem.dll'; Feature='Spawn, pick up, and save/reload the test crate'}
    StableApi=@{File='Roadworthy_StableApi.dll'; Feature='Read a stable-shaped save, construct an item record, and spawn its beta prefab'}
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
$identity=[ordered]@{ game=Hash $gameAssembly; runtime=Hash $runtime; originalLoader=Hash $loader }
foreach ($name in $Case) {
    if (!(Test-Path -LiteralPath (Join-Path $ModsDirectory $cases[$name].File) -PathType Leaf)) {
        throw "Missing package for $name in $ModsDirectory"
    }
}
if ($CoreBundle) { $CoreBundle=(Resolve-Path -LiteralPath $CoreBundle).Path }
if (Get-Process -Name TheLongDrive -ErrorAction SilentlyContinue) {
    throw 'Close The Long Drive before running the isolated test player.'
}

$root=Join-Path (Split-Path $PSScriptRoot -Parent) '.local-analysis\mod-compat-runner'
$run=Join-Path $root ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$clone=Join-Path $run 'game'
New-Item -ItemType Directory -Path $clone -Force | Out-Null
foreach ($file in @('TheLongDrive.exe','UnityPlayer.dll','UnityCrashHandler64.exe')) {
    Copy-Item -LiteralPath (Join-Path $game $file) -Destination $clone
}
foreach ($directory in @('MonoBleedingEdge','TheLongDrive_Data')) {
    & robocopy.exe (Join-Path $game $directory) (Join-Path $clone $directory) /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XF 'TLDLoader.roadworthy-*' | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $directory into the test game." }
}
Copy-Item -LiteralPath $gameAssembly -Destination (Join-Path $clone "$managed\Assembly-CSharp.dll") -Force
Copy-Item -LiteralPath $runtime -Destination (Join-Path $clone "$managed\Tld.Multiplayer.dll") -Force
Copy-Item -LiteralPath $loader -Destination (Join-Path $clone "$managed\TLDLoader.dll") -Force
Set-Content -LiteralPath (Join-Path $clone 'steam_appid.txt') -Value 1017180 -Encoding Ascii

function Wait-For([scriptblock]$Check, [int]$Timeout, [string]$Label) {
    $deadline=[DateTime]::UtcNow.AddSeconds($Timeout)
    do {
        if (& $Check) { return }
        if ($client.HasExited) { throw "$Label failed. Game exited with code $($client.ExitCode)." }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "$Label timed out. See $log."
}
function Read-Text([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return [string](Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue) }
    return ''
}
function Send-Camera([string]$Command) {
    $pending=Join-Path $camera 'command.txt'
    $temp=Join-Path $camera 'runner-command.tmp'
    Wait-For { !(Test-Path -LiteralPath $pending) } 10 'Previous camera command'
    Set-Content -LiteralPath $temp -Value $Command -Encoding Ascii
    Move-Item -LiteralPath $temp -Destination $pending
}

$results=foreach ($name in $Case) {
    $caseDirectory=Join-Path $run $name
    $profile=Join-Path $caseDirectory 'profile'
    $camera=Join-Path $profile 'roadworthy-camera'
    $log=Join-Path $caseDirectory 'Player.log'
    $modFolder=Join-Path $profile 'Mods'
    New-Item -ItemType Directory -Path $camera,$modFolder -Force | Out-Null
    $package=Join-Path $ModsDirectory $cases[$name].File
    Copy-Item -LiteralPath $package -Destination $modFolder
    if ($CoreBundle) {
        $assets=Join-Path $modFolder 'Assets\TLDLoader_Core'
        New-Item -ItemType Directory -Path $assets -Force | Out-Null
        Copy-Item -LiteralPath $CoreBundle -Destination (Join-Path $assets 'core.unity3d')
    }
    Set-Content -LiteralPath (Join-Path $profile 'roadworthy-options.txt') -Value 'tldloadermods=on' -Encoding Ascii
    $status=Join-Path $camera 'status.txt'
    $settings=@{TLD_MP_SAVE_ROOT=$profile; TLD_MP_TLDLOADER='1'; TLD_MP_JOIN_POPUP_PREVIEW='0'; TLD_MP_PROMO_CAMERA='1'}
    if ($name -eq 'StableApi') { $settings.RW_TEST_STABLE_MENU_START='1' }
    $previous=@{}
    $client=$null
    $result=[ordered]@{
        case=$name
        file=$cases[$name].File
        sha256=Hash $package
        smoke='failed'
        feature=$cases[$name].Feature
        featureStatus='pending'
        playerLog=$log
    }
    try {
        if (Get-Process -Name TheLongDrive -ErrorAction SilentlyContinue) { throw 'A game instance is already open.' }
        foreach ($variable in $settings.Keys) {
            $previous[$variable]=[Environment]::GetEnvironmentVariable($variable)
            [Environment]::SetEnvironmentVariable($variable,$settings[$variable])
        }
        $client=Start-Process -FilePath (Join-Path $clone 'TheLongDrive.exe') -WorkingDirectory $clone -WindowStyle Hidden -PassThru -ArgumentList @(
            '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"{0}"' -f $log))
        $modPattern=[regex]::Escape($result.file)+': translated for the beta'
        Wait-For { (Read-Text $status) -match '(?m)^build=roadworthy\s*$' } 60 'Roadworthy camera startup'
        Wait-For { (Read-Text $log) -match $modPattern -and (Read-Text $log) -match 'started on the beta: 1 mod\(s\) loaded' } 45 'One-mod discovery and translation'
        if ($name -eq 'StableApi') {
            Wait-For { (Read-Text $log) -match '\[RW stable API\] menu screen adapters passed\.' } 45 'Stable screen visibility'
            Wait-For { (Read-Text $log) -match 'CLOG menuhandler PressedStart' } 15 'World start through the translated menu call'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] ran:' } 110 'World load through the translated menu call'
        } else {
            Send-Camera 'newworld -1752327066'
            Wait-For { (Read-Text $status) -match '(?m)^result=world ready\s*$' } 110 'World load'
        }
        if (!$CoreBundle) {
            Wait-For { (Read-Text $log) -match 'ran OnLoad for 1 mod\(s\) without TLDLoader' } 25 'One-mod OnLoad'
        }
        $cache=Join-Path $profile ('Roadworthy\TldLoaderCache\'+$result.file)
        if (!(Test-Path -LiteralPath $cache -PathType Leaf)) { throw 'Translated DLL missing from the isolated cache.' }
        $result.translatedSha256=Hash $cache
        if ($name -eq 'TestItem') {
            Wait-For { (Read-Text $log) -match '\[RW test item\] registered: save id \d+, in database True' } 15 'Test item registration'
            $result.featureStatus='registration verified, gameplay and save/reload pending'
        }
        if ($name -eq 'StableApi') {
            Wait-For { (Read-Text $log) -match '\[RW stable API\] part adapters passed\.' } 45 'Stable part and attachment adapters'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] changed overloads passed\.' } 45 'Stable overload adapters'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] world and component adapters passed\.' } 45 'Stable world and component adapters'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] terrain markers passed\.' } 45 'Stable terrain markers applied to native height query'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] terrain holes passed\.' } 45 'Stable terrain holes populated from native geometry'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] Harmony adapters passed\.' } 45 'Stable Harmony patches running at beta entry points'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] joint recovery passed\.' } 45 'Stable hitch reattached through native physics'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] building adapter passed\.' } 45 'Stable starting house lookup'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] save adapter passed:' } 45 'Stable save adapter'
            Wait-For { (Read-Text $log) -match '\[RW stable API\] item category passed\.' } 45 'Stable item category registry'
            $result.featureStatus='verified'
        }
        if ((Read-Text $log) -match 'TLDLoader did not start|threw in OnLoad|mod .* could not be translated') {
            throw 'The game logged a loader failure.'
        }
        $result.smoke='passed'
    } catch {
        $result.error=$_.Exception.Message
    } finally {
        if ($client -and !$client.HasExited) {
            try { Send-Camera 'quit' } catch { Write-Warning $_ }
            if (!$client.WaitForExit(15000)) {
                # This is the specific process launched from the disposable game copy.
                $client.Kill()
                [void]$client.WaitForExit(5000)
            }
        }
        foreach ($variable in $previous.Keys) {
            [Environment]::SetEnvironmentVariable($variable,$previous[$variable])
        }
    }
    Write-Host "$name smoke $($result.smoke) $($result.error)"
    $result
}
$receipt=Join-Path $run 'results.json'
[ordered]@{build=$identity; coreBundle=if ($CoreBundle) { Hash $CoreBundle } else { $null }; cases=@($results)} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receipt -Encoding UTF8
Write-Host "Compatibility smoke results $receipt"
if (@($results | Where-Object { $_.smoke -ne 'passed' }).Count) { exit 1 }

