"""Update only own core/tactical/appearance and revision metadata."""
from prepare_mobile_resources import ROOT,S,BASELINES,FULL_SHA
from prepare_codec_release import sha,dump
from pack_single_scmods import raw_member,write_archive
from package_codec_release import member
import json,zipfile

def main():
    packages={}
    for role,label,expected in [("core","轻量",BASELINES["core"][1]),("agents","探员",BASELINES["agents"][1]),("full","全量",FULL_SHA)]:
        name=f"[API1.9]CS武器1.3.0-{label}包.scmod";baseline=S/"baseline"/name
        assert sha(baseline.read_bytes())==expected
        replacements={}
        prefix="full/" if role=="full" else ""
        if role in ["core","full"]:replacements["ScCsgoKnives.dll"]=(S/(prefix+"core/source/bin/Release/net10.0/ScCsgoKnives.dll")).read_bytes()
        if role in ["agents","full"]:
            replacements["ScCsgoTactical.dll"]=(S/(prefix+"agents/source/bin/Release/net10.0/ScCsgoTactical.dll")).read_bytes()
            replacements["Integrations/ScCsgoAppearance.bin"]=(S/(prefix+"appearance/source/bin/Release/net10.0/ScCsgoAppearance.dll")).read_bytes()
        with zipfile.ZipFile(baseline) as z:
            entries={i.filename:(i.compress_type,i.CRC,i.file_size,raw_member(z,i)) for i in z.infolist()}
            hashes={n:sha(z.read(n)) for n in z.namelist()}
            if role in ["core","full"]:
                family=json.loads(z.read("Integrations/CompatibilityFamily.json"))
                family.update(build_revision="mobile-resources-20260927",core_sha256=sha(replacements["ScCsgoKnives.dll"]))
                replacements["Integrations/CompatibilityFamily.json"]=json.dumps(family,ensure_ascii=False,indent=2).encode()
            if role!="full":
                meta=json.loads(z.read("Integrations/ScSplit.json"));meta["build_revision"]="mobile-resources-20260927"
                replacements["Integrations/ScSplit.json"]=json.dumps(meta,ensure_ascii=False,indent=2).encode()
            meta=json.loads(z.read("Integrations/ScMobileCommon.json"))
            meta.update(revision="mobile-resources-20260927",neoBufferSync=True,texturePreparation=True,worldRestorePreparation=True)
            replacements["Integrations/ScMobileCommon.json"]=json.dumps(meta).encode()
            for n,data in replacements.items():entries[n]=member(data);hashes[n]=sha(data)
            target=S/"candidate"/name;target.parent.mkdir(exist_ok=True);write_archive(target,entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n:sha(out.read(n)) for n in out.namelist()}==hashes
                for n in z.namelist():
                    if n not in replacements:assert raw_member(out,out.getinfo(n))==raw_member(z,z.getinfo(n))
        if role!="full":assert target.stat().st_size<40_000_000
        packages[role]=dict(file=name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),baselineSha256=expected,entries=hashes,changes={n:hashes[n] for n in replacements})
        print(role,target.stat().st_size,flush=True)
    dump(S/"packages.json",packages)

if __name__=="__main__":main()
