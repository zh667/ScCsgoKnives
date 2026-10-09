#Requires -Version 7.0
# Recoverable alternative after the exact permanent cleanup was denied. Never empties the Recycle Bin.
$ErrorActionPreference='Stop'
$targets=@('E:\projects\ScCsgoKnives\.tmp\dev-temp\metadata-150-20261009\check','E:\projects\ScCsgoKnives-repack-20261009\tools\MetadataCheck\obj')
$rows=@()
foreach($target in $targets){
    $item=Get-Item -LiteralPath $target -Force
    if($item.FullName-ne $target){throw 'Unexpected target'}
    $node=$item
    while($null-ne $node){if($node.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked target'};$node=$node.Parent}
    $children=@(Get-ChildItem -LiteralPath $target -Recurse -Force)
    if(@($children|Where-Object {$_.Attributes-band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Nested link'}
    if(@(Get-CimInstance Win32_Process|Where-Object {$_.ProcessId-ne $PID-and $_.CommandLine-and $_.CommandLine.Contains($target,[StringComparison]::OrdinalIgnoreCase)}).Count){throw 'Target in use'}
    $rows+=@{path=$target;bytes=[long](($children|Where-Object {!$_.PSIsContainer}|Measure-Object Length -Sum).Sum)}
}
$rows|ConvertTo-Json -Compress
Add-Type -AssemblyName Microsoft.VisualBasic.Core
$done=@()
foreach($row in $rows){
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($row.path,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,[Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
    if(Test-Path -LiteralPath $row.path){throw 'Target still exists'}
    $done+=$row
}
$receipt=@{complete=$true;recycled=$done;recovery='Recoverable from Windows Recycle Bin; it was not emptied';priorAttempt='Permanent deletion rejected before execution: blocked by policy. Not retried; this action recycles instead.'}
[IO.File]::WriteAllText('E:\projects\ScCsgoKnives\output\release-1.5.0\metadata-20261009\cleanup.json',($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
