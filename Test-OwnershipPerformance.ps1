param([string]$BuildDirectory=(Join-Path $env:TEMP 'tld-world-performance-build'),[Parameter(Mandatory=$true)][string]$ManagedDirectory)
$ErrorActionPreference='Stop'
$managed=$ManagedDirectory
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runtime=Join-Path $BuildDirectory 'Tld.Multiplayer.dll'
$cecil=Join-Path $PSScriptRoot '.tools\patcher\Mono.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $BuildDirectory -Force
Copy-Item -LiteralPath (Join-Path $managed 'UnityEngine.CoreModule.dll') -Destination $BuildDirectory -Force
Copy-Item -LiteralPath (Join-Path $managed 'netstandard.dll') -Destination $BuildDirectory -Force
# Test-loader environment fix: executables compiled into the project tree run at
# Low Mandatory Level on this machine (Documents carries a low-integrity label),
# so their writes to Medium-IL locations such as %TEMP% are denied. Execute test
# binaries from the approved %TEMP%\opencode scratch instead; arguments, inputs
# and outputs keep their original paths.
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
        foreach($n in @('UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.VehiclesModule.dll','UnityEngine.JSONSerializeModule.dll','netstandard.dll','com.rlabrecque.steamworks.net.dll')){
            $dep=Join-Path $managed $n
            if((Test-Path -LiteralPath $dep) -and !(Test-Path -LiteralPath (Join-Path $scratch $n))){Copy-Item -LiteralPath $dep -Destination $scratch -Force}
        }
    }
    return $copy
}
function Run([string]$exe,[string[]]$arguments){$target=Test-ExePath $exe;& $target @arguments;if($LASTEXITCODE -ne 0){throw "Failed ($LASTEXITCODE): $exe"}}
$nativeRefs=@('Assembly-CSharp','UnityEngine.CoreModule','UnityEngine.PhysicsModule','netstandard') | ForEach-Object {'/reference:'+(Join-Path $managed ($_+'.dll'))}
foreach($name in @('TerrainMembershipTests','WorldMapTests')){
 $exe=Join-Path $BuildDirectory ($name+'.exe')
 Run $csc (@('/nologo','/optimize+',('/out:'+$exe),('/reference:'+$runtime))+$nativeRefs+@(Join-Path $PSScriptRoot ('tests\'+$name+'.cs')))
 Run $exe @($managed,(Join-Path $managed 'Assembly-CSharp.dll'))
}
foreach($entry in @(
 @('SyncItemSchedulerTests','SyncItemScheduler'),
 @('NativeLoopTimingsTests','NativeLoopTimings')
)){
 $exe=Join-Path $BuildDirectory ($entry[0]+'.exe')
 $refs=if($entry[0] -eq 'NativeLoopTimingsTests'){@(('/reference:'+(Join-Path $managed 'UnityEngine.CoreModule.dll')),('/reference:'+(Join-Path $managed 'netstandard.dll')))}else{@()}
 Run $csc (@('/nologo','/optimize+',('/out:'+$exe))+$refs+@((Join-Path $PSScriptRoot ('runtime\'+$entry[1]+'.cs')),(Join-Path $PSScriptRoot ('tests\'+$entry[0]+'.cs'))))
 Run $exe @()
}
$exe=Join-Path $BuildDirectory 'TerrainWorkDiagnosticsTests.exe'
Run $csc (@('/nologo','/optimize+',('/out:'+$exe))+$nativeRefs+@((Join-Path $PSScriptRoot 'runtime\TerrainWorkDiagnostics.cs'),(Join-Path $PSScriptRoot 'runtime\ModDiagnostics.cs'),(Join-Path $PSScriptRoot 'tests\TerrainWorkDiagnosticsTests.cs')))
Run $exe @($managed,(Join-Path $managed 'Assembly-CSharp.dll'))
$exe=Join-Path $BuildDirectory 'ContentFingerprintTests.exe'
$sources=@('runtime\NetCore.cs','runtime\StableCore.cs','runtime\StablePackets.cs','runtime\ContentFingerprint.cs','tests\ContentFingerprintTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
Run $csc (@('/nologo','/optimize+',('/out:'+$exe))+$sources)
Run $exe @()
$exe=Join-Path $BuildDirectory 'ContentFingerprintBridgeTests.exe'
Run $csc @('/nologo','/optimize+',('/out:'+$exe),('/reference:'+$cecil),(Join-Path $PSScriptRoot 'tests\ContentFingerprintBridgeTests.cs'))
Run $exe @($runtime)
