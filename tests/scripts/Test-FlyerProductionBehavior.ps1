[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BaselineAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [string]$CecilPath=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '.tools\patcher\Mono.Cecil.dll'),
    [string]$BuildDirectory=(Join-Path $env:TEMP 'tld-flyer-production-behavior')
)
$ErrorActionPreference='Stop'
foreach($path in @($BaselineAssembly,$CandidateAssembly,$CecilPath)){
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Required input not found: $path"}
}
New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixture=Join-Path $BuildDirectory 'FlyerProductionBehaviorTests.exe'
Copy-Item -LiteralPath $CecilPath -Destination (Join-Path $BuildDirectory 'Mono.Cecil.dll') -Force
& $csc '/nologo' '/optimize+' ("/out:"+$fixture) ("/reference:"+$CecilPath) (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tests\FlyerProductionBehaviorTests.cs')
if($LASTEXITCODE -ne 0){throw 'Could not compile production flyer IL behavior fixture.'}

function Invoke-Fixture([string]$AssemblyPath,[string]$Role){
    $localAssembly=Join-Path $BuildDirectory ('Assembly-CSharp-'+$Role+'.dll')
    [IO.File]::WriteAllBytes($localAssembly,[IO.File]::ReadAllBytes($AssemblyPath))
    $output=& $fixture $localAssembly $Role 2>&1
    if($LASTEXITCODE -ne 0){throw ($Role+' production flyer replay failed: '+($output -join "`n"))}
    $text=($output -join "`n")
    if($text -notmatch ('PASS exact production randomFlyer.Upd IL role='+[regex]::Escape($Role)+' checks=\d+')){throw ($Role+' fixture returned no pass marker: '+$text)}
    Write-Host $text
    $values=@{}
    foreach($name in @('dynamicOut','kinematicOut','transitionOut')){
        $match=[regex]::Match($text,'(?m)^'+$name+'=(.*)$')
        if(!$match.Success){throw ($Role+' fixture omitted '+$name)}
        $values[$name]=$match.Groups[1].Value
    }
    return $values
}
$baseline=Invoke-Fixture $BaselineAssembly 'baseline'
$candidate=Invoke-Fixture $CandidateAssembly 'candidate'
foreach($name in @('dynamicOut','kinematicOut','transitionOut')){
    if($baseline[$name] -cne $candidate[$name]){throw "Baseline and candidate production IL differ in ${name}: '$($baseline[$name])' vs '$($candidate[$name])'"}
}
Write-Output 'PASS: exact native flyer IL preserves dynamic velocity, kinematic body state, direction, rotation, and dynamic/kinematic transition outputs. Candidate only skips the no-op kinematic setter and increments its counter; null receiver still throws NullReferenceException.'
