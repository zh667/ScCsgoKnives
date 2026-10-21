# Called through tools/dev.ps1, locally and on a clean GitHub Windows runner.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resultRoot = Join-Path $repoRoot '.tmp/ci-results'
[IO.Directory]::CreateDirectory($resultRoot) | Out-Null
$reportPath = Join-Path $resultRoot 'deathmatch.json'
# An old passing report must never stand in for a runner that did not execute.
if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }

Push-Location $repoRoot
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify tested source.' }
    & dotnet run --project tools/DeathmatchCheck/DeathmatchCheck.csproj -c Release -p:SkipScmodPackaging=true -- $reportPath
    $runnerExit = $LASTEXITCODE
    if ($runnerExit -ne 0) { throw "DeathmatchCheck failed with exit code $runnerExit." }
    if (!(Test-Path -LiteralPath $reportPath)) { throw 'DeathmatchCheck produced no report.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $cases = @($report.checks)
    if ($report.total -le 0 -or $report.total -ne $cases.Count -or $report.failed -ne 0 -or @($cases | Where-Object { $_.Ok -ne $true }).Count -ne 0) {
        throw 'Behavior report is empty, inconsistent or contains failed checks.'
    }
    $binaryRoot = Join-Path $repoRoot 'tools/DeathmatchCheck/bin/Release/net10.0'
    $identities = foreach ($name in @('DeathmatchCheck.dll', 'ScCsgoDeathmatch.dll', 'ScCsgoKnives.dll', 'Survivalcraft.dll')) {
        $path = Join-Path $binaryRoot $name
        @{ file = $name; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    @{
        sourceCommit = $sourceCommit
        githubHeadSha = $env:GITHUB_SHA
        enginePackage = 'SurvivalcraftAPI.Survivalcraft 1.9.3.1'
        runner = 'tools/DeathmatchCheck/DeathmatchCheck.csproj'
        total = $report.total
        failed = $report.failed
        binaries = @($identities)
        scope = 'Offline gameplay rules; not real-game, multiplayer transport, Android or release compatibility acceptance.'
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultRoot 'identity.json') -Encoding utf8
    Write-Host "Behavior checks passed: $($report.total); source $sourceCommit"
} finally {
    Pop-Location
}
