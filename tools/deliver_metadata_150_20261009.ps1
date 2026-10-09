#Requires -Version 7.0
$ErrorActionPreference='Stop'
$metadataRoot='E:\projects\ScCsgoKnives'
$metadataStage=Join-Path $metadataRoot '.tmp\dev-temp\metadata-150-20261009'
$metadataOutput=Join-Path $metadataRoot 'output'
$metadataReport=Join-Path $metadataOutput 'release-1.5.0\metadata-20261009'
$metadataManifest=Join-Path $metadataOutput 'release-1.5.0\manifest.json'
function Read-MetadataJson([string]$Path){Get-Content -Raw -LiteralPath $Path|ConvertFrom-Json -AsHashtable}
function Hash-MetadataFile([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Save-MetadataJson([string]$Path,$Data){[IO.File]::WriteAllText($Path,($Data|ConvertTo-Json -Depth 35),[Text.UTF8Encoding]::new($false))}
function Assert-MetadataPath([string]$Path,[string]$Scope){
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($Scope+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Out of scope path'}
    $node=Get-Item -LiteralPath $full -Force
    while($null-ne $node){if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked path'};$node=if($node.PSIsContainer){$node.Parent}else{$node.Directory}}
}
$packages=Read-MetadataJson (Join-Path $metadataStage 'metadata-packages.json')
$native=Read-MetadataJson (Join-Path $metadataStage 'native-metadata.json')
if($native.failed-ne 0-or $native.checks.Count-ne 7){throw 'Native metadata validation not passed'}
foreach($name in @('native-full.json','native-lite-agents.json','native-full-identity.json')){
    $r=Read-MetadataJson (Join-Path $metadataStage $name)
    if($r.failed-ne 0){throw "Native loading failed: $name"}
}
$manifest=Read-MetadataJson $metadataManifest
foreach($label in $packages.Keys){
    $p=$packages[$label];$source=Join-Path $metadataStage $p.file;$current=Join-Path $metadataOutput $p.file
    Assert-MetadataPath $source $metadataStage;Assert-MetadataPath $current $metadataOutput
    if((Hash-MetadataFile $source)-ne $p.sha256-or (Hash-MetadataFile $current)-ne $p.baselineSha256-or $manifest.packages[$label].sha256-ne $p.baselineSha256){throw "Input changed: $label"}
    if($native.packages[$p.file]-ne $p.sha256){throw 'Native check used another package'}
    $history=Join-Path $metadataOutput ('history-1.5.0\'+$p.baselineSha256)
    New-Item -ItemType Directory -Path $history -Force|Out-Null;Assert-MetadataPath $history $metadataOutput
    if(Test-Path -LiteralPath (Join-Path $history $p.file)){throw 'History target already exists'}
}
New-Item -ItemType Directory -Path $metadataReport -Force|Out-Null
if(Test-Path -LiteralPath (Join-Path $metadataReport 'delivery.json')){throw 'Inspect prior delivery before retrying'}
Copy-Item -LiteralPath $metadataManifest -Destination (Join-Path $metadataReport 'previous-manifest.json')
$moves=[Collections.Generic.List[object]]::new()
foreach($label in $packages.Keys){
    $p=$packages[$label];$source=Join-Path $metadataStage $p.file;$dest=Join-Path $metadataOutput $p.file
    $old=Join-Path $metadataOutput ('history-1.5.0\'+$p.baselineSha256+'\'+$p.file)
    Move-Item -LiteralPath $dest -Destination $old
    try{Move-Item -LiteralPath $source -Destination $dest}catch{Move-Item -LiteralPath $old -Destination $dest;throw}
    if((Hash-MetadataFile $dest)-ne $p.sha256-or (Hash-MetadataFile $old)-ne $p.baselineSha256){throw 'Readback mismatch'}
    $moves.Add(@{label=$label;path=$dest;sha256=$p.sha256;bytes=$p.bytes;archived=$old;previousSha256=$p.baselineSha256})
    Save-MetadataJson (Join-Path $metadataReport 'delivery.json') @{complete=$false;moves=$moves.ToArray()}
}
foreach($f in Get-ChildItem -LiteralPath $metadataStage -File -Filter '*.json'){Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $metadataReport $f.Name)}
$manifest.revision='repack-20261009-metadata-corrected'
$manifest.packages=$packages
$manifest.archiveChanges=@{}
foreach($label in $packages.Keys){$p=$packages[$label];$manifest.archiveChanges[$label]=@{previousSha256=$p.baselineSha256;changed=@($p.changed.Keys);unchanged=$p.unchangedMembers;assembliesAndResourcesUnchanged=$true}}
$manifest.delivery=$moves.ToArray()
$manifest.metadataCorrection=@{date='2026-10-09';scope='Root and embedded modinfo, INSTALL.txt, current bundle versions/core hash; historical provenance untouched';
    nativeMetadata='7/7';nativeFullResource='12/12';nativeFullIdentity='14/14';nativeLiteAgents='7/7';evidence=$metadataReport;
    inheritedEvidence='repack-20261009: unchanged DLL/resource hashes; gameplay/save/network identity unchanged. Runtime tests were not rerun.';
    sourceBranch='release/1.5.0-repack-20261009'}
Save-MetadataJson $metadataManifest $manifest
Save-MetadataJson (Join-Path $metadataReport 'manifest.json') $manifest
Save-MetadataJson (Join-Path $metadataReport 'delivery.json') @{complete=$true;moves=$moves.ToArray();manifestSha256=(Hash-MetadataFile $metadataManifest)}
$moves.ToArray()|ConvertTo-Json -Depth 5
