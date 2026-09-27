"""Validate candidate/source/check identity, then replace only the authorized trio."""
import json,os,shutil,zipfile
from prepare_mobile_common import ROOT,S,BASELINES,FULL_SHA
from prepare_codec_release import sha,dump
from pack_single_scmods import raw_member

def load(path):return json.loads(path.read_bytes())

def main():
    packages=load(S/"packages.json");checks={}
    names=["codec-check.json","codec-check-software.json","native-core.json","native-agents.json",
        "gate.json","native.json","local-nmm.json","zip-core.json","zip-agents.json",
        "resources-native/checks.json","npc/checks.json","appearance/checks.json","actor-ct.json","actor-t.json",
        "smoke/checks.json","sampling/checks.json","performance/checks.json","ai.json"]
    names+=["official-"+m+".json" for m in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]]
    names+=["compatibility-"+m+".json" for m in ["historical","current","mini"]]
    names+=["full-check/"+n for n in ["smoke/checks.json","sampling/checks.json","npc/checks.json",
        "appearance/checks.json","actor-ct.json","actor-t.json","gate.json","native.json","zip.json","compatibility.json","ai.json"]]
    names+=["full-check/official-"+m+".json" for m in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]]
    newest=max((S/"candidate"/p["file"]).stat().st_mtime_ns for p in packages.values())
    for name in names:
        path=S/name;data=load(path)
        assert data["failed"]==0,name
        rows=data.get("checks",[])
        if isinstance(rows,list):
            assert all(not isinstance(c,dict) or not any(c.get(k) is False for k in ["Ok","ok","passed"]) for c in rows),name
        assert path.stat().st_mtime_ns>=newest,("stale report",name)
        checks[name]=dict(sha256=sha(path.read_bytes()),failed=0,checks=len(rows) if isinstance(rows,list) else rows,
                         frames=len(data.get("frames",[])),clips=data.get("clips"))
    assert load(S/"resources-native/checks.json")["packageSha256"]==packages["core"]["sha256"]
    for name in ["codec-check.json","codec-check-software.json"]:
        report=load(S/name)
        assert report["streams"]==191 and report["packages"]=={k:v["sha256"] for k,v in packages.items()}
    assert load(S/"codec-check-software.json")["softwareFallback"]
    for p,name,core,addon in [(S,"ai.json","core","agents"),(S/"full-check","ai.json","full","full")]:
        data=load(p/name)
        assert data["coreSha256"]==packages[core]["sha256"] and data["dlcSha256"]==packages[addon]["sha256"]
    archives={}
    for role,row in packages.items():
        candidate=S/"candidate"/row["file"];baseline=S/"baseline"/row["file"]
        assert sha(candidate.read_bytes())==row["sha256"] and candidate.stat().st_size==row["bytes"]
        assert sha(baseline.read_bytes())==row["baselineSha256"]
        expected=FULL_SHA if role=="full" else BASELINES[role][1]
        assert row["baselineSha256"]==expected
        if role!="full":assert row["bytes"]<40_000_000
        with zipfile.ZipFile(candidate) as z,zipfile.ZipFile(baseline) as old:
            assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
            assert {n:sha(z.read(n)) for n in z.namelist()}==row["entries"]
            assert json.loads(z.read("modinfo.json"))["Version"]=="1.3.0"
            for n in old.namelist():
                if n not in row["changes"]:
                    assert z.read(n)==old.read(n)
                    assert raw_member(z,z.getinfo(n))==raw_member(old,old.getinfo(n))
            # Assets/resources are never replaced by the common-code update.
            assert not any(n.startswith("Assets/") or n=="ScCsgoResources.dll" for n in row["changes"])
            archives[role]={n:sha(z.read(n)) for n in z.namelist() if n.endswith((".dll",".bin"))}
        assert sha((ROOT/"output"/row["file"]).read_bytes()) in [expected,row["sha256"]]
    for edition,root in [("core",S),("full",S/"full-check")]:
        dlls={**archives[edition]}
        if edition=="core":dlls.update(archives["agents"])
        folders=["sampling","mobile","npc","actor"]+(["resources","performance","codec"] if edition=="core" else [])
        for folder in folders:
            runtime=root/f"checks/{folder}/bin/Release/net10.0"
            for n in ["ScCsgoKnives.dll","ScCsgoTactical.dll","ScCsgoResources.dll"]+ (["ScCsgoResourceCodec.dll"] if edition=="core" else []):
                assert sha((runtime/n).read_bytes())==dlls[n],(folder,n)
        for n in ["ScCsgoKnives.dll","ScCsgoTactical.dll","ScCsgoResources.dll"]:
            assert sha((root/"runtime/AppearanceCheck"/n).read_bytes())==dlls[n],n
        with zipfile.ZipFile(S/"candidate"/packages[edition]["file"]) as z:
            for n in z.namelist():
                if n.startswith("Assets/"):assert sha((root/"fixture/src/ScCsgoKnives"/n).read_bytes())==sha(z.read(n)),n
    with zipfile.ZipFile(S/"candidate"/packages["agents"]["file"]) as z:
        for n in z.namelist():
            if n.startswith("Assets/"):assert sha((S/"fixture/src/ScCsgoKnives"/n).read_bytes())==sha(z.read(n)),n
    for n,h in load(S/"source-hashes.json").items():
        assert sha((ROOT/"src"/n).read_bytes())==h,("source changed since build",n)
    for name,h in load(S/"untouched-output.json").items():
        if name==packages["full"]["file"]:continue
        assert sha((ROOT/"output"/name).read_bytes())==h
    pending=[]
    for row in packages.values():
        target=ROOT/"output"/row["file"];partial=target.with_suffix(".scmod.partial")
        shutil.copyfile(S/"candidate"/row["file"],partial)
        assert sha(partial.read_bytes())==row["sha256"];pending.append((partial,target))
    for partial,target in pending:os.replace(partial,target)
    evidence=dict(version="1.3.0",build="mobile-common-20260927",androidTested=False,packages=packages,checks=checks,
        sourceHashes=load(S/"source-hashes.json"),baselineRetention="Project .tmp/mobile-common-130-20260927/baseline; not player-world backups.",
        sampling={edition:load(root/"sampling/checks.json")["measurements"] for edition,root in [("split",S),("full",S/"full-check")]},
        scope="Native Windows fixtures on exact DLLs and each edition's assets. Phone acceptance pending. Mini/Mods/worlds unchanged.",
        smokeSave="Optional SmokeBodyBounceUsed; new Full/Lite retain it. Older builds ignore and can drop it while retaining legacy flight state.",
        tools={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in (ROOT/"tools").glob("*mobile_common*.py")})
    dump(ROOT/"docs/release-mobile-common-1.3.0-2026-09-27-evidence.json",evidence)
    print(json.dumps({k:{n:v for n,v in row.items() if n not in ["entries","changes"]} for k,row in packages.items()},ensure_ascii=False,indent=2))

if __name__=="__main__":main()
