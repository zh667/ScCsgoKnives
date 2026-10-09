#Requires -Version 7.0
param([switch]$Execute)
$ErrorActionPreference='Stop'
$cleanupRoot='E:\projects\ScCsgoKnives'
$cleanupReport=Join-Path $cleanupRoot 'output\release-1.5.0\repack-20261009'
$cleanupManifest=Get-Content -Raw -LiteralPath (Join-Path $cleanupReport 'manifest.json')|ConvertFrom-Json -AsHashtable
function Save-CleanupJson([string]$Name,$Data){[IO.File]::WriteAllText((Join-Path $cleanupReport $Name),($Data|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
function Test-CleanupDelivery{
    foreach($entry in $cleanupManifest.delivery){
        if((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne $entry.sha256){throw 'Delivered package changed'}
        if((Get-FileHash -LiteralPath $entry.archived -Algorithm SHA256).Hash.ToLowerInvariant()-ne $entry.previousSha256){throw 'Historical package changed'}
    }
}
function Get-SafeCleanupItem([string]$Relative){
    $path=[IO.Path]::GetFullPath((Join-Path $cleanupRoot $Relative))
    if(-not $path.StartsWith($cleanupRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Path escaped project: $path"}
    if(-not (Test-Path -LiteralPath $path)){return $null}
    $item=Get-Item -LiteralPath $path -Force;$node=$item
    while($null-ne $node){
        if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw "Linked path: $($node.FullName)"}
        $node=if($node.PSIsContainer){$node.Parent}else{$node.Directory}
    }
    return $item
}
Test-CleanupDelivery
$targets=[Collections.Generic.List[object]]::new()
function Add-CleanupTarget([string]$Relative,[string]$Mode,[string]$Reason){
    $item=Get-SafeCleanupItem $Relative;if($null-eq $item){return}
    $children=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $item.FullName -Recurse -Force)}else{@($item)}
    if(@($children|Where-Object {$_.Attributes-band [IO.FileAttributes]::ReparsePoint}).Count){throw "Link below $($item.FullName)"}
    $files=@($children|Where-Object {-not $_.PSIsContainer})
    $targets.Add(@{path=$item.FullName;directory=$item.PSIsContainer;bytes=[long](($files|Measure-Object Length -Sum).Sum);files=$files.Count;mode=$Mode;reason=$Reason})
}
# This task's new, reproducible builds: exact children, never .tmp or a workspace root.
foreach($name in @('baseline','candidate','full','lite','tree','resources','dm','net','old130','old140','old150','old150all')){
    Add-CleanupTarget ('.tmp/dev-temp/repack-150-20261009/'+$name) 'permanent' 'New task build or extracted DLLs; source and official input packages retained, reports copied to output'
}
# Prior permanent deletions were denied. Recycling is deliberately recoverable and does not retry that irreversible action.
foreach($name in @('full','split')){Add-CleanupTarget ('.tmp/dev-temp/airdrop-item-presentation-20261007/'+$name) 'recycle' 'Superseded visual candidate and split build; official rebuilt family delivered'}
foreach($name in @('bin','obj')){Add-CleanupTarget ('.tmp/dev-temp/airdrop-item-presentation-20261007/Probe/'+$name) 'recycle' 'Rebuildable probe output; source and result JSON retained'}
foreach($name in @('DeathmatchCheck','ScCsgoDeathmatch')){Add-CleanupTarget ('.tmp/dev-temp/code-quality-20261005/artifacts/bin/'+$name+'/release/Assets') 'recycle' 'Previously audited duplicate resource copy; original assets and official packages retained'}
# Keep old build source/embedded inputs and evidence; remove only its reproducible binary output and extracted official DLLs.
foreach($name in @('core/bin','core/obj','tactical/bin','tactical/obj','feedback/bin','feedback/obj','ui-tool/bin','ui-tool/obj','old-full','old-lite','old150','current150')){
    Add-CleanupTarget ('.tmp/dev-temp/release-full-150/'+$name) 'recycle' 'Superseded standalone Full build output; compact source/embedded inputs and archived official packages retained'
}
foreach($round in @('airdrop-item-review-20261007','airdrop-item-review-20261007-r2')){
    foreach($label in @('全量','轻量','探员')){Add-CleanupTarget ('output/'+$round+'/[API1.9]CS武器1.5.0-'+$label+'包.scmod') 'recycle' 'Superseded user-tested development package; production family now includes accepted repair'}
}
# Preserve the actual probe inputs for reproduction before removing its generated bin/obj.
$probeEvidence=Join-Path $cleanupReport 'airdrop-probe'
New-Item -ItemType Directory -Path $probeEvidence -Force|Out-Null
foreach($name in @('Program.cs','Probe.csproj')){Copy-Item -LiteralPath (Join-Path $cleanupRoot ('.tmp/dev-temp/airdrop-item-presentation-20261007/Probe/'+$name)) -Destination (Join-Path $probeEvidence $name) -Force}
Copy-Item -LiteralPath (Join-Path $cleanupRoot 'tools/PackageCheck/AirdropMaterialRegression.cs') -Destination (Join-Path $probeEvidence 'AirdropMaterialRegression.cs') -Force
foreach($required in @('.tmp/release-140-20260928','.tmp/capacity-fix-20260927/1.0.0/src/ScCsgoKnives/bin/Release/net10.0','.tmp/capacity-fix-20260927/1.2.0/src/ScCsgoKnives/bin/Release/net10.0','.tmp/third-party-refs','.tmp/dev-temp/release-full-150/core/AnimationData','.tmp/dev-temp/release-full-150/tactical/ArmData')){
    if(-not(Test-Path -LiteralPath (Join-Path $cleanupRoot $required))){throw "Required input absent: $required"}
}
$processes=Get-CimInstance Win32_Process
foreach($target in $targets){
    $busy=@($processes|Where-Object {($_.ExecutablePath-and $_.ExecutablePath.StartsWith($target.path,[StringComparison]::OrdinalIgnoreCase))-or ($_.CommandLine-and $_.CommandLine.Contains($target.path,[StringComparison]::OrdinalIgnoreCase))})
    if($busy.Count){throw "Active process references $($target.path)"}
}
$bridgeToken=[IO.File]::ReadAllText('E:\Develop\AgentBridge\private\token').Trim()
$bridgeHealth=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18765' -ContentType 'application/json' -Body '{"op":"health","args":{}}' -Headers @{Authorization=('Bearer '+$bridgeToken)} -TimeoutSec 20
if(-not $bridgeHealth.ok-or @($bridgeHealth.result.running_jobs).Count-ne 0){throw 'Windows worker is not idle'}
$plan=@{date='2026-10-09';targets=$targets.ToArray();totalBytes=[long](($targets|Measure-Object bytes -Sum).Sum);
    permanentBytes=[long](($targets|Where-Object mode -eq 'permanent'|Measure-Object bytes -Sum).Sum);
    recycleBytes=[long](($targets|Where-Object mode -eq 'recycle'|Measure-Object bytes -Sum).Sum);
    retained='Source assets, formal release histories, world/backup fixtures, release-140 base, old compatibility readers, compact old Full build source/embedded inputs, logs/JSON/visual evidence';execute=[bool]$Execute}
Save-CleanupJson 'cleanup-plan.json' $plan
$plan|Select-Object totalBytes,permanentBytes,recycleBytes,execute|ConvertTo-Json
if(-not $Execute){return}
if(Test-Path -LiteralPath (Join-Path $cleanupReport 'cleanup-receipt.json')){throw 'Cleanup already attempted; inspect receipt before repeating'}
Add-Type -AssemblyName Microsoft.VisualBasic.Core
$done=[Collections.Generic.List[object]]::new();$freeBefore=(Get-PSDrive E).Free
foreach($target in $targets){
    $relative=[IO.Path]::GetRelativePath($cleanupRoot,$target.path);$null=Get-SafeCleanupItem $relative
    if($target.mode-eq 'permanent'){
        if($target.directory){Remove-Item -LiteralPath $target.path -Recurse -Force}else{Remove-Item -LiteralPath $target.path -Force}
    }elseif($target.directory){
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($target.path,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,[Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
    }else{
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($target.path,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,[Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
    }
    if(Test-Path -LiteralPath $target.path){throw "Target still exists: $($target.path)"}
    $done.Add($target);Save-CleanupJson 'cleanup-receipt.json' @{complete=$false;removed=$done.ToArray();freeBefore=$freeBefore}
}
Test-CleanupDelivery
Save-CleanupJson 'cleanup-receipt.json' @{complete=$true;removed=$done.ToArray();permanentBytes=$plan.permanentBytes;recycleBytes=$plan.recycleBytes;freeBefore=$freeBefore;freeAfter=(Get-PSDrive E).Free;deliveredAndArchivedHashesUnchanged=$true;recovery='Permanent entries rebuildable; recycle entries recoverable from Windows Recycle Bin, which has not been emptied'}
Write-Output ('Completed cleanup of '+$done.Count+' exact targets')
