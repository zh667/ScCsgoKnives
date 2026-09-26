"""Snapshot current core source in isolation; never overwrite regular build outputs.

Source hashes record the exact inputs, including any local changes. Review these
against the release evidence before rebuilding an existing release.
"""
from pathlib import Path
import hashlib,json
r=Path(__file__).resolve().parents[1];s=r/'.tmp/minimal-130-20260926';d=s/'core';d.mkdir(parents=True,exist_ok=True)
files={}
for p in (r/'src/ScCsgoKnives').rglob('*'):
 if not p.is_file():continue
 rel=p.relative_to(r/'src/ScCsgoKnives')
 if rel.parts[0] in ['bin','obj','Assets']:continue
 if p.suffix not in ['.cs','.json','.psh','.vsh']:continue
 if p.name.endswith('.cs2.animation.json'):continue
 q=d/rel;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(p.read_bytes());files[rel.as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
staged={p.relative_to(d).as_posix() for p in d.rglob('*') if p.is_file() and p.relative_to(d).parts[0] not in ['bin','obj'] and p.suffix in ['.cs','.json','.psh','.vsh']}
assert staged==set(files),'Stale snapshot sources; use a fresh reviewed stage directory'
project='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoKnives</AssemblyName><RootNamespace>Game</RootNamespace><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateDependencyFile>false</GenerateDependencyFile><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DefineConstants>SC_MINIMAL</DefineConstants></PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/><Reference Include="ScCsgoResources"><HintPath>../resources/bin/Release/net10.0/ScCsgoResources.dll</HintPath></Reference><EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh" /></ItemGroup></Project>'''
(d/'ScCsgoKnives.csproj').write_text(project,encoding='utf8');(s/'core-source-hashes.json').write_text(json.dumps(files,indent=2),encoding='utf8')
