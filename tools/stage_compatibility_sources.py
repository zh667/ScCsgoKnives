"""Rebuild historical code with an explicit shared persistence backport. Never touch original archives."""
import argparse,hashlib,io,json,subprocess,tarfile,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SOURCES={
 '1.0.0':('cf6907f',Path(r'D:\下载\[API1.9]CS武器1.0- 作者ZH667.scmod'),'9c700bc8133942ca4fe0ce7e9604296e7acd1533f684aaa5dd4bfda252e84424'),
 '1.2.0':('0f78a1b',Path(r'D:\下载\[API1.9]CS武器1.2.0-全量版.scmod'),'b370ad7ff6c7cae0ec4abe584d8389bea813eb790b29dc3c2a4772adca184c95')}
SHARED=['ScGunRegistry','ScGunGrowth','ScGunGrowthMigration','ScGunGrowthService','ScGunSaveGuard','ScGunSchemaUpgrade',
        'ScGunLoadIntegrity','ScGunTravel','ScGunHolders','ScGunMutation','ScInventoryIdentity','ScSushiInventory',
        'ScInventoryTransaction','ScGunRecovery','ScCompatibility','ScGun0282Migration','ScGhoulTestBridge']
def main():
    p=argparse.ArgumentParser();p.add_argument('version',choices=SOURCES);a=p.parse_args()
    ref,package,digest=SOURCES[a.version];assert hashlib.sha256(package.read_bytes()).hexdigest()==digest
    stage=ROOT/'.tmp/compatibility'/a.version;stage.mkdir(parents=True,exist_ok=True)
    src=stage/'src/ScCsgoKnives'
    if not src.exists():
        paths=subprocess.check_output(['git','ls-tree','--name-only',f'{ref}:src/ScCsgoKnives'],cwd=ROOT,text=True).splitlines()
        paths=['src/ScCsgoKnives/'+n for n in paths if n not in ('Assets','bin','obj')]
        archive=subprocess.check_output(['git','archive',ref,*paths],cwd=ROOT)
        with tarfile.open(fileobj=io.BytesIO(archive)) as t:t.extractall(stage,filter='data')
    refs=stage/'refs';refs.mkdir(exist_ok=True)
    for name in ('LICENSE','ASSET_SOURCES.md','THIRD_PARTY_NOTICES.md'):
        (stage/name).write_bytes(subprocess.check_output(['git','show',ref+':'+name],cwd=ROOT))
    with zipfile.ZipFile(package) as z:
        for n in ('ScCsgoResources.dll','ScCsgoKnives.dll'):(refs/n).write_bytes(z.read(n))
        metadata=json.loads(z.read('modinfo.json'))
    changes=[]
    for name in SHARED:
        path=ROOT/f'src/ScCsgoKnives/World/{name}.cs';assert path.exists(),name
        data=path.read_text('utf8');(src/'World'/path.name).write_text(data,'utf8');changes.append(str(path.relative_to(ROOT)))
    # Keep unavailable later item identities registered. These are inert carriers, not substitute guns.
    later=[('ScChickenEggBlock','CS小鸡生成蛋',40),('ScTacticalShieldBlock','防爆盾',1),('ScTacticalBeaconBlock','招募信标/战术维修包',10),('ScTacticalDefuserBlock','拆弹钳',1),('ScTacticalSquadBlock','敌队挑战信标',1)]
    if a.version=='1.0.0':later.append(('ScC4Block','C4定时炸弹',10))
    definitions='namespace Game;\n'
    for name,label,stack in later:
        definitions+='public sealed class '+name+' : ScCompatibilityItemBlock { public '+name+'(){ DefaultDisplayName="'+label+'"; MaxStacking='+str(stack)+'; } }\n'
    (src/'World/ScCompatibilityLegacyItems.cs').write_text(definitions,'utf8')
    # The persistent growth/identity curve is shared. Preserve the historical cadence endpoints and recharge
    # baseline rather than replacing old weapon behavior with the latest balance.
    growth=src/'World/ScGunGrowth.cs';s=growth.read_text('utf8')
    start=s.index('    public static float FireRateMultiplier(int variant, int level)')
    end=s.index('    public static bool IsAutoSniper',start)
    rate=('1f + .05f * Tier(level, 1) + .05f * Tier(level, 2) + .075f * Tier(level, 3) + .075f * Tier(level, 4)'
          if a.version=='1.0.0' else 'IsSniper(variant) ? 1f + .05f * Tier(level, 1) + .05f * Tier(level, 2) + .075f * Tier(level, 3) + .075f * Tier(level, 4) : 1f + .01f * Clamp(level)')
    s=s[:start]+'    public static float FireRateMultiplier(int variant, int level) => '+rate+';\n'+s[end:]
    s=s.replace('return spec.RechargeSeconds / (1f + .65f * (1f / factor - 1f));','return spec.RechargeSeconds * factor;')
    if a.version=='1.0.0':
        s=s.replace('    public static float SkinDamageMultiplier', '    public static float ShotInterval(float baseSeconds, int level) => ShotInterval(-1, baseSeconds, level);\n    public static float SkinDamageMultiplier')
    growth.write_text(s,'utf8')
    # Historical 1.0 originally accepted only Full; both new editions must enter worlds.
    if a.version=='1.0.0':
        required=src/'World/ScRequiredResources.cs';s=required.read_text('utf8')
        s=s.replace('(string)marker.Attribute("Edition")!="Full"', '(string)marker.Attribute("Edition") is not ("Full" or "Optimized512")')
        s=s.replace('请安装匹配的 CS 武器完整资源前置包 "+Version+"，不要只安装主包。', '请安装匹配的 CS 武器 "+Version+" 全量包或512轻量包，内置资源缺失或不兼容。')
        required.write_text(s,'utf8')
        policy=src/'Rendering/ScResourcePolicy.cs';s=policy.read_text('utf8')
        policy.write_text(s.replace('edition is "Lite" or "Mini";', 'edition is "Lite" or "Mini" or "Optimized512";'),'utf8')
    # Add the current pre-play integrity/backup calls without replacing historical input/render hooks.
    loader=src/'Mod/ScCsgoKnivesModLoader.cs';s=loader.read_text('utf8')
    old='try { ScGunSchemaUpgrade.BeforeLoad(project, world); ScGunTravel.BeforeLoad(project,world); }'
    replacement='try { string backup = ScGunSchemaUpgrade.BeforeLoad(project, world); ScGunTravel.BeforeLoad(project,world); string integrity = ScGunLoadIntegrity.BeforeLoad(project,world,backup); ScGunSchemaUpgrade.BeforeReleaseLoad(project,world,backup ?? integrity); }'
    assert old in s or replacement in s
    loader.write_text(s.replace(old,replacement),'utf8')
    # Preserve optional protection metadata through the historical subsystem's own save boundary.
    subsystem=src/'World/SubsystemScGunBlockBehavior.cs';s=subsystem.read_text('utf8')
    if 'm_compatProtection' not in s:
        s=s.replace('ScGunRegistry m_registry;','ScGunRegistry m_registry;\n    ValuesDictionary m_compatProtection, m_compatRelease;')
        s=s.replace('ScGunSaveGuard.Validate(valuesDictionary);','m_compatProtection=valuesDictionary.GetValue<ValuesDictionary>(ScGunLoadIntegrity.ProtectionKey,null);\n        m_compatRelease=valuesDictionary.GetValue<ValuesDictionary>(ScGunSchemaUpgrade.ReleaseMarker,null);\n        ScGunSaveGuard.Validate(valuesDictionary);')
        s=s.replace('base.Save(valuesDictionary);','base.Save(valuesDictionary);\n        if(m_compatProtection is not null) valuesDictionary.SetValue(ScGunLoadIntegrity.ProtectionKey,m_compatProtection);\n        if(m_compatRelease is not null) valuesDictionary.SetValue(ScGunSchemaUpgrade.ReleaseMarker,m_compatRelease);')
    s=s.replace('原世界已备份。','备份由玩家自行管理。')
    subsystem.write_text(s,'utf8')
    ui=src/'World/ScUiSettings.cs';s=ui.read_text('utf8')
    original='WriteAtomic(Storage.GetSystemPath(Path), JsonSerializer.SerializeToUtf8Bytes(file, s_json));'
    s=s.replace(original,'WriteAtomic(Storage.GetSystemPath(Path), ScCompatibility.PreserveUiSettings(JsonSerializer.SerializeToUtf8Bytes(file, s_json),Storage.GetSystemPath(Path),ScGunFunctions.All));')
    ui.write_text(s,'utf8')
    csproj=src/'ScCsgoKnives.csproj';s=csproj.read_text('utf8').replace('Version="1.9.2.1"','Version="1.9.3.1"')
    s=s.replace('<ProjectReference Include="../ScCsgoResources/ScCsgoResources.csproj" />','<Reference Include="ScCsgoResources"><HintPath>../../refs/ScCsgoResources.dll</HintPath></Reference>')
    s=s.replace('<Target Name="PostBuild" AfterTargets="PostBuildEvent">','<Target Name="PostBuild" AfterTargets="PostBuildEvent" Condition="\'$(SkipScmodPackaging)\' != \'true\'">')
    csproj.write_text(s,'utf8')
    metadata['Version']=a.version;metadata['Name']+=' · 双向兼容修订';metadata['ApiVersion']='1.9.3.1'
    (src/'modinfo.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n','utf8')
    (stage/'backport.json').write_text(json.dumps(dict(source_commit=ref,source_package_sha256=digest,profile=a.version,shared_sources=changes),indent=2)+'\n','utf8')
    print(src)
if __name__=='__main__':main()
