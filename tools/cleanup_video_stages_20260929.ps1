#Requires -Version 7.0
param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.tmp/video-fix-140-20260929'))
$reportRoot = Join-Path $projectRoot '.tmp/cleanup-video-stages-20260929'
$tags = @('s0-01','r1-01','r1-02','r3-01','r2-01','r2-02','r2-03','r2-04','r2-05','r2-06')
$directories = @('candidate','lite','full','assets-full','resources','tools','tree','baseline')
$expected = @{
    '[API1.9]CS武器1.4.0-全量包.scmod' = '9219228aa6e8203cf51c850dde2e9ad354497949bdb5170fd4e47191e0be6c66'
    '[API1.9]CS武器1.4.0-轻量包.scmod' = '6d9ed6ea31e03a9b43411dd54b5fe53c9f93ad967ab455a4aa7746a41a00f319'
    '[API1.9]CS武器1.4.0-探员包.scmod' = 'e92811fa9d9d728df949793669ece2d331fdac9847136f2c43e417522bc4fa0f'
}
function Verify-Delivery {
    foreach ($entry in $expected.GetEnumerator()) {
        $path = Join-Path $projectRoot ('output/' + $entry.Key)
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) {
            throw "Delivery differs from accepted r2-06: $path"
        }
    }
}
function Assert-ScopedPath([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($stageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target escaped the task stage: $resolved"
    }
    $node = Get-Item -LiteralPath $resolved -Force
    while ($node -and $node.FullName.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if ($node.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point in target chain: $($node.FullName)" }
        $node = if ($node.PSIsContainer) {$node.Parent} else {$node.Directory}
    }
}
Verify-Delivery
$targets = [Collections.Generic.List[object]]::new()
foreach ($tag in $tags) {
    $stage = Join-Path $stageRoot $tag
    if (-not (Test-Path -LiteralPath $stage -PathType Container)) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'source-hashes.json'))) { throw "Missing source receipt: $tag" }
    foreach ($name in $directories) {
        $path = Join-Path $stage $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Assert-ScopedPath $path
        $items = @(Get-ChildItem -LiteralPath $path -Recurse -Force)
        if ($items | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}) { throw "Nested link: $path" }
        $bytes = ($items | Where-Object {-not $_.PSIsContainer} | Measure-Object Length -Sum).Sum
        $targets.Add([pscustomobject]@{Path=$path;Bytes=[long]$bytes;Directory=$true})
    }
    foreach ($name in @('air','clips')) {
        $folder = Join-Path $stage $name
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        Assert-ScopedPath $folder
        foreach ($file in Get-ChildItem -LiteralPath $folder -File) {
            if ($file.Extension -notin @('.glb','.scanim')) { continue }
            Assert-ScopedPath $file.FullName
            $targets.Add([pscustomobject]@{Path=$file.FullName;Bytes=$file.Length;Directory=$false})
        }
    }
}
# Build sources are outside this disposable stage: release-140, original dense actors and raw exports stay intact.
$keep = @('.tmp/release-140-20260928','.tmp/actor-freeze-20260926/before','.tmp/cs2-companions-audit-20260920/export')
foreach ($relative in $keep) { if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relative))) { throw "Required next-build input missing: $relative" } }
$token = [IO.File]::ReadAllText('E:\Develop\AgentBridge\private\token').Trim()
$health = Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:18765' -ContentType 'application/json' -Body '{"op":"health","args":{}}' -Headers @{Authorization=('Bearer '+$token)} -TimeoutSec 20
if (-not $health.ok -or @($health.result.running_jobs).Count -ne 0) { throw 'Worker not idle; refusing cleanup' }
$active = @(Get-CimInstance Win32_Process | Where-Object {
    ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($stageRoot,[StringComparison]::OrdinalIgnoreCase)) -or
    ($_.CommandLine -and $_.CommandLine.Contains($stageRoot))
})
if ($active.Count) { throw 'A process still refers to a cleanup stage' }
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$plan = @{Task='video-feedback-20260929';Targets=@($targets.ToArray());TotalBytes=[long](($targets|Measure-Object Bytes -Sum).Sum);KeepBuildInputs=$keep;KeepEvidence='Root JSON, logs, throw/motion/ui/hotspots renders and reports in every round';Delivered=$expected;Execute=[bool]$Execute}
$plan | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reportRoot 'plan.json') -Encoding utf8
if (-not $Execute) { $plan | Select-Object TotalBytes,Execute,KeepEvidence | ConvertTo-Json; exit 0 }
$before = (Get-PSDrive -Name E).Free
$removed = [Collections.Generic.List[string]]::new()
foreach ($target in $targets) {
    Assert-ScopedPath $target.Path
    if ($target.Directory) { Remove-Item -LiteralPath $target.Path -Recurse -Force }
    else { Remove-Item -LiteralPath $target.Path -Force }
    $removed.Add($target.Path)
    @{Removed=@($removed.ToArray());Completed=$false} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $reportRoot 'receipt.json') -Encoding utf8
}
Verify-Delivery
$receipt = @{Removed=@($removed.ToArray());Completed=$true;RemovedLogicalBytes=$plan.TotalBytes;FreeBefore=$before;FreeAfter=(Get-PSDrive -Name E).Free;DeliveryHashesUnchanged=$true;Recovery='Permanent deletion of reproducible intermediates; evidence and original build inputs retained';Utc=[DateTime]::UtcNow.ToString('o')}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportRoot 'receipt.json') -Encoding utf8
$receipt | Select-Object Completed,RemovedLogicalBytes,FreeBefore,FreeAfter,DeliveryHashesUnchanged,Recovery | ConvertTo-Json
