# Run project tooling with temporary files on the project drive, without changing
# machine/user environment variables. Example: ./tools/dev.ps1 dotnet build ...
$ErrorActionPreference = 'Stop'
if ($args.Count -eq 0) { throw 'Usage: ./tools/dev.ps1 <command> <arguments>' }
$devCommand = [string]$args[0]
$devArguments = @($args | Select-Object -Skip 1)
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectTempRoot = Join-Path $projectRoot '.tmp/dev-temp'
if ($env:SC_CSGO_DEV_TEMP) { $projectTempRoot = [IO.Path]::GetFullPath($env:SC_CSGO_DEV_TEMP) }
[IO.Directory]::CreateDirectory($projectTempRoot) | Out-Null
$previousEnvironment = @{}
foreach ($name in @('TEMP','TMP','TMPDIR','SC_CSGO_DEV_TEMP')) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $projectTempRoot, 'Process')
}
$commandExitCode = 0
try {
    Push-Location $projectRoot
    try {
        & $devCommand @devArguments
        $commandExitCode = $LASTEXITCODE
    } finally { Pop-Location }
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
exit $commandExitCode
