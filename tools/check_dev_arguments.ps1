param([Parameter(Mandatory=$true)][string]$Report)
$ErrorActionPreference = 'Stop'
$prior = @($env:TEMP, $env:TMP, $env:TMPDIR)
$expected = @('-c', '-p:SkipScmodPackaging=true', '-v:quiet', 'E:/有 空格/a=b.dll', '-p:DefineConstants=A;B', 'literal$and`tick')
$direct = & "$PSScriptRoot/dev.ps1" python tools/argv_probe.py -c '-p:SkipScmodPackaging=true' '-v:quiet' 'E:/有 空格/a=b.dll' '-p:DefineConstants=A;B' 'literal$and`tick'
$directResult = $direct | ConvertFrom-Json
$argv = @('python', 'tools/argv_probe.py') + $expected
$splatted = & "$PSScriptRoot/dev.ps1" @argv
$splatResult = $splatted | ConvertFrom-Json
& "$PSScriptRoot/dev.ps1" python -c 'import sys;sys.exit(23)'
$propagatedExit = $LASTEXITCODE
$checks = @(
    @{name='quoted direct invocation';ok=([string]::Join('|', $directResult.argv) -ceq [string]::Join('|', $expected))},
    @{name='literal argument array';ok=([string]::Join('|', $splatResult.argv) -ceq [string]::Join('|', $expected))},
    @{name='scoped temporary environment';ok=($directResult.temp -eq $env:SC_CSGO_DEV_TEMP -and $directResult.tmp -eq $directResult.temp -and $directResult.tmpdir -eq $directResult.temp)},
    @{name='caller environment restored';ok=([string]::Join('|', $prior) -ceq [string]::Join('|', @($env:TEMP,$env:TMP,$env:TMPDIR)))}
    @{name='native nonzero exit code preserved';ok=($propagatedExit -eq 23)}
)
$failed = @($checks | Where-Object { !$_.ok }).Count
$result = @{checks=$checks;failed=$failed;received=@($directResult,$splatResult);wrapperSha256=(Get-FileHash -LiteralPath "$PSScriptRoot/dev.ps1").Hash;powerShell=$PSVersionTable.PSVersion.ToString()}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Report -Encoding utf8
Write-Output "dev.ps1 arguments: $($checks.Count - $failed)/$($checks.Count)"
exit $(if ($failed) { 1 } else { 0 })
