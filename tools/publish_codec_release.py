"""Publish the validated pair only; keep baselines, other editions and game data intact."""
from prepare_codec_release import ROOT, S, BASELINES, sha, dump
import json, os, shutil, zipfile

def load(p):return json.loads(p.read_bytes())

def main():
    packages=load(S/"packages.json")
    reports=["codec-check.json","codec-check-software.json","native-core.json","native-agents.json",
             "gate.json","native.json","local-nmm.json","zip-core.json","zip-agents.json",
             "resources-native/checks.json","npc/checks.json","appearance/checks.json","actor-ct.json","actor-t.json"]
    reports += ["official-"+mode+".json" for mode in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]]
    reports += ["compatibility-"+mode+".json" for mode in ["historical","current","mini"]]
    checks={}
    for name in reports:
        value=load(S/name)
        assert value.get("failed",0)==0,name
        rows=value.get("checks",[])
        if isinstance(rows,list):
            assert not any(isinstance(r,dict) and (r.get("ok") is False or r.get("passed") is False) for r in rows),name
        checks[name]=dict(sha256=sha((S/name).read_bytes()),failed=0,
                         checks=len(rows) if isinstance(rows,list) else rows,
                         frames=len(value.get("frames",[])),clips=value.get("clips"))
    assert load(S/"resources-native/checks.json")["packageSha256"]==packages["core"]["sha256"]
    for name in ["codec-check.json","codec-check-software.json"]:
        report=load(S/name)
        assert report["streams"]==191 and report["embedded"]==126
        assert report["packages"]=={k:p["sha256"] for k,p in packages.items()}
    assert load(S/"codec-check-software.json")["softwareFallback"]
    expected={}
    for owner,p in packages.items():
        candidate=S/"candidate"/p["file"];data=candidate.read_bytes()
        assert len(data)==p["bytes"]<40_000_000 and sha(data)==p["sha256"]
        assert sha((S/"baseline"/p["file"]).read_bytes())==BASELINES[owner][1]
        with zipfile.ZipFile(candidate) as z:
            assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
            assert {n:sha(z.read(n)) for n in z.namelist()}==p["entries"]
            assert json.loads(z.read("modinfo.json"))["Version"]=="1.3.0"
            assert json.loads(z.read("Integrations/ScSplit.json"))["resourceCodec"]==1
            for n in z.namelist():
                if n.startswith("Assets/") or n.endswith((".dll",".bin")):
                    digest=sha(z.read(n))
                    if n in expected:assert expected[n]==digest,n
                    expected[n]=digest
        target=ROOT/"output"/p["file"]
        prior_record=ROOT/"docs/release-split-zstd-1.3.0-2026-09-27-evidence.json"
        allowed=[BASELINES[owner][1],p["sha256"]]
        if prior_record.exists():
            prior=load(prior_record)
            assert prior["build"]=="split-zstd-130-20260927" and prior["packages"][owner]["baselineSha256"]==BASELINES[owner][1]
            allowed.append(prior["packages"][owner]["sha256"])
        assert sha(target.read_bytes()) in allowed
    assert load(S/"fixture-hashes.json")==expected
    with zipfile.ZipFile(S/"fixture-union.scmod") as z:
        assert all(sha(z.read(n))==digest for n,digest in expected.items())
    for runtime in [S/"runtime/AppearanceCheck"]+[S/f"checks/{name}/bin/Release/net10.0" for name in ["resources","npc","actor"]]:
        for name in ["ScCsgoKnives.dll","ScCsgoResources.dll","ScCsgoTactical.dll","ScCsgoResourceCodec.dll"]:
            assert sha((runtime/name).read_bytes())==expected[name],(runtime,name)
    for name,digest in load(S/"untouched-output.json").items():
        assert sha((ROOT/"output"/name).read_bytes())==digest,name
    for name,digest in load(S/"source-hashes.json").items():
        assert sha((ROOT/"src"/name).read_bytes())==digest,("source changed after build",name)
    pending=[]
    for p in packages.values():
        target=ROOT/"output"/p["file"];partial=target.with_suffix(".scmod.partial")
        shutil.copyfile(S/"candidate"/p["file"],partial)
        assert sha(partial.read_bytes())==p["sha256"]
        pending.append((partial,target))
    for partial,target in pending:os.replace(partial,target)
    tool_paths=[ROOT/"tools"/n for n in ["prepare_codec_release.py","package_codec_release.py","stage_codec_checks.py","check_codec_release.py","publish_codec_release.py"]]
    tool_paths+=list((ROOT/"tools/CodecReleaseCheck").glob("*.cs"))+list((ROOT/"tools/CodecReleaseCheck").glob("*.csproj"))
    evidence=dict(version="1.3.0",build="split-zstd-130-20260927",androidTested=False,backupPolicy="manual",
                  packages=packages,checks=checks,upstream=load(S/"upstream.json"),
                  resources=load(S/"encoded.json"),sourceHashes=load(S/"source-hashes.json"),
                  untouchedOutput=load(S/"untouched-output.json"),
                  tools={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in tool_paths},
                  decoderProject={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in (ROOT/"src/ScCsgoResourceCodec").glob("*") if p.is_file()},
                  scope="Windows native loading/render/cache/compatibility checks. All original decoded resource bytes retained. Mobile acceptance pending; no Mods/worlds writes.")
    dump(ROOT/"docs/release-split-zstd-1.3.0-2026-09-27-evidence.json",evidence)
    print(json.dumps({k:{n:v for n,v in p.items() if n not in ["entries","changes"]} for k,p in packages.items()},ensure_ascii=False,indent=2))

if __name__=="__main__":main()
