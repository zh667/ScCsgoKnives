"""Build shared mobile fixes in isolated split and Full source snapshots."""
from pathlib import Path
import json, shutil, subprocess, zipfile
import prepare_codec_release as prep

ROOT=prep.ROOT
S=ROOT/".tmp/mobile-common-130-20260927"
OLD=ROOT/".tmp/codec-release-130-20260927"
BASELINES={
    "core":("轻量","de1fb1807585d9981c845ddacbdb1185e74b33e86938b01e8a7c10057c8b31e8"),
    "agents":("探员","c57293831fc9d6773e1aa31f1a59dca2dc3fe12aaee0e9f310a07661505554b0"),
}
FULL_SHA="a2922a8115fe010b564691115351667dba1c3327329fd1a36870184ffef10f02"

def main():
    S.mkdir(parents=True,exist_ok=True)
    for name in ["resources","encoded"]:
        if not (S/name).exists():shutil.copytree(OLD/name,S/name)
    prep.S=S;prep.BASELINES=BASELINES
    # Fixed upstream archive verified again by the staging function.
    if not (S/"ZstdSharp-source.zip").exists():shutil.copyfile(OLD/"ZstdSharp-source.zip",S/"ZstdSharp-source.zip")
    prep.main()
    # An earlier unshipped gait shortcut failed native boundary equivalence.
    # Remove only this known obsolete task file from isolated snapshots.
    for rel in ["agents/source/ScActorGait.cs","full/agents/source/ScActorGait.cs"]:
        stale=(S/rel).resolve()
        assert stale.is_relative_to(S.resolve())
        if stale.exists():stale.unlink()
    full=S/"baseline/[API1.9]CS武器1.3.0-全量包.scmod"
    if not full.exists():shutil.copyfile(ROOT/"output"/full.name,full)
    assert prep.sha(full.read_bytes())==FULL_SHA
    # Full uses the same sources but retains its original resources, asset quality,
    # non-split item identities and uncompressed resource format.
    for kind in ["core","agents"]:
        destination=S/"full"/kind/"source"
        shutil.copytree(S/kind/"source",destination,dirs_exist_ok=True,ignore=shutil.ignore_patterns("bin","obj"))
    tactical=ROOT/"src/ScCsgoTactical/TacticalBlocks.cs"
    extra=S/"full/core/source/TacticalBlocks.cs"
    assert extra.resolve().is_relative_to((S/"full/core/source").resolve())
    if extra.exists():extra.unlink()
    prep.write(S/"full/agents/source/TacticalBlocks.cs",tactical.read_bytes())
    with zipfile.ZipFile(full) as z:prep.write(S/"full/resources/ScCsgoResources.dll",z.read("ScCsgoResources.dll"))
    for kind,project in [("core","ScCsgoKnives"),("agents","ScCsgoTactical")]:
        path=S/f"full/{kind}/source/{project}.csproj"
        text=path.read_text("utf8").replace("SC_SPLIT;SC_RESOURCE_ZSTD","")
        if kind=="core":
            start=text.index('<Reference Include="ScCsgoResourceCodec">');end=text.index("</Reference>",start)+len("</Reference>")
            text=text[:start]+text[end:]
            text=text.replace("../../resources/bin/Release/net10.0/ScCsgoResources.dll","../../resources/ScCsgoResources.dll")
        path.write_text(text,"utf8")
    print("Full and split share source input hashes; resources retain each edition's own baseline.")

if __name__=="__main__":main()
