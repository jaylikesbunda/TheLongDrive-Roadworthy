[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$ManagedDirectory,
    [string]$BuildDirectory=(Join-Path $env:TEMP 'opencode\tld-placement-center-parity')
)
$ErrorActionPreference='Stop'
$runtimePath=[IO.Path]::GetFullPath($Runtime)
$managed=[IO.Path]::GetFullPath($ManagedDirectory)
$build=[IO.Path]::GetFullPath($BuildDirectory)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $runtimePath)){throw ('Candidate runtime was not found: '+$runtimePath)}
if(!(Test-Path -LiteralPath $csc)){throw ('C# compiler unavailable: '+$csc)}
foreach($name in @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','netstandard.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $managed $name))){throw ('Game reference assembly unavailable: '+$name)}
}
New-Item -ItemType Directory -Force -Path $build | Out-Null
Copy-Item -LiteralPath $runtimePath -Destination (Join-Path $build 'Tld.Multiplayer.dll') -Force

# Test executables run from a TEMP scratch copy so the machine's project-tree
# integrity label cannot affect assembly loading or test output.
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
        Get-ChildItem -LiteralPath $sourceDir -Filter '*.dll' -File -ErrorAction SilentlyContinue | Where-Object {$_.Name -ne 'Assembly-CSharp.dll'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $scratch -Force}
        foreach($name in @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.VehiclesModule.dll','UnityEngine.JSONSerializeModule.dll','com.rlabrecque.steamworks.net.dll','netstandard.dll')){
            $dependency=Join-Path $managed $name
            if((Test-Path -LiteralPath $dependency) -and !(Test-Path -LiteralPath (Join-Path $scratch $name))){Copy-Item -LiteralPath $dependency -Destination $scratch -Force}
        }
    }
    return $copy
}
function Run([string]$exe,[string[]]$arguments){$target=Test-ExePath $exe;& $target @arguments;if($LASTEXITCODE -ne 0){throw ('Failed ('+$LASTEXITCODE+'): '+$exe)}}

$exe=Join-Path $build 'PlacementCenterParityTests.exe'
$sources=@(
    '/nologo','/optimize+',('/out:'+$exe),
    ('/reference:'+(Join-Path $build 'Tld.Multiplayer.dll')),
    ('/reference:'+(Join-Path $managed 'Assembly-CSharp.dll')),
    ('/reference:'+(Join-Path $managed 'UnityEngine.CoreModule.dll')),
    ('/reference:'+(Join-Path $managed 'netstandard.dll')),
    (Join-Path $PSScriptRoot 'tests\PlacementCenterParityTests.cs')
)
Run $csc $sources
Run $exe @()
