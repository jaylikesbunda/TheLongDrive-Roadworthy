[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RuntimeAssembly,
    [string]$StableAssembly,
    [string]$GameDirectory,
    [string]$OutputDirectory,
    [string]$BaselineCsv,
    [string[]]$ModDirectory=@()
)

$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Resolve-GameDirectory.ps1')
$game=Resolve-GameDirectory $GameDirectory
$managed=Join-Path $game 'TheLongDrive_Data\Managed'
$beta=Join-Path $managed 'Assembly-CSharp.tldmp-v3-original.bin'
if (!(Test-Path -LiteralPath $beta -PathType Leaf)) { $beta=Join-Path $managed 'Assembly-CSharp.dll' }
if (!$StableAssembly) { $StableAssembly=Join-Path $repo '.local-analysis\stable-1017181\Assembly-CSharp.dll' }
if (!$OutputDirectory) { $OutputDirectory=Join-Path $repo '.local-analysis\mod-audit\coverage' }
$stable=(Resolve-Path -LiteralPath $StableAssembly).Path
$runtime=(Resolve-Path -LiteralPath $RuntimeAssembly).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
$before=if ($BaselineCsv) { @(Import-Csv -LiteralPath $BaselineCsv) } else { @() }
$scratch=Join-Path $env:TEMP ('roadworthy-api-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch,$output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo '.tools\patcher\Mono.Cecil.dll') -Destination $scratch
$cecil=Join-Path $scratch 'Mono.Cecil.dll'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$common=@('runtime\TldLoaderTranslator.cs','runtime\TldLoaderCompatPolicy.cs','runtime\TldStableShellNames.cs') |
    ForEach-Object { Join-Path $repo $_ }
$mods=@($ModDirectory | ForEach-Object { Get-ChildItem -LiteralPath $_ -File -Filter '*.dll' } |
    Sort-Object FullName | Select-Object -ExpandProperty FullName)
foreach ($tool in @('TldApiProbe','TldModAudit')) {
    $exe=Join-Path $scratch ($tool+'.exe')
    & $csc /nologo /optimize+ "/out:$exe" "/reference:$cecil" (Join-Path $PSScriptRoot ($tool+'.cs')) @common
    if ($LASTEXITCODE -ne 0) { throw "$tool failed to build." }
    if ($tool -eq 'TldApiProbe') { & $exe $stable $beta $managed $runtime $output @mods }
    else { & $exe $managed $beta $stable (Join-Path $output 'mods.md') @mods }
    if ($LASTEXITCODE -ne 0) { throw "$tool failed." }
}

$inputs=@($stable,$beta,$runtime,$PSCommandPath,(Join-Path $PSScriptRoot 'TldApiProbe.cs'),
    (Join-Path $PSScriptRoot 'TldModAudit.cs'))+$common+$mods
$inputs | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
    Select-Object Path,Hash | ConvertTo-Json -Depth 3 |
    Set-Content -LiteralPath (Join-Path $output 'inputs.json') -Encoding UTF8

if ($BaselineCsv) {
    $baseline=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($row in $before) { $baseline[$row.Operation+'|'+$row.'Stable signature']=$row.Outcome }
    $changes=@(Import-Csv -LiteralPath (Join-Path $output 'api.csv') | ForEach-Object {
        $key=$_.Operation+'|'+$_.'Stable signature'
        $old=if ($baseline.ContainsKey($key)) { $baseline[$key] } else { 'new' }
        if ($old -ne $_.Outcome) {
            [pscustomobject]@{Operation=$_.Operation; Member=$_.'Stable signature'; Before=$old;
                After=$_.Outcome; Detail=$_.Detail; Mods=$_.'Mod references'}
        }
    })
    $changes | Export-Csv -LiteralPath (Join-Path $output 'changes.csv') -Encoding UTF8 -NoTypeInformation
    $changes | Group-Object Before,After | Select-Object Count,Name | Format-Table -AutoSize
}
