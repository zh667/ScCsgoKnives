param([switch]$Apply)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$manifestPath=Join-Path $projectRoot 'docs/cleanup-world-props-20260920.json'
$targets=@(
    @('output/[API1.9]CS武器1.4.5-作者ZH667-全量版.scmod','369915bc5f0f70ac6d047b181dde1c9c233c1405cfc0adc9919a60d1cfb8620f'),
    @('output/[API1.9]CS战术同伴拓展1.2.1-作者ZH667.scmod','c9873dbe2c54b86176378df5652a372fd6a58841f10e7ef49ce517c43118ce92')
)
$rows=@(foreach($target in $targets){
    $resolved=[IO.Path]::GetFullPath((Join-Path $projectRoot $target[0]))
    if(-not $resolved.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside project'}
    $file=Get-Item -LiteralPath $resolved
    if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Only exact regular files are allowed'}
    $hash=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if($hash -ne $target[1]){throw 'Superseded package changed; preserve it'}
    [ordered]@{path=$target[0];bytes=$file.Length;sha256=$hash;state='planned';recovery='Windows Recycle Bin'}
})
$manifest=[ordered]@{date='2026-09-20';authorization='User authorized removal of superseded releases';retained=@('core 1.4.6','tactical 1.2.2','NekoMeko 1.1','all installed Mods','worlds/backups/source exports/compatibility fixtures');files=$rows;totalBytes=($rows|ForEach-Object {$_.bytes}|Measure-Object -Sum).Sum;applied=$false}
if($Apply){
    if(Test-Path -LiteralPath $manifestPath){throw 'Preserve existing audit; do not repeat cleanup'}
    $evidence=Get-Content -LiteralPath (Join-Path $projectRoot 'docs/release-tactical-1.2.2-evidence.json') -Raw|ConvertFrom-Json
    if($evidence.failed -ne 0 -or $evidence.coreChecks -lt 12156 -or $evidence.installedTacticalChecks -ne 52 -or $evidence.checks.Count -lt 7332 -or $evidence.optionalLoading.runs.Count -ne 18 -or $evidence.preservationChecks -ne 1340){throw 'Final verification is incomplete'}
    foreach($package in $evidence.packages.PSObject.Properties){
        $actual=(Get-FileHash -LiteralPath (Join-Path $projectRoot ('output/'+$package.Name)) -Algorithm SHA256).Hash.ToLowerInvariant()
        if($actual -ne $package.Value.sha256){throw 'Release bytes differ from validation'}
    }
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
