"""Stage lossless 1.3.0 codec release without changing other edition outputs."""
from pathlib import Path
import concurrent.futures, hashlib, io, json, shutil, struct, sys, urllib.request, zipfile

ROOT = Path(__file__).resolve().parents[1]
S = ROOT / ".tmp/codec-release-130-20260927"
RESEARCH = ROOT / ".tmp/resource-codecs-20260926"
COMMIT = "2cd0c019693bc786a5fe5c3be94e107b24e7267e"
SOURCE_SHA = "7c2f6dc348f9ba4017e858f40b2c32f570148a8a4d98e96a74896c9723d41916"
BASELINES = {
    "core": ("轻量", "2d4cfc231c7a4e419eb3e7332fbf6b89d1fdcc0601e2c174d0c60b2543c43ea3"),
    "agents": ("探员", "9c430eecdbfd44ee501cd75352f3086c26ad98e6cda210bccc120ffac6c5d2db"),
}
sys.path[:0] = [str(RESEARCH / "deps"), str(ROOT / ".tmp/optimization-deps")]
import zstandard as zstd

def sha(b): return hashlib.sha256(b).hexdigest()
def write(p, b):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(b)
def dump(p, data): write(p, (json.dumps(data, ensure_ascii=False, indent=2)+"\n").encode())
def envelope(raw):
    assert 0 < len(raw) <= 64 * 1024 * 1024
    packed = zstd.ZstdCompressor(level=19).compress(raw)
    assert zstd.ZstdDecompressor().decompress(packed) == raw
    return b"SCZSTD01" + struct.pack("<II", len(raw), len(packed)) + hashlib.sha256(raw).digest() + packed

def encode(row):
    raw = (RESEARCH / row["path"]).read_bytes()
    assert sha(raw) == row["sha256"]
    target = S / ("resources/AnimationData" if row["owner"] == "core" else "encoded") / row["name"]
    encoded = target.read_bytes() if target.exists() else envelope(raw)
    assert encoded[:8] == b"SCZSTD01" and encoded[16:48] == hashlib.sha256(raw).digest()
    assert zstd.ZstdDecompressor().decompress(encoded[48:]) == raw
    write(target, encoded)
    return dict(name=row["name"], owner=row["owner"], group=row["group"],
                rawBytes=len(raw), encodedBytes=len(encoded), rawSha256=sha(raw),
                sha256=sha(encoded), path=target.relative_to(S).as_posix())

def main():
    S.mkdir(parents=True, exist_ok=True)
    archive = S / "ZstdSharp-source.zip"
    if not archive.exists():
        write(archive, urllib.request.urlopen(f"https://codeload.github.com/oleg-st/ZstdSharp/zip/{COMMIT}", timeout=60).read())
    assert sha(archive.read_bytes()) == SOURCE_SHA
    with zipfile.ZipFile(archive) as z:
        for info in z.infolist():
            target = S / "upstream" / info.filename
            assert target.resolve().is_relative_to((S / "upstream").resolve())
            if not info.is_dir(): write(target, z.read(info))
    for owner, (label, expected) in BASELINES.items():
        name = f"[API1.9]CS武器1.3.0-{label}包.scmod"
        baseline = S / "baseline" / name
        if not baseline.exists(): write(baseline, (ROOT / "output" / name).read_bytes())
        assert sha(baseline.read_bytes()) == expected
    untouched = S / "untouched-output.json"
    if not untouched.exists():
        dump(untouched, {p.name: sha(p.read_bytes()) for p in (ROOT / "output").glob("*.scmod")
                        if p.name not in [f"[API1.9]CS武器1.3.0-{v[0]}包.scmod" for v in BASELINES.values()]})
    rows = [r for r in json.loads((RESEARCH / "inventory.json").read_bytes()) if r["group"] != "glb"]
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        encoded = list(pool.map(encode, rows))
    dump(S / "encoded.json", encoded)
    write(S / "resources/ResourceMarker.cs", (ROOT / "src/ScCsgoResources/ResourceMarker.cs").read_bytes())
    write(S / "resources/ScCsgoResources.csproj", b'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoResources</AssemblyName><Version>1.10.4</Version><AssemblyVersion>1.10.4.0</AssemblyVersion><DebugType>none</DebugType><GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup><ItemGroup><EmbeddedResource Include="AnimationData/*"><LogicalName>Game.AnimationData.%(Filename)%(Extension)</LogicalName></EmbeddedResource></ItemGroup></Project>''')
    hashes = {}
    for project, target in [("ScCsgoKnives", "core"), ("ScCsgoTactical", "agents")]:
        for p in (ROOT / "src" / project).rglob("*"):
            rel = p.relative_to(ROOT / "src" / project)
            if not p.is_file() or rel.parts[0] in ["bin", "obj", "Assets"] or p.suffix not in [".cs", ".json", ".vsh", ".psh", ".skin", ".parts"]: continue
            if project == "ScCsgoKnives" and p.name.endswith(".cs2.animation.json"): continue
            if project == "ScCsgoTactical" and p.name == "TacticalBlocks.cs": continue
            write(S / target / "source" / rel, p.read_bytes())
            hashes[f"{project}/{rel.as_posix()}"] = sha(p.read_bytes())
    p = ROOT / "src/ScCsgoTactical/TacticalBlocks.cs"
    write(S / "core/source/TacticalBlocks.cs", p.read_bytes())
    hashes["ScCsgoTactical/TacticalBlocks.cs"] = sha(p.read_bytes())
    base = '''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Game</RootNamespace><GenerateDependencyFile>false</GenerateDependencyFile><Nullable>disable</Nullable><LangVersion>preview</LangVersion><DefineConstants>SC_SPLIT;SC_RESOURCE_ZSTD</DefineConstants><DebugType>none</DebugType>{props}</PropertyGroup><ItemGroup><PackageReference Include="SurvivalcraftAPI.Survivalcraft" Version="1.9.3.1"/>{items}</ItemGroup></Project>'''
    codec = (ROOT / "src/ScCsgoResourceCodec/bin/Release/net10.0/ScCsgoResourceCodec.dll").as_posix()
    write(S / "core/source/ScCsgoKnives.csproj", base.format(
        props="<AssemblyName>ScCsgoKnives</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
        items=f'<Reference Include="ScCsgoResourceCodec"><HintPath>{codec}</HintPath></Reference><Reference Include="ScCsgoResources"><HintPath>../../resources/bin/Release/net10.0/ScCsgoResources.dll</HintPath></Reference><EmbeddedResource Include="AnimationData/*.json;Shaders/*.vsh;Shaders/*.psh"/>').encode())
    write(S / "agents/source/ScCsgoTactical.csproj", base.format(
        props="<AssemblyName>ScCsgoTactical</AssemblyName><AssemblyVersion>1.2.0.0</AssemblyVersion><Version>1.3.0</Version><ImplicitUsings>enable</ImplicitUsings>",
        items='<Reference Include="ScCsgoKnives"><HintPath>../../core/source/bin/Release/net10.0/ScCsgoKnives.dll</HintPath></Reference><EmbeddedResource Include="ArmData/*"/>').encode())
    dump(S / "source-hashes.json", hashes)
    dump(S / "upstream.json", dict(repo="https://github.com/oleg-st/ZstdSharp", commit=COMMIT,
                                  archiveSha256=SOURCE_SHA, version="0.8.8",
                                  assembly="ScCsgoResourceCodec", modifications="Isolated assembly identity; original sources and Fody inlining retained."))
    print(f"Staged {len(encoded)} lossless resources and {len(hashes)} source files.")

if __name__ == "__main__": main()
