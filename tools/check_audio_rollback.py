"""Check the requested rollback against the immutable pre-redesign source."""
import subprocess,json,zipfile
from prepare_settings_rollback import ROOT,S
from prepare_codec_release import dump,sha

def main():
    files=["ScCsgoKnives/World/ScPresentationSound.cs","ScCsgoKnives/World/ScCombatAudio.cs",
        "ScCsgoKnives/World/SubsystemScGunBlockBehavior.cs","ScCsgoKnives/Mod/ScCsgoKnivesModLoader.cs",
        "ScCsgoTactical/ComponentTacticalEnemy.cs","ScCsgoTactical/ComponentTacticalCompanion.cs","ScCsgoVoice/AgentVoice.cs"]
    checks=[]
    for name in files:
        previous=subprocess.check_output(["git","show","fc023e9:src/"+name],cwd=ROOT).decode("utf-8-sig").replace("\r\n","\n")
        current=(ROOT/"src"/name).read_text("utf-8-sig")
        assert current==previous,name
        checks.append("exact pre-redesign source: "+name)
    for p in (ROOT/"src").rglob("*.cs"):
        assert "ScOwnedAudio" not in p.read_text("utf-8-sig"),p
    checks.append("no own-audio override callers remain")
    packages=json.loads((S/"packages.json").read_bytes())
    for role,row in packages.items():
        with zipfile.ZipFile(S/"candidate"/row["file"]) as z,zipfile.ZipFile(S/"baseline"/row["file"]) as old:
            audio=[n for n in old.namelist() if n.startswith("Assets/Audio/")]
            assert audio
            assert all(sha(z.read(n))==sha(old.read(n)) for n in audio)
            checks.append(f"{role}: all {len(audio)} audio files byte-identical")
    dump(S/"audio-rollback.json",dict(failed=0,baseline="fc023e9",checks=checks,packages={k:v["sha256"] for k,v in packages.items()}))
    print("PASS audio rollback source and resources")

if __name__=="__main__":main()
