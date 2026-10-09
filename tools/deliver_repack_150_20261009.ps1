#Requires -Version 7.0
$ErrorActionPreference='Stop'
$repackRoot='E:\projects\ScCsgoKnives'
$repackStage=Join-Path $repackRoot '.tmp\dev-temp\repack-150-20261009'
$repackOutput=Join-Path $repackRoot 'output'
$repackReport=Join-Path $repackOutput 'release-1.5.0\repack-20261009'
$repackManifest=Join-Path $repackOutput 'release-1.5.0\manifest.json'
function Read-RepackJson([string]$Path){Get-Content -Raw -LiteralPath $Path|ConvertFrom-Json -AsHashtable}
function Write-RepackJson([string]$Path,$Data){[IO.File]::WriteAllText($Path,($Data|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))}
function Get-RepackHash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Assert-RepackScope([string]$Path,[string]$Parent){
    $resolved=[IO.Path]::GetFullPath($Path)
    if(-not $resolved.StartsWith($Parent+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Outside scope: $resolved"}
    $node=if(Test-Path -LiteralPath $resolved){Get-Item -LiteralPath $resolved -Force}else{Get-Item -LiteralPath (Split-Path -Parent $resolved) -Force}
    while($null-ne $node){if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw "Linked target: $($node.FullName)"};$node=if($node.PSIsContainer){$node.Parent}else{$node.Directory}}
}
$execution=Read-RepackJson (Join-Path $repackStage 'execution.json')
$required=@('prepare','build','appearance','package','dmbuild','dmpackage','dmcheck','dmabi','netloop','dmloop','dmload','appnet','gates','compat')
foreach($step in $required){$last=@($execution|Where-Object {$_.step-eq $step})[-1];if(-not $last.passed){throw "Gate not passed: $step"}}
$extra=Read-RepackJson (Join-Path $repackStage 'extra-switching.json')
if($extra.Count-ne 10-or @($extra.Values|Where-Object {-not $_}).Count){throw 'Extra historical switching incomplete'}
$checks=[ordered]@{}
foreach($name in @('main-full','main-lite','airdrop-full','airdrop-lite','dmcheck','family','switching-140','switching-150','inventory','tactical-full-suite')){
    $data=Read-RepackJson (Join-Path $repackStage ($name+'.json'))
    $cases=if($data.ContainsKey('checks')){@($data.checks)}else{@($data.cases)}
    if(-not $cases.Count-or $null-eq $cases[0]){throw "No executed cases: $name"}
    $failed=@($cases|Where-Object {if($_.ContainsKey('ok')){-not $_.ok}else{-not $_.Ok}})
    if($name-eq 'tactical-full-suite'){
        $expected=@('native-gltf/ct','native-gltf/t','npc-full-registry-reload-refuses-without-ammo-loss','optional-package-identity-and-no-bundled-engine')|Sort-Object
        if(Compare-Object $expected @($failed|ForEach-Object {$_.Name}|Sort-Object)){throw 'Tactical failures changed'}
    }elseif($failed.Count){throw "Failed check: $name"}
    $checks[$name]=@{total=$cases.Count;failed=$failed.Count;knownFailures=@($failed|ForEach-Object {$_.Name})}
}
$packages=Read-RepackJson (Join-Path $repackStage 'packages.json')
$archives=Read-RepackJson (Join-Path $repackStage 'archive-verification.json')
if($packages.Count-ne 4){throw 'Expected four packages'}
foreach($label in $packages.Keys){
    $item=$packages[$label];$source=Join-Path $repackStage ('candidate\'+$item.file);$dest=Join-Path $repackOutput $item.file
    Assert-RepackScope $source $repackStage;Assert-RepackScope $dest $repackOutput
    if((Get-RepackHash $source)-ne $item.sha256-or (Get-RepackHash $dest)-ne $archives[$label].previousSha256){throw "Delivery input changed: $label"}
    $archiveFolder=Join-Path $repackOutput ('history-1.5.0\'+$archives[$label].previousSha256)
    New-Item -ItemType Directory -Path $archiveFolder -Force|Out-Null
    $old=Join-Path $archiveFolder $item.file;Assert-RepackScope $old $repackOutput
    if(Test-Path -LiteralPath $old){throw "Historical target already exists; inspect before mutation: $old"}
}
New-Item -ItemType Directory -Path $repackReport -Force|Out-Null
if(Test-Path -LiteralPath (Join-Path $repackReport 'delivery.json')){throw 'Delivery already attempted; inspect receipt instead of repeating'}
Copy-Item -LiteralPath $repackManifest -Destination (Join-Path $repackReport 'previous-manifest.json')
$moves=[Collections.Generic.List[object]]::new()
foreach($label in $packages.Keys){
    $item=$packages[$label];$source=Join-Path $repackStage ('candidate\'+$item.file);$dest=Join-Path $repackOutput $item.file
    $old=Join-Path $repackOutput ('history-1.5.0\'+$archives[$label].previousSha256+'\'+$item.file)
    Move-Item -LiteralPath $dest -Destination $old
    try{Move-Item -LiteralPath $source -Destination $dest}catch{Move-Item -LiteralPath $old -Destination $dest;throw}
    if((Get-RepackHash $dest)-ne $item.sha256-or (Get-RepackHash $old)-ne $archives[$label].previousSha256){throw 'Post-move checksum mismatch'}
    $moves.Add(@{label=$label;path=$dest;sha256=$item.sha256;bytes=$item.bytes;archived=$old;previousSha256=$archives[$label].previousSha256})
    Write-RepackJson (Join-Path $repackReport 'delivery.json') @{complete=$false;moves=$moves.ToArray()}
}
foreach($file in Get-ChildItem -LiteralPath $repackStage -File -Filter '*.json'){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $repackReport $file.Name)}
Copy-Item -LiteralPath (Join-Path $repackStage 'logs') -Destination (Join-Path $repackReport 'logs') -Recurse
$identity=Read-RepackJson (Join-Path $repackStage 'identity.json');$deathmatch=Read-RepackJson (Join-Path $repackStage 'dm.json')
$manifest=[ordered]@{version='1.5.0';date='2026-10-09';revision='repack-20261009';sourceCommit='d3a9568a90c6d9e856e5ca1b653c17a6766625e9';
    source='Current main product sources, independently verified against all staged product .cs files';identity=$identity.identity;deathmatchIdentity=$deathmatch.identity;
    packages=$packages;archiveChanges=$archives;checks=$checks;extraHistoricalSwitching=$extra;evidence=$repackReport;delivery=$moves.ToArray();
    notRun=@('new real-game single-player session','new real-game multiplayer session','Android');
    acceptance='Airdrop visual repair accepted by user on 2026-10-08; this is a stamped rebuild of that code. Native/offline release checks executed on these exact packages; 4 pre-existing tactical-suite failures are explicitly retained.'}
Write-RepackJson $repackManifest $manifest
Write-RepackJson (Join-Path $repackReport 'manifest.json') $manifest
Write-RepackJson (Join-Path $repackReport 'delivery.json') @{complete=$true;moves=$moves.ToArray();manifestSha256=(Get-RepackHash $repackManifest)}
$moves.ToArray()|ConvertTo-Json -Depth 5
