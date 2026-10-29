[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OriginalAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [Parameter(Mandatory=$true)][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [string]$RejectedAssembly='',
    [int]$ExpectedCandidateMethods=101,
    [int]$ExpectedControlMethods=6
)
$ErrorActionPreference='Stop'
$cecil=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\patcher\Mono.Cecil.dll'
$mono=Join-Path $GameDirectory 'MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll'
foreach($path in @($OriginalAssembly,$CandidateAssembly,$Runtime,$cecil,$mono)){
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing JIT probe input: '+$path)}
}
New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
Copy-Item -LiteralPath $cecil -Destination (Join-Path $BuildDirectory 'Mono.Cecil.dll') -Force
$exe=Join-Path $BuildDirectory 'MonoRuntimeJitProbe.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /platform:x64 /optimize+ ('/reference:'+$cecil) ('/out:'+$exe) (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\MonoRuntimeJitProbe.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile the shipped Mono JIT probe.'}
function Probe([string]$assembly,[string]$role,[bool]$reject){
    $dir=Join-Path $BuildDirectory $role
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $inputAssembly=Join-Path $dir 'Assembly-CSharp.dll'
    Copy-Item -LiteralPath $assembly -Destination $inputAssembly -Force
    Copy-Item -LiteralPath $Runtime -Destination (Join-Path $dir 'Tld.Multiplayer.dll') -Force
    $stdout=Join-Path $dir 'stdout.txt';$stderr=Join-Path $dir 'stderr.txt'
    # Separate child process: malformed IL can crash native Mono. No managed
    # game method is invoked and no Unity player is initialized.
    $arguments='"'+[IO.Path]::GetFullPath($GameDirectory)+'" "'+[IO.Path]::GetFullPath($inputAssembly)+'"'
    $start=New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName=$exe;$start.Arguments=$arguments
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=New-Object System.Diagnostics.Process
    $process.StartInfo=$start
    if(!$process.Start()){throw ('Could not start JIT probe: '+$role)}
    $readOutput=$process.StandardOutput.ReadToEndAsync()
    $readError=$process.StandardError.ReadToEndAsync()
    if(!$process.WaitForExit(60000)){$process.Kill();throw ('JIT probe timed out: '+$role)}
    [IO.File]::WriteAllText($stdout,$readOutput.Result)
    [IO.File]::WriteAllText($stderr,$readError.Result)
    $output=Get-Content -LiteralPath $stdout -Raw
    $passed=$process.ExitCode -eq 0 -and $output -match 'PASS: shipped Unity Mono compiled \d+ selected native methods without executing game code\.'
    if($reject){
        if($passed -or $process.ExitCode -eq 0){throw 'Malformed exception-boundary negative control was accepted by shipped Mono.'}
        if($output -notmatch 'JIT System.Void poiGenScript::Upd\(System.Boolean\)'){throw 'Negative control did not reach the deliberately malformed method.'}
        Write-Output 'PASS: shipped Unity Mono rejects malformed observer exception-boundary negative control.'
    }else{
        if(!$passed){throw ('Shipped Mono JIT failed for '+$role+' (exit '+$process.ExitCode+'). Logs: '+$dir)}
        $actualCount=[int][regex]::Match($output,'PASS: shipped Unity Mono compiled (\d+) selected').Groups[1].Value
        $expectedCount=if($role -eq 'candidate'){$ExpectedCandidateMethods}else{$ExpectedControlMethods}
        if($actualCount -ne $expectedCount){throw ('JIT coverage changed for '+$role+': actual='+$actualCount+', expected='+$expectedCount)}
        Write-Output ($role+': '+[regex]::Match($output,'PASS: shipped Unity Mono compiled \d+ selected native methods without executing game code\.').Value)
    }
}
Probe $OriginalAssembly 'native-control' $false
Probe $CandidateAssembly 'candidate' $false
if($RejectedAssembly -ne ''){Probe $RejectedAssembly 'malformed-eh-negative-control' $true}
# Missing Unity internal-call messages in these isolated logs are expected:
# the compiler runs without Unity initialization and never invokes those calls.

