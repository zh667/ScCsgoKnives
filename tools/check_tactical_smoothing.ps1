param([string]$GameDirectory='D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repoRoot '.tmp/crowd-smooth-20260926'
$candidate=Join-Path $stage 'candidate/[API1.9]CS武器1.3.0-全量包.scmod'
$content=Join-Path $GameDirectory 'Content.zip'
$official=Join-Path $GameDirectory 'Mods/[API1.9]NekoMeko Model-v1.1.scmod'
function ExtractMember([string]$package,[string]$member,[string]$destination){
    $archive=[IO.Compression.ZipFile]::OpenRead($package)
    try{[IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry($member),$destination,$true)}finally{$archive.Dispose()}
}
function RunCheck([string]$label,[string[]]$commandArguments){
    & (Join-Path $PSScriptRoot 'dev.ps1') dotnet @commandArguments *> (Join-Path $stage ($label+'.log'))
    if($LASTEXITCODE -ne 0){throw "Check $label failed; see $stage/$label.log"}
    Write-Output "PASS $label"
}
Push-Location $repoRoot
$oldNmm=$env:SC_NMM_CHECK_PACKAGE
try{
    ExtractMember $official 'sc-nekomekomodel.dll' (Join-Path $repoRoot 'tools/AppearanceCheck/bin/Release/net10.0/sc-nekomekomodel.dll')
    foreach($version in @('1.0.0','1.2.0')){
        ExtractMember (Join-Path $repoRoot "output/[API1.9]CS武器$version-双向兼容-全量包.scmod") 'ScCsgoKnives.dll' (Join-Path $stage "$version.dll")
    }
    ExtractMember $candidate 'ScCsgoKnives.dll' (Join-Path $stage 'ScCsgoKnives.dll')
    RunCheck 'sampling' @('tools/ActorSamplingCheck/bin/Release/net10.0/ActorSamplingCheck.dll',$repoRoot,$content,(Join-Path $stage 'sampling'))
    RunCheck 'weapons' @('tools/NpcWeaponCheck/bin/Release/net10.0/NpcWeaponCheck.dll',$repoRoot,$candidate,$content,(Join-Path $stage 'weapons'))
    RunCheck 'probe' @('tools/TacticalPerformanceCheck/bin/Release/net10.0/TacticalPerformanceCheck.dll',$repoRoot,$content,(Join-Path $stage 'probe'))
    RunCheck 'ai' @('tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll','--scmod',$candidate,'--tactical-package',$candidate,'--tactical-ai-only','--json',(Join-Path $stage 'ai.json'))
    RunCheck 'actors' @('tools/TacticalRenderCheck/bin/Release/net10.0/TacticalRenderCheck.dll',(Join-Path $repoRoot 'src/ScCsgoTactical/Assets'),$content,(Join-Path $stage 'actors'),'--actors-only')
    RunCheck 'appearance' @('tools/AppearanceCheck/bin/Release/net10.0/AppearanceCheck.dll',$repoRoot,$content,(Join-Path $stage 'appearance'))
    RunCheck 'gate' @('tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll','--world-resource-gate',$candidate,(Join-Path $stage 'gate.json'))
    RunCheck 'native' @('tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll','--compat-native',$candidate,$content,(Join-Path $stage 'native.json'))
    RunCheck 'compatibility' @('tools/CompatibilityCheck/bin/Release/net10.0/CompatibilityCheck.dll',(Join-Path $stage '1.0.0.dll'),(Join-Path $stage '1.2.0.dll'),(Join-Path $stage 'ScCsgoKnives.dll'),(Join-Path $stage 'compatibility.json'))
    $env:SC_NMM_CHECK_PACKAGE=$official
    foreach($mode in @('both','reversed','none','nmm-only','neo-only','disabled','outdated')){
        RunCheck "official-$mode" @('tools/TacticalLoadCheck/bin/Release/net10.0/TacticalLoadCheck.dll',$repoRoot,$content,$candidate,$mode,(Join-Path $stage "official-$mode.json"),$candidate)
    }
}finally{$env:SC_NMM_CHECK_PACKAGE=$oldNmm;Pop-Location}
