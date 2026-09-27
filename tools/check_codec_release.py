"""Run detached native checks, recording logs outside Mods and worlds."""
from prepare_codec_release import ROOT, S
import argparse, os, subprocess, zipfile

GAME = __import__("pathlib").Path(r"D:\下载\[Windows]SurvivalcraftAPI_1.9.3.1")

def run(label, *args, env=None):
    with (S/(label+".log")).open("w",encoding="utf8") as log:
        p=subprocess.run(["dotnet",*map(str,args)],cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,env=env)
    if p.returncode:
        print((S/(label+".log")).read_text("utf8",errors="replace")[-12000:],flush=True)
        raise RuntimeError(label)
    print("PASS",label,flush=True)

def runner(folder,name):return S/f"checks/{folder}/bin/Release/net10.0/{name}.dll"
def main():
    parser=argparse.ArgumentParser();parser.add_argument("group",choices=["load","runtime","compat"])
    group=parser.parse_args().group
    core=S/"candidate/[API1.9]CS武器1.3.0-轻量包.scmod";addon=S/"candidate/[API1.9]CS武器1.3.0-探员包.scmod";content=GAME/"Content.zip"
    if group=="load":
        split=runner("split","SplitCheck")
        for mode in ["core","agents"]:run("native-"+mode,split,core,addon,content,mode,S/("native-"+mode+".json"))
        native=runner("load-check","TacticalLoadCheck")
        environment=os.environ.copy()
        environment["SC_NMM_CHECK_PACKAGE"]=str(GAME/"Mods/[API1.9]NekoMeko Model-v1.1.scmod")
        for mode in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]:
            run("official-"+mode,native,ROOT,content,addon,mode,S/("official-"+mode+".json"),core,env=environment)
        environment["SC_NMM_CHECK_PACKAGE"]=str(ROOT/".tmp/actor-freeze-20260926/legacy-nmm-fixture.scmod")
        run("local-nmm",native,ROOT,content,addon,"both",S/"local-nmm.json",core,env=environment)
        run("gate",native,"--world-resource-gate",core,S/"gate.json")
        run("native",native,"--compat-native",core,content,S/"native.json")
        for owner,path in [("core",core),("agents",addon)]:
            plain=S/(owner+"-reference.zip")
            with zipfile.ZipFile(path) as z,zipfile.ZipFile(plain,"w",zipfile.ZIP_STORED) as dest:
                for n in z.namelist():dest.writestr(n,z.read(n))
            run("zip-"+owner,native,"--archive-identical",plain,path,S/("zip-"+owner+".json"))
    elif group=="runtime":
        run("resources",runner("resources","SplitResourceCheck"),core,ROOT/".tmp/split-lite-130-20260926/original-lite.scmod",content,S/"resources-native")
        run("npc",runner("npc","NpcWeaponCheck"),S/"fixture",S/"fixture-union.scmod",content,S/"npc",S/"npc-rebake")
        run("appearance",S/"runtime/AppearanceCheck/AppearanceCheck.dll",S/"fixture",content,S/"appearance")
        for role in ["ct","t"]:
            run("actor-"+role,runner("actor","ActorLoadCheck"),"verify",S/f"fixture/src/ScCsgoTactical/Assets/Models/ScCsgoTactical/{role}.glb",
                S/f"fixture/src/ScCsgoTactical/Assets/Animations/ScCsgoTactical/{role}.scanim",S/("actor-"+role+".json"))
    else:
        compat=runner("compat-runner","CompatibilityCheck");new=S/"compat-baselines/new/ScCsgoKnives.dll"
        groups={
            "historical":[ROOT/".tmp/lite-smooth-20260926/1.0.0.dll",ROOT/".tmp/lite-smooth-20260926/1.2.0.dll"],
            "current":[S/"compat-baselines/full/ScCsgoKnives.dll",S/"compat-baselines/lite/ScCsgoKnives.dll"],
            "mini":[S/"compat-baselines/mini/ScCsgoKnives.dll",S/"compat-baselines/old-split/ScCsgoKnives.dll"],
        }
        for name,paths in groups.items():run("compatibility-"+name,compat,*paths,new,S/("compatibility-"+name+".json"))

if __name__=="__main__":main()
