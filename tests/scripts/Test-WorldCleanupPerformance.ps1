[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OriginalAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [Parameter(Mandatory=$true)][string]$ManagedDirectory,
    [string]$CecilPath=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\patcher\Mono.Cecil.dll'),
    [string]$BuildDirectory=(Join-Path $env:TEMP 'tld-world-cleanup-performance'),
    [int]$Chunks=5000,
    [int]$Needed=2000,
    [int]$Rounds=3
)
$ErrorActionPreference='Stop'
foreach($path in @($OriginalAssembly,$CandidateAssembly,$CecilPath)){
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Required input not found: $path"}
}
if(!(Test-Path -LiteralPath $ManagedDirectory -PathType Container)){throw "Managed directory not found: $ManagedDirectory"}
if($Chunks -lt 1 -or $Needed -lt 0 -or $Needed -gt $Chunks -or $Rounds -lt 1){throw 'Invalid benchmark dimensions.'}
New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture=Join-Path $BuildDirectory 'WorldCleanupProductionTests.exe'
Copy-Item -LiteralPath $CecilPath -Destination (Join-Path $BuildDirectory 'Mono.Cecil.dll') -Force
& $csc '/nologo' '/optimize+' ("/out:"+$fixture) ("/reference:"+$CecilPath) (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\WorldCleanupProductionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile production cleanup IL fixture.'}

# Test executables launched directly from Documents can inherit a low-integrity
# label and fail to write temporary Cecil outputs. Run a byte-identical copy
# from the test scratch directory while keeping all declared inputs read-only.
function Invoke-Fixture([string]$AssemblyPath,[string]$Role){
    $localAssembly=Join-Path $BuildDirectory ('Assembly-CSharp-'+$Role+'.dll')
    [IO.File]::WriteAllBytes($localAssembly,[IO.File]::ReadAllBytes($AssemblyPath))
    $argsList=@($localAssembly,$ManagedDirectory,$Role,$Chunks.ToString(),$Needed.ToString(),$Rounds.ToString())
    $output=& $fixture @argsList 2>&1
    if($LASTEXITCODE -ne 0){throw ($Role+' production cleanup replay failed: '+($output -join "`n"))}
    $text=($output -join "`n")
    $match=[regex]::Match($text,'PASS production UpdGen cleanup IL role='+[regex]::Escape($Role)+' checks=\d+ parity=(.*?) chunks=')
    if(!$match.Success){throw ($Role+' fixture returned no parity fingerprint: '+$text)}
    Write-Host $text
    return $match.Groups[1].Value
}
$baseline=Invoke-Fixture $OriginalAssembly 'baseline'
$candidate=Invoke-Fixture $CandidateAssembly 'candidate'
if($baseline -cne $candidate){throw 'Native baseline and candidate cleanup IL produced different removal queues, retained keys, or object flags.'}
Write-Output ('PASS: exact production cleanup outputs match for '+$Chunks+' generated keys and '+$Needed+' needed keys. Timings are synthetic only and are not game FPS evidence.')
