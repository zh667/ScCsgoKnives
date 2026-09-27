"""Isolated capacity builds. Copy verified current deliveries; never install or publish here."""
import hashlib,json,shutil,subprocess,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/capacity-fix-20260927'
def sha(b):return hashlib.sha256(b).hexdigest()
def main():
    STAGE.mkdir(parents=True,exist_ok=True)
    baselines={}
    names=[f'[API1.9]CS武器1.3.0-{label}包.scmod' for label in ['轻量','全量','探员']]
    names += [f'[API1.9]CS武器{v}-双向兼容-{label}包.scmod' for v in ['1.0.0','1.2.0'] for label in ['轻量','全量']]
    for name in names:
        src=ROOT/'output'/name;dst=STAGE/'baseline'/name;dst.parent.mkdir(exist_ok=True)
        if not dst.exists():shutil.copyfile(src,dst)
        assert sha(src.read_bytes())==sha(dst.read_bytes()),'Delivery changed since capacity staging'
        baselines[name]=dict(bytes=dst.stat().st_size,sha256=sha(dst.read_bytes()))
    builds={}
    for version,label in [('1.0.0','old100'),('1.2.0','old120'),('1.3.0','old130')]:
        package=(f'[API1.9]CS武器{version}-双向兼容-全量包.scmod' if version!='1.3.0' else '[API1.9]CS武器1.3.0-全量包.scmod')
        folder=STAGE/label;folder.mkdir(exist_ok=True)
        with zipfile.ZipFile(STAGE/'baseline'/package) as z:
            for name in ['ScCsgoKnives.dll','ScCsgoResources.dll']:(folder/name).write_bytes(z.read(name))
    for role,label in [('lite','轻量'),('full','全量')]:
        previous=ROOT/'.tmp/feedback-fixes-130-20260927'/('full/core/source' if role=='full' else 'core/source')
        dest=STAGE/role/'source'
        shutil.copytree(previous,dest,dirs_exist_ok=True,ignore=shutil.ignore_patterns('bin','obj'))
        # Preserve the split-specific stable type shims while updating shared production sources.
        for path in (ROOT/'src/ScCsgoKnives').rglob('*.cs'):
            rel=path.relative_to(ROOT/'src/ScCsgoKnives')
            if any(p in ['bin','obj'] for p in rel.parts):continue
            target=dest/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,target)
        refs=STAGE/role/'refs';refs.mkdir(exist_ok=True)
        with zipfile.ZipFile(STAGE/'baseline'/f'[API1.9]CS武器1.3.0-{label}包.scmod') as z:
            for n in ['ScCsgoResources.dll','ScCsgoResourceCodec.dll']:
                if n in z.namelist():(refs/n).write_bytes(z.read(n))
        codec='<Reference Include="ScCsgoResourceCodec"><HintPath>../refs/ScCsgoResourceCodec.dll</HintPath></Reference>' if role=='lite' else ''
        constants='SC_SPLIT;SC_RESOURCE_ZSTD' if role=='lite' else ''
        csproj=f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Game</RootNamespace><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DefineConstants>{constants}</DefineConstants><DebugType>none</DebugType><AssemblyName>ScCsgoKnives</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/><Reference Include="ScCsgoResources"><HintPath>../refs/ScCsgoResources.dll</HintPath></Reference>{codec}<EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh"/></ItemGroup></Project>'
        (dest/'ScCsgoKnives.csproj').write_text(csproj,'utf8');builds[role]=str(dest/'ScCsgoKnives.csproj')
    for v in ['1.0.0','1.2.0']:
        subprocess.run(['python',str(ROOT/'tools/stage_compatibility_sources.py'),v],cwd=ROOT,check=True)
        src=ROOT/'.tmp/compatibility'/v;dest=STAGE/v
        shutil.copytree(src,dest,dirs_exist_ok=True,ignore=shutil.ignore_patterns('bin','obj'))
        builds[v]=str(dest/'src/ScCsgoKnives/ScCsgoKnives.csproj')
    (STAGE/'baselines.json').write_text(json.dumps(baselines,ensure_ascii=False,indent=2),'utf8')
    (STAGE/'builds.json').write_text(json.dumps(builds,indent=2),'utf8')
    print(json.dumps(builds,indent=2))
if __name__=='__main__':main()
