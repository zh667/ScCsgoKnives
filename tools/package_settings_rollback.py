import json,zipfile,xml.etree.ElementTree as ET
from prepare_settings_rollback import ROOT,S,BASELINES,FULL_SHA
from prepare_codec_release import sha,dump
from pack_single_scmods import raw_member,write_archive
from package_codec_release import member
def main():
    packages={}
    for role,label,expected in [("core","轻量",BASELINES["core"][1]),("agents","探员",BASELINES["agents"][1]),("full","全量",FULL_SHA)]:
        name=f"[API1.9]CS武器1.3.0-{label}包.scmod";baseline=S/"baseline"/name
        assert sha(baseline.read_bytes())==expected
        changes={};prefix="full/" if role=="full" else ""
        for directory,assembly in ([("core","ScCsgoKnives")] if role=="core" else [("agents","ScCsgoTactical"),("voice","ScCsgoVoice")] if role=="agents" else [("core","ScCsgoKnives"),("agents","ScCsgoTactical"),("voice","ScCsgoVoice")]):
            changes[assembly+".dll"]=(S/(prefix+directory+"/source/bin/Release/net10.0/"+assembly+".dll")).read_bytes()
        with zipfile.ZipFile(baseline) as z:
            entries={i.filename:(i.compress_type,i.CRC,i.file_size,raw_member(z,i)) for i in z.infolist()}
            hashes={n:sha(z.read(n)) for n in z.namelist()}
            if role in ["core","full"]:
                sound="grenade_fire_airburst."+("wav" if role=="full" else "ogg")
                path="Assets/Audio/ScCsgoKnives/"+sound
                changes[path]=(S/"new-assets"/sound).read_bytes()
                marker=ET.fromstring(z.read("Assets/ScCsgoResources.xml"))
                ET.SubElement(marker,"File",Path=path,Sha256=sha(changes[path]))
                changes["Assets/ScCsgoResources.xml"]=ET.tostring(marker,encoding="utf-8",xml_declaration=True)
            for n in ["Integrations/CompatibilityFamily.json","Integrations/ScSplit.json","Integrations/ScMobileCommon.json"]:
                if n not in z.namelist():continue
                meta=json.loads(z.read(n))
                if n.endswith("CompatibilityFamily.json"):meta.update(build_revision="settings-audio-rollback-20260927",core_sha256=sha(changes["ScCsgoKnives.dll"]))
                elif n.endswith("ScSplit.json"):meta["build_revision"]="settings-audio-rollback-20260927"
                else:meta.update(revision="settings-audio-rollback-20260927",ownedAudio=False,hudEditor="button-layout",distinctFireAirburst=True)
                changes[n]=json.dumps(meta,ensure_ascii=False,indent=2).encode()
            for n,b in changes.items():entries[n]=member(b);hashes[n]=sha(b)
            target=S/"candidate"/name;target.parent.mkdir(exist_ok=True);write_archive(target,entries)
            with zipfile.ZipFile(target) as out:
                assert out.testzip() is None and {n:sha(out.read(n)) for n in out.namelist()}==hashes
                for n in z.namelist():
                    if n not in changes:assert raw_member(out,out.getinfo(n))==raw_member(z,z.getinfo(n))
        if role!="full":assert target.stat().st_size<40_000_000
        packages[role]=dict(file=name,bytes=target.stat().st_size,sha256=sha(target.read_bytes()),baselineSha256=expected,entries=hashes,changes={n:hashes[n] for n in changes})
        print(role,target.stat().st_size,flush=True)
    dump(S/"packages.json",packages)
if __name__=="__main__":main()
