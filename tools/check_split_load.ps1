param([string]$GameDirectory='D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repoRoot '.tmp/split-lite-130-20260926'
$core=Join-Path $stage 'candidate/[API1.9]CS武器1.3.0-轻量包.scmod'
$addon=Join-Path $stage 'candidate/[API1.9]CS武器1.3.0-探员包.scmod'
$content=Join-Path $GameDirectory 'Content.zip'
function RunCheck([string]$label,[string[]]$commandArguments){
    & (Join-Path $PSScriptRoot 'dev.ps1') dotnet @commandArguments *> (Join-Path $stage ($label+'.log'))
    if($LASTEXITCODE -ne 0){throw "Check $label failed; see $stage/$label.log"}
    Write-Output "PASS $label"
}
Push-Location $repoRoot
$previousNmm=$env:SC_NMM_CHECK_PACKAGE
try{
    RunCheck 'load-build' @('build',(Join-Path $stage 'load-check/TacticalLoadCheck.csproj'),'-c','Release','--nologo','-v','quiet')
    $runner=Join-Path $stage 'load-check/bin/Release/net10.0/TacticalLoadCheck.dll'
    $env:SC_NMM_CHECK_PACKAGE=Join-Path $GameDirectory 'Mods/[API1.9]NekoMeko Model-v1.1.scmod'
    foreach($mode in @('both','reversed','none','nmm-only','neo-only','disabled','outdated')){
        RunCheck "official-$mode" @($runner,$repoRoot,$content,$addon,$mode,(Join-Path $stage "official-$mode.json"),$core)
    }
    $env:SC_NMM_CHECK_PACKAGE=Join-Path $repoRoot '.tmp/actor-freeze-20260926/legacy-nmm-fixture.scmod'
    RunCheck 'local-nmm' @($runner,$repoRoot,$content,$addon,'both',(Join-Path $stage 'local-nmm.json'),$core)
    RunCheck 'gate' @($runner,'--world-resource-gate',$core,(Join-Path $stage 'gate.json'))
    RunCheck 'native' @($runner,'--compat-native',$core,$content,(Join-Path $stage 'native.json'))
}finally{$env:SC_NMM_CHECK_PACKAGE=$previousNmm;Pop-Location}
