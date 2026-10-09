#Requires -Version 7.0
$ErrorActionPreference='Stop'
$deliveryRoot='E:\projects\ScCsgoKnives'
$deliveryStage=Join-Path $deliveryRoot '.tmp\dev-temp\dm-settings-release-20261009'
$deliveryOutput=Join-Path $deliveryRoot 'output'
$deliveryEvidence=Join-Path $deliveryOutput 'release-1.5.0\dm-settings-20261009'
$deliveryManifest=Join-Path $deliveryOutput 'release-1.5.0\manifest.json'
function Read-DeliveryJson([string]$Path){Get-Content -Raw -LiteralPath $Path|ConvertFrom-Json -AsHashtable}
function Write-DeliveryJson([string]$Path,$Data){[IO.File]::WriteAllText($Path,($Data|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))}
function Get-DeliveryHash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Assert-DeliveryPath([string]$Path,[string]$Scope){
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($Scope+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Path outside release scope'}
    $node=Get-Item -LiteralPath $full -Force
    while($null-ne $node){if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Release path contains a link'};$node=if($node.PSIsContainer){$node.Parent}else{$node.Directory}}
}
$execution=Read-DeliveryJson (Join-Path $deliveryStage 'execution.json')
foreach($name in @('prepare','build','appearance','package','dmbuild','dmpackage','dmcheck','dmabi','netloop','dmloop','dmload','appnet','gates','compat')){
    $last=@($execution|Where-Object {$_.step-eq $name})[-1]
    if(-not $last.passed){throw "Incomplete gate: $name"}
}
$historical=Read-DeliveryJson (Join-Path $deliveryStage 'historical-checks.json')
if($historical.Count-lt 18-or @($historical.Values|Where-Object {-not $_}).Count){throw 'Historical release checks incomplete'}
$packages=Read-DeliveryJson (Join-Path $deliveryStage 'packages.json')
$archives=Read-DeliveryJson (Join-Path $deliveryStage 'archive-verification.json')
$metadata=Read-DeliveryJson (Join-Path $deliveryStage 'metadata.json')
if($metadata.failed-ne 0-or $metadata.checks.Count-ne 7){throw 'Metadata invalid'}
$summary=[ordered]@{}
foreach($name in @('main-full','main-lite','dmcheck','family','switching-140','switching-150','inventory','settings-full/settings','settings-lite/settings')){
    $r=Read-DeliveryJson (Join-Path $deliveryStage ($name+'.json'))
    $cases=if($r.ContainsKey('checks')){@($r.checks)}else{@($r.cases)}
    if($r.failed-ne 0-or $cases.Count-eq 0){throw "Gate failed: $name"}
    $summary[$name]=@{total=$cases.Count;failed=$r.failed}
}
foreach($edition in @('full','lite')){
    $r=Read-DeliveryJson (Join-Path $deliveryStage ('settings-'+$edition+'/settings.json'))
    if($r.checks.Count-lt 41){throw 'Settings cases missing'}
    $core=Join-Path $deliveryStage ($edition+'/core/source/bin/Release/net10.0/ScCsgoKnives.dll')
    if($r.core-ne (Get-DeliveryHash $core)){throw 'UI tested a different core'}
}
$tactical=Read-DeliveryJson (Join-Path $deliveryStage 'tactical-full-suite.json')
$bad=@($tactical.checks|Where-Object {!$_.Ok}|ForEach-Object {$_.Name}|Sort-Object)
$known=@('native-gltf/ct','native-gltf/t','npc-full-registry-reload-refuses-without-ammo-loss','optional-package-identity-and-no-bundled-engine')|Sort-Object
if(Compare-Object $known $bad){throw 'New tactical regression'}
foreach($label in $packages.Keys){
    $p=$packages[$label];$from=Join-Path $deliveryStage ('candidate\'+$p.file);$to=Join-Path $deliveryOutput $p.file
    Assert-DeliveryPath $from $deliveryStage;Assert-DeliveryPath $to $deliveryOutput
    if((Get-DeliveryHash $from)-ne $p.sha256-or (Get-DeliveryHash $to)-ne $archives[$label].previousSha256){throw "Package changed: $label"}
    if($metadata.packages[$p.file]-ne $p.sha256){throw 'Metadata checked another package'}
    $history=Join-Path $deliveryOutput ('history-1.5.0\'+$archives[$label].previousSha256)
    New-Item -ItemType Directory -Path $history -Force|Out-Null;Assert-DeliveryPath $history $deliveryOutput
    if(Test-Path -LiteralPath (Join-Path $history $p.file)){throw 'Historical archive already exists'}
}
New-Item -ItemType Directory -Path $deliveryEvidence -Force|Out-Null
if(Test-Path -LiteralPath (Join-Path $deliveryEvidence 'delivery.json')){throw 'Prior delivery exists; inspect rather than repeat'}
Copy-Item -LiteralPath $deliveryManifest -Destination (Join-Path $deliveryEvidence 'previous-manifest.json')
$moves=[Collections.Generic.List[object]]::new()
foreach($label in $packages.Keys){
    $p=$packages[$label];$from=Join-Path $deliveryStage ('candidate\'+$p.file);$to=Join-Path $deliveryOutput $p.file
    $old=Join-Path $deliveryOutput ('history-1.5.0\'+$archives[$label].previousSha256+'\'+$p.file)
    Move-Item -LiteralPath $to -Destination $old
    try{Move-Item -LiteralPath $from -Destination $to}catch{Move-Item -LiteralPath $old -Destination $to;throw}
    if((Get-DeliveryHash $to)-ne $p.sha256-or (Get-DeliveryHash $old)-ne $archives[$label].previousSha256){throw 'Delivery checksum mismatch'}
    $moves.Add(@{label=$label;path=$to;sha256=$p.sha256;bytes=$p.bytes;archived=$old;previousSha256=$archives[$label].previousSha256})
    Write-DeliveryJson (Join-Path $deliveryEvidence 'delivery.json') @{complete=$false;moves=$moves.ToArray()}
}
foreach($file in Get-ChildItem -LiteralPath $deliveryStage -File -Filter '*.json'){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $deliveryEvidence $file.Name)}
foreach($folder in @('logs','settings-full','settings-lite')){Copy-Item -LiteralPath (Join-Path $deliveryStage $folder) -Destination (Join-Path $deliveryEvidence $folder) -Recurse}
$stamp=Read-DeliveryJson (Join-Path $deliveryStage 'identity.json');$dm=Read-DeliveryJson (Join-Path $deliveryStage 'dm.json')
$manifest=[ordered]@{version='1.5.0';date='2026-10-09';revision='dm-settings-20261009';sourceCommit='d0c6d250b54709f5c170dcd2e78531a376e9d67a';
    sourceBranch='fix/deathmatch-settings-entry';identity=$stamp.identity;deathmatchIdentity=$dm.identity;packages=$packages;
    archiveChanges=$archives;checks=$summary;historicalChecks=$historical;knownTacticalFailures=$bad;delivery=$moves.ToArray();evidence=$deliveryEvidence;
    behavior='Game Settings button removed. Mod Settings > CS weapons > Deathmatch settings opens the existing world menu; host explicitly enables the arena.';
    verificationScope='Native 1.9.3.1 UI clicks/layout, package/rules/save checks; native MP assembly loopback. No player world or Mods changes.';
    notRun=@('actual gameplay session','Android','new real-game multiplayer session');visualAcceptance='User observation pending for final in-game menu layout; required behavior checks executed.'}
Write-DeliveryJson $deliveryManifest $manifest
Write-DeliveryJson (Join-Path $deliveryEvidence 'manifest.json') $manifest
Write-DeliveryJson (Join-Path $deliveryEvidence 'delivery.json') @{complete=$true;moves=$moves.ToArray();manifestSha256=(Get-DeliveryHash $deliveryManifest)}
$moves.ToArray()|ConvertTo-Json -Depth 5
