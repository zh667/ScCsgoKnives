"""Run final per-edition checks with exact candidate DLLs, log every gate."""
import argparse,concurrent.futures,json,os,subprocess,sys,zipfile
from prepare_mobile_common import ROOT,S
import check_codec_release as base
base.S=S
run=base.run
CONTENT=base.GAME/"Content.zip"
F=S/"full-check"

def dll(root,kind,name):return root/f"checks/{kind}/bin/Release/net10.0/{name}.dll"
def build(path):
    label="build-"+path.relative_to(S).as_posix().replace("/","-").replace(".csproj","")
    run(label,"build",path,"-c","Release","--nologo","-v","quiet")

def main():
    group=sys.argv[1]
    if group=="build":
        projects=list((S/"checks").glob("*/*.csproj"))+list((F/"checks").glob("*/*.csproj"))
        with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(build,projects))
    elif group in ["load","runtime","compat"]:
        base.main()
    elif group=="focused":
        for root,label in [(S,"split"),(F,"full")]:
            run(label+"-smoke",dll(root,"mobile","MobileCommonCheck"),root/"smoke")
            run(label+"-sampling",dll(root,"sampling","ActorSamplingCheck"),root/"fixture",CONTENT,root/"sampling")
        run("diagnostics",dll(S,"performance","TacticalPerformanceCheck"),S/"fixture",CONTENT,S/"performance")
    elif group=="codec":
        run("codec",dll(S,"codec","CodecReleaseCheck"),S)
        env=os.environ.copy();env["DOTNET_EnableHWIntrinsic"]="0"
        run("codec-software",dll(S,"codec","CodecReleaseCheck"),S,env=env)
    elif group=="ai":
        packages=json.loads((S/"packages.json").read_bytes())
        for label,core,addon,out in [
            ("split",packages["core"],packages["agents"],S/"ai.json"),
            ("full",packages["full"],packages["full"],F/"ai.json")]:
            run(label+"-ai",ROOT/"tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll",
                "--scmod",S/"candidate"/core["file"],"--tactical-package",S/"candidate"/addon["file"],
                "--tactical-ai-only","--vanilla-content",CONTENT,"--json",out)
    elif group=="full":
        packages=json.loads((S/"packages.json").read_bytes());full=S/"candidate"/packages["full"]["file"]
        run("full-npc",dll(F,"npc","NpcWeaponCheck"),F/"fixture",full,CONTENT,F/"npc",F/"npc-rebake")
        run("full-appearance",F/"runtime/AppearanceCheck/AppearanceCheck.dll",F/"fixture",CONTENT,F/"appearance")
        for role in ["ct","t"]:
            run("full-actor-"+role,dll(F,"actor","ActorLoadCheck"),"verify",F/f"fixture/src/ScCsgoTactical/Assets/Models/ScCsgoTactical/{role}.glb",
                F/f"fixture/src/ScCsgoTactical/Assets/Animations/ScCsgoTactical/{role}.scanim",F/("actor-"+role+".json"))
    elif group=="full-load":
        packages=json.loads((S/"packages.json").read_bytes());full=S/"candidate"/packages["full"]["file"]
        native=dll(F,"load","TacticalLoadCheck");env=os.environ.copy()
        env["SC_NMM_CHECK_PACKAGE"]=str(base.GAME/"Mods/[API1.9]NekoMeko Model-v1.1.scmod")
        for mode in ["both","reversed","none","nmm-only","neo-only","disabled","outdated"]:
            run("full-official-"+mode,native,ROOT,CONTENT,full,mode,F/("official-"+mode+".json"),full,env=env)
        run("full-gate",native,"--world-resource-gate",full,F/"gate.json")
        run("full-native",native,"--compat-native",full,CONTENT,F/"native.json")
        plain=F/"full-reference.zip"
        with zipfile.ZipFile(full) as source,zipfile.ZipFile(plain,"w",zipfile.ZIP_STORED) as dest:
            for n in source.namelist():dest.writestr(n,source.read(n))
        run("full-zip",native,"--archive-identical",plain,full,F/"zip.json")
        compat=dll(S,"compat-runner","CompatibilityCheck")
        run("compatibility-split-full",compat,S/"compat-baselines/new/ScCsgoKnives.dll",
            S/"compat-baselines/old-split/ScCsgoKnives.dll",F/"assemblies/ScCsgoKnives.dll",F/"compatibility.json")
    else:raise ValueError(group)

if __name__=="__main__":main()
