param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$keep = @('release-1.4.4','tactical-1.1.4','appearance-1.0.0')
$candidates = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($relative in @(
    'output/[API1.9]CS武器1.4.2-作者ZH667-全量版.scmod',
    'output/[API1.9]CS武器1.4.3-作者ZH667-全量版.scmod',
    'output/[API1.9]CS战术同伴拓展1.1.2-作者ZH667.scmod',
    'output/[API1.9]CS战术同伴拓展1.1.3-作者ZH667.scmod',
    'output/check-1.0.14-final.json',
    '.tmp/tactical-incomplete-core-candidate.scmod'
)) { $target = Join-Path $projectRoot $relative; if (Test-Path -LiteralPath $target -PathType Leaf) { $candidates.Add((Get-Item -LiteralPath $target)) } }
# Only reproducible reports/previews from superseded output directories. World/compatibility
# fixtures, sources, shared resources, backups and installed game files are outside this allowlist.
$oldReports = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'output') -Directory |
    Where-Object { $_.Name -match '^(release|tactical)-1\.' -and $_.Name -notin $keep }
foreach ($directory in $oldReports) {
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File -Recurse) {
        if ($file.Extension -in @('.json','.log','.png','.svg','.html','.txt','.webp')) { $candidates.Add($file) }
    }
}
$retired = Join-Path $projectRoot '.tmp/retired-output-20260913'
if (Test-Path -LiteralPath $retired) {
    foreach ($file in Get-ChildItem -LiteralPath $retired -File -Recurse) {
        if ($file.Extension -in @('.scmod','.zip','.json','.log','.txt')) { $candidates.Add($file) }
    }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot '.tmp') -File) {
    if ($file.Name -match '^(full|final|test|feedback|litecheck|fullcheck|c4-opt).*\.log$') { $candidates.Add($file) }
}
$files = @($candidates | Sort-Object FullName -Unique)
$rows = foreach ($file in $files) {
    $resolved = [IO.Path]::GetFullPath($file.FullName)
    if (-not $resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Outside project: $resolved" }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing reparse point: $resolved" }
    [ordered]@{ path = [IO.Path]::GetRelativePath($projectRoot,$resolved); bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant(); recovery = 'Windows Recycle Bin'; state = 'planned' }
}
$manifest = [ordered]@{ date='2026-09-20'; authorization='User requested removing superseded packages and useless small files'; retained=$keep; files=@($rows); totalBytes=($files | Measure-Object Length -Sum).Sum; applied=$false }
$manifestPath = Join-Path $projectRoot 'docs/cleanup-20260920.json'
if ($Apply) {
    if (Test-Path -LiteralPath $manifestPath) { throw 'A cleanup manifest already exists; preserve that audit record and review it before another cleanup.' }
    Add-Type -AssemblyName Microsoft.VisualBasic
    # Record exact targets before deleting anything. Always use the native recycle operation.
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    foreach ($row in $rows) {
        $target = [IO.Path]::GetFullPath((Join-Path $projectRoot $row.path))
        if (-not $target.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped project' }
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($target,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
        $row.state = 'recycled'
        [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    }
    $manifest.applied=$true
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
[pscustomobject]@{ Count=$files.Count; Bytes=$manifest.totalBytes; GiB=[Math]::Round($manifest.totalBytes/1GB,2); Applied=$Apply.IsPresent }
