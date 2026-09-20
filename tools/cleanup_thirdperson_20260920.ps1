param([switch]$Apply)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$manifestPath=Join-Path $projectRoot 'docs/cleanup-thirdperson-20260920.json'
$targets=@(
    'output/[API1.9]CS武器1.4.4-作者ZH667-全量版.scmod',
    'output/[API1.9]CS战术同伴拓展1.1.4-作者ZH667.scmod',
    'output/[API1.9]CS玩家T-CT外观1.0.0-作者ZH667.scmod'
)
$rows=@(foreach($relative in $targets){
    $resolved=[IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    if(-not $resolved.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside project'}
    $file=Get-Item -LiteralPath $resolved
    if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Only exact regular files are allowed'}
    [ordered]@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant();state='planned';recovery='Windows Recycle Bin'}
})
$manifest=[ordered]@{date='2026-09-20';authorization='User requested cleanup of superseded versions';retained=@('core 1.4.5','tactical 1.2.0','appearance 1.1.0','NekoMeko Model 1.1');files=$rows;totalBytes=($rows|ForEach-Object {$_.bytes}|Measure-Object -Sum).Sum;applied=$false}
if($Apply){
    if(Test-Path -LiteralPath $manifestPath){throw 'Preserve existing audit; do not repeat cleanup'}
    $evidence=Get-Content -LiteralPath (Join-Path $projectRoot 'docs/release-appearance-1.1.0-evidence.json') -Raw|ConvertFrom-Json
    if($evidence.failed -ne 0 -or $evidence.coreChecks -lt 12000 -or $evidence.installedTacticalChecks -lt 52){throw 'Final release verification is incomplete'}
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
