# Prove that the staged patch actually redirects the game's save root.
# Loads the patched Assembly-CSharp plus the runtime, calls the real
# pathscript.path(), and checks both the redirected and native results.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$StageDirectory,
    [string]$AssemblyName='Assembly-CSharp.v3.dll',
    [string]$GameDirectory=''
)
$ErrorActionPreference='Stop'
. (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'tools\Resolve-GameDirectory.ps1')
$game=Resolve-GameDirectory $GameDirectory
$managed=Join-Path $game 'TheLongDrive_Data\Managed'
$stage=[IO.Path]::GetFullPath($StageDirectory)
$patched=Join-Path $stage $AssemblyName
$runtime=Join-Path $stage 'Tld.Multiplayer.dll'
if(!(Test-Path -LiteralPath $patched)){ throw "Patched assembly not found: $patched" }
if(!(Test-Path -LiteralPath $runtime)){ throw "Runtime assembly not found: $runtime" }

$global:tldManaged=$managed
$resolver=[System.ResolveEventHandler]{
    param($sender,$args)
    $simple=$args.Name.Split(',')[0]
    $candidate=Join-Path $global:tldManaged ($simple+'.dll')
    if(Test-Path -LiteralPath $candidate){ return [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($candidate)) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
# UnityEngine.CoreModule defines MonoBehaviour, the base of pathscript. Load the
# direct dependency set up front so type resolution does not depend on the
# AppDomain callback alone.
foreach($name in @('UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.AnimationModule','UnityEngine.AudioModule','UnityEngine.VehiclesModule','UnityEngine.ParticleSystemModule','UnityEngine.JSONSerializeModule','com.rlabrecque.steamworks.net','netstandard','System.IO.Compression')){
    $dependency=Join-Path $managed ($name+'.dll')
    if(Test-Path -LiteralPath $dependency){ try { [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dependency)) } catch {} }
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('tld-save-isolation-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$previous=$env:TLD_MP_SAVE_ROOT
try{
    $patchedAssembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($patched))
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($runtime))
    $type=$patchedAssembly.GetType('pathscript')
    if($type -eq $null){ throw 'pathscript was not found in the patched assembly.' }
    $method=$type.GetMethod('path',[Reflection.BindingFlags]'Public,NonPublic,Instance,Static')
    if($method -eq $null){ throw 'pathscript.path() was not found.' }
    $instance=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)

    $env:TLD_MP_SAVE_ROOT=$temp
    $redirected=$method.Invoke($instance,$null)
    if($redirected -ne $temp){ throw "Redirect failed: expected '$temp', got '$redirected'." }

    [Environment]::SetEnvironmentVariable('TLD_MP_SAVE_ROOT',$null,'Process')
    $native=$method.Invoke($instance,$null)
    $expected=[Environment]::GetFolderPath('MyDocuments')+'/TheLongDrive'
    if($native -ne $expected){ throw "Native path changed: expected '$expected', got '$native'." }

    Write-Output "PASS: patched pathscript.path() honours TLD_MP_SAVE_ROOT and preserves the native path"
}
finally{
    [Environment]::SetEnvironmentVariable('TLD_MP_SAVE_ROOT',$previous,'Process')
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    if(Test-Path -LiteralPath $temp){ Remove-Item -LiteralPath $temp -Recurse -Force }
}
