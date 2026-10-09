#Requires -Version 7.0
param([switch]$Execute)
$ErrorActionPreference='Stop'
$cleanupRoot='E:\projects\ScCsgoKnives'
$cleanupWorktree='E:\projects\ScCsgoKnives-dm-settings-20261009'
$cleanupStage=Join-Path $cleanupRoot '.tmp\dev-temp\dm-settings-release-20261009'
$cleanupEvidence=Join-Path $cleanupRoot 'output\release-1.5.0\dm-settings-20261009'
$cleanupManifest=Get-Content -Raw -LiteralPath (Join-Path $cleanupEvidence 'manifest.json')|ConvertFrom-Json
function Check-Delivery{
    foreach($p in $cleanupManifest.delivery){
        if((Get-FileHash -LiteralPath $p.path -Algorithm SHA256).Hash.ToLowerInvariant()-ne $p.sha256){throw 'Delivered package changed'}
        if((Get-FileHash -LiteralPath $p.archived -Algorithm SHA256).Hash.ToLowerInvariant()-ne $p.previousSha256){throw 'Archived package changed'}
    }
}
function Save-Cleanup([string]$Name,$Data){[IO.File]::WriteAllText((Join-Path $cleanupEvidence $Name),($Data|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
Check-Delivery
$targets=[Collections.Generic.List[object]]::new()
function Add-Target([string]$Path,[string]$Scope){
    if(-not (Test-Path -LiteralPath $Path)){return}
    $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($Scope+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Target escaped scope: $full"}
    $item=Get-Item -LiteralPath $full -Force;$node=$item
    while($null-ne $node){if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked target'};$node=$node.Parent}
    $children=@(Get-ChildItem -LiteralPath $full -Recurse -Force)
    if(@($children|Where-Object {$_.Attributes-band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Nested link'}
    $files=@($children|Where-Object {!$_.PSIsContainer})
    $targets.Add(@{path=$full;files=$files.Count;bytes=[long](($files|Measure-Object Length -Sum).Sum)})
}
foreach($name in @('baseline','candidate','full','lite','tree','dm','resources','net','old130','old140','old150','historical-readers','settings-check','settings-check-lite','metadata-check')){
    Add-Target (Join-Path $cleanupStage $name) $cleanupStage
}
Add-Target (Join-Path $cleanupRoot '.tmp\dev-temp\settings-fix-20261009') (Join-Path $cleanupRoot '.tmp\dev-temp')
foreach($project in @('src\ScCsgoKnives','src\ScCsgoTactical','src\ScCsgoDeathmatch','src\ScCsgoVoice','tools\DmSettingsCheck','tools\MetadataCheck')){
    foreach($dir in @('bin','obj')){Add-Target (Join-Path $cleanupWorktree ($project+'\'+$dir)) (Join-Path $cleanupWorktree $project)}
}
foreach($path in @('.tmp/release-140-20260928','.tmp/capacity-fix-20260927/1.0.0/src/ScCsgoKnives/bin/Release/net10.0','.tmp/capacity-fix-20260927/1.2.0/src/ScCsgoKnives/bin/Release/net10.0','.tmp/mp-m0-20260929/refs/mp')){
    if(-not(Test-Path -LiteralPath (Join-Path $cleanupRoot $path))){throw "Required input absent: $path"}
}
$active=Get-CimInstance Win32_Process
foreach($target in $targets){
    if(@($active|Where-Object {$_.ProcessId-ne $PID-and $_.CommandLine-and $_.CommandLine.Contains($target.path,[StringComparison]::OrdinalIgnoreCase)}).Count){throw "Target in use: $($target.path)"}
}
$bridgeToken=[IO.File]::ReadAllText('E:\Develop\AgentBridge\private\token').Trim()
$health=Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18765' -ContentType 'application/json' -Body '{"op":"health","args":{}}' -Headers @{Authorization=('Bearer '+$bridgeToken)} -TimeoutSec 20
if(-not $health.ok-or @($health.result.running_jobs).Count-ne 0){throw 'Worker busy'}
$plan=@{date='2026-10-09';targets=$targets.ToArray();totalBytes=[long](($targets|Measure-Object bytes -Sum).Sum);
    retained='source worktree and Git, logs/JSON/native UI results, original assets, official release histories and build/compatibility inputs';execute=[bool]$Execute}
Save-Cleanup 'cleanup-plan.json' $plan
$plan|Select-Object totalBytes,execute|ConvertTo-Json
if(-not $Execute){return}
if(Test-Path -LiteralPath (Join-Path $cleanupEvidence 'cleanup.json')){throw 'Prior receipt exists; inspect instead of repeating'}
$removed=[Collections.Generic.List[object]]::new();$free=(Get-PSDrive E).Free
foreach($target in $targets){
    Remove-Item -LiteralPath $target.path -Recurse -Force
    if(Test-Path -LiteralPath $target.path){throw 'Target still exists'}
    $removed.Add($target);Save-Cleanup 'cleanup.json' @{complete=$false;removed=$removed.ToArray()}
}
Check-Delivery
Save-Cleanup 'cleanup.json' @{complete=$true;removed=$removed.ToArray();totalBytes=$plan.totalBytes;freeBefore=$free;freeAfter=(Get-PSDrive E).Free;
    deliveredAndArchivedHashesUnchanged=$true;recovery='Permanent deletion of reproducible task intermediates; no Recycle Bin claim. Sources, fixtures, official archives and evidence retained.'}
Write-Output ('Cleaned '+$removed.Count+' task targets')
