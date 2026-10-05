"""deathmatch-addon round 2: a TEST-ONLY deathmatch package from the present sources, for the runtime walkthroughs
(sp_deathmatch.py / mp_deathmatch.py). Built against the core of the delivered output/ Lite package (the package
references the core by name only), packed with every file under src/ScCsgoDeathmatch/Assets. Never delivered: release
candidates come from tools/deathmatch_140.py.
Usage (Windows): ./tools/dev.ps1 python tools/MpM0/dm_testpkg.py <tag>   ->  .tmp/mp-m0-20260929/pkg/dm-<tag>.scmod
"""
import hashlib, json, shutil, subprocess, sys, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
PKG = ROOT / ".tmp/mp-m0-20260929/pkg"; WORK = ROOT / ".tmp/mp-m0-20260929/build/dm-testpkg"
LITE = ROOT / "output/[API1.9]CS武器1.4.0-轻量包.scmod"
def sha(b): return hashlib.sha256(b).hexdigest()
tag = sys.argv[1]
if WORK.exists(): shutil.rmtree(WORK)
src = WORK / "source"; shutil.copytree(ROOT / "src/ScCsgoDeathmatch", src, ignore=shutil.ignore_patterns("bin", "obj"))
refs = WORK / "refs"; refs.mkdir(parents=True)
with zipfile.ZipFile(LITE) as z: (refs / "ScCsgoKnives.dll").write_bytes(z.read("ScCsgoKnives.dll"))
(src / "ScCsgoDeathmatch.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoDeathmatch</AssemblyName><RootNamespace>Game</RootNamespace>'
    '<ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DebugType>none</DebugType><GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup>'
    '<ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>'
    f'<Reference Include="ScCsgoKnives"><HintPath>{(refs / "ScCsgoKnives.dll").as_posix()}</HintPath><Private>false</Private></Reference></ItemGroup>'
    '<ItemGroup><EmbeddedResource Include="Data/*.json"/></ItemGroup></Project>\n', "utf8")
r = subprocess.run(["dotnet", "build", str(src / "ScCsgoDeathmatch.csproj"), "-c", "Release", "--nologo", "-v:q"], capture_output=True, text=True, encoding="utf-8", errors="replace")
print(r.stdout[-3000:], r.stderr[-2000:])
if r.returncode: raise SystemExit("build failed")
dll = (src / "bin/Release/net10.0/ScCsgoDeathmatch.dll").read_bytes()
members = {"modinfo.json": (src / "modinfo.json").read_bytes(), "ScCsgoDeathmatch.dll": dll}
for f in sorted((src / "Assets").rglob("*")):
    if f.is_file(): members["Assets/" + f.relative_to(src / "Assets").as_posix()] = f.read_bytes()
target = PKG / f"dm-{tag}.scmod"; PKG.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
    for n, b in members.items(): z.writestr(n, b)
info = {"tag": tag, "file": str(target), "sha256": sha(target.read_bytes()), "assembly": sha(dll), "members": len(members), "core": sha((refs / "ScCsgoKnives.dll").read_bytes()), "kind": "TEST-ONLY"}
(PKG / f"dm-{tag}.json").write_text(json.dumps(info, indent=1), "utf8"); print(json.dumps(info, indent=1))
