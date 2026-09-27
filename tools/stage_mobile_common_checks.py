"""Reuse native gates plus focused shared-code regression and sampling checks."""
from prepare_mobile_common import ROOT,S,OLD
from prepare_codec_release import write,dump
import stage_codec_checks as checks
import json,shutil,zipfile

def main():
    # Existing fixture staging expects the two split packages only.
    all_packages=json.loads((S/"packages.json").read_bytes())
    dump(S/"split-packages.json",{k:v for k,v in all_packages.items() if k!="full"})
    dump(S/"packages.json",{k:v for k,v in all_packages.items() if k!="full"})
    try:
        checks.S=S;checks.main()
    finally:dump(S/"packages.json",all_packages)
    refs={n:S/"assemblies"/(n+".dll") for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoTactical","ScCsgoResourceCodec"]}
    for kind in ["sampling","mobile","performance"]:
        name={"sampling":"ActorSamplingCheck","mobile":"MobileCommonCheck","performance":"TacticalPerformanceCheck"}[kind]
        folder=S/"checks"/kind
        write(folder/"Check.csproj",checks.project(name,refs).encode())
        if kind=="sampling":
            write(folder/"Program.cs",(ROOT/"tools/ActorSamplingCheck/Program.cs").read_bytes())
            write(folder/"MobileRenderChecks.cs",(ROOT/"tools/ActorSamplingCheck/MobileRenderChecks.cs").read_bytes())
            for target in [folder/"MobileSamplingChecks.cs",S/"full-check/checks/sampling/MobileSamplingChecks.cs"]:
                assert target.resolve().is_relative_to(S.resolve())
                if target.exists():target.unlink()
        elif kind=="mobile":write(folder/"Program.cs",(ROOT/"tools/MobileCommonCheck/Program.cs").read_bytes())
        else:
            p=(ROOT/"tools/TacticalPerformanceCheck/Program.cs").read_text("utf8")
            p=p.replace('Path.Combine(root,"output/[API1.9]CS武器1.3.0-全量包.scmod")',
                '"'+(S/"candidate"/all_packages["core"]["file"]).as_posix()+'"')
            write(folder/"Program.cs",p.encode())
    for n in ["core","agents","full"]:
        path=S/"candidate"/all_packages[n]["file"]
        with zipfile.ZipFile(path) as z:
            for entry in z.namelist():
                if entry.endswith(".dll") and "/" not in entry:write(S/"compat-baselines"/n/entry,z.read(entry))
    # Full gameplay fixtures use Full assets, original texture quality and actual DLLs.
    full=S/"full-check"
    with zipfile.ZipFile(S/"candidate"/all_packages["full"]["file"]) as z:
        for entry in z.namelist():
            if entry.startswith("Assets/"):
                write(full/"fixture/src/ScCsgoKnives"/entry,z.read(entry))
            elif entry.endswith(".dll") and "/" not in entry:
                write(full/"assemblies"/entry,z.read(entry))
        write(full/"assemblies/ScCsgoAppearance.dll",z.read("Integrations/ScCsgoAppearance.bin"))
    for name in ["ScCsgoTactical","ScCsgoAppearance"]:checks.junction(full/f"fixture/src/{name}/Assets",full/"fixture/src/ScCsgoKnives/Assets")
    checks.junction(full/"fixture/.tmp/nmm-player-appearance-audit-20260920",ROOT/".tmp/nmm-player-appearance-audit-20260920")
    checks.junction(full/"fixture/tools",ROOT/"tools")
    fullrefs={n:full/"assemblies"/(n+".dll") for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoTactical"]}
    for name in ["sampling","mobile","npc","actor"]:
        src=S/"checks"/name;dest=full/"checks"/name
        for p in src.glob("*.cs"):write(dest/p.name,p.read_bytes())
        assembly={"sampling":"ActorSamplingCheck","mobile":"MobileCommonCheck","npc":"NpcWeaponCheck","actor":"ActorLoadCheck"}[name]
        write(dest/"Check.csproj",checks.project(assembly,fullrefs).encode())
    shutil.copytree(OLD/"runtime/AppearanceCheck",full/"runtime/AppearanceCheck",dirs_exist_ok=True)
    for n in ["ScCsgoKnives","ScCsgoResources","ScCsgoTactical","ScCsgoAppearance"]:
        shutil.copyfile(full/"assemblies"/(n+".dll"),full/"runtime/AppearanceCheck"/(n+".dll"))
    write(full/"checks/load/Program.cs",(ROOT/"tools/TacticalLoadCheck/Program.cs").read_bytes())
    write(full/"checks/load/Check.csproj",checks.project("TacticalLoadCheck",{}).encode())
    # The old native split core in this guard must predate the codec; the current
    # baseline correctly already supports protocol 1.
    shutil.copyfile(OLD/"compat-baselines/old-split/ScCsgoKnives.dll",S/"compat-baselines/old-split/ScCsgoKnives.dll")
    # Exercise background parsing through the production cache path on every mesh.
    for target in [S/"checks/npc/Program.cs",full/"checks/npc/Program.cs"]:
        p=target.read_text("utf8")
        p=p.replace('Check(asset+" ContentManager reader",', '''ScNpcWeaponGeometry.Request(asset,legacy);
    Check(asset+" prefetch deduplicates",ScNpcWeaponGeometry.PendingCount==1);
    ScNpcWeaponGeometry.Request(asset,legacy);Check(asset+" prefetch no duplicate",ScNpcWeaponGeometry.PendingCount==1);
    Check(asset+" ContentManager reader",''')
        p=p.replace('Check(asset+" corrupt cache refuses expensive fallback",noFallback);', '''Check(asset+" corrupt cache refuses expensive fallback",noFallback);
    ScNpcWeaponGeometry.Request(asset,legacy);bool asyncRejected=false;
    try{ScNpcWeaponGeometry.For(asset,legacy);}catch(Exception e)when(e is ScResourceCodecException or InvalidDataException){asyncRejected=true;}
    Check(asset+" prefetch corruption refuses fallback",asyncRejected);
    ScNpcWeaponGeometry.Clear();Check(asset+" clear drops pending",ScNpcWeaponGeometry.PendingCount==0);''')
        target.write_text(p,"utf8")
    codec=S/"checks/codec"
    p=(ROOT/"tools/CodecReleaseCheck/Program.cs").read_text("utf8").replace("byte[] Bytes(ZipArchive z","byte[] Bytes(System.IO.Compression.ZipArchive z")
    # The current baseline is itself encoded: compare decoded bytes on both sides.
    p=p.replace("expected.ToArray()));embedded++","Decoded(expected.ToArray())));embedded++")
    p=p.replace("actual.SequenceEqual(Bytes(baseline,entry.FullName))","actual.SequenceEqual(Decoded(Bytes(baseline,entry.FullName)))")
    write(codec/"Program.cs",p.encode())
    write(codec/"Check.csproj",checks.project("CodecReleaseCheck",refs).encode())
    print("Staged split and Full checks, shared regression sources, exact edition resources.")

if __name__=="__main__":main()
