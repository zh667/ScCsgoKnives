# Exact recoverable replacement; third-party packages and worlds are excluded.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mods='D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods'
$game=Split-Path -Parent $mods
if(Get-Process | Where-Object {$_.Path -and $_.Path.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)}){throw 'Game is running; installation has not started.'}
$evidence=Get-Content -LiteralPath (Join-Path $repo 'docs/release-firstperson-1.4.8-evidence.json') -Raw | ConvertFrom-Json
$previous=Get-Content -LiteralPath (Join-Path $repo 'docs/crafting-1.4.7-installation.json') -Raw | ConvertFrom-Json
$pairs=@(
    @{Old='[API1.9]CS武器1.4.7-作者ZH667-全量版.scmod';New='[API1.9]CS武器1.4.8-作者ZH667-全量版.scmod'},
    @{Old='[API1.9]CS战术同伴拓展1.2.3-作者ZH667.scmod';New='[API1.9]CS战术同伴拓展1.3.0-作者ZH667.scmod'}
)
foreach($p in $pairs){
    $p.Hash=$evidence.packages.($p.New).sha256
    $p.OldHash=($previous.Installed | Where-Object { [IO.Path]::GetFileName($_.Path) -eq $p.Old }).Sha256
    if(!$p.Hash -or !$p.OldHash){throw 'Missing verified digest.'}
    if((Get-FileHash -LiteralPath (Join-Path $repo ('output/'+$p.New))).Hash -ne $p.Hash){throw 'Untested source.'}
    foreach($base in @($mods,(Join-Path $repo 'output'))){
        $old=[IO.Path]::GetFullPath((Join-Path $base $p.Old))
        if((Split-Path -Parent $old) -ne $base -or (Get-FileHash -LiteralPath $old).Hash -ne $p.OldHash){throw "Changed obsolete target: $old"}
    }
    if(Test-Path -LiteralPath (Join-Path $mods $p.New)){throw 'New version already present; inspect before retry.'}
}
$untouched=@(Get-ChildItem -LiteralPath $mods -File | Where-Object Name -NotIn $pairs.Old | ForEach-Object {@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$archive=Join-Path $repo ('output/retired-firstperson-1.4.8-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Join-Path $archive 'installed'),(Join-Path $archive 'output') | Out-Null
$moves=[Collections.Generic.List[object]]::new();$installed=[Collections.Generic.List[string]]::new()
try{
    foreach($p in $pairs){
        $stage=Join-Path $archive $p.New
        Copy-Item -LiteralPath (Join-Path $repo ('output/'+$p.New)) -Destination $stage
        if((Get-FileHash -LiteralPath $stage).Hash -ne $p.Hash){throw 'Stage hash mismatch.'}
    }
    foreach($p in $pairs){
        $old=Join-Path $mods $p.Old;$saved=Join-Path $archive ('installed/'+$p.Old)
        Move-Item -LiteralPath $old -Destination $saved;$moves.Add(@{Original=$old;Archived=$saved;Sha256=$p.OldHash})
        $dest=Join-Path $mods $p.New;Move-Item -LiteralPath (Join-Path $archive $p.New) -Destination $dest;$installed.Add($dest)
    }
    foreach($p in $pairs){
        if((Get-FileHash -LiteralPath (Join-Path $mods $p.New)).Hash -ne $p.Hash){throw 'Installed hash mismatch.'}
        $old=Join-Path $repo ('output/'+$p.Old);$saved=Join-Path $archive ('output/'+$p.Old)
        Move-Item -LiteralPath $old -Destination $saved;$moves.Add(@{Original=$old;Archived=$saved;Sha256=$p.OldHash})
    }
    foreach($p in $untouched){if((Get-FileHash -LiteralPath (Join-Path $mods $p.Name)).Hash -ne $p.Sha256){throw "Unrelated file changed: $($p.Name)"}}
}catch{
    foreach($path in $installed){if(Test-Path -LiteralPath $path){Move-Item -LiteralPath $path -Destination (Join-Path $archive ([IO.Path]::GetFileName($path)))}}
    foreach($move in $moves){if(Test-Path -LiteralPath $move.Archived){Move-Item -LiteralPath $move.Archived -Destination $move.Original}}
    throw
}
$manifest=@{Installed=@($pairs | ForEach-Object {@{Path=(Join-Path $mods $_.New);Sha256=$_.Hash}});Archived=$moves.ToArray();Unchanged=$untouched;Recovery='Prior packages moved, not deleted. No worlds or third-party mods modified.'}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repo 'docs/firstperson-1.4.8-installation.json') -Encoding utf8
Write-Output "Installed 2 verified packages. Archived 4 superseded copies at $archive. $($untouched.Count) unrelated files unchanged."
