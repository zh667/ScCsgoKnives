"""Package derived assets after optimize_weapon_resources.py and the normal Full build.
Keeps published resource 1.0.0 immutable. Both editions use the same gameplay DLL.
"""
from pathlib import Path
import argparse
import json, zipfile, hashlib, xml.etree.ElementTree as ET, subprocess
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output';STAGE=ROOT/'.tmp/optimized-resources'
VERSION='1.9.0'

def sha(b):return hashlib.sha256(b).hexdigest()
def write(path,items):
    pending=path.with_suffix('.pending')
    with zipfile.ZipFile(pending,'w',zipfile.ZIP_DEFLATED) as z:
        for name,data in sorted(items.items()):
            e=zipfile.ZipInfo(name,(2026,1,1,0,0,0));e.compress_type=zipfile.ZIP_DEFLATED;z.writestr(e,data)
    if path.exists() and path.name.startswith('ScCsgoResources-'):
        if sha(path.read_bytes()) != sha(pending.read_bytes()):
            pending.unlink()
            raise SystemExit('Resource version already exists with different bytes; bump version.')
        pending.unlink()
    else: pending.replace(path)
def asset(n):return n.startswith(('Assets/Textures/','Assets/Models/','Assets/Audio/'))
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--full-resource',default='ScCsgoResources-1.8.0.scmod')
    parser.add_argument('--full-core',default='ScCsgoKnives-1.0.12-preview.scmod')
    args=parser.parse_args()
    # Build a separate assembly of derived geometry. Animation names/bytes are untouched.
    project=STAGE/'ScCsgoResources.csproj'
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoResources</AssemblyName>
<RootNamespace>ScCsgoResources</RootNamespace><Version>1.5.0</Version><AssemblyVersion>1.0.0.0</AssemblyVersion>
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
<Deterministic>true</Deterministic><GenerateDependencyFile>false</GenerateDependencyFile>
</PropertyGroup><ItemGroup><EmbeddedResource Include="AnimationData/*.cs2.animation.json;AnimationData/*.skin;AnimationData/*.parts">
<LogicalName>Game.AnimationData.%(Filename)%(Extension)</LogicalName></EmbeddedResource>
</ItemGroup></Project>''')
    (STAGE/'ResourceMarker.cs').write_bytes((ROOT/'src/ScCsgoResources/ResourceMarker.cs').read_bytes())
    subprocess.run(['dotnet','build',str(project),'-c','Release','--nologo','--verbosity','quiet'],check=True)
    baseline=OUT/args.full_resource
    with zipfile.ZipFile(baseline) as z:resources={n:z.read(n) for n in z.namelist()}
    before=sha(baseline.read_bytes())
    for n in list(resources):
        if n.endswith('.png'):
            target=n[:-4]+'.webp';resources[target]=(STAGE/target).read_bytes();del resources[n]
        elif n.startswith('Assets/Models/') and n.endswith('.obj'):resources[n]=(STAGE/n).read_bytes()
    resources['ScCsgoResources.dll']=(STAGE/'bin/Release/net10.0/ScCsgoResources.dll').read_bytes()
    meta=json.loads(resources['modinfo.json']);meta.update(Version=VERSION,Name='CS 武器 512 优化资源前置包',ApiVersion='1.9.3.1',
        Description='全部武器资源；512 上限 WebP Q85，特殊图集保留，模型减面与顶点整理。需配套优化版主包；与全量资源二选一。')
    resources['modinfo.json']=(json.dumps(meta,ensure_ascii=False,indent=2)+'\n').encode()
    marker=ET.Element('Resources',Version=VERSION,Format='1',Edition='Optimized512')
    for n,b in sorted(resources.items()):
        if asset(n):ET.SubElement(marker,'File',Path=n,Sha256=sha(b))
    resources['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8')
    target=OUT/f'ScCsgoResources-{VERSION}-Optimized512.scmod';write(target,resources)
    full=OUT/args.full_core
    with zipfile.ZipFile(full) as z:core={n:z.read(n) for n in z.namelist()}
    meta=json.loads(core['modinfo.json']);meta.update(Version='1.0.12-optimized-preview',Name='CS武器 · 512 优化版')
    meta['Dependencies']['zh667.ScCsgoResources']=f'[{VERSION}]'
    meta['Description']='全部武器和玩法；配套 512 WebP、模型优化资源包 1.5.0。简化材质默认开启，可在模组设置关闭。与全量版二选一安装。'
    core['modinfo.json']=(json.dumps(meta,ensure_ascii=False,indent=2)+'\n').encode()
    core['Assets/ScCsgoKnivesEdition.xml']=b'<Edition Name="Optimized512" />\n'
    corepath=OUT/'ScCsgoKnives-1.0.12-Optimized512-preview.scmod';write(corepath,core)
    assert sha(baseline.read_bytes())==before
    manifest={'fullResourceSha256':before,'sameGameplayDll':True,'packages':[]}
    with zipfile.ZipFile(full) as z:assert z.read('ScCsgoKnives.dll')==core['ScCsgoKnives.dll']
    for p in (full,baseline,corepath,target):manifest['packages'].append(dict(path=p.name,bytes=p.stat().st_size,sha256=sha(p.read_bytes())))
    manifest['coreDllSha256']=sha(core['ScCsgoKnives.dll'])
    (OUT/'c4-hud-111/delivery.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(manifest,ensure_ascii=False,indent=2),flush=True)
if __name__=='__main__':main()
