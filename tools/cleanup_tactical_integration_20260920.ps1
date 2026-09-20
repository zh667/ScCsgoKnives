param([switch]$Apply)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$manifestPath=Join-Path $projectRoot 'docs/cleanup-tactical-integration-20260920.json'
$targets=@('output/[API1.9]CS战术同伴拓展1.2.0-作者ZH667.scmod','output/[API1.9]CS玩家T-CT外观1.1.0-作者ZH667.scmod')
$rows=@(foreach($relative in $targets){
    $resolved=[IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    if(-not $resolved.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside project'}
    $file=Get-Item -LiteralPath $resolved
    if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Only exact regular files are allowed'}
    [ordered]@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant();state='planned';recovery='Windows Recycle Bin'}
})
$manifest=[ordered]@{date='2026-09-20';authorization='User requested integration and removal of superseded packages';retained=@('core 1.4.5','tactical 1.2.1 with integrated appearance','NekoMeko Model 1.1','appearance 1.1.0 compatibility fixture');files=$rows;totalBytes=($rows|ForEach-Object {$_.bytes}|Measure-Object -Sum).Sum;applied=$false}
if($Apply){
    if(Test-Path -LiteralPath $manifestPath){throw 'Preserve existing audit; do not repeat cleanup'}
    $evidence=Get-Content -LiteralPath (Join-Path $projectRoot 'docs/release-tactical-1.2.1-evidence.json') -Raw|ConvertFrom-Json
    if($evidence.failed -ne 0 -or $evidence.installedTacticalChecks -ne 52 -or $evidence.checks.Count -lt 542 -or $evidence.optionalLoading.runs.Count -ne 18){throw 'Final release verification is incomplete'}
    foreach($package in $evidence.packages.PSObject.Properties){
        $actual=(Get-FileHash -LiteralPath (Join-Path $projectRoot ('output/'+$package.Name)) -Algorithm SHA256).Hash.ToLowerInvariant()
        if($actual -ne $package.Value.sha256){throw 'Release bytes differ from validation'}
    }
    $fixture=Join-Path $projectRoot 'tools/fixtures/appearance-1.1.0.scmod'
    if((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant() -ne $rows[1].sha256){throw 'Missing immutable coexistence fixture'}
    Add-Type -AssemblyName Microsoft.VisualBasic
    [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    foreach($row in $rows){
        $resolved=[IO.Path]::GetFullPath((Join-Path $projectRoot $row.path))
        if(-not $resolved.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside project'}
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($resolved,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
        $row.state='recycled'
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    }
    $manifest.applied=$true
    [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
$manifest|ConvertTo-Json -Depth 6
