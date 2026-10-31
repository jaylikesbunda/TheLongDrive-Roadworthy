[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Host','Client')][string]$Role,
    [string]$GameDirectory='',
    [ValidateRange(1024,65535)][int]$Port=27050,
    [ValidateRange(0,5000)][int]$RttMs=0,
    [ValidateRange(0,100)][double]$LossPercent=0,
    [int]$NetworkSeed=1907,
    [ValidateRange(0,2000)][int]$JitterMs=0,
    [ValidateRange(5,60)][int]$CarRate=60,
    [switch]$NativeVehicleMotion,
    [switch]$NativeCarSmoothing
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'tools\Resolve-GameDirectory.ps1')
$GameDirectory=Resolve-GameDirectory $GameDirectory
$exe=Join-Path $GameDirectory 'TheLongDrive.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Game executable not found: $exe" }
$logs=Join-Path $PSScriptRoot '.local-analysis\loopback-logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$log=Join-Path $logs ((Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+$Role.ToLowerInvariant()+'.log')
$start=New-Object System.Diagnostics.ProcessStartInfo
$start.FileName=$exe
$start.WorkingDirectory=$GameDirectory
$start.UseShellExecute=$false
$start.Arguments='-logFile "'+$log+'"'
$start.EnvironmentVariables['TLD_MP_LOOPBACK']=if($Role -eq 'Host'){'host:'+ $Port}else{'client:127.0.0.1:'+ $Port}
$start.EnvironmentVariables['TLD_MP_DIAGNOSTICS']='1'
$start.EnvironmentVariables['TLD_MP_DIAGNOSTIC_DIR']=$logs
$start.EnvironmentVariables['TLD_MP_RTT_MS']=$RttMs.ToString()
$start.EnvironmentVariables['TLD_MP_LOSS_PERCENT']=$LossPercent.ToString([Globalization.CultureInfo]::InvariantCulture)
$start.EnvironmentVariables['TLD_MP_NET_SEED']=$NetworkSeed.ToString()
$start.EnvironmentVariables['TLD_MP_JITTER_MS']=$JitterMs.ToString()
$start.EnvironmentVariables['TLD_MP_VEHICLE_SNAPSHOTS']=if($NativeVehicleMotion){'0'}else{'1'}
$start.EnvironmentVariables['TLD_MP_SMOOTH_CARS']=if($NativeCarSmoothing){'0'}else{'1'}
$start.EnvironmentVariables['TLD_MP_CAR_RATE']=$CarRate.ToString([Globalization.CultureInfo]::InvariantCulture)
$start.EnvironmentVariables['SteamAppId']='1017180'
$start.EnvironmentVariables['SteamGameId']='1017180'
$process=[System.Diagnostics.Process]::Start($start)
Write-Host "$Role started (PID $($process.Id))."
Write-Host "Log: $log"
Write-Host "Combined snapshots/repeats/impact bursts: $(!$NativeVehicleMotion). Velocity-aware car smoothing: $(!$NativeCarSmoothing). Car pose target: $CarRate Hz (limited by frame rate)."
Write-Host 'Check [TLD MP sync-health] vehicleReceive receivedTotal/processedTotal on the receiving peer; vehicleMotion combinedTotal/repeatedTotal and impactTriggersTotal/impactSentTotal are sender counters.'
if($Role -eq 'Host') { Write-Host 'Load a test save and wait until the world is visible before starting the client.' }
else {
    Write-Host 'The client should connect and load the host world automatically.'
    Write-Host 'Loopback supports one host and one client at a time.'
    Write-Host "Simulated RTT: $RttMs ms. Jitter: up to $JitterMs ms extra one-way delay. Packet loss:$LossPercent percent per direction (reliable packets are delayed/retransmitted)."
}

