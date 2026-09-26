param([string]$GameDirectory='D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$stage=Join-Path $repoRoot '.tmp/split-lite-130-20260926'
$content=Join-Path $GameDirectory 'Content.zip'
$core=Join-Path $stage 'candidate/[API1.9]CS武器1.3.0-轻量包.scmod'
$addon=Join-Path $stage 'candidate/[API1.9]CS武器1.3.0-探员包.scmod'
$fixture=Join-Path $stage 'fixture'
function RunCheck([string]$label,[string[]]$commandArguments){
    & (Join-Path $PSScriptRoot 'dev.ps1') dotnet @commandArguments *> (Join-Path $stage ($label+'.log'))
    if($LASTEXITCODE -ne 0){throw "Check $label failed; see $stage/$label.log"}
    Write-Output "PASS $label"
}
Push-Location $repoRoot
try{
    RunCheck 'resources' @((Join-Path $stage 'resource-check/bin/Release/net10.0/SplitResourceCheck.dll'),$core,(Join-Path $stage 'original-lite.scmod'),$content,(Join-Path $stage 'resources-native'))
    RunCheck 'npc' @((Join-Path $stage 'runtime/NpcWeaponCheck/NpcWeaponCheck.dll'),$fixture,(Join-Path $stage 'fixture-union.scmod'),$content,(Join-Path $stage 'npc'),(Join-Path $stage 'npc-rebake'))
    RunCheck 'appearance' @((Join-Path $stage 'runtime/AppearanceCheck/AppearanceCheck.dll'),$fixture,$content,(Join-Path $stage 'appearance'))
    foreach($role in @('ct','t')){
        RunCheck "actor-$role" @((Join-Path $stage 'runtime/ActorLoadCheck/ActorLoadCheck.dll'),'verify',(Join-Path $fixture "src/ScCsgoTactical/Assets/Models/ScCsgoTactical/$role.glb"),(Join-Path $fixture "src/ScCsgoTactical/Assets/Animations/ScCsgoTactical/$role.scanim"),(Join-Path $stage "actor-$role.json"))
    }
    $compat=Join-Path $stage 'compat-runner/bin/Release/net10.0/CompatibilityCheck.dll'
    $coreDll=Join-Path $stage 'compat-baselines/split/ScCsgoKnives.dll'
    RunCheck 'compatibility-historical' @($compat,(Join-Path $repoRoot '.tmp/lite-smooth-20260926/1.0.0.dll'),(Join-Path $repoRoot '.tmp/lite-smooth-20260926/1.2.0.dll'),$coreDll,(Join-Path $stage 'compatibility-historical.json'))
    RunCheck 'compatibility-current' @($compat,(Join-Path $stage 'compat-baselines/full/ScCsgoKnives.dll'),(Join-Path $stage 'compat-baselines/lite/ScCsgoKnives.dll'),$coreDll,(Join-Path $stage 'compatibility-current.json'))
    RunCheck 'compatibility-mini' @($compat,(Join-Path $stage 'compat-baselines/lite/ScCsgoKnives.dll'),(Join-Path $stage 'compat-baselines/mini/ScCsgoKnives.dll'),$coreDll,(Join-Path $stage 'compatibility-mini.json'))
    $native=Join-Path $stage 'load-check/bin/Release/net10.0/TacticalLoadCheck.dll'
    RunCheck 'zip-core' @($native,'--archive-identical',(Join-Path $stage 'core-before-compression.scmod'),$core,(Join-Path $stage 'zip-core.json'))
    RunCheck 'zip-agents' @($native,'--archive-identical',(Join-Path $stage 'agents-before-compression.scmod'),$addon,(Join-Path $stage 'zip-agents.json'))
}finally{Pop-Location}
