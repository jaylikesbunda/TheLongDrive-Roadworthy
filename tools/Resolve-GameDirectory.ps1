# Dot-source this file, then call Resolve-GameDirectory $GameDirectory.
# An explicit path wins, then $env:TLD_GAME_DIR, then the Steam libraries that
# contain The Long Drive (app 1017180).
function Resolve-GameDirectory([string]$GameDirectory){
    if(!$GameDirectory){$GameDirectory=$env:TLD_GAME_DIR}
    if($GameDirectory){
        if(!(Test-Path -LiteralPath (Join-Path $GameDirectory 'TheLongDrive_Data\Managed\Assembly-CSharp.dll'))){throw ('Not a The Long Drive installation: '+$GameDirectory)}
        return [IO.Path]::GetFullPath($GameDirectory)
    }
    $steamRoots=@()
    foreach($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam','HKLM:\SOFTWARE\Valve\Steam')){
        $item=Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if($item -and $item.SteamPath){$steamRoots+=$item.SteamPath}
        if($item -and $item.InstallPath){$steamRoots+=$item.InstallPath}
    }
    foreach($name in @('ProgramFiles(x86)','ProgramFiles')){
        $base=[Environment]::GetEnvironmentVariable($name)
        if($base){$steamRoots+=(Join-Path $base 'Steam')}
    }
    $libraries=@()
    foreach($steam in $steamRoots){
        $libraries+=$steam
        $folders=Join-Path $steam 'steamapps\libraryfolders.vdf'
        if(Test-Path -LiteralPath $folders){
            foreach($match in [regex]::Matches((Get-Content -LiteralPath $folders -Raw),'"path"\s+"((?:\\.|[^"\\])*)"')){
                $libraries+=($match.Groups[1].Value -replace '\\(["\\])','$1')
            }
        }
    }
    $found=@()
    foreach($library in ($libraries | ForEach-Object {[IO.Path]::GetFullPath($_)} | Sort-Object -Unique)){
        $appManifest=Join-Path $library 'steamapps\appmanifest_1017180.acf'
        if(!(Test-Path -LiteralPath $appManifest)){continue}
        $installDir=[regex]::Match((Get-Content -LiteralPath $appManifest -Raw),'"installdir"\s+"([^"]+)"').Groups[1].Value
        if(!$installDir){continue}
        $game=Join-Path $library ('steamapps\common\'+$installDir)
        if(Test-Path -LiteralPath (Join-Path $game 'TheLongDrive_Data\Managed\Assembly-CSharp.dll')){$found+=$game}
    }
    if($found.Count -eq 0){throw 'The Long Drive was not found in any Steam library. Pass -GameDirectory or set TLD_GAME_DIR.'}
    if($found.Count -gt 1){throw ('Several The Long Drive installations found; pass -GameDirectory: '+($found -join '; '))}
    return $found[0]
}
