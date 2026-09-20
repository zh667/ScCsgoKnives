# Fetch the exact public NMM source used by the optional adapter. Does not install mods.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot '.tmp/nmm-player-appearance-audit-20260920'
$pin = '48c8f4fd8269441eb59add85e9538d17d739f95c'
if (-not (Test-Path -LiteralPath $source)) {
    git clone --no-checkout https://gitee.com/yangsanfengsc/sc-nekomekomodel.git $source
    if ($LASTEXITCODE -ne 0) { throw 'NMM clone failed' }
    git -C $source checkout --detach $pin
    if ($LASTEXITCODE -ne 0) { throw 'Pinned NMM checkout failed' }
}
if ((git -C $source rev-parse HEAD) -ne $pin) { throw 'Existing NMM checkout is not the tested revision; preserve it and supply the pinned source separately' }
if (git -C $source status --porcelain) { throw 'Existing NMM source has local edits; preserve them' }
Write-Output "Pinned NMM source ready: $source"
