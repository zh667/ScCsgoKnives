"""Update own code/voice and add one edition-appropriate visual-only atlas."""
from prepare_gameplay_followup import ROOT,S,BASELINES,FULL_SHA
from prepare_codec_release import sha,dump
from pack_single_scmods import raw_member,write_archive
from package_codec_release import member
import json,zipfile,xml.etree.ElementTree as ET

def main():
    packages={}
    for role,label,expected in [("core","轻量",BASELINES["core"][1]),("agents","探员",BASELINES["agents"][1]),("full","全量",FULL_SHA)]:
        name=f"[API1.9]CS武器1.3.0-{label}包.scmod";baseline=S/"baseline"/name
        assert sha(baseline.read_bytes())==expected
        repl={};prefix="full/" if role=="full" else ""
        if role in ["core","full"]:repl["ScCsgoKnives.dll"]=(S/(prefix+"core/source/bin/Release/net10.0/ScCsgoKnives.dll")).read_bytes()
        if role in ["agents","full"]:
            repl["ScCsgoTactical.dll"]=(S/(prefix+"agents/source/bin/Release/net10.0/ScCsgoTactical.dll")).read_bytes()
            repl["ScCsgoVoice.dll"]=(S/(prefix+"voice/source/bin/Release/net10.0/ScCsgoVoice.dll")).read_bytes()
        with zipfile.ZipFile(baseline) as z:
            entries={i.filename:(i.compress_type,i.CRC,i.file_size,raw_member(z,i)) for i in z.infolist()}
            hashes={n:sha(z.read(n)) for n in z.namelist()}
            if role in ["core","full"]:
                family=json.loads(z.read("Integrations/CompatibilityFamily.json"))
                family.update(build_revision="gameplay-audio-20260927",core_sha256=sha(repl["ScCsgoKnives.dll"]))
                repl["Integrations/CompatibilityFamily.json"]=json.dumps(family,ensure_ascii=False,indent=2).encode()
                atlas="grenade_fireburst_atlas."+("png" if role=="full" else "webp")
                path="Assets/Textures/ScCsgoKnives/"+atlas
                repl[path]=(S/"new-assets"/atlas).read_bytes()
                marker=ET.fromstring(z.read("Assets/ScCsgoResources.xml"))
                ET.SubElement(marker,"File",Path=path,Sha256=sha(repl[path]))
                repl["Assets/ScCsgoResources.xml"]=ET.tostring(marker,encoding="utf-8",xml_declaration=True)
            if role!="full":
                meta=json.loads(z.read("Integrations/ScSplit.json"));meta["build_revision"]="gameplay-audio-20260927"
                repl["Integrations/ScSplit.json"]=json.dumps(meta,ensure_ascii=False,indent=2).encode()
            meta=json.loads(z.read("Integrations/ScMobileCommon.json"))
            meta.update(revision="gameplay-audio-20260927",fireFuseSeconds=3,enemyGraceDays=30,ownedAudio=True)
            repl["Integrations/ScMobileCommon.json"]=json.dumps(meta).encode()
            for n,b in repl.items():entries[n]=member(b);hashes[n]=sha(b)
            target=S/"candidate"/name;target.parent.mkdir(exist_ok=True);write_archive(target,entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n:sha(out.read(n)) for n in out.namelist()}==hashes
                for n in z.namelist():
                    if n not in repl:assert raw_member(out,out.getinfo(n))==raw_member(z,z.getinfo(n))
        if role!="full":assert target.stat().st_size<40_000_000
        packages[role]=dict(file=name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),baselineSha256=expected,entries=hashes,changes={n:hashes[n] for n in repl})
        print(role,target.stat().st_size,flush=True)
    dump(S/"packages.json",packages)

if __name__=="__main__":main()
