# Install the corrected tactical package; validate the already present core and archive exact old copies.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mods='D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1\Mods'
$output=Join-Path $repo 'output'
$game=Split-Path -Parent $mods
if(Get-Process | Where-Object {$_.Path -and $_.Path.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)}){throw 'Game is running; installation has not started.'}
$evidence=Get-Content -LiteralPath (Join-Path $repo 'docs/release-gloves-1.4.9-evidence.json') -Raw | ConvertFrom-Json
$previous=Get-Content -LiteralPath (Join-Path $repo 'docs/firstperson-1.4.8-installation.json') -Raw | ConvertFrom-Json
$core='[API1.9]CS武器1.4.9-作者ZH667-全量版.scmod'
$current='[API1.9]CS战术同伴拓展1.3.1-作者ZH667.scmod'
$replacement='[API1.9]CS战术同伴拓展1.3.2-作者ZH667.scmod'
$oldTacticalHash='07e8495399622ba30415e548312b2aefc059ce6d8b6773834b35f81c153e5763'
$newHash=$evidence.packages.($replacement).sha256
$targets=@(
    @{Base=$mods;Name=$current;Hash=$oldTacticalHash;Section='installed'},
    @{Base=$output;Name=$current;Hash=$oldTacticalHash;Section='output'}
)
foreach($old in @('[API1.9]CS武器1.4.8-作者ZH667-全量版.scmod','[API1.9]CS战术同伴拓展1.3.0-作者ZH667.scmod')){
    $digest=($previous.Installed | Where-Object {[IO.Path]::GetFileName($_.Path) -eq $old}).Sha256
    $targets+=@{Base=$output;Name=$old;Hash=$digest;Section='output'}
    if(Test-Path -LiteralPath (Join-Path $mods $old)){throw "Unexpected old installed package: $old"}
}
if(!$newHash){throw 'Missing verified digest.'}
foreach($base in @($output,$mods)){
    if((Get-FileHash -LiteralPath (Join-Path $base $core)).Hash -ne $evidence.packages.($core).sha256){throw 'Changed core package.'}
}
if((Get-FileHash -LiteralPath (Join-Path $output $replacement)).Hash -ne $newHash){throw 'Untested replacement.'}
if(Test-Path -LiteralPath (Join-Path $mods $replacement)){throw 'Replacement already installed; inspect before retry.'}
foreach($target in $targets){
    $target.Path=[IO.Path]::GetFullPath((Join-Path $target.Base $target.Name))
    if((Split-Path -Parent $target.Path) -ne $target.Base -or !$target.Hash -or (Get-FileHash -LiteralPath $target.Path).Hash -ne $target.Hash){throw "Changed obsolete target: $($target.Path)"}
}
$untouched=@(Get-ChildItem -LiteralPath $mods -File | Where-Object Name -NotIn @($core,$current) | ForEach-Object {@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
$archive=Join-Path $output ('retired-gloves-1.4.9-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Join-Path $archive 'installed'),(Join-Path $archive 'output') | Out-Null
$moves=[Collections.Generic.List[object]]::new();$installed=$false
$dest=Join-Path $mods $replacement;$stage=Join-Path $archive $replacement
try{
    Copy-Item -LiteralPath (Join-Path $output $replacement) -Destination $stage
    if((Get-FileHash -LiteralPath $stage).Hash -ne $newHash){throw 'Stage hash mismatch.'}
    foreach($target in $targets){
        $saved=Join-Path $archive ($target.Section+'/'+$target.Name)
        Move-Item -LiteralPath $target.Path -Destination $saved
        $moves.Add(@{Original=$target.Path;Archived=$saved;Sha256=$target.Hash.ToLowerInvariant()})
    }
    Move-Item -LiteralPath $stage -Destination $dest;$installed=$true
    if((Get-FileHash -LiteralPath $dest).Hash -ne $newHash){throw 'Installed hash mismatch.'}
    foreach($p in $untouched){if((Get-FileHash -LiteralPath (Join-Path $mods $p.Name)).Hash -ne $p.Sha256){throw "Unrelated file changed: $($p.Name)"}}
}catch{
    if($installed -and (Test-Path -LiteralPath $dest)){Move-Item -LiteralPath $dest -Destination $stage}
    foreach($move in $moves){if(Test-Path -LiteralPath $move.Archived){Move-Item -LiteralPath $move.Archived -Destination $move.Original}}
    throw
}
@{Installed=@(@{Path=(Join-Path $mods $core);Sha256=$evidence.packages.($core).sha256;Action='Verified already present'},@{Path=$dest;Sha256=$newHash;Action='Installed'});Archived=$moves.ToArray();Unchanged=$untouched;Recovery='Old packages archived, not deleted. No worlds or unrelated mods modified. Mods already contained verified core 1.4.9 and tactical 1.3.1 before this installation.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repo 'docs/gloves-1.4.9-installation.json') -Encoding utf8
Write-Output "Verified core 1.4.9; installed tactical 1.3.2; archived 4 old copies at $archive; $($untouched.Count) unrelated files unchanged."
