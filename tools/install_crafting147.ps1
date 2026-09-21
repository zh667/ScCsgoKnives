# Exact, recoverable replacement of the two user-scoped CS releases.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mods = 'D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods'
$game = Split-Path -Parent $mods
if (Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    throw 'Game process is still running; installation was not started.'
}
$report = Join-Path $repo 'output\release-crafting-1.4.7'
$coreTest = Get-Content -LiteralPath (Join-Path $report 'core-check.json') -Raw | ConvertFrom-Json
$tacticalTest = Get-Content -LiteralPath (Join-Path $report 'tactical-check.json') -Raw | ConvertFrom-Json
if ($coreTest.failed -ne 0 -or $tacticalTest.failed -ne 0) { throw 'Final package tests must pass.' }
$pairs = @(
    @{ Old='[API1.9]CS武器1.4.6-作者ZH667-全量版.scmod'; New='[API1.9]CS武器1.4.7-作者ZH667-全量版.scmod'; Hash=$coreTest.packageSha256; OldHash='8bd32438862d5911f99f6fea057023d17c8aa258f3e17e3c02a51b07db071aa9' },
    @{ Old='[API1.9]CS战术同伴拓展1.2.2-作者ZH667.scmod'; New='[API1.9]CS战术同伴拓展1.2.3-作者ZH667.scmod'; Hash=$tacticalTest.dlcSha256; OldHash='34a12fe08262c4f580e287d697e01607e0f863aa992348de327b1f4b979cf778' }
)
if ($coreTest.packageSha256 -ne $tacticalTest.coreSha256) { throw 'Tests used different core packages.' }
$archive = Join-Path $repo ('output\retired-crafting-1.4.7-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
# Validate every exact target before moving anything. Worlds and other packages are excluded.
foreach ($p in $pairs) {
    $source = Join-Path $repo ('output\' + $p.New)
    if ((Get-FileHash -LiteralPath $source).Hash -ne $p.Hash) { throw "Untested source: $source" }
    foreach ($base in @($mods,(Join-Path $repo 'output'))) {
        $old = [IO.Path]::GetFullPath((Join-Path $base $p.Old))
        if ((Split-Path -Parent $old) -ne $base -or (Get-FileHash -LiteralPath $old).Hash -ne $p.OldHash) { throw "Changed obsolete package: $old" }
    }
    if (Test-Path -LiteralPath (Join-Path $mods $p.New)) { throw 'New version already exists; inspect before retrying.' }
}
$untouched = @(Get-ChildItem -LiteralPath $mods -File | Where-Object Name -NotIn $pairs.Old | ForEach-Object {
    @{ Name=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
New-Item -ItemType Directory -Path (Join-Path $archive 'installed'),(Join-Path $archive 'output') | Out-Null
$moves = [Collections.Generic.List[object]]::new()
$installed = [Collections.Generic.List[string]]::new()
try {
    foreach ($p in $pairs) {
        $staged = Join-Path $archive $p.New
        Copy-Item -LiteralPath (Join-Path $repo ('output\' + $p.New)) -Destination $staged
        if ((Get-FileHash -LiteralPath $staged).Hash -ne $p.Hash) { throw 'Staging hash mismatch.' }
    }
    foreach ($p in $pairs) {
        $old = Join-Path $mods $p.Old; $saved = Join-Path $archive ('installed\' + $p.Old)
        Move-Item -LiteralPath $old -Destination $saved
        $moves.Add(@{ Original=$old; Archived=$saved; Sha256=$p.OldHash })
        $destination = Join-Path $mods $p.New
        Move-Item -LiteralPath (Join-Path $archive $p.New) -Destination $destination
        $installed.Add($destination)
    }
    foreach ($p in $pairs) {
        if ((Get-FileHash -LiteralPath (Join-Path $mods $p.New)).Hash -ne $p.Hash) { throw 'Installed hash mismatch.' }
        $old = Join-Path $repo ('output\' + $p.Old); $saved = Join-Path $archive ('output\' + $p.Old)
        Move-Item -LiteralPath $old -Destination $saved
        $moves.Add(@{ Original=$old; Archived=$saved; Sha256=$p.OldHash })
    }
    foreach ($p in $untouched) {
        if ((Get-FileHash -LiteralPath (Join-Path $mods $p.Name)).Hash -ne $p.Sha256) { throw "Unrelated file changed: $($p.Name)" }
    }
    $obsoleteReport = Join-Path $repo 'output\ScCsgoKnives-1.4.7.resources.json'
    if (Test-Path -LiteralPath $obsoleteReport) {
        $saved = Join-Path $archive 'output\ScCsgoKnives-1.4.7.resources.json'
        $digest = (Get-FileHash -LiteralPath $obsoleteReport).Hash.ToLowerInvariant()
        Move-Item -LiteralPath $obsoleteReport -Destination $saved
        $moves.Add(@{ Original=$obsoleteReport; Archived=$saved; Sha256=$digest })
    }
} catch {
    foreach ($path in $installed) { if (Test-Path -LiteralPath $path) { Move-Item -LiteralPath $path -Destination (Join-Path $archive ([IO.Path]::GetFileName($path))) } }
    foreach ($move in $moves) { if (Test-Path -LiteralPath $move.Archived) { Move-Item -LiteralPath $move.Archived -Destination $move.Original } }
    throw
}
$manifest = @{ Installed=$pairs | ForEach-Object { @{ Path=(Join-Path $mods $_.New); Sha256=$_.Hash } }; Archived=$moves.ToArray(); Unchanged=$untouched; Recovery='Old packages were moved, not deleted. Restore from archive with game closed; never downgrade a newer world without its compatible backup.' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repo 'docs\crafting-1.4.7-installation.json') -Encoding utf8
Write-Output "Installed two verified CS packages; archived four superseded copies to $archive; $($untouched.Count) unrelated files unchanged."
