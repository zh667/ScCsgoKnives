"""Isolated split source snapshot; never overwrite normal Full/Mini build outputs."""
from pathlib import Path
import hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[1];S=ROOT/'.tmp/split-lite-130-20260926'
def write(p,b):p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
hashes={}
for project,target in [('ScCsgoKnives','core'),('ScCsgoTactical','agents')]:
    for p in (ROOT/'src'/project).rglob('*'):
        rel=p.relative_to(ROOT/'src'/project)
        if not p.is_file() or rel.parts[0] in ['bin','obj','Assets'] or p.suffix not in ['.cs','.json','.vsh','.psh','.skin','.parts']:continue
        if project=='ScCsgoKnives' and p.name.endswith('.cs2.animation.json'):
            stale=S/target/'source'/rel
            assert stale.resolve().is_relative_to((S/target/'source').resolve())
            if stale.exists():stale.unlink()
            continue
        if project=='ScCsgoTactical' and p.name=='TacticalBlocks.cs':continue
        data=p.read_bytes();write(S/target/'source'/rel,data);hashes[f'{project}/{rel.as_posix()}']=hashlib.sha256(data).hexdigest()
p=ROOT/'src/ScCsgoTactical/TacticalBlocks.cs';write(S/'core/source/TacticalBlocks.cs',p.read_bytes());hashes['ScCsgoTactical/TacticalBlocks.cs']=hashlib.sha256(p.read_bytes()).hexdigest()
base='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Game</RootNamespace><GenerateDependencyFile>false</GenerateDependencyFile><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DefineConstants>SC_SPLIT</DefineConstants><DebugType>none</DebugType>{props}</PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>'''
write(S/'core/source/ScCsgoKnives.csproj',base.format(props='<AssemblyName>ScCsgoKnives</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo>',items='<Reference Include="ScCsgoResources"><HintPath>../../resources/bin/Release/net10.0/ScCsgoResources.dll</HintPath></Reference><EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh"/>').encode())
write(S/'agents/source/ScCsgoTactical.csproj',base.format(props='<AssemblyName>ScCsgoTactical</AssemblyName><AssemblyVersion>1.2.0.0</AssemblyVersion><Version>1.3.0</Version><ImplicitUsings>enable</ImplicitUsings>',items='<Reference Include="ScCsgoKnives"><HintPath>../../core/source/bin/Release/net10.0/ScCsgoKnives.dll</HintPath></Reference><EmbeddedResource Include="ArmData/*"/>').encode())
write(S/'source-hashes.json',json.dumps(hashes,indent=2).encode())
print('Staged',len(hashes),'source files')
