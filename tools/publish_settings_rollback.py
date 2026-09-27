"""Publish validated common changes; preserve other assets, mods and player data."""
import json,os,shutil,zipfile
from prepare_settings_rollback import ROOT,S,BASELINES,FULL_SHA
from prepare_codec_release import sha,dump
from pack_single_scmods import raw_member

def load(p):return json.loads(p.read_bytes())
def main():
    packages=load(S/"packages.json");checks={}
    names=["codec-check.json","codec-check-software.json","native-core.json","native-agents.json","gate.json","native.json","local-nmm.json","zip-core.json","zip-agents.json",
           "resources-native/checks.json","npc/checks.json","appearance/checks.json","actor-ct.json","actor-t.json","smoke/checks.json","sampling/checks.json","performance/checks.json","ai.json","textures/checks.json","features/checks.json"]
    names+=["official-"+m+".json" for m in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]]
    names+=["compatibility-"+m+".json" for m in ["historical","current","mini"]]
    names+=["full-check/"+n for n in ["smoke/checks.json","sampling/checks.json","npc/checks.json","appearance/checks.json","actor-ct.json","actor-t.json","gate.json","native.json","zip.json","compatibility.json","ai.json","textures/checks.json","features/checks.json"]]
    names+=["full-check/official-"+m+".json" for m in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]]
    newest=max((S/"candidate"/p["file"]).stat().st_mtime_ns for p in packages.values())
    for name in names:
        path=S/name;data=load(path);assert data["failed"]==0 and path.stat().st_mtime_ns>=newest,name
        rows=data.get("checks",[])
        if isinstance(rows,list):assert all(not isinstance(r,dict) or not any(r.get(k) is False for k in ["Ok","ok","passed"]) for r in rows),name
        checks[name]=dict(sha256=sha(path.read_bytes()),failed=0,checks=len(rows) if isinstance(rows,list) else rows,frames=len(data.get("frames",[])),clips=data.get("clips"))
    assert load(S/"resources-native/checks.json")["packageSha256"]==packages["core"]["sha256"]
    for n in ["codec-check.json","codec-check-software.json"]:
        data=load(S/n);assert data["streams"]==191 and data["packages"]=={k:v["sha256"] for k,v in packages.items()}
    for prefix,core,addon in [("","core","agents"),("full-check/","full","full")]:
        ai=load(S/(prefix+"ai.json"));assert ai["coreSha256"]==packages[core]["sha256"] and ai["dlcSha256"]==packages[addon]["sha256"]
        assert load(S/(prefix+"textures/checks.json"))["packageSha256"]==packages[core]["sha256"]
        assert len(load(S/(prefix+"features/checks.json"))["checks"])>=80
    rollback=load(S/"audio-rollback.json");assert rollback["failed"]==0 and rollback["packages"]=={k:v["sha256"] for k,v in packages.items()}
    archives={}
    for role,row in packages.items():
        candidate=S/"candidate"/row["file"];baseline=S/"baseline"/row["file"];expected=FULL_SHA if role=="full" else BASELINES[role][1]
        assert sha(candidate.read_bytes())==row["sha256"] and candidate.stat().st_size==row["bytes"]
        assert sha(baseline.read_bytes())==row["baselineSha256"]==expected
        if role!="full":assert row["bytes"]<40_000_000
        with zipfile.ZipFile(candidate) as z,zipfile.ZipFile(baseline) as old:
            assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
            assert {n:sha(z.read(n)) for n in z.namelist()}==row["entries"]
            assert json.loads(z.read("modinfo.json"))["Version"]=="1.3.0"
            for n in old.namelist():
                if n not in row["changes"]:assert z.read(n)==old.read(n) and raw_member(z,z.getinfo(n))==raw_member(old,old.getinfo(n))
            allowedAssets={"Assets/ScCsgoResources.xml","Assets/Audio/ScCsgoKnives/grenade_fire_airburst.wav","Assets/Audio/ScCsgoKnives/grenade_fire_airburst.ogg"}
            assert all(not n.startswith("Assets/") or n in allowedAssets for n in row["changes"])
            assert "ScCsgoResources.dll" not in row["changes"]
            assert not any("neorxna" in n.lower() or "nekomeko" in n.lower() for n in row["changes"])
            archives[role]={n:sha(z.read(n)) for n in z.namelist() if n.endswith((".dll",".bin"))}
        assert sha((ROOT/"output"/row["file"]).read_bytes()) in [expected,row["sha256"]]
    for role,root in [("core",S),("full",S/"full-check")]:
        dlls=dict(archives[role])
        if role=="core":dlls.update(archives["agents"])
        for folder in ["sampling","mobile","npc","actor","textures","appearance","features"]+(["resources","performance","codec"] if role=="core" else []):
            for n in ["ScCsgoKnives.dll","ScCsgoTactical.dll","ScCsgoResources.dll"]+(["ScCsgoResourceCodec.dll"] if role=="core" else []):
                assert sha((root/f"checks/{folder}/bin/Release/net10.0"/n).read_bytes())==dlls[n],(folder,n)
        for n in ["ScCsgoKnives.dll","ScCsgoTactical.dll","ScCsgoResources.dll","ScCsgoAppearance.dll"]:
            target="Integrations/ScCsgoAppearance.bin" if n=="ScCsgoAppearance.dll" else n
            assert sha((root/"runtime/AppearanceCheck"/n).read_bytes())==dlls[target]
        with zipfile.ZipFile(S/"candidate"/packages[role]["file"]) as z:
            for n in z.namelist():
                if n.startswith("Assets/"):assert sha((root/"fixture/src/ScCsgoKnives"/n).read_bytes())==sha(z.read(n))
    for n,h in load(S/"source-hashes.json").items():assert sha((ROOT/"src"/n).read_bytes())==h,("source changed",n)
    for n,h in load(S/"third-party.json").items():assert sha((ROOT/n).read_bytes())==h
    for n,h in load(S/"untouched-output.json").items():
        if n!=packages["full"]["file"]:assert sha((ROOT/"output"/n).read_bytes())==h
    assert sha((ROOT/"src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/grenade_fireburst_atlas.png").read_bytes())==load(S/"fireburst-source.json")["assets"]["grenade_fireburst_atlas.png"]["sha256"]
    assert sha((ROOT/"src/ScCsgoKnives/Assets/Audio/ScCsgoKnives/grenade_fire_airburst.wav").read_bytes())==load(S/"airburst-source.json")["sourceSha256"]
    pending=[]
    for row in packages.values():
        target=ROOT/"output"/row["file"];part=target.with_suffix(".scmod.partial");shutil.copyfile(S/"candidate"/row["file"],part)
        assert sha(part.read_bytes())==row["sha256"];pending.append((part,target))
    for part,target in pending:os.replace(part,target)
    evidence=dict(version="1.3.0",build="settings-audio-rollback-20260927",androidTested=False,packages=packages,checks=checks,
        sourceHashes=load(S/"source-hashes.json"),thirdPartyUnchanged=load(S/"third-party.json"),fireburst=load(S/"fireburst-source.json"),airburstSound=load(S/"airburst-source.json"),audioRollback=rollback,
        scope="Windows native regression, HUD touch-drag and prior audio callers. Android listening/frame acceptance pending. No installed Mods/phone/world writes.",
        tools={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in (ROOT/"tools").glob("*settings*.py")})
    dump(ROOT/"docs/release-settings-rollback-1.3.0-2026-09-27-evidence.json",evidence)
    for size in ["360x640","850x479"]:
        for kind in ["settings","hud-editor","hud-gesture"]:shutil.copyfile(S/f"features/{kind}-{size}.png",ROOT/f"docs/rollback-{kind}-{size}-2026-09-27.png")
    print(json.dumps({k:{n:v for n,v in row.items() if n not in ["entries","changes"]} for k,row in packages.items()},ensure_ascii=False,indent=2))

if __name__=="__main__":main()
