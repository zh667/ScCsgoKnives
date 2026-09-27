import sys,json,os,shutil
from prepare_feedback_fixes import ROOT,S
import check_mobile_resources as previous
import check_mobile_common as common
import check_codec_release as base
previous.S=common.S=base.S=S;common.F=S/"full-check"

def main():
    if sys.argv[1]=="features":
        packages=json.loads((S/"packages.json").read_bytes())
        env=os.environ.copy();env["ALSOFT_DRIVERS"]="null"
        for role,root in [("core",S),("full",S/"full-check")]:
            base.run(role+"-features",common.dll(root,"features","GameplayFollowupCheck"),
                S/"candidate"/packages[role]["file"],common.CONTENT,root/"features",env=env)
    elif sys.argv[1]=="sushi":
        packages=json.loads((S/"packages.json").read_bytes())
        base.run("sushi",common.dll(S,"sushi","SushiFeedbackCheck"),S/"candidate"/packages["core"]["file"],base.GAME/"Mods",S/"sushi.json")
    elif sys.argv[1]=="ai":
        packages=json.loads((S/"packages.json").read_bytes())
        for label,core,addon,out in [("split","core","agents",S/"ai.json"),("full","full","full",S/"full-check/ai.json")]:
            base.run(label+"-ai",common.dll(S,"package","PackageCheck"),"--scmod",S/"candidate"/packages[core]["file"],
                "--tactical-package",S/"candidate"/packages[addon]["file"],"--tactical-ai-only","--vanilla-content",common.CONTENT,"--json",out)
    else:previous.main()

if __name__=="__main__":main()
