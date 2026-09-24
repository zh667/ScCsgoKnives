param(
    [string]$Version120 = 'D:\下载\[API1.9]CS武器1.2.0-全量版.scmod',
    [string]$Version100 = 'D:\下载\[API1.9]CS武器1.0- 作者ZH667.scmod'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$legacyRoot = Join-Path $repo '.tmp/balance-implementation-20260924/legacy'
$expected = @('b370ad7ff6c7cae0ec4abe584d8389bea813eb790b29dc3c2a4772adca184c95','9c700bc8133942ca4fe0ce7e9604296e7acd1533f684aaa5dd4bfda252e84424')
$packages = @($Version120, $Version100)
$manifests = @()
for ($i = 0; $i -lt $packages.Count; $i++) {
    $path = [IO.Path]::GetFullPath($packages[$i])
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$i]) { throw "Historical package hash mismatch: $path" }
    $target = Join-Path $legacyRoot ([string]$i)
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = @($zip.Entries | Where-Object { $_.Name -eq 'ScCsgoKnives.dll' })
        if ($entry.Count -ne 1) { throw "Expected exactly one historical core DLL" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], (Join-Path $target 'ScCsgoKnives.dll'), $true)
        $metadataEntry = @($zip.Entries | Where-Object { $_.Name -eq 'modinfo.json' })
        if ($metadataEntry.Count -ne 1) { throw 'Expected one modinfo.json' }
        $reader = [IO.StreamReader]::new($metadataEntry[0].Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        $manifests += @{ file = [IO.Path]::GetFileName($path); sha256 = $expected[$i]; metadata = $metadata; dll = 'ScCsgoKnives.dll' }
    } finally { $zip.Dispose() }
}
[IO.File]::WriteAllText((Join-Path $legacyRoot 'packages.json'), ($manifests | ConvertTo-Json -Depth 32), [Text.UTF8Encoding]::new($false))
& (Join-Path $repo 'tools/dev.ps1') dotnet run --project tools/BalanceCheck -c Release '-p:SkipScmodPackaging=true' -- .tmp/balance-check.json $legacyRoot
exit $LASTEXITCODE
